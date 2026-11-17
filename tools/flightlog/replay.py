#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.
"""``.wrpl`` 回放导入：调用 ``wrpl-acmi.exe`` 转 ACMI，再交给 :mod:`flightlog.acmi` 解析。

设计要点：

* ``wrpl-acmi.exe`` 是第三方转换器（AGPL-3.0，见 README），放在 ``FlightLog.exe``
  或本工具目录旁边即可；找不到时给出下载提示（``fetch_wrpl_acmi.ps1``）；
* 转换产物写进 ``%TEMP%\\WtHud\\flightlog``，默认只保留最近 3 个（可配置）；
* 文件按「回放路径 + 大小 + mtime + 参数版本」做缓存，同一个回放重复看不同玩家
  时不必再转换一次；
* **不要**把 ``-ground-units=false`` 写进默认参数：实测某些回放（自定义战斗）
  在该开关下会连飞机位置一起丢掉（795 行 → 61 行），宁可多导地面单位。
"""

from __future__ import annotations

import hashlib
import os
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence

from . import acmi
from .acmi import AcmiPlayer, AcmiScan
from .parse import DEFAULT_GAP_SPLIT_S, LogError, LogModel

__all__ = [
    "CONVERTER_NAMES",
    "DEFAULT_CONVERT_ARGS",
    "ReplayError",
    "ReplaySession",
    "cache_dir",
    "describe_source",
    "find_wrpl_acmi",
    "open_acmi",
    "open_replay",
    "prune_cache",
    "temp_acmi_path",
    "validate_wrpl",
]

#: 转换器可执行文件名（Windows 是 .exe，其它平台留个无后缀的名字方便测试）
CONVERTER_NAMES = ("wrpl-acmi.exe", "wrpl-acmi")

#: 调用 wrpl-acmi.exe 的默认参数（保留飞机遥测估算；只砍掉对本工具没用的大块内容）。
#: 注意：``-estimate-telemetry`` 必须保持默认 true，否则没有 IAS/TAS/Mach/AOA/G。
DEFAULT_CONVERT_ARGS: Sequence[str] = (
    "-events=none",  # 聊天/击杀时间线：报告用不上，砍掉省体积
    "-radar-locks=false",  # 雷达锁定属性：报告不画雷达
    "-radar-modes=false",
    "-countermeasures=false",
    "-missile-seeker=false",
    "-bookmarks=false",
    "-airfields=false",
    "-estimate-bomb-trajectories=false",
)

#: 缓存键里带上参数版本：改了默认参数就自动重新转换
_CACHE_TAG = "v2"

#: 转换超时（秒）；18 MB 回放实测约 4 s，给足余量
DEFAULT_TIMEOUT_S = 900.0


class ReplayError(LogError):
    """回放无法导入（找不到转换器 / 不是 .wrpl / 转换失败 / 没有飞机）。"""


# ---------------------------------------------------------------------------
# 转换器定位 / 校验
# ---------------------------------------------------------------------------


def find_wrpl_acmi(explicit: Optional[str] = None, extra_dirs: Sequence[str] = ()) -> str:
    """定位 ``wrpl-acmi.exe``，找不到时抛 :class:`ReplayError`（带下载提示）。"""
    candidates: List[Path] = []
    if explicit:
        candidates.append(Path(explicit))
    env_value = os.environ.get("WRPL_ACMI")
    if env_value:
        candidates.append(Path(env_value))
    dirs: List[Path] = [Path(directory) for directory in extra_dirs if directory]
    # 1) 本模块所在目录（源码运行 / PyInstaller 解包目录）
    dirs.append(Path(__file__).resolve().parent)
    # 2) exe 所在目录（打包后的 FlightLog.exe 旁边）
    try:
        dirs.append(Path(sys.executable).resolve().parent)
    except OSError:  # pragma: no cover - 极端情况下取不到
        pass
    # 3) 当前工作目录
    dirs.append(Path.cwd())
    for directory in dirs:
        for name in CONVERTER_NAMES:
            candidates.append(directory / name)

    seen = set()
    for candidate in candidates:
        try:
            key = str(candidate.resolve())
        except OSError:
            key = str(candidate)
        if key in seen:
            continue
        seen.add(key)
        if candidate.is_file():
            return str(candidate)

    raise ReplayError(
        "找不到 wrpl-acmi.exe（.wrpl → ACMI 转换器）。\n"
        "请把 wrpl-acmi.exe 放在 FlightLog.exe（或 tools\\flightlog 目录）旁边；\n"
        "没有的话运行 tools\\flightlog\\fetch_wrpl_acmi.ps1 自动下载，\n"
        "或手动从 https://github.com/3794432/wrpl-acmi/releases 下载（AGPL-3.0）。"
    )


