#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""命令行入口：``python -m flightlog <log.jsonl> [选项]``。

示例::

    # 生成 HTML（默认写到本目录 out/<日志名>.html）
    python -m flightlog ..\\..\\recordings\\wtlog-20261117-131039-jh_7a.jsonl

    # 指定输出 + 同时出 PNG 预览
    python -m flightlog wtlog-xxx.jsonl --out out/demo.html --png out/preview.png

    # 只打印统计 JSON（不写 HTML）
    python -m flightlog wtlog-xxx.jsonl --summary
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import List, Optional

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
        description="WtHud 飞行日志（JSONL）解析与可视化：生成自包含单文件 HTML 报告。",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "能量定义: Es = alt_m + (tas_kmh/3.6)^2 / (2*9.80665)  # 米\n"
            "默认 HTML 输出目录: " + str(default_out_dir())
        ),
    )
    parser.add_argument("log", help="wtlog-*.jsonl 日志路径")
    parser.add_argument(
        "--out",
        help="HTML 输出路径（默认 <工具目录>/out/<日志名>.html）",
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


def main(argv: Optional[List[str]] = None) -> int:
    _utf8_stdout()
    args = build_parser().parse_args(argv)

    try:
        model = analyze(args.log, gap_split_s=args.gap)
    except LogError as exc:
        print("错误: %s" % exc, file=sys.stderr)
        return 2

    if args.summary:
        print(json.dumps(model.summary, ensure_ascii=False, indent=2))
        if args.out:
            out_path = write_report(model, args.out, title=args.title)
            print("\nHTML: %s" % out_path, file=sys.stderr)
        return 0

    if args.out:
        out_path = Path(args.out)
    else:
        stem = Path(args.log).stem
        out_path = default_out_dir() / (stem + ".html")
    written = write_report(model, str(out_path), title=args.title)
    size_kb = os.path.getsize(written) / 1024.0
    print("HTML: %s (%.1f KB)" % (written, size_kb))
    _print_summary_line(model)

    if args.png:
        try:
            from .preview_png import PreviewUnavailable, render_preview

            png_path = render_preview(model, args.png)
            print("PNG : %s" % png_path)
        except PreviewUnavailable as exc:
            print("跳过 PNG: %s" % exc, file=sys.stderr)
        except Exception as exc:  # pragma: no cover - 绘图失败不影响 HTML
            print("PNG 生成失败（已忽略）: %s" % exc, file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
