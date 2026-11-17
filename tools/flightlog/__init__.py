#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""WtHud 飞行日志解析 / 可视化工具（独立子工具，不属于 WtHud 产品代码）。

模块结构：

* :mod:`flightlog.parse`   —— JSONL 解析、架次切分、能量、统计与平均速度
* :mod:`flightlog.report`  —— 生成自包含单文件 HTML
* :mod:`flightlog.preview_png` —— 可选：用 Pillow 出 PNG 预览
* ``python -m flightlog``  —— 命令行入口（见 README.md）

典型用法::

    python -m flightlog wtlog-20261117-131039-jh_7a.jsonl --out out/report.html
    python -m flightlog wtlog-20261117-131039-jh_7a.jsonl --summary
"""

from .parse import (  # noqa: F401  (对外导出)
    DEFAULT_GAP_SPLIT_S,
    EVENT_LABELS,
    G,
    Event,
    Flight,
    LogError,
    LogModel,
    analyze,
    average_speed,
    build_flights,
    build_series,
    compute_flight_stats,
    energy_rate,
    load_log,
    parse_utc,
    specific_energy,
)
from .report import render_report, write_report  # noqa: F401

__version__ = "1.0.0"

__all__ = [
    "DEFAULT_GAP_SPLIT_S",
    "EVENT_LABELS",
    "Event",
    "Flight",
    "G",
    "LogError",
    "LogModel",
    "analyze",
    "average_speed",
    "build_flights",
    "build_series",
    "compute_flight_stats",
    "energy_rate",
    "load_log",
    "parse_utc",
    "render_report",
    "specific_energy",
    "write_report",
    "__version__",
]