def validate_wrpl(path: str) -> None:
    """粗校验 ``.wrpl``：真实回放是二进制头 + ``levels/<地图>.bin`` 路径。"""
    target = Path(path)
    if target.is_dir():
        raise ReplayError("暂不支持服务器回放分片目录，请选择单个 .wrpl 文件：%s" % path)
    if not target.is_file():
        raise ReplayError("回放文件不存在：%s" % path)
    size = target.stat().st_size
    if size < 1024:
        raise ReplayError("回放文件太小（%d 字节），可能拷贝/下载不完整：%s" % (size, path))
    try:
        with open(path, "rb") as handle:
            head = handle.read(64)
    except OSError as exc:
        raise ReplayError("无法读取回放文件：%s (%s)" % (path, exc))
    if b"levels/" not in head:
        raise ReplayError(
            "不是有效的 .wrpl 回放（文件头里没有 levels/ 地图路径）：%s\n"
            "请确认选择的是 War Thunder 的 .wrpl 回放文件。" % path
        )


# ---------------------------------------------------------------------------
# 临时目录 / 缓存
# ---------------------------------------------------------------------------


def cache_dir() -> Path:
    """临时 ACMI 目录：``%TEMP%\\WtHud\\flightlog``（其它平台用系统临时目录）。"""
    root = os.environ.get("TEMP") or os.environ.get("TMP") or tempfile.gettempdir()
    directory = Path(root) / "WtHud" / "flightlog"
    directory.mkdir(parents=True, exist_ok=True)
    return directory


def _cache_key(wrpl_path: str) -> str:
    """回放指纹：绝对路径 + 大小 + mtime + 默认参数 + 参数版本。"""
    stat = Path(wrpl_path).stat()
    raw = "%s|%d|%d|%s|%s" % (
        os.path.abspath(wrpl_path),
        stat.st_size,
        int(stat.st_mtime),
        " ".join(DEFAULT_CONVERT_ARGS),
        _CACHE_TAG,
    )
    return hashlib.sha1(raw.encode("utf-8", "replace")).hexdigest()[:12]


def temp_acmi_path(wrpl_path: str) -> Path:
    """缓存 ACMI 的路径（同一回放稳定复用）。"""
    return cache_dir() / ("%s-%s.acmi" % (_safe_stem(Path(wrpl_path).stem), _cache_key(wrpl_path)))


def _safe_stem(text: str) -> str:
    cleaned = "".join(ch if (ch.isalnum() or ch in "-_.") else "_" for ch in text)
    return (cleaned or "replay")[:48]


def prune_cache(keep: int, extra_keep: Optional[Sequence[str]] = None) -> List[str]:
    """只保留最近 ``keep`` 个临时 ACMI（0 = 全删），``extra_keep`` 里的永不删。

    返回被删掉的路径列表。
    """
    keep = max(0, int(keep))
    directory = cache_dir()
    protected = {os.path.abspath(str(p)) for p in (extra_keep or [])}
    try:
        entries = [item for item in directory.iterdir() if item.is_file() and item.suffix.lower() == ".acmi"]
    except OSError:  # pragma: no cover - 目录不可读时静默跳过
        return []
    entries.sort(key=lambda item: item.stat().st_mtime if item.exists() else 0.0, reverse=True)
    keep_set = set(protected)
    remaining = keep
    for item in entries:
        if remaining <= 0:
            break
        keep_set.add(os.path.abspath(str(item)))
        remaining -= 1
    removed: List[str] = []
    for item in entries:
        absolute = os.path.abspath(str(item))
        if absolute in keep_set:
            continue
        try:
            item.unlink()
            removed.append(str(item))
        except OSError:
            pass
    return removed


# ---------------------------------------------------------------------------
# 转换
# ---------------------------------------------------------------------------


