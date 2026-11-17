#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.
"""ACMI 2.1（Tacview 文本）解析：从 War Thunder ``.wrpl`` 回放转换出的 ``.acmi``
里提取**飞机类对象**的玩家列表与逐帧样本，并映射成 :mod:`flightlog.parse` 的
报告模型（``LogModel``）。

分工：

* :mod:`flightlog.replay` —— 调用 ``wrpl-acmi.exe`` 把 ``.wrpl`` 转成 ``.acmi``；
* 本模块 —— 纯文本解析 + 玩家归组 + 样本抽取 + 模型构建（纯标准库，无第三方依赖）。

ACMI 关键约定（按实际样本核对过，2026-11 的 wrpl-acmi v3.0.0）：

* 头部是 ``Key=Value``（无对象 id 前缀），全局行以 ``0,`` 开头；
* 对象声明：``<id>,Name=..,CallSign=..,Pilot=..,Type=..,Coalition=..,...``；
* 帧头：``#<秒>``（回放内相对秒数，浮点）；
* 对象更新：``<id>,T=lon|lat|alt|roll|pitch|yaw,HDG=..,TAS=..,Mach=..,IAS=..,AOA=..,VerticalGForce=..``；
* ``T`` 的前三个分量是**相对** ``ReferenceLongitude/Latitude/Altitude`` 的位置
  （度/米），不是增量；6 个分量时后三个是 roll/pitch/yaw（度）；
* ``TAS`` / ``IAS`` 单位是 **m/s**（``Mach`` 用标准声速反算，可交叉验证）；
* 字段可能是空值（``TAS=,Mach=,``），一律当缺失。

两遍流式读取（147 MB 级别的文件不能整体进内存）：

1. :func:`scan` —— 收集头部、对象声明、每个对象的出现时间与更新时间数；
2. :func:`extract` —— 只保留所选玩家/对象的样本。
"""

from __future__ import annotations

import io
import os
import re
from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

from .parse import (
    DEFAULT_GAP_SPLIT_S,
    Flight,
    LogError,
    LogModel,
    build_flights,
    build_series,
    build_summary,
    compute_flight_stats,
)

__all__ = [
    "AcmiError",
    "AcmiHeader",
    "AcmiObject",
    "AcmiPlayer",
    "AcmiSampleStats",
    "AcmiScan",
    "build_model",
    "extract",
    "is_air_type",
    "player_rows",
    "resolve_players",
    "scan",
]

#: 相对位置的时间窗口（vy 由高度差分得到）
_VY_MAX_DT_S = 3.0
_VY_MIN_DT_S = 0.2
_VY_MAX_ABS_MS = 400.0

#: ACMI 里我们真正需要的对象字段
_IDENTITY_KEYS = ("Name", "CallSign", "Pilot", "Type", "Coalition")

#: 对象更新里我们真正需要的数值字段
_UPDATE_KEYS = ("HDG", "TAS", "IAS", "Mach", "AOA", "VerticalGForce")


class AcmiError(LogError):
    """ACMI 无法使用（不是 ACMI / 没有飞机 / 没有样本）。"""


# ---------------------------------------------------------------------------
# 基础类型
# ---------------------------------------------------------------------------


@dataclass
class AcmiHeader:
    """ACMI 文件头（只保留报告/调试用得上的字段）。"""

    file_type: Optional[str] = None
    file_version: Optional[str] = None
    reference_time: Optional[str] = None
    reference_lon: Optional[float] = None
    reference_lat: Optional[float] = None
    reference_alt: Optional[float] = None
    title: Optional[str] = None
    data_source: Optional[str] = None
    author: Optional[str] = None
    recording_time: Optional[str] = None
    map_id: Optional[str] = None
    comments: Optional[str] = None
    extra: Dict[str, str] = field(default_factory=dict)

    @property
    def exporter(self) -> Optional[str]:
        """从 ``Comments`` 里抠出导出器版本（``wrpl-acmi v3.0.0-...``，太长会截断）。"""
        if not self.comments:
            return None
        m = re.search(r"Exporter\s+([^;]+)", self.comments)
        if not m:
            return None
        text = m.group(1).strip().split("+")[0]
        match = re.match(r"^(wrpl-acmi[\s\-]*v?\d+(?:\.\d+)*)", text)
        if match:
            return match.group(1)
        return text[:40]

    def as_dict(self) -> Dict[str, Any]:
        return {
            "file_type": self.file_type,
            "file_version": self.file_version,
            "reference_time": self.reference_time,
            "reference_lon": self.reference_lon,
            "reference_lat": self.reference_lat,
            "reference_alt": self.reference_alt,
            "title": self.title,
            "map_id": self.map_id,
            "data_source": self.data_source,
            "author": self.author,
            "recording_time": self.recording_time,
            "exporter": self.exporter,
        }


