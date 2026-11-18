#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""WtHud 飞行日志解析 / 可视化工具（独立子工具，不属于 WtHud 产品代码）。

模块结构：

* :mod:`flightlog.parse`   —— JSONL 解析、架次切分、能量、统计与平均速度
* :mod:`flightlog.report`  —— 生成自包含单文件 HTML
* :mod:`flightlog.mfd`     —— MFD 面板数据导出（``--mfd-json``，schema=1 机器可读 JSON）
* :mod:`flightlog.acmi`    —— ACMI（Tacview 文本）解析、玩家归组、回放样本映射
* :mod:`flightlog.replay`  —— ``.wrpl`` 回放导入（调用 wrpl-acmi.exe 转 ACMI）
* :mod:`flightlog.picker`  —— 玩家列表表格 / 命令行选择 / Tk 多选对话框
* :mod:`flightlog.preview_png` —— 可选：用 Pillow 出 PNG 预览
* ``python -m flightlog``  —— 命令行入口（见 README.md）

典型用法::

    python -m flightlog wtlog-20261117-131039-jh_7a.jsonl --out out/report.html
    python -m flightlog replay.wrpl --list
    python -m flightlog replay.wrpl --player HCN_WT --out out/replay.html
    python -m flightlog --mfd-json wtlog-20261117-131039-jh_7a.jsonl --out out/mfd/sample.json
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
from .mfd import (  # noqa: F401  (MFD 面板导出)
    MFD_SCHEMA,
    MFD_TOOL,
    MFD_TOOL_VERSION,
    build_mfd_payload,
    dump_mfd_json,
    write_mfd_json,
)
from .acmi import (  # noqa: F401
    AcmiError,
    AcmiHeader,
    AcmiObject,
    AcmiPlayer,
    AcmiScan,
    build_model,
    extract,
    is_air_type,
    player_rows,
    resolve_players,
    scan,
)
from .replay import (  # noqa: F401
    ReplayError,
    ReplaySession,
    cache_dir,
    find_wrpl_acmi,
    open_acmi,
    open_replay,
    prune_cache,
    temp_acmi_path,
    validate_wrpl,
)

__version__ = "1.2.0"  # 1.2.0: 新增 --mfd-json（MFD 面板数据导出，schema=1）

__all__ = [
    "DEFAULT_GAP_SPLIT_S",
    "EVENT_LABELS",
    "Event",
    "Flight",
    "G",
    "LogError",
    "LogModel",
    "MFD_SCHEMA",
    "MFD_TOOL",
    "MFD_TOOL_VERSION",
    "AcmiError",
    "AcmiHeader",
    "AcmiObject",
    "AcmiPlayer",
    "AcmiScan",
    "ReplayError",
    "ReplaySession",
    "analyze",
    "average_speed",
    "build_flights",
    "build_mfd_payload",
    "build_model",
    "build_series",
    "cache_dir",
    "compute_flight_stats",
    "energy_rate",
    "extract",
    "find_wrpl_acmi",
    "is_air_type",
    "load_log",
    "dump_mfd_json",
    "open_acmi",
    "open_replay",
    "parse_utc",
    "player_rows",
    "prune_cache",
    "render_report",
    "resolve_players",
    "scan",
    "specific_energy",
    "temp_acmi_path",
    "validate_wrpl",
    "write_report",
    "write_mfd_json",
    "__version__",
]
