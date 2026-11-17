#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""命令行入口：``python -m flightlog <日志或回放> [选项]``。

支持两种输入：

* ``wtlog-*.jsonl`` —— WtHud 自己录的飞行日志（原来的主流程）；
* ``*.wrpl`` —— War Thunder 回放，自动调用同目录 ``wrpl-acmi.exe`` 转 ACMI；
* ``*.acmi`` —— 已经转好的 ACMI，跳过转换直接解析（调试/复用）。

示例::

    # JSONL：生成 HTML（默认写到本目录 out/<日志名>.html）
    python -m flightlog ..\\..\\recordings\\wtlog-20261117-131039-jh_7a.jsonl

    # 回放：先看有哪些玩家
    python -m flightlog "D:\\Replays\\xxx.wrpl" --list

    # 回放：给指定玩家出报告（可多人，逗号或重复 --player）
    python -m flightlog xxx.wrpl --player HCN_WT --player "送塟者" --out out/replay.html

    # 只打印统计 JSON（不写 HTML）
    python -m flightlog wtlog-xxx.jsonl --summary
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import List, Optional

from . import picker
from .parse import DEFAULT_GAP_SPLIT_S, LogError, analyze
from .report import write_report

_TOOL_DIR = Path(__file__).resolve().parent


def _utf8_stdout() -> None:
    """Windows GBK 控制台也能打印中文/中文路径。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[attr-defined]
        except Exception:
            pass


def default_out_dir() -> Path:
    return _TOOL_DIR / "out"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="python -m flightlog",
        description=(
            "WtHud 飞行日志 / 回放分析：解析 JSONL 飞行日志或 War Thunder .wrpl 回放，"
            "生成自包含单文件 HTML 报告。"
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "能量定义: Es = alt_m + (tas_kmh/3.6)^2 / (2*9.80665)  # 米\n"
            "回放数据说明: IAS = EAS 近似，姿态/过载由回放位置反演估算，非座舱遥测。\n"
            "默认 HTML 输出目录: " + str(default_out_dir())
        ),
    )
    parser.add_argument("log", help="wtlog-*.jsonl 日志、.wrpl 回放或 .acmi 文件路径")
    parser.add_argument(
        "--out",
        help="HTML 输出路径（默认 <工具目录>/out/<文件名>.html）",
        default=None,
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        help="只打印统计摘要 JSON（配合 --out 时同时写出 HTML）",
    )
    parser.add_argument("--png", default=None, help="额外输出 Pillow PNG 预览到该路径")
    parser.add_argument("--title", default=None, help="HTML 页面标题")
    parser.add_argument(
        "--gap",
        type=float,
        default=DEFAULT_GAP_SPLIT_S,
        help="相邻样本间隔超过多少秒就切新架次（默认 %(default)s）",
    )
    parser.add_argument(
        "--list",
        dest="list_players",
        action="store_true",
        help="只列出回放里的玩家（.wrpl / .acmi），不生成 HTML",
    )
    parser.add_argument(
        "--player",
        action="append",
        default=None,
        metavar="NAME",
        help="回放玩家名（可重复或用逗号分隔；也支持 1 起的序号）",
    )
    parser.add_argument(
        "--acmi",
        default=None,
        help="跳过转换，直接解析这个 .acmi（配合 .wrpl 名称用于报告元数据）",
    )
    parser.add_argument(
        "--wrpl-acmi",
        dest="wrpl_acmi",
        default=None,
        help="wrpl-acmi.exe 路径（默认在本工具目录 / FlightLog.exe 旁边查找）",
    )
    parser.add_argument(
        "--keep-acmi",
        dest="keep_acmi",
        type=int,
        default=3,
        metavar="N",
        help="%%TEMP%%\\WtHud\\flightlog 里保留最近几个转换产物（默认 %(default)s，0 = 用完即删）",
    )
    parser.add_argument(
        "--convert-timeout",
        dest="convert_timeout",
        type=float,
        default=900.0,
        metavar="SEC",
        help="单次 wrpl-acmi 转换超时秒数（默认 %(default)s）",
    )
    return parser


def _print_summary_line(model) -> None:
    summary = model.summary
    totals = summary["totals"]
    counts = summary["counts"]
    print(
        "架次 %d | 样本 %d | 事件 %d | 时长 %.1f s | 最大 IAS %.0f km/h | "
        "最大 G %.2f | 能量范围 %s–%s m"
        % (
            counts["flights"],
            counts["samples"],
            counts["events"],
            totals["duration_s"] or 0.0,
            totals["max_ias_kmh"] or 0.0,
            totals["max_ny_g"] or 0.0,
            "--" if totals["energy_min_m"] is None else "%.0f" % totals["energy_min_m"],
            "--" if totals["energy_max_m"] is None else "%.0f" % totals["energy_max_m"],
        )
    )


# ---------------------------------------------------------------------------
# 回放模式
# ---------------------------------------------------------------------------


def _split_players(values: Optional[List[str]]) -> List[str]:
    names: List[str] = []
    for value in values or []:
        names.extend(part.strip() for part in value.replace("，", ",").split(",") if part.strip())
    return names


def _print_replay_header(session, kind: str, stream=None) -> None:
    """打印回放/ACMI 概览；``--summary`` 时把这段改到 stderr，保证 stdout 只有 JSON。"""
    stream = stream or sys.stdout
    header = session.scan_result.header
    title = header.title or "（无标题）"
    if kind == "acmi":
        print("ACMI: %s" % Path(session.acmi_path).name, file=stream)
    else:
        print("回放: %s" % Path(session.wrpl_path).name, file=stream)
    print("  场景: %s" % title, file=stream)
    bits = []
    if header.reference_time:
        bits.append("参考时间 %s" % header.reference_time)
    if header.file_version:
        bits.append("ACMI %s" % header.file_version)
    if header.exporter:
        bits.append("导出器 %s" % header.exporter)
    if bits:
        print("  " + " · ".join(bits), file=stream)
    print(
        "  帧 %d · 对象 %d（飞机 %d）· 时长 %s"
        % (
            session.scan_result.frames,
            len(session.scan_result.objects),
            session.scan_result.air_object_count,
            _fmt_seconds(session.scan_result.duration_s),
        ),
        file=stream,
    )
    if session.converted:
        print(
            "  转换: %.2f s（%s）→ %.1f MB"
            % (
                session.convert_s,
                Path(session.converter or "wrpl-acmi.exe").name,
                os.path.getsize(session.acmi_path) / (1024.0 * 1024.0)
                if os.path.isfile(session.acmi_path)
                else 0.0,
            ),
            file=stream,
        )
    else:
        print("  转换: 复用已有 ACMI（0 s）", file=stream)
    print("  扫描: %.2f s" % session.scan_result.scan_s, file=stream)


def _fmt_seconds(value: Optional[float]) -> str:
    if value is None:
        return "--"
    total = int(round(value))
    return "%d:%02d" % (total // 60, total % 60)


def _safe_stem(stem: str) -> str:
    """默认输出文件名：把 ``#``、空格等换成 ``-``（回放名常带这些字符）。"""
    cleaned = re.sub(r"[^0-9A-Za-z\u4e00-\u9fff._-]+", "-", stem or "").strip("-._")
    return cleaned or "report"