@dataclass
class AcmiObject:
    """ACMI 里的一个对象（飞机 / 地面单位 / 导弹…）。"""

    oid: str
    name: Optional[str] = None
    callsign: Optional[str] = None
    pilot: Optional[str] = None
    type: Optional[str] = None
    coalition: Optional[str] = None
    updates: int = 0
    position_updates: int = 0
    t_first: Optional[float] = None
    t_last: Optional[float] = None

    @property
    def is_air(self) -> bool:
        return is_air_type(self.type)

    @property
    def has_pilot(self) -> bool:
        return bool(self.pilot)

    @property
    def display_name(self) -> str:
        """对象级展示名（用于没有 Pilot 的对象）。"""
        return self.callsign or self.name or ("#%s" % self.oid)

    def as_dict(self) -> Dict[str, Any]:
        return {
            "id": self.oid,
            "name": self.name,
            "callsign": self.callsign,
            "pilot": self.pilot,
            "type": self.type,
            "coalition": self.coalition,
            "updates": self.updates,
            "t_first": self.t_first,
            "t_last": self.t_last,
        }


@dataclass
class AcmiPlayer:
    """一名玩家（同一个 ``Pilot`` 的全部飞机对象；飞行员缺失时按对象分组）。"""

    name: str
    pilot: Optional[str]
    has_pilot: bool
    object_ids: List[str] = field(default_factory=list)
    vehicles: List[str] = field(default_factory=list)
    coalitions: List[str] = field(default_factory=list)
    updates: int = 0
    t_first: Optional[float] = None
    t_last: Optional[float] = None

    @property
    def duration_s(self) -> Optional[float]:
        if self.t_first is None or self.t_last is None:
            return None
        return round(self.t_last - self.t_first, 3)

    @property
    def coalition_label(self) -> str:
        parts = [c for c in self.coalitions if c]
        if not parts:
            return "未知"
        mapping = {"Blue": "蓝", "Red": "红", "Neutral": "中立"}
        return "/".join(mapping.get(c, c) for c in parts)

    @property
    def vehicle_label(self) -> str:
        vehicles = [v for v in self.vehicles if v]
        if not vehicles:
            return "未知机型"
        if len(vehicles) <= 3:
            return " / ".join(vehicles)
        return "%s 等 %d 种" % (" / ".join(vehicles[:3]), len(vehicles))

    def as_dict(self) -> Dict[str, Any]:
        return {
            "name": self.name,
            "pilot": self.pilot,
            "has_pilot": self.has_pilot,
            "vehicles": list(self.vehicles),
            "coalitions": list(self.coalitions),
            "coalition_label": self.coalition_label,
            "objects": len(self.object_ids),
            "object_ids": list(self.object_ids),
            "updates": self.updates,
            "t_first": self.t_first,
            "t_last": self.t_last,
            "duration_s": self.duration_s,
        }


@dataclass
class AcmiScan:
    """第一遍扫描的结果：头部 + 对象表 + 玩家列表 + 计数。"""

    path: str
    header: AcmiHeader
    objects: Dict[str, AcmiObject]
    players: List[AcmiPlayer]
    t_min: Optional[float]
    t_max: Optional[float]
    frames: int = 0
    lines: int = 0
    air_object_count: int = 0
    ground_object_count: int = 0
    skipped_lines: int = 0
    scan_s: float = 0.0
    warnings: List[str] = field(default_factory=list)

    @property
    def duration_s(self) -> Optional[float]:
        if self.t_min is None or self.t_max is None:
            return None
        return round(self.t_max - self.t_min, 3)

    def player_by_name(self, name: str) -> Optional[AcmiPlayer]:
        low = name.strip().lower()
        for player in self.players:
            if player.name.lower() == low:
                return player
        return None

    def as_dict(self) -> Dict[str, Any]:
        return {
            "path": os.path.abspath(self.path),
            "header": self.header.as_dict(),
            "frames": self.frames,
            "lines": self.lines,
            "t_min": self.t_min,
            "t_max": self.t_max,
            "duration_s": self.duration_s,
            "players": [p.as_dict() for p in self.players],
            "air_objects": self.air_object_count,
            "ground_objects": self.ground_object_count,
        }