def convert(
    wrpl_path: str,
    out_path: str,
    converter: Optional[str] = None,
    *,
    timeout_s: float = DEFAULT_TIMEOUT_S,
    extra_args: Sequence[str] = (),
) -> Dict[str, Any]:
    """调用 ``wrpl-acmi.exe`` 把 ``.wrpl`` 转成 ``.acmi``，返回耗时/输出摘要。"""
    exe = find_wrpl_acmi(converter)
    args = [exe, "-in", os.path.abspath(wrpl_path), "-out", os.path.abspath(out_path)]
    args.extend(DEFAULT_CONVERT_ARGS)
    args.extend(extra_args)
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
    started = time.time()
    try:
        proc = subprocess.run(
            args,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout_s,
            creationflags=creationflags,
        )
    except FileNotFoundError:
        raise ReplayError("无法启动转换器：%s" % exe)
    except subprocess.TimeoutExpired:
        raise ReplayError(
            "转换超时（> %.0f s）：%s\n可尝试用更小的回放，或手动运行 wrpl-acmi.exe 排查。"
            % (timeout_s, wrpl_path)
        )
    elapsed = time.time() - started
    stdout = _decode(proc.stdout)
    stderr = _decode(proc.stderr)
    tail = _tail_text(stdout, stderr)
    if proc.returncode != 0:
        if "no tracks found" in (stdout + stderr).lower():
            raise ReplayError(
                "该回放没有可导出的单位轨迹（wrpl-acmi: no tracks found）。\n"
                "常见原因：空/中断的对局、或只有静态建筑没有载具。\n文件：%s" % wrpl_path
            )
        raise ReplayError(
            "回放转换失败（wrpl-acmi 退出码 %s）：\n%s\n文件：%s"
            % (proc.returncode, tail or "（无输出）", wrpl_path)
        )
    if not os.path.isfile(out_path) or os.path.getsize(out_path) <= 0:
        raise ReplayError(
            "转换器退出码 0 但没有生成 ACMI：%s\n输出：%s" % (out_path, tail or "（无输出）")
        )
    return {
        "ok": True,
        "elapsed_s": round(elapsed, 3),
        "converter": exe,
        "output": os.path.abspath(out_path),
        "size": os.path.getsize(out_path),
        "log_tail": tail,
    }


