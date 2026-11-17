# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

"""FlightLog 独立启动器：双击选日志（.jsonl）或回放（.wrpl）→ 生成 HTML → 浏览器打开。

* ``.jsonl`` —— WtHud 自己录的飞行日志，直接出报告；
* ``.wrpl`` —— War Thunder 回放，自动调用同目录 ``wrpl-acmi.exe`` 转 ACMI，
  再弹出**玩家多选**对话框（只选一次，取消则退出）；
* ``.acmi`` —— 已转好的 ACMI，跳过转换直接选玩家。

打包：见 build_exe.bat（PyInstaller --onefile --noconsole），记得把
``wrpl-acmi.exe`` 一起放到 ``dist\\``。
也可以直接用 Python 跑：``python launch.py <log.jsonl|replay.wrpl> [--player NAME]``
"""

from __future__ import annotations

import os
import re
import sys
import time
import traceback
from pathlib import Path

HERE = Path(__file__).resolve().parent
# 包目录的上一级（tools/）才是 import flightlog 的根
for p in (str(HERE.parent), str(HERE)):
    if p not in sys.path:
        sys.path.insert(0, p)

APP_TITLE = "WtHud 飞行日志解析"


def _default_dir() -> Path:
    """优先桌面发布目录的 recordings，其次 exe 旁边的 recordings。"""
    candidates = [
        Path(os.path.expanduser("~")) / "Desktop" / "WtHud-发布" / "recordings",
        Path(os.path.dirname(sys.executable)) / "recordings",
        Path.cwd() / "recordings",
    ]
    for c in candidates:
        try:
            if c.is_dir():
                return c
        except OSError:
            pass
    return Path.home()


def _replay_dir() -> Path:
    """回放默认目录：优先 War Thunder 安装目录下的 Replays。"""
    candidates = [
        Path(os.path.dirname(sys.executable)) / "Replays",
        Path("C:/Program Files (x86)/Steam/steamapps/common/War Thunder/Replays"),
        Path("D:/Program Files (x86)/Steam/steamapps/common/War Thunder/Replays"),
        Path.home() / "Documents" / "My Games" / "WarThunder" / "Replays",
    ]
    for c in candidates:
        try:
            if c.is_dir():
                return c
        except OSError:
            pass
    return _default_dir()


def _pick_log() -> Path | None:
    try:
        import tkinter as tk
        from tkinter import filedialog
    except Exception:
        return None
    root = tk.Tk()
    root.withdraw()
    root.title(APP_TITLE)
    path = filedialog.askopenfilename(
        title="选择飞行日志（.jsonl）或 War Thunder 回放（.wrpl）",
        initialdir=str(_replay_dir()),
        filetypes=[
            ("飞行日志 / 回放", "*.jsonl *.wrpl *.acmi"),
            ("WtHud 飞行日志", "*.jsonl"),
            ("War Thunder 回放", "*.wrpl"),
            ("ACMI (Tacview)", "*.acmi"),
            ("所有文件", "*.*"),
        ],
    )
    root.destroy()
    return Path(path) if path else None


def _out_path(source: Path, suffix: str = "") -> Path:
    base = Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "WtHud" / "flightlog"
    base.mkdir(parents=True, exist_ok=True)
    stamp = __import__("datetime").datetime.now().strftime("%Y%m%d-%H%M%S")
    tail = ("-" + suffix) if suffix else ""
    stem = re.sub(r"[^0-9A-Za-z\u4e00-\u9fff._-]+", "-", source.stem or "").strip("-._") or "report"
    return base / f"{stem}{tail}-{stamp}.html"


def _parse_argv(argv: list[str]) -> tuple[Path | None, list[str]]:
    """极简参数解析：第一个非选项是路径，``--player NAME`` 可重复。"""
    path: Path | None = None
    players: list[str] = []
    index = 1
    while index < len(argv):
        token = argv[index]
        if token in ("--player", "-p"):
            if index + 1 < len(argv):
                players.extend(
                    part.strip() for part in argv[index + 1].replace("，", ",").split(",") if part.strip()
                )
                index += 2
                continue
        elif token.startswith("--player="):
            players.extend(
                part.strip() for part in token.split("=", 1)[1].replace("，", ",").split(",") if part.strip()
            )
            index += 1
            continue
        elif not token.startswith("-") and path is None:
            path = Path(token)
        index += 1
    return path, players