@dataclass
class AcmiSampleStats:
    """抽取阶段的计数（用于报告提示与自检）。"""

    samples: int = 0
    objects_used: int = 0
    objects_without_samples: List[str] = field(default_factory=list)
    no_speed: int = 0
    no_attitude: int = 0
    t_min: Optional[float] = None
    t_max: Optional[float] = None
    elapsed_s: float = 0.0

    @property
    def duration_s(self) -> Optional[float]:
        if self.t_min is None or self.t_max is None:
            return None
        return round(self.t_max - self.t_min, 3)

    @property
    def rate_hz(self) -> Optional[float]:
        span = self.duration_s
        if not span or span <= 0 or self.samples <= 0:
            return None
        return round((self.samples - 1) / span, 2)


# ---------------------------------------------------------------------------
# 文本解析小工具
# ---------------------------------------------------------------------------


def is_air_type(type_text: Optional[str]) -> bool:
    """``Type=Air+FixedWing`` → True；``Ground+Vehicle+AntiAircraft`` → False。

    必须按 ``+`` 切词再比对，否则 ``AntiAircraft`` 里的 ``Air`` 会把防空车
    误判成飞机（实测确实会出现这种情况）。
    """
    if not type_text:
        return False
    return "Air" in type_text.split("+")


def _to_float(text: Optional[str]) -> Optional[float]:
    """宽松转 float：空串 / 非法 / nan / inf 一律 None。"""
    if text is None:
        return None
    text = text.strip()
    if not text:
        return None
    try:
        value = float(text)
    except (TypeError, ValueError):
        return None
    if value != value or value in (float("inf"), float("-inf")):
        return None
    return value


def _to_text(text: Optional[str]) -> Optional[str]:
    if text is None:
        return None
    text = text.strip()
    return text or None


def _split_pairs(text: str) -> Dict[str, str]:
    """按未转义的逗号切成 ``Key=Value`` 字典（ACMI 值里用 ``\\,`` 转义逗号）。"""
    pairs: Dict[str, str] = {}
    if not text:
        return pairs
    start = 0
    length = len(text)
    for index in range(length + 1):
        if index == length or (text[index] == "," and text[index - 1] != "\\"):
            part = text[start:index]
            if part:
                key, sep, value = part.partition("=")
                if sep:
                    pairs[key.strip()] = value.replace("\\,", ",").replace("\\\\", "\\")
            start = index + 1
    return pairs


def _read_lines(path: str) -> Iterable[str]:
    """按行读取（UTF-8，容错），不去掉行内空格。"""
    try:
        handle = io.open(path, "r", encoding="utf-8", errors="replace", newline="")
    except OSError as exc:
        raise AcmiError("无法打开 ACMI 文件: %s (%s)" % (path, exc))
    with handle:
        for raw in handle:
            yield raw.rstrip("\r\n")


def _read_head(path: str, size: int = 4096) -> str:
    try:
        with io.open(path, "rb") as handle:
            return handle.read(size).decode("utf-8", "replace")
    except OSError as exc:
        raise AcmiError("无法读取 ACMI 文件: %s (%s)" % (path, exc))


# ---------------------------------------------------------------------------
# 第一遍扫描：对象 / 玩家 / 时间范围
# ---------------------------------------------------------------------------