def _print_players(session, stream=None) -> None:
    stream = stream or sys.stdout
    players = session.players
    real = sum(1 for player in players if player.has_pilot)
    print("\n玩家（共 %d 个，真人 %d 个；按样本数排序）：" % (len(players), real), file=stream)
    print(picker.format_player_table(players), file=stream)
    print(
        "提示: 用 --player NAME 选择（可多次 / 逗号分隔 / 也可用 1 起的序号）；"
        "无飞行员的 AI 对象名字带 “机型 #id”。",
        file=stream,
    )


def _replay_main(args) -> int:
    from .replay import ReplayError, describe_source, open_acmi, open_replay

    kind = describe_source(args.log)
    keep_acmi = max(0, int(args.keep_acmi))
    session = None
    try:
        if args.acmi:
            kind = "wrpl"
            session = open_replay(
                args.log,
                acmi_path=args.acmi,
                converter=args.wrpl_acmi,
                keep_acmi=keep_acmi,
                timeout_s=args.convert_timeout,
            )
        elif kind == "acmi":
            session = open_acmi(args.log)
        else:
            session = open_replay(
                args.log,
                converter=args.wrpl_acmi,
                keep_acmi=keep_acmi,
                timeout_s=args.convert_timeout,
            )

        info_stream = sys.stderr if args.summary else sys.stdout
        _print_replay_header(session, kind, stream=info_stream)
        if args.list_players:
            _print_players(session, stream=info_stream)
            return 0

        names = _split_players(args.player)
        if not names:
            if not sys.stdin.isatty():
                _print_players(session, stream=info_stream)
                print(
                    "\n错误: 非交互环境必须用 --player 指定玩家（例如 --player 1）。",
                    file=sys.stderr,
                )
                return 2
            picked = picker.prompt_players(session.players)
            if picked is None:
                print("已取消。", file=info_stream)
                return 0
            names = [session.players[index - 1].name for index in picked]

        from .acmi import resolve_players

        chosen = resolve_players(session.players, names)
        print("选择玩家: %s" % "、".join(player.name for player in chosen), file=info_stream)
        model = session.model_for(players=chosen, gap_split_s=args.gap)
        replay_meta = model.meta.get("replay") or {}
        print(
            "  解析: 扫描 %.2f s + 抽取 %.2f s · 架次 %d · 样本 %d"
            % (
                session.scan_result.scan_s,
                replay_meta.get("extract_s") or 0.0,
                len(model.flights),
                model.summary["counts"]["samples"],
            ),
            file=info_stream,
        )
    except ReplayError as exc:
        print("错误: %s" % exc, file=sys.stderr)
        return 2
    finally:
        if session is not None:
            session.cleanup(keep_acmi)

    return _finish_model(args, model, source_stem=Path(args.log).stem)


