#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
# This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.
"""玩家列表展示 + 选择（命令行表格 / 交互输入 / Tk 多选对话框）。

表格里给的是「玩家、机型、阵营、对象数、样本数、时长」；真人玩家（有 ``Pilot``）
排在前面，没有飞行员的 AI/无人对象排在后面（名字形如 ``A-10A #33``）。
"""

from __future__ import annotations

import sys
import unicodedata
from typing import Any, Dict, List, Optional, Sequence

from .acmi import AcmiPlayer, player_rows

__all__ = [
    "build_picker_ui",
    "choose_players_tk",
    "columns",
    "describe_players",
    "format_player_table",
    "parse_selection",
    "prompt_players",
]

#: 表头与列宽（按显示宽度算，中文按 2 列）
COLUMNS = (
    ("index", "#", 3),
    ("name", "玩家", 24),
    ("vehicles", "机型", 18),
    ("coalition", "阵营", 6),
    ("objects", "对象", 5),
    ("updates", "样本", 7),
    ("duration", "时长", 7),
)


def _display_width(text: str) -> int:
    width = 0
    for ch in text:
        width += 2 if unicodedata.east_asian_width(ch) in ("W", "F") else 1
    return width


def _pad(text: str, width: int) -> str:
    text = "" if text is None else str(text)
    fill = max(0, width - _display_width(text))
    return text + " " * fill


def columns(players: Sequence[AcmiPlayer]) -> List[Dict[str, Any]]:
    """玩家列表的表格行（与 :func:`acmi.player_rows` 同一份数据，便于测试）。"""
    return player_rows(players)


def describe_players(players: Sequence[AcmiPlayer]) -> List[Dict[str, Any]]:
    """给对话框/表格用的玩家描述（含序号、机型、阵营、对象、样本、时长）。"""
    return columns(players)


def format_player_table(players: Sequence[AcmiPlayer], indent: str = "  ") -> str:
    """把玩家列表排成等宽表格（中文按 2 列宽对齐）。"""
    rows = describe_players(players)
    if not rows:
        return indent + "（没有玩家）"
    lines: List[str] = []
    header = indent + "  ".join(_pad(title, width) for _, title, width in COLUMNS)
    lines.append(header.rstrip())
    lines.append(indent + "  ".join("-" * width for _, _, width in COLUMNS))
    for row in rows:
        values = []
        for key, _title, width in COLUMNS:
            values.append(_pad(row.get(key, ""), width))
        lines.append((indent + "  ".join(values)).rstrip())
    return "\n".join(lines)


def parse_selection(text: str, count: int) -> List[int]:
    """解析 ``1,3-5`` / ``all`` / 空 之类的选择文本，返回 1 起的序号列表。

    空文本返回 ``[]``；非法片段直接忽略；自动去重并保持输入顺序。
    """
    text = (text or "").strip().lower()
    if not text:
        return []
    if text in ("a", "all", "*"):
        return list(range(1, count + 1))
    picked: List[int] = []
    seen = set()

    def _add(value: int) -> None:
        if 1 <= value <= count and value not in seen:
            seen.add(value)
            picked.append(value)

    for chunk in text.replace("，", ",").replace(" ", ",").split(","):
        chunk = chunk.strip()
        if not chunk:
            continue
        if "-" in chunk[1:]:
            start_text, _, end_text = chunk.partition("-")
            try:
                start = int(start_text)
                end = int(end_text)
            except ValueError:
                continue
            step = 1 if end >= start else -1
            for value in range(start, end + step, step):
                _add(value)
            continue
        try:
            _add(int(chunk))
        except ValueError:
            continue
    return picked


def prompt_players(
    players: Sequence[AcmiPlayer],
    stdin=None,
    stdout=None,
) -> Optional[List[int]]:
    """命令行交互：打印表格 → 让用户输入序号（回车默认第 1 名，q 退出）。

    返回 1 起的序号列表；用户退出返回 ``None``。
    """
    stdin = stdin or sys.stdin
    stdout = stdout or sys.stdout
    count = len(players)
    if count == 0:
        return []
    stdout.write("\n玩家列表（按样本数排序，真人玩家在前）：\n")
    stdout.write(format_player_table(players) + "\n")
    while True:
        stdout.write(
            "请选择玩家（序号；逗号/范围多选，如 1,3-5；回车 = 1；q = 退出）: "
        )
        stdout.flush()
        line = stdin.readline()
        if not line:
            return [1]
        text = line.strip()
        if text.lower() in ("q", "quit", "exit"):
            return None
        if not text:
            return [1]
        picked = parse_selection(text, count)
        if picked:
            return picked
        stdout.write("  无效选择，请重试（1–%d）。\n" % count)


# ---------------------------------------------------------------------------
# Tk 多选对话框（launch.py 双击时用；导入失败时由调用方弹错误框）
# ---------------------------------------------------------------------------