def scan(path: str, air_only: bool = True) -> AcmiScan:
    """流式扫描 ACMI，返回头部、对象表与玩家列表。

    ``air_only=True`` 时只把 ``Type`` 含 ``Air`` 的词条算作飞机对象；地面单位
    与导弹仍会出现在 ``objects`` 里（带 ``is_air=False``），方便排查。
    """
    import time

    if not os.path.isfile(path):
        raise AcmiError("ACMI 文件不存在: %s" % path)

    head = _read_head(path)
    if "FileType=" not in head and "FileVersion=" not in head:
        raise AcmiError(
            "不是 ACMI (Tacview) 文本文件，缺少 FileType/FileVersion 头: %s" % path
        )

    started = time.time()
    header = AcmiHeader()
    objects: Dict[str, AcmiObject] = {}
    lines = 0
    frames = 0
    skipped = 0
    cur_t = 0.0
    t_min: Optional[float] = None
    t_max: Optional[float] = None
    order: List[str] = []

    for raw in _read_lines(path):
        lines += 1
        line = raw.strip()
        if not line:
            continue
        first = line[0]
        if first == "#":
            frames += 1
            stamp = _to_float(line[1:])
            if stamp is not None:
                cur_t = stamp
                if t_max is None or stamp > t_max:
                    t_max = stamp
            continue
        if line.startswith("0,"):
            key, sep, value = line[2:].partition("=")
            if not sep:
                skipped += 1
                continue
            key = key.strip()
            if key in ("ReferenceLongitude", "ReferenceLatitude", "ReferenceAltitude"):
                setattr(header, _REF_FIELDS[key], _to_float(value))
            elif key == "ReferenceTime":
                header.reference_time = _to_text(value)
            elif key == "Title":
                header.title = _to_text(value)
            elif key == "MapId":
                header.map_id = _to_text(value)
            elif key == "Comments":
                header.comments = _to_text(value)
            elif key in ("FileType", "FileVersion", "DataSource", "Author", "RecordingTime"):
                header.extra[key] = value
                setattr(header, _HEADER_FIELDS[key], _to_text(value))
            else:
                header.extra[key] = value
            continue

        oid, sep, rest = line.partition(",")
        if not sep:
            # 文件最前面两行没有 ``0,`` 前缀：FileType=.. / FileVersion=..
            key, eq, value = line.partition("=")
            if eq:
                key = key.strip()
                header.extra[key] = value
                if key in _HEADER_FIELDS:
                    setattr(header, _HEADER_FIELDS[key], _to_text(value))
            else:
                skipped += 1
            continue
        if "Name=" in rest:
            # 对象声明（可能出现在文件开头，也可能在回放中途）
            kv = _split_pairs(rest)
            obj = objects.get(oid)
            if obj is None:
                obj = AcmiObject(oid=oid)
                objects[oid] = obj
                order.append(oid)
            for key in _IDENTITY_KEYS:
                value = _to_text(kv.get(key))
                if value is not None:
                    setattr(obj, _IDENTITY_FIELDS[key], value)
            continue

        # 对象更新
        obj = objects.get(oid)
        if obj is None:
            obj = AcmiObject(oid=oid)
            objects[oid] = obj
            order.append(oid)
        obj.updates += 1
        if "T=" in rest or rest.startswith("T="):
            obj.position_updates += 1
            if obj.t_first is None:
                obj.t_first = cur_t
                if t_min is None or cur_t < t_min:
                    t_min = cur_t
            obj.t_last = cur_t

    if not frames and not objects:
        raise AcmiError("ACMI 文件里没有任何对象或帧标记，可能已损坏: %s" % path)

    air_objects = [objects[oid] for oid in order if objects[oid].is_air] if air_only else [
        objects[oid] for oid in order
    ]
    players = _group_players(air_objects, order)
    scan_result = AcmiScan(
        path=path,
        header=header,
        objects=objects,
        players=players,
        t_min=t_min,
        t_max=t_max,
        frames=frames,
        lines=lines,
        air_object_count=len(air_objects),
        ground_object_count=len(order) - len(air_objects),
        skipped_lines=skipped,
        scan_s=round(time.time() - started, 3),
    )
    if not frames:
        scan_result.warnings.append("ACMI 里没有帧头（#秒），样本时间可能都按 0 处理")
    if not air_objects:
        scan_result.warnings.append("ACMI 里没有飞机类对象（Type 不含 Air）")
    if any(p.updates == 0 for p in players):
        scan_result.warnings.append("部分玩家对象没有任何位置更新")
    return scan_result


#: 头部字段名映射（``0,Key=Value`` → AcmiHeader 属性）
_HEADER_FIELDS = {
    "FileType": "file_type",
    "FileVersion": "file_version",
    "DataSource": "data_source",
    "Author": "author",
    "RecordingTime": "recording_time",
}
_REF_FIELDS = {
    "ReferenceLongitude": "reference_lon",
    "ReferenceLatitude": "reference_lat",
    "ReferenceAltitude": "reference_alt",
}
_IDENTITY_FIELDS = {
    "Name": "name",
    "CallSign": "callsign",
    "Pilot": "pilot",
    "Type": "type",
    "Coalition": "coalition",
}


def _group_players(air_objects: Sequence[AcmiObject], order: Sequence[str]) -> List[AcmiPlayer]:
    """按 ``Pilot`` 归组；没有 Pilot 的对象单独成组（``机型 #id``）。"""
    rank = {oid: index for index, oid in enumerate(order)}
    grouped: Dict[str, AcmiPlayer] = {}
    for obj in air_objects:
        if obj.has_pilot:
            name = obj.pilot or obj.display_name
            key = "pilot:" + name
            has_pilot = True
        else:
            name = "%s #%s" % (obj.display_name, obj.oid)
            key = "object:" + obj.oid
            has_pilot = False
        player = grouped.get(key)
        if player is None:
            player = AcmiPlayer(name=name, pilot=obj.pilot, has_pilot=has_pilot)
            grouped[key] = player
        player.object_ids.append(obj.oid)
        if obj.name and obj.name not in player.vehicles:
            player.vehicles.append(obj.name)
        if obj.coalition and obj.coalition not in player.coalitions:
            player.coalitions.append(obj.coalition)
        player.updates += obj.updates
        if obj.t_first is not None:
            player.t_first = obj.t_first if player.t_first is None else min(player.t_first, obj.t_first)
        if obj.t_last is not None:
            player.t_last = obj.t_last if player.t_last is None else max(player.t_last, obj.t_last)

    players = list(grouped.values())
    for player in players:
        player.object_ids.sort(key=lambda oid: rank.get(oid, 1 << 30))
    # 有飞行员的真人优先，其次按更新时间数（≈样本量）从多到少
    players.sort(key=lambda p: (0 if p.has_pilot else 1, -(p.updates or 0), p.name))
    return players