def _finish_model(args, model, source_stem: str) -> int:
    if args.summary:
        print(json.dumps(model.summary, ensure_ascii=False, indent=2))
        if args.out:
            out_path = write_report(model, args.out, title=args.title)
            print("\nHTML: %s" % out_path, file=sys.stderr)
        return 0

    if args.out:
        out_path = Path(args.out)
    else:
        out_path = default_out_dir() / (_safe_stem(source_stem) + ".html")
    written = write_report(model, str(out_path), title=args.title)
    size_kb = os.path.getsize(written) / 1024.0
    print("HTML: %s (%.1f KB)" % (written, size_kb))
    _print_summary_line(model)

    if args.png:
        _render_png(model, args.png)
    return 0


def _render_png(model, target: str) -> None:
    try:
        from .preview_png import PreviewUnavailable, render_preview

        png_path = render_preview(model, target)
        print("PNG : %s" % png_path)
    except PreviewUnavailable as exc:
        print("跳过 PNG: %s" % exc, file=sys.stderr)
    except Exception as exc:  # pragma: no cover - 绘图失败不影响 HTML
        print("PNG 生成失败（已忽略）: %s" % exc, file=sys.stderr)


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------


def main(argv: Optional[List[str]] = None) -> int:
    _utf8_stdout()
    args = build_parser().parse_args(argv)

    from .replay import describe_source

    kind = describe_source(args.log)
    if kind in ("wrpl", "acmi") or args.acmi or args.list_players or args.player:
        if kind == "jsonl" and (args.list_players or args.player):
            print(
                "错误: --list / --player 只对回放（.wrpl / .acmi）有效；"
                "JSONL 日志里没有玩家信息。",
                file=sys.stderr,
            )
            return 2
        return _replay_main(args)

    try:
        model = analyze(args.log, gap_split_s=args.gap)
    except LogError as exc:
        print("错误: %s" % exc, file=sys.stderr)
        return 2

    return _finish_model(args, model, source_stem=Path(args.log).stem)


if __name__ == "__main__":
    raise SystemExit(main())