def _decode(data: Optional[bytes]) -> str:
    if not data:
        return ""
    for encoding in ("utf-8", "gbk", "latin-1"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-8", "replace")


def _tail_text(stdout: str, stderr: str, limit: int = 12) -> str:
    lines = [line.strip() for line in ((stderr or "") + "\n" + (stdout or "")).splitlines() if line.strip()]
    if not lines:
        return ""
    return "\n".join(lines[-limit:])


# ---------------------------------------------------------------------------
# 会话：一次转换 + 扫描，可以反复按玩家出模型
# ---------------------------------------------------------------------------


@dataclass
class ReplaySession:
    """一次回放导入的上下文（转换结果、ACMI 扫描、玩家列表、耗时）。"""

    wrpl_path: str
    acmi_path: str
    scan_result: AcmiScan
    converted: bool = True
    converter: Optional[str] = None
    convert_s: float = 0.0
    total_s: float = 0.0
    log_tail: str = ""
    warnings: List[str] = field(default_factory=list)

    @property
    def header(self):
        return self.scan_result.header

    @property
    def players(self) -> List[AcmiPlayer]:
        return self.scan_result.players

    def model_for(
        self,
        names: Optional[Sequence[str]] = None,
        *,
        players: Optional[Sequence[AcmiPlayer]] = None,
        gap_split_s: float = DEFAULT_GAP_SPLIT_S,
    ) -> LogModel:
        """按玩家名（或直接给玩家对象）生成报告模型。"""
        chosen = list(players) if players is not None else acmi.resolve_players(self.players, names or [])
        model = acmi.build_model(
            self.scan_result,
            chosen,
            acmi_path=self.acmi_path,
            wrpl_path=self.wrpl_path,
            gap_split_s=gap_split_s,
            convert_s=self.convert_s,
            converter=self.converter,
            converted=self.converted,
        )
        replay_meta = model.meta.setdefault("replay", {})
        replay_meta["total_s"] = round(self.total_s, 3)
        replay_meta["convert_log_tail"] = self.log_tail
        return model

    def cleanup(self, keep: int) -> List[str]:
        """报告生成完之后清理临时 ACMI（``keep=0`` 会连本次产物一起删）。"""
        return prune_cache(keep)


def open_acmi(acmi_path: str, *, wrpl_path: Optional[str] = None) -> ReplaySession:
    """直接解析一个 ACMI（不转换；``wrpl_path`` 仅用于展示/元数据）。"""
    started = time.time()
    if not os.path.isfile(acmi_path):
        raise ReplayError("ACMI 不存在：%s" % acmi_path)
    scan_result = acmi.scan(acmi_path)
    if not scan_result.players:
        raise ReplayError(
            "ACMI 里没有飞机类对象（Type 含 Air）。\n文件：%s" % acmi_path
        )
    return ReplaySession(
        wrpl_path=os.path.abspath(wrpl_path) if wrpl_path else os.path.abspath(acmi_path),
        acmi_path=os.path.abspath(acmi_path),
        scan_result=scan_result,
        converted=False,
        converter=None,
        convert_s=0.0,
        total_s=round(time.time() - started, 3),
        log_tail="",
        warnings=list(scan_result.warnings),
    )


def open_replay(
    wrpl_path: str,
    *,
    acmi_path: Optional[str] = None,
    converter: Optional[str] = None,
    keep_acmi: int = 3,
    timeout_s: float = DEFAULT_TIMEOUT_S,
    extra_args: Sequence[str] = (),
) -> ReplaySession:
    """导入一个 ``.wrpl``：必要时转换 → 扫描 ACMI → 返回会话。

    ``acmi_path`` 用于跳过转换直接解析已有 ACMI；``keep_acmi`` 是临时文件保留
    个数（0 = 报告生成后删掉；真正的删除由 :meth:`ReplaySession.cleanup` 完成）。
    """
    started = time.time()
    validate_wrpl(wrpl_path)

    if acmi_path:
        if not os.path.isfile(acmi_path):
            raise ReplayError("指定的 ACMI 不存在：%s" % acmi_path)
        session = open_acmi(acmi_path, wrpl_path=wrpl_path)
        session.total_s = round(time.time() - started, 3)
        if keep_acmi > 0:
            prune_cache(keep_acmi - 1, extra_keep=[session.acmi_path])
        return session

    target_acmi = str(temp_acmi_path(wrpl_path))
    reused = _cache_valid(target_acmi, wrpl_path)
    if reused:
        convert_s = 0.0
        converter_used: Optional[str] = None
        log_tail = "（复用临时 ACMI）"
        converted = False
    else:
        result = convert(wrpl_path, target_acmi, converter, timeout_s=timeout_s, extra_args=extra_args)
        convert_s = float(result["elapsed_s"])
        converter_used = result["converter"]
        log_tail = result["log_tail"]
        converted = True

    scan_result = acmi.scan(target_acmi)
    if not scan_result.players:
        raise ReplayError(
            "回放里没有飞机类对象（Type 含 Air）。\n"
            "可能是纯地面战回放，或该回放没有导出空中单位。\n文件：%s" % wrpl_path
        )
    session = ReplaySession(
        wrpl_path=os.path.abspath(wrpl_path),
        acmi_path=os.path.abspath(target_acmi),
        scan_result=scan_result,
        converted=converted,
        converter=converter_used,
        convert_s=convert_s,
        total_s=round(time.time() - started, 3),
        log_tail=log_tail,
        warnings=list(scan_result.warnings),
    )
    # 旧缓存按 keep 清理；本次产物先留着（报告生成后由 cleanup 处理）
    if keep_acmi > 0:
        prune_cache(keep_acmi - 1, extra_keep=[session.acmi_path])
    return session


def _cache_valid(acmi_file: str, wrpl_path: str) -> bool:
    try:
        acmi_stat = os.stat(acmi_file)
        wrpl_stat = os.stat(wrpl_path)
    except OSError:
        return False
    if acmi_stat.st_size <= 1024:
        return False
    return acmi_stat.st_mtime + 1.0 >= wrpl_stat.st_mtime


def describe_source(path: str) -> str:
    """按扩展名判断来源类型：``jsonl`` / ``wrpl`` / ``acmi``。"""
    suffix = Path(path).suffix.lower()
    if suffix == ".wrpl":
        return "wrpl"
    if suffix == ".acmi":
        return "acmi"
    return "jsonl"