def player_rows(players: Sequence[AcmiPlayer]) -> List[Dict[str, Any]]:
    """玩家列表的表格数据（序号 / 玩家 / 机型 / 阵营 / 对象 / 样本 / 时长）。"""
    rows: List[Dict[str, Any]] = []
    for index, player in enumerate(players, start=1):
        rows.append(
            {
                "index": index,
                "name": player.name,
                "pilot": player.pilot,
                "has_pilot": player.has_pilot,
                "vehicles": player.vehicle_label,
                "coalition": player.coalition_label,
                "objects": len(player.object_ids),
                "updates": player.updates,
                "duration_s": player.duration_s,
                "duration": _fmt_duration(player.duration_s),
            }
        )
    return rows


def _fmt_duration(seconds: Optional[float]) -> str:
    if seconds is None:
        return "--"
    total = int(round(seconds))
    return "%d:%02d" % (total // 60, total % 60)


def resolve_players(
    players: Sequence[AcmiPlayer], names: Sequence[str]
) -> List[AcmiPlayer]:
    """把用户给的名字/序号解析成玩家列表。

    支持：完全匹配（大小写不敏感）→ 子串匹配 → 纯数字当作 **1 起的序号**。
    解析不到时抛 :class:`AcmiError`（消息里带候选项）。
    """
    selected: List[AcmiPlayer] = []
    seen = set()
    missing: List[str] = []
    for raw in names:
        token = (raw or "").strip()
        if not token:
            continue
        match = _match_player(players, token)
        if match is None:
            missing.append(token)
            continue
        if match.name not in seen:
            seen.add(match.name)
            selected.append(match)
    if missing:
        hint = "、".join(p.name for p in players[:8])
        raise AcmiError(
            "找不到玩家: %s\n可用玩家（前 8 个）: %s\n提示: 用 --list 查看完整列表，"
            "或 --player 序号（1 起）" % ("、".join(missing), hint or "（无）")
        )
    if not selected:
        raise AcmiError("没有选定任何玩家（--player 为空？）")
    return selected


def _match_player(players: Sequence[AcmiPlayer], token: str) -> Optional[AcmiPlayer]:
    low = token.lower()
    for player in players:
        if player.name.lower() == low:
            return player
    for player in players:
        if low in player.name.lower():
            return player
    if token.isdigit():
        index = int(token)
        if 1 <= index <= len(players):
            return players[index - 1]
    return None


# ---------------------------------------------------------------------------
# 第二遍抽取：只保留所选对象的样本
# ---------------------------------------------------------------------------


class _AircraftState:
    """一个飞机对象的"最近一次已知状态"（ACMI 更新是增量的）。"""

    __slots__ = (
        "alt",
        "roll",
        "pitch",
        "yaw",
        "hdg",
        "tas",
        "mach",
        "ias",
        "aoa",
        "g",
        "has_pos",
    )

    def __init__(self) -> None:
        self.alt: Optional[float] = None
        self.roll: Optional[float] = None
        self.pitch: Optional[float] = None
        self.yaw: Optional[float] = None
        self.hdg: Optional[float] = None
        self.tas: Optional[float] = None
        self.mach: Optional[float] = None
        self.ias: Optional[float] = None
        self.aoa: Optional[float] = None
        self.g: Optional[float] = None
        self.has_pos = False


def extract(
    path: str, object_ids: Iterable[str], vehicle_by_id: Optional[Dict[str, str]] = None
) -> Tuple[Dict[str, List[Dict[str, Any]]], AcmicSampleStats]:
    """第二遍流式读取，只保留 ``object_ids`` 的样本。

    返回 ``(samples_by_object, stats)``；每个样本是 :mod:`flightlog.parse`
    兼容的 dict（``t`` / ``ias`` / ``tas`` / ``mach`` / ``alt`` / ``aoa`` /
    ``ny`` / ``pitch`` / ``bank`` / ``hdg_deg`` / ``vehicle`` …），``vy`` 留空，
    由 :func:`build_model` 在合并后按高度差分补齐。
    """
    wanted = set(object_ids)
    if not wanted:
        raise AcmiError("没有指定要抽取的对象")
    import time

    started = time.time()
    vehicles = vehicle_by_id or {}
    samples: Dict[str, List[Dict[str, Any]]] = {}
    states: Dict[str, _AircraftState] = {}
    stats = AcmiSampleStats()
    cur_t = 0.0
    no_speed = 0
    no_attitude = 0
    total = 0

    for raw in _read_lines(path):
        line = raw.strip()
        if not line:
            continue
        first = line[0]
        if first == "#":
            stamp = _to_float(line[1:])
            if stamp is not None:
                cur_t = stamp
            continue
        if line.startswith("0,"):
            continue
        oid, sep, rest = line.partition(",")
        if not sep or oid not in wanted:
            continue
        if "Name=" in rest:
            # 中途的重新声明：只更新展示名，不产生样本
            kv = _split_pairs(rest)
            name = _to_text(kv.get("Name"))
            if name:
                vehicles[oid] = name
            continue

        state = states.get(oid)
        if state is None:
            state = _AircraftState()
            states[oid] = state

        kv: Optional[Dict[str, str]] = None
        t_value = None
        if rest.startswith("T="):
            t_value = rest[2:].split(",", 1)[0]
        else:
            marker = rest.find(",T=")
            if marker >= 0:
                t_value = rest[marker + 3:].split(",", 1)[0]
        if t_value is not None:
            comps = t_value.split("|")
            if len(comps) >= 3:
                alt = _to_float(comps[2])
                if alt is not None:
                    state.alt = alt
                    state.has_pos = True
                if len(comps) >= 6:
                    roll = _to_float(comps[3])
                    pitch = _to_float(comps[4])
                    yaw = _to_float(comps[5])
                    if roll is not None:
                        state.roll = roll
                    if pitch is not None:
                        state.pitch = pitch
                    if yaw is not None:
                        state.yaw = yaw
            kv = _split_pairs(rest)
        elif ("HDG=" in rest) or ("TAS=" in rest):
            kv = _split_pairs(rest)

        if kv:
            for key in _UPDATE_KEYS:
                if key not in kv:
                    continue
                value = _to_float(kv[key])
                if value is None:
                    continue
                if key == "HDG":
                    state.hdg = value
                elif key == "TAS":
                    state.tas = value
                elif key == "IAS":
                    state.ias = value
                elif key == "Mach":
                    state.mach = value
                elif key == "AOA":
                    state.aoa = value
                elif key == "VerticalGForce":
                    state.g = value

        if t_value is None or not state.has_pos:
            # 没有位置更新的行（雷达/挂载/武器状态等）不产生样本
            continue

        t = round(cur_t, 3)
        sample = _make_sample(t, state, vehicles.get(oid))
        if sample["ias"] is None and sample["tas"] is None:
            no_speed += 1
        if sample["pitch"] is None and sample["bank"] is None:
            no_attitude += 1
        bucket = samples.setdefault(oid, [])
        if bucket and bucket[-1]["t"] == t:
            bucket[-1] = sample  # 同一帧里的重复更新：后到的覆盖
        else:
            bucket.append(sample)
        total += 1
        if stats.t_min is None or t < stats.t_min:
            stats.t_min = t
        if stats.t_max is None or t > stats.t_max:
            stats.t_max = t

    stats.samples = total
    stats.objects_used = len(samples)
    stats.no_speed = no_speed
    stats.no_attitude = no_attitude
    stats.objects_without_samples = sorted(wanted - set(samples))
    stats.elapsed_s = round(time.time() - started, 3)
    return samples, stats


def _make_sample(t: float, state: _AircraftState, vehicle: Optional[str]) -> Dict[str, Any]:
    """把对象状态映射成 :mod:`flightlog.parse` 的样本结构（速度 m/s → km/h）。"""
    return {
        "t": t,
        "utc": None,
        "valid": True,
        "inflight": True,
        "vehicle": vehicle,
        "ias": None if state.ias is None else state.ias * 3.6,
        "tas": None if state.tas is None else state.tas * 3.6,
        "mach": state.mach,
        "alt": state.alt,
        "ralt": None,
        "vy": None,
        "aoa": state.aoa,
        "ny": state.g,
        "pitch": state.pitch,
        "bank": state.roll,
        "aos_deg": None,
        "roll_rate_dps": None,
        "hdg_deg": state.hdg if state.hdg is not None else state.yaw,
        "gear_pct": None,
        "flaps_pct": None,
        "airbrake_pct": None,
        "fuel_kg": None,
        "fuel0_kg": None,
        "has_hdg": state.hdg is not None or state.yaw is not None,
        "att_src": "replay-acmi",
        "att_conf": None,
    }


def _fill_vy(samples: Sequence[Dict[str, Any]]) -> None:
    """按中心差分补 ``vy``（m/s）：跨缺口/跨架次/跳变过大时留空。"""
    count = len(samples)
    for index in range(1, count - 1):
        prev = samples[index - 1]
        nxt = samples[index + 1]
        alt_prev = prev.get("alt")
        alt_next = nxt.get("alt")
        if alt_prev is None or alt_next is None:
            continue
        dt = nxt["t"] - prev["t"]
        if dt < _VY_MIN_DT_S or dt > _VY_MAX_DT_S:
            continue
        if prev.get("vehicle") != samples[index].get("vehicle"):
            continue
        vy = (alt_next - alt_prev) / dt
        if abs(vy) > _VY_MAX_ABS_MS:
            continue
        samples[index]["vy"] = vy


#: 单帧跳变过滤：(字段, 绝对阈值)。回放的速度/过载是从位置反演出来的，
#: 偶尔会出现一两帧物理上不可能的尖峰（例如 AH-1W 蹦到 1400 km/h、68 g），
#: 这里把「相对相邻样本中位数跳得离谱」的孤立值置空，并在报告里明示。
_GLITCH_KEYS = (
    ("ias", 150.0),
    ("tas", 150.0),
    ("mach", 0.12),
    ("ny", 4.0),
    ("aoa", 25.0),
)
_GLITCH_WINDOW = 7
_GLITCH_PASSES = 2


def _deglitch(samples: List[Dict[str, Any]]) -> int:
    """把相邻帧剧烈跳变的孤立速度/过载/AOA 值置空，返回置空的数值个数。"""
    count = len(samples)
    if count < 5:
        return 0
    dropped = 0
    for _pass in range(_GLITCH_PASSES):
        for key, min_abs in _GLITCH_KEYS:
            values = [sample.get(key) for sample in samples]
            flags = [False] * count
            for index in range(count):
                value = values[index]
                if value is None:
                    continue
                neighbours = [
                    values[j]
                    for j in range(
                        max(0, index - _GLITCH_WINDOW), min(count, index + _GLITCH_WINDOW + 1)
                    )
                    if j != index and values[j] is not None
                ]
                if len(neighbours) < 3:
                    continue
                neighbours.sort()
                median = neighbours[len(neighbours) // 2]
                if abs(value - median) > max(min_abs, 0.5 * abs(median)):
                    flags[index] = True
            for index, flag in enumerate(flags):
                if flag:
                    samples[index][key] = None
                    dropped += 1
    return dropped


# ---------------------------------------------------------------------------
# 模型构建
# ---------------------------------------------------------------------------


def build_model(
    scan_result: AcmiScan,
    players: Sequence[AcmiPlayer],
    *,
    acmi_path: Optional[str] = None,
    wrpl_path: Optional[str] = None,
    gap_split_s: float = DEFAULT_GAP_SPLIT_S,
    convert_s: float = 0.0,
    converter: Optional[str] = None,
    converted: bool = True,
) -> LogModel:
    """把所选玩家的 ACMI 样本构建成报告模型（与 :func:`flightlog.parse.analyze` 同结构）。

    ``convert_s`` 为调用 ``wrpl-acmi.exe`` 的耗时（秒），只用于报告里的信息。
    """
    if not players:
        raise AcmiError("没有选择玩家")

    path = acmi_path or scan_result.path
    vehicle_by_id: Dict[str, str] = {}
    for obj in scan_result.objects.values():
        if obj.name:
            vehicle_by_id[obj.oid] = obj.name

    all_object_ids: List[str] = []
    for player in players:
        all_object_ids.extend(player.object_ids)
    object_samples, sample_stats = extract(path, all_object_ids, vehicle_by_id)

    flights: List[Flight] = []
    warnings: List[str] = []
    empty_players: List[str] = []
    missing_speed = 0
    missing_att = 0
    glitch_count = 0
    total_samples = 0

    # 每个 ACMI 对象 = 一次出击（玩家死亡后重新出生会换新对象 id），
    # 所以按对象切架次，而不是把整场拼成一个"架次"。
    for player in players:
        groups: List[Tuple[float, str, List[Dict[str, Any]]]] = []
        for oid in player.object_ids:
            bucket = object_samples.get(oid) or []
            if bucket:
                groups.append((bucket[0]["t"], oid, bucket))
        if not groups:
            empty_players.append(player.name)
            continue
        groups.sort(key=lambda item: item[0])
        for _start_t, _oid, bucket in groups:
            bucket.sort(key=lambda item: item["t"])
            deduped: List[Dict[str, Any]] = []
            for sample in bucket:
                if deduped and sample["t"] == deduped[-1]["t"]:
                    deduped[-1] = sample
                else:
                    deduped.append(sample)
            for sample in deduped:
                if sample["ias"] is None and sample["tas"] is None:
                    missing_speed += 1
                if sample["pitch"] is None and sample["bank"] is None:
                    missing_att += 1
            glitch_count += _deglitch(deduped)
            _fill_vy(deduped)
            total_samples += len(deduped)

            object_vehicle = next((s.get("vehicle") for s in deduped if s.get("vehicle")), None)
            object_flights = build_flights(
                deduped, [], meta_vehicle=object_vehicle or player.name, gap_split_s=gap_split_s
            )
            for flight in object_flights:
                if flight.reason == "log_start":
                    flight.reason = "spawn"
                flight.index = len(flights) + 1
                flight.player = player.name
            flights.extend(object_flights)

    if not flights:
        raise AcmiError(
            "所选玩家在回放里没有可用的飞机位置样本"
            "（回放可能没有导出空中单位数据，或所选对象全程未移动）"
        )
    if empty_players:
        warnings.append("以下玩家没有可用样本，已跳过：%s" % "、".join(empty_players))

    for flight in flights:
        flight.series = build_series(flight)
        flight.stats = compute_flight_stats(flight)

    rate = sample_stats.rate_hz
    warnings.append(
        "回放估算数据：IAS = EAS 近似，姿态/过载/AOA 由回放位置反演，不是座舱遥测"
    )
    if rate:
        warnings.append("ACMI 采样约 %.1f Hz；vy 由高度中心差分得到" % rate)
    else:
        warnings.append("ACMI 采样率未知；vy 由高度中心差分得到")
    warnings.append("回放不含击杀/复活事件，架次按「一个 ACMI 飞机对象 = 一次出击」切分")
    warnings.append("回放样本一律按「空中」处理（无法区分停机/滑行）")
    if glitch_count:
        warnings.append(
            "剔除 %d 个单帧跳变的速度/过载/AOA 估计（回放反演伪影，已置空）" % glitch_count
        )
    if missing_speed:
        warnings.append(
            "%d / %d 个样本缺少 IAS/TAS（该回放未提供速度估算字段），速度页对应区间会断开"
            % (missing_speed, total_samples)
        )
    if missing_att and missing_att < total_samples:
        warnings.append("%d / %d 个样本缺少姿态（roll/pitch）" % (missing_att, total_samples))

    vehicle = None
    for player in players:
        vehicle = next((v for v in player.vehicles if v), None)
        if vehicle:
            break
    players_label = "、".join(p.name for p in players)
    meta: Dict[str, Any] = {
        "schema": "ACMI %s" % (scan_result.header.file_version or "2.1"),
        "app": "wrpl-acmi" + ((" · " + scan_result.header.exporter) if scan_result.header.exporter else ""),
        "utc": scan_result.header.reference_time,
        "vehicle": vehicle if len(players) == 1 else "%d 名玩家" % len(players),
        "rate": ("≈%.1f Hz" % rate) if rate else "回放",
        "source": "replay",
        "replay": {
            "estimated": True,
            "wrpl_file": os.path.basename(wrpl_path) if wrpl_path else None,
            "wrpl_path": os.path.abspath(wrpl_path) if wrpl_path else None,
            "acmi_file": os.path.basename(path),
            "acmi_path": os.path.abspath(path),
            "converter": converter,
            "converted": converted,
            "convert_s": round(convert_s, 3),
            "scan_s": scan_result.scan_s,
            "extract_s": sample_stats.elapsed_s,
            "selected_players": players_label,
            "multi_player": len(players) > 1,
            "all_players": len(scan_result.players),
            "header": scan_result.header.as_dict(),
            "players": [p.as_dict() for p in players],
            "samples": sample_stats.samples,
            "objects_used": sample_stats.objects_used,
            "duration_s": sample_stats.duration_s,
            "no_speed_samples": missing_speed,
            "glitch_samples": glitch_count,
            "warnings": list(scan_result.warnings),
        },
    }
    counters = {
        "lines": scan_result.lines,
        "samples": total_samples,
        "events": 0,
        "meta": 0,
        "bad_json": 0,
        "bad_record": 0,
        "bad_sample": 0,
        "skipped_event": 0,
        "unknown_type": 0,
    }
    model = LogModel(
        # 报告里显示的"文件"用 .wrpl（回放）而不是临时 ACMI
        path=wrpl_path or path,
        meta=meta,
        flights=flights,
        events=[],
        counters=counters,
        warnings=warnings,
    )
    model.summary = build_summary(model)
    return model