def main(argv: list[str]) -> int:
    path, players = _parse_argv(argv)
    source = path if path is not None else _pick_log()
    if source is None:
        return 0
    if not source.is_file():
        _error(f"找不到文件：\n{source}")
        return 2

    from flightlog.replay import describe_source

    kind = describe_source(str(source))
    started = time.time()
    try:
        if kind == "jsonl":
            out = _run_jsonl(source)
        else:
            out = _run_replay(source, players, kind)
            if out is None:
                return 0
    except Exception as exc:  # noqa: BLE001 - 双击运行时必须把错误弹出来
        _error(f"处理失败：{exc}\n\n{traceback.format_exc(limit=2)}")
        return 1

    if os.environ.get("FLIGHTLOG_NO_BROWSER") != "1":
        try:
            import webbrowser

            webbrowser.open(out.as_uri())
        except Exception:
            pass
    _log("%s" % out)
    _log("总耗时 %.1f s" % (time.time() - started))
    return 0


def _run_jsonl(source: Path) -> Path:
    from flightlog import analyze, write_report

    _log("读取飞行日志: %s" % source.name)
    result = analyze(str(source))
    out = _out_path(source)
    write_report(result, str(out))
    _log("架次 %d | 样本 %d" % (len(result.flights), result.summary["counts"]["samples"]))
    return out


def _run_replay(source: Path, players: list[str], kind: str) -> Path | None:
    """回放导入：转换 → 玩家列表 → Tk 多选 → 报告。返回 None 表示用户取消。"""
    from flightlog import acmi, picker
    from flightlog.replay import ReplaySession, open_acmi, open_replay

    _log("导入回放: %s" % source.name)
    session: ReplaySession
    try:
        if kind == "acmi":
            session = open_acmi(str(source))
        else:
            session = open_replay(str(source))
    except Exception as exc:  # noqa: BLE001
        _error(str(exc))
        return None

    _log(
        "转换 %.2f s · 扫描 %.2f s · 飞机对象 %d · 玩家 %d"
        % (session.convert_s, session.scan_result.scan_s, session.scan_result.air_object_count, len(session.players))
    )

    try:
        if players:
            chosen = acmi.resolve_players(session.players, players)
        else:
            picked = picker.choose_players_tk(
                session.players,
                title="选择玩家（回放）",
                subtitle="%s · %s" % (source.name, _scene_title(session)),
            )
            if not picked:
                _log("已取消")
                return None
            chosen = [session.players[index - 1] for index in picked]
        _log("选择玩家: %s" % "、".join(p.name for p in chosen))
        model = session.model_for(players=chosen)
    except Exception as exc:  # noqa: BLE001
        _error(str(exc))
        return None
    finally:
        session.cleanup(3)

    out = _out_path(source)
    from flightlog import write_report

    write_report(model, str(out))
    totals = model.summary["totals"]
    _log(
        "架次 %d | 样本 %d | 时长 %.0f s | 最大 IAS %.0f km/h"
        % (
            len(model.flights),
            model.summary["counts"]["samples"],
            totals["duration_s"] or 0.0,
            totals["max_ias_kmh"] or 0.0,
        )
    )
    return out


def _scene_title(session) -> str:
    header = getattr(session, "scan_result", None)
    title = getattr(getattr(header, "header", None), "title", None)
    return title or "War Thunder 回放"


def _log(message: str) -> None:
    """打包成窗口程序后 stdout 可能是 None，安全输出。"""
    stream = sys.stdout
    if stream is None:
        return
    try:
        print(message)
        stream.flush()
    except Exception:
        pass


def _error(msg: str) -> None:
    try:
        import tkinter as tk
        from tkinter import messagebox

        root = tk.Tk()
        root.withdraw()
        messagebox.showerror(APP_TITLE, msg)
        root.destroy()
    except Exception:
        print(msg, file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