def build_picker_ui(
    root,
    players: Sequence[AcmiPlayer],
    *,
    subtitle: str = "",
    on_confirm=None,
    on_cancel=None,
) -> Dict[str, Any]:
    """在给定的 Tk 根窗口里搭好玩家多选界面（**不进入 mainloop**，便于自检）。

    返回 ``{"tree", "rows", "frame_columns", "headings", "default_index", "ok_button"}``。
    """
    import tkinter as tk
    from tkinter import ttk

    rows = describe_players(players)
    header = tk.Frame(root, padx=14, pady=10)
    header.pack(fill="x")
    tk.Label(
        header,
        text="回放里的玩家（可多选；真人玩家在前，无飞行员的 AI 对象在后）",
        anchor="w",
        font=("Microsoft YaHei UI", 11, "bold") if _has_yahei() else None,
    ).pack(fill="x")
    if subtitle:
        tk.Label(header, text=subtitle, anchor="w", fg="#555555").pack(fill="x", pady=(3, 0))

    body = tk.Frame(root, padx=14)
    body.pack(fill="both", expand=True)
    tree_frame = tk.Frame(body)
    tree_frame.pack(fill="both", expand=True)

    frame_columns = ("index", "name", "vehicles", "coalition", "objects", "updates", "duration")
    tree = ttk.Treeview(
        tree_frame, columns=frame_columns, show="headings", selectmode="extended"
    )
    headings = {
        "index": "#",
        "name": "玩家",
        "vehicles": "机型",
        "coalition": "阵营",
        "objects": "对象",
        "updates": "样本",
        "duration": "时长",
    }
    widths = {
        "index": 46,
        "name": 240,
        "vehicles": 200,
        "coalition": 60,
        "objects": 60,
        "updates": 80,
        "duration": 74,
    }
    for key in frame_columns:
        tree.heading(key, text=headings[key])
        tree.column(key, width=widths[key], anchor="w" if key in ("name", "vehicles") else "center")
    scroll = ttk.Scrollbar(tree_frame, orient="vertical", command=tree.yview)
    tree.configure(yscrollcommand=scroll.set)
    tree.pack(side="left", fill="both", expand=True)
    scroll.pack(side="right", fill="y")

    for row in rows:
        tree.insert(
            "",
            "end",
            iid=str(row["index"]),
            values=(
                row["index"],
                row["name"],
                row["vehicles"],
                row["coalition"],
                row["objects"],
                row["updates"],
                row["duration"],
            ),
        )
    # 默认选中第一位真人玩家
    default_index = next((row["index"] for row in rows if row["has_pilot"]), 1)
    tree.selection_set(str(default_index))
    tree.focus(str(default_index))
    tree.see(str(default_index))

    buttons = tk.Frame(root, padx=14, pady=12)
    buttons.pack(fill="x")

    def select_all() -> None:
        tree.selection_set([str(row["index"]) for row in rows])

    def clear_all() -> None:
        tree.selection_remove(tree.selection())

    def confirm() -> None:
        if on_confirm is not None:
            on_confirm()
            return
        root.destroy()

    def cancel() -> None:
        if on_cancel is not None:
            on_cancel()
            return
        root.destroy()

    tk.Button(buttons, text="全选", width=8, command=select_all).pack(side="left")
    tk.Button(buttons, text="清空", width=8, command=clear_all).pack(side="left", padx=(8, 0))
    tk.Button(buttons, text="取消", width=10, command=cancel).pack(side="right")
    ok_button = tk.Button(buttons, text="生成报告", width=12, command=confirm)
    ok_button.pack(side="right", padx=(0, 8))
    root.bind("<Return>", lambda _event: confirm())
    root.bind("<Escape>", lambda _event: cancel())

    return {
        "tree": tree,
        "rows": rows,
        "frame_columns": frame_columns,
        "headings": headings,
        "default_index": default_index,
        "ok_button": ok_button,
        "header": header,
        "buttons": buttons,
    }


def choose_players_tk(
    players: Sequence[AcmiPlayer],
    *,
    title: str = "选择要生成报告的玩家",
    subtitle: str = "",
) -> Optional[List[int]]:
    """弹出 Tk 多选对话框，返回 1 起的序号列表；取消/关窗返回 ``None``。"""
    if not players:
        return []
    try:
        import tkinter as tk
        from tkinter import ttk
    except Exception:  # pragma: no cover - 没有 Tk 时退回命令行
        return prompt_players(players)

    result: Dict[str, Optional[List[int]]] = {"value": None}

    root = tk.Tk()
    root.title(title)
    root.geometry("820x560")
    root.minsize(640, 380)
    try:
        root.attributes("-topmost", True)
    except Exception:
        pass

    try:
        style = ttk.Style(root)
        style.theme_use("vista")
    except Exception:
        pass

    def on_confirm() -> None:
        picked = sorted(int(iid) for iid in ui["tree"].selection())
        if not picked:
            _warn(root, "还没有选择玩家。\n请至少选中一名玩家，或点「取消」。")
            return
        result["value"] = picked
        root.destroy()

    def on_cancel() -> None:
        result["value"] = None
        root.destroy()

    ui = build_picker_ui(root, players, subtitle=subtitle, on_confirm=on_confirm, on_cancel=on_cancel)

    _center(root)
    try:
        root.lift()
        ui["ok_button"].focus_set()
    except Exception:
        pass
    root.mainloop()
    return result["value"]


def _has_yahei() -> bool:
    try:
        import tkinter.font as tkfont

        families = set(tkfont.families())
    except Exception:  # pragma: no cover
        return False
    return "Microsoft YaHei UI" in families or "Microsoft YaHei" in families


def _center(root) -> None:
    try:
        root.update_idletasks()
        width = root.winfo_width()
        height = root.winfo_height()
        screen_w = root.winfo_screenwidth()
        screen_h = root.winfo_screenheight()
        root.geometry(
            "%dx%d+%d+%d"
            % (width, height, max(0, (screen_w - width) // 2), max(0, (screen_h - height) // 3))
        )
    except Exception:
        pass


def _warn(root, message: str) -> None:
    try:
        from tkinter import messagebox

        messagebox.showwarning("选择玩家", message, parent=root)
    except Exception:  # pragma: no cover
        print(message, file=sys.stderr)
