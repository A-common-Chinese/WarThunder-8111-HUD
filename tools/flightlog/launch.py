# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

"""FlightLog 独立启动器：双击后先选数据来源，再选文件 → 生成 HTML → 浏览器打开。

启动流程：
1. 小窗口二选一：
   * ``War Thunder 回放 (.wrpl)`` —— 自动调用同目录 ``wrpl-acmi.exe`` 转 ACMI，
     再弹出**玩家多选**对话框；
   * ``WtHud 飞行记录 (.jsonl)`` —— 直接读取 HUD 录制的飞行日志。
2. 文件对话框只在所选来源对应的目录里打开（回放目录 / recordings 目录），
   不再默认打开回放目录。
3. 生成报告后用默认浏览器打开。

``.acmi`` （已转好的 Tacview 文件）仍可直接拖到 exe 上或作为命令行参数使用。
打包：见 build_exe.bat（PyInstaller --onefile --noconsole），记得把
``wrpl-acmi.exe`` 一起放到 ``dist\\``。
命令行：``python launch.py <log.jsonl|replay.wrpl> [--player NAME]``
自动化测试可加 ``--mode jsonl|wrpl`` 跳过启动选择框。
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
MODE_WRPL = "wrpl"
MODE_JSONL = "jsonl"


def _exe_dir() -> Path:
    try:
        return Path(sys.executable).resolve().parent
    except OSError:
        return HERE


def _log_dir() -> Path:
    """WtHud 记录目录：优先 exe 旁边的 recordings，其次桌面发布目录。"""
    candidates = [
        _exe_dir() / "recordings",
        Path(os.path.expanduser("~")) / "Desktop" / "WtHud-发布" / "recordings",
        Path.cwd() / "recordings",
    ]
    for c in candidates:
        try:
            if c.is_dir():
                return c
        except OSError:
            pass
    return Path.home()


def _wt_replays_dir() -> Path:
    """War Thunder 回放目录：先查注册表安装路径，再查常见 Steam 路径，最后 Documents。"""
    candidates: list[Path] = []
    try:
        import winreg  # type: ignore

        for hive, sub in (
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        ):
            try:
                with winreg.OpenKey(hive, sub) as root:
                    for i in range(winreg.QueryInfoKey(root)[0]):
                        try:
                            name = winreg.EnumKey(root, i)
                            with winreg.OpenKey(root, name) as app:
                                display = str(winreg.QueryValueEx(app, "DisplayName")[0])
                                if "War Thunder" not in display:
                                    continue
                                location = str(winreg.QueryValueEx(app, "InstallLocation")[0]).strip()
                                if location:
                                    candidates.append(Path(location) / "Replays")
                        except OSError:
                            continue
            except OSError:
                continue
    except Exception:
        pass

    candidates += [
        Path("D:/Program Files (x86)/Steam/steamapps/common/War Thunder/Replays"),
        Path("C:/Program Files (x86)/Steam/steamapps/common/War Thunder/Replays"),
        Path("C:/Program Files/Steam/steamapps/common/War Thunder/Replays"),
        Path.home() / "Documents" / "My Games" / "WarThunder" / "Replays",
    ]
    for c in candidates:
        try:
            if c.is_dir():
                return c
        except OSError:
            pass
    return Path.home()


def _pick_kind() -> str | None:
    """启动选择框：返回 MODE_WRPL / MODE_JSONL / None（取消）。"""
    try:
        import tkinter as tk
        from tkinter import font as tkfont
    except Exception:
        return None

    chosen: list[str] = []
    root = tk.Tk()
    root.title(APP_TITLE)
    root.resizable(False, False)
    try:
        bg = root.cget("bg")
    except Exception:
        bg = None

    title_font = tkfont.Font(family="Microsoft YaHei UI", size=13, weight="bold")
    body_font = tkfont.Font(family="Microsoft YaHei UI", size=10)

    tk.Label(root, text="选择要打开的数据来源", font=title_font).pack(padx=28, pady=(22, 6))
    tk.Label(
        root,
        text="回放需要同目录的 wrpl-acmi.exe（随包提供）",
        font=body_font,
        fg="#888888",
    ).pack(padx=28, pady=(0, 14))

    def choose(mode: str) -> None:
        chosen.append(mode)
        root.destroy()

    row = tk.Frame(root)
    row.pack(padx=24, pady=(0, 8))
    tk.Button(
        row,
        text="War Thunder 回放（.wrpl）",
        font=body_font,
        width=24,
        height=2,
        command=lambda: choose(MODE_WRPL),
    ).pack(side="left", padx=6)
    tk.Button(
        row,
        text="WtHud 飞行记录（.jsonl）",
        font=body_font,
        width=24,
        height=2,
        command=lambda: choose(MODE_JSONL),
    ).pack(side="left", padx=6)

    tk.Button(root, text="取消", font=body_font, width=10, command=root.destroy).pack(pady=(4, 18))

    root.update_idletasks()
    width, height = root.winfo_width(), root.winfo_height()
    x = (root.winfo_screenwidth() - width) // 2
    y = (root.winfo_screenheight() - height) // 2
    root.geometry(f"+{max(0, x)}+{max(0, y)}")
    root.grab_set()
    root.mainloop()
    return chosen[0] if chosen else None


def _pick_log(kind: str) -> Path | None:
    try:
        import tkinter as tk
        from tkinter import filedialog
    except Exception:
        return None

    if kind == MODE_WRPL:
        title = "选择 War Thunder 回放（.wrpl / .acmi）"
        initial = _wt_replays_dir()
        types = [
            ("War Thunder 回放", "*.wrpl"),
            ("ACMI (Tacview)", "*.acmi"),
            ("所有文件", "*.*"),
        ]
    else:
        title = "选择 WtHud 飞行记录（.jsonl）"
        initial = _log_dir()
        types = [
            ("WtHud 飞行日志", "*.jsonl"),
            ("所有文件", "*.*"),
        ]

    root = tk.Tk()
    root.withdraw()
    root.title(APP_TITLE)
    path = filedialog.askopenfilename(
        title=title,
        initialdir=str(initial),
        filetypes=types,
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


def _parse_argv(argv: list[str]) -> tuple[Path | None, list[str], str | None]:
    """极简参数解析：第一个非选项是路径，``--player NAME`` 可重复，``--mode`` 跳过选择框。"""
    path: Path | None = None
    players: list[str] = []
    mode: str | None = None
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
        elif token in ("--mode", "--kind"):
            if index + 1 < len(argv):
                value = argv[index + 1].strip().lower()
                mode = MODE_WRPL if value in ("wrpl", "replay") else MODE_JSONL
                index += 2
                continue
        elif not token.startswith("-") and path is None:
            path = Path(token)
        index += 1
    return path, players, mode


def main(argv: list[str]) -> int:
    # ``--mfd-json`` 是 flightlog 包自己的 CLI 选项：直接转交给 __main__ 的实现，
    # 这样打包成 FlightLog.exe 后 ``FlightLog.exe --mfd-json ...`` 与
    # ``python -m flightlog --mfd-json ...`` 行为一致（HUD 侧只需调 exe）。
    cli_argv = argv[1:]
    if any(token == "--mfd-json" or token.startswith("--mfd-json=") for token in cli_argv):
        from flightlog.__main__ import main as flightlog_main

        return flightlog_main(cli_argv)

    path, players, mode = _parse_argv(argv)

    if path is not None:
        source = path
    else:
        kind = mode or _pick_kind()
        if kind is None:
            return 0
        source = _pick_log(kind)
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
