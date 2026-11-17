# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

"""FlightLog 独立启动器：双击选日志 → 生成 HTML → 浏览器打开。

打包：见 build_exe.bat（PyInstaller --onefile --noconsole）。
也可以直接用 Python 跑：python launch.py <log.jsonl>
"""

from __future__ import annotations

import os
import sys
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
        title="选择飞行日志（.jsonl）",
        initialdir=str(_default_dir()),
        filetypes=[("WtHud 飞行日志", "*.jsonl"), ("所有文件", "*.*")],
    )
    root.destroy()
    return Path(path) if path else None


def _out_path(log: Path) -> Path:
    base = Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "WtHud" / "flightlog"
    base.mkdir(parents=True, exist_ok=True)
    stamp = __import__("datetime").datetime.now().strftime("%Y%m%d-%H%M%S")
    return base / f"{log.stem}-{stamp}.html"


def main(argv: list[str]) -> int:
    log = Path(argv[1]) if len(argv) > 1 else _pick_log()
    if log is None:
        return 0
    if not log.is_file():
        _error(f"找不到日志文件：\n{log}")
        return 2

    try:
        from flightlog import analyze, write_report

        result = analyze(str(log))
        out = _out_path(log)
        write_report(result, str(out))
    except Exception as exc:  # noqa: BLE001 - 双击运行时必须把错误弹出来
        _error(f"解析失败：{exc}\n\n{traceback.format_exc(limit=2)}")
        return 1

    if os.environ.get("FLIGHTLOG_NO_BROWSER") != "1":
        try:
            import webbrowser
            webbrowser.open(out.as_uri())
        except Exception:
            pass
    print(out)
    return 0


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
