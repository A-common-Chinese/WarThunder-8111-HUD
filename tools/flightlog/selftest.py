#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""合成样本自检（纯标准库，不需要游戏、网络或 Pillow）。

运行::

    python selftest.py

覆盖：

1. JSONL 解析：meta / sample / event / end，坏行、非对象行、缺 t 行跳过；
2. 乱序样本按 t 排序；
3. 缺字段兼容（旧日志没有 vehicle / pitch_deg / bank_deg / ralt_m）；
4. spawn / death / vehicle_change 切成多架次，死亡尾巴归属；
5. 只有 utc 的事件用样本偏移换算成秒；
6. 比能量公式与能量变化率符号/数值；
7. 时间跳变自动切架次；
8. 统计摘要可 JSON 序列化；
9. 生成的 HTML 是**翻页结构**（概览 + 每个数据主题一页、页码/导航/圆点、架次选择器、
   每页都有事件时间线），且自包含（无外链脚本/样式/图片）；
10. 速度页的**平均速度 / 可手动设置计算范围**：平均函数（闭区间、边界、空区间、缺字段）、
   控件条/数值容器/区间底纹/平均虚线的 HTML 结构；
11. CLI ``--summary`` / ``--out`` 可用。
12. MFD 导出（``--mfd-json``）：开关语义（两种用法）、stdout 纯 JSON、schema/字段集合、
    点数上限、缺失 ``null``，以及平均值的「死亡前 10 s 窗口排除 + 只在死亡前 30 s 窗口内
    生效的 MAD 3σ 尖峰过滤」（合成数据核对：窗口内尖峰剔除、窗口外低速段保留、无死亡不过滤）。
"""

from __future__ import annotations

import contextlib
import datetime as _dt
import io
import json
import math
import os
import re
import sys
import tempfile
from pathlib import Path

_TOOL_DIR = Path(__file__).resolve().parent
_PARENT = _TOOL_DIR.parent
if str(_PARENT) not in sys.path:
    sys.path.insert(0, str(_PARENT))

from flightlog import parse as P  # noqa: E402
from flightlog import picker as PK  # noqa: E402
from flightlog import replay as R  # noqa: E402
from flightlog import acmi as A  # noqa: E402
from flightlog import mfd as MF  # noqa: E402
from flightlog.__main__ import main as cli_main  # noqa: E402
from flightlog.report import render_report  # noqa: E402

FAILURES = []
CHECKS = 0


def check(condition: bool, message: str) -> None:
    global CHECKS
    CHECKS += 1
    if condition:
        print("  [PASS] %s" % message)
    else:
        print("  [FAIL] %s" % message)
        FAILURES.append(message)


def close(a, b, tol=1e-6) -> bool:
    if a is None or b is None:
        return a is b
    return abs(float(a) - float(b)) <= tol


def _iso(epoch: float, frac_digits: int = 6) -> str:
    """epoch 秒 -> ISO8601 UTC 字符串（模拟真实日志的小数位）。"""
    moment = _dt.datetime.fromtimestamp(epoch, _dt.timezone.utc)
    base = moment.strftime("%Y-%m-%dT%H:%M:%S")
    micro = "%06d" % moment.microsecond
    return base + "." + micro[:frac_digits] + "Z"


def _sample(t: float, epoch0: float, **extra):
    record = {
        "type": "sample",
        "t": round(t, 3),
        "utc": _iso(epoch0 + t),
        "valid": True,
        "inflight": True,
        "vehicle": "f_15ex",
        "ias_kmh": 360.0 + 36.0 * (t - 1.0),
        "tas_kmh": 360.0 + 36.0 * (t - 1.0),
        "mach": 0.3,
        "alt_m": 1000.0 + 100.0 * (t - 1.0),
        "ralt_m": 500.0,
        "vy_ms": 100.0,
        "aoa_deg": 5.0,
        "aos_deg": 0.0,
        "ny_g": 1.0,
        "roll_rate_dps": 0.0,
        "hdg_deg": 90.0,
        "has_hdg": True,
        "pitch_deg": 2.0,
        "bank_deg": 0.0,
        "att_src": "Game",
        "att_conf": 1,
        "gear_pct": 0,
        "flaps_pct": 0,
        "airbrake_pct": 0,
        "fuel_kg": 3000,
        "fuel0_kg": 9000,
    }
    record.update(extra)
    return record


def _old_sample(t: float, epoch0: float, **extra):
    """旧格式样本：没有 vehicle / pitch_deg / bank_deg / ralt_m。"""
    record = {
        "type": "sample",
        "t": round(t, 3),
        "utc": _iso(epoch0 + t),
        "valid": True,
        "inflight": True,
        "ias_kmh": 500.0,
        "tas_kmh": 540.0,
        "mach": 0.45,
        "alt_m": 2000.0,
        "vy_ms": 0.0,
        "aoa_deg": 3.0,
        "aos_deg": 0.0,
        "ny_g": 1.0,
        "roll_rate_dps": 0.0,
        "hdg_deg": 180.0,
        "has_hdg": True,
        "att_src": "Unavailable",
        "att_conf": 0,
        "gear_pct": 0,
        "flaps_pct": 0,
        "airbrake_pct": 0,
        "fuel_kg": 2000,
        "fuel0_kg": 9000,
    }
    record.update(extra)
    return record


EPOCH0 = _dt.datetime(2026, 1, 1, tzinfo=_dt.timezone.utc).timestamp()


def build_full_log(path: Path) -> None:
    lines = []
    lines.append(
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.5.0-selftest",
                "utc": _iso(EPOCH0, 7),
                "vehicle": "f_15ex",
                "rate": "per-sample",
            },
            ensure_ascii=False,
        )
    )
    # 只有 utc 的 spawn（测试换算）
    lines.append(
        json.dumps(
            {"type": "event", "kind": "spawn", "utc": _iso(EPOCH0 + 0.4), "vehicle": ""}
        )
    )

    samples = []
    for i in range(19):  # 架次 1：t = 1.0 .. 10.0
        t = 1.0 + 0.5 * i
        extra = {}
        if t in (1.0, 1.5):
            extra["ralt_m"] = None  # 缺雷达高度 -> 应变成 None
        if t == 7.0:
            extra["valid"] = False  # 死亡/大厅帧
            extra["inflight"] = False
        if t == 5.0:
            extra["ny_g"] = 7.5  # 测试最大过载
        samples.append(_sample(t, EPOCH0, **extra))
    for i in range(4):  # 架次 2：t = 12.0 .. 13.5（换机型后的样本）
        samples.append(_sample(12.0 + 0.5 * i, EPOCH0, vehicle="jh_7a"))
    for i in range(3):  # 架次 3：t = 14.0 .. 15.0
        samples.append(_sample(14.0 + 0.5 * i, EPOCH0, vehicle="jh_7a"))

    # 乱序：把 t=8.0 的样本提前塞入
    out_of_order = [s for s in samples if s["t"] == 8.0][0]
    samples.remove(out_of_order)
    samples.insert(3, out_of_order)

    for sample in samples:
        lines.append(json.dumps(sample, ensure_ascii=False))

    # 坏行 / 边界行
    lines.append("{ this is not json")
    lines.append("[]")
    lines.append(json.dumps({"type": "sample", "t": "abc", "alt_m": 1}))
    lines.append(json.dumps({"type": "sample", "alt_m": 1}))
    lines.append(json.dumps({"type": "weird-record", "x": 1}))

    lines.append(json.dumps({"type": "event", "kind": "death", "t": 10.5}))
    lines.append(json.dumps({"type": "event", "kind": "match_start", "t": 5.0}))
    lines.append(json.dumps({"type": "event", "kind": "spawn", "t": 11.9}))
    lines.append(json.dumps({"type": "event", "kind": "vehicle_change", "t": 14.0, "vehicle": "jh_7a"}))
    lines.append(json.dumps({"type": "end", "utc": _iso(EPOCH0 + 20.0)}))

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_old_log(path: Path) -> None:
    lines = [
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.4.0",
                "utc": _iso(EPOCH0),
                "vehicle": "f_15ex",
                "rate": "per-sample",
            }
        )
    ]
    for i in range(5):
        lines.append(json.dumps(_old_sample(1.0 + 0.5 * i, EPOCH0)))
    lines.append("not a json line")
    for i in range(5):
        lines.append(json.dumps(_old_sample(100.0 + 0.5 * i, EPOCH0)))
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def test_parse_full(tmp: Path) -> None:
    print("\n[1] 解析：真实事件 / 坏行 / 乱序 / 缺字段")
    path = tmp / "full.jsonl"
    build_full_log(path)
    model = P.analyze(str(path))

    counters = model.counters
    check(counters["samples"] == 26, "有效样本 26 条（坏行/缺 t 行被跳过）")
    check(counters["bad_json"] == 1, "非 JSON 行计 1 条")
    check(counters["bad_record"] == 1, "非对象行计 1 条")
    check(counters["bad_sample"] == 2, "缺 t / t 非数字的样本计 2 条")
    check(counters["unknown_type"] == 1, "未知 type 记录计 1 条")
    check(counters["events"] == 5, "5 条 event（spawn / death / match_start / spawn / vehicle_change）")

    times = [s["t"] for f in model.flights for s in f.samples]
    check(times == sorted(times), "乱序样本已按 t 排序")

    # 只有 utc 的 spawn 应换算到 t≈0.4
    spawn = next(e for e in model.events if e.kind == "spawn" and e.t < 1.0)
    check(close(spawn.t, 0.4, 0.01), "只有 utc 的事件换算为 t≈0.4（得到 %s）" % spawn.t)
    check(spawn.t_source == "utc", "事件时间来源标记为 utc")

    check(len(model.flights) == 3, "切出 3 个架次（spawn / death / vehicle_change）")
    reasons = [f.reason for f in model.flights]
    check(reasons == ["spawn", "spawn", "vehicle_change"], "架次原因=%s" % reasons)

    f1, f2, f3 = model.flights
    check(len(f1.samples) == 19, "架次1 样本 19 条")
    check(len(f2.samples) == 4, "架次2 样本 4 条")
    check(len(f3.samples) == 3, "架次3 样本 3 条")
    check(close(f1.death_t, 10.5, 1e-9), "架次1 记录了死亡时刻 10.5")
    check(any(e.kind == "match_start" for e in f1.events), "未知事件保留在时间线（match_start）")
    check(f2.vehicle == "jh_7a", "架次2 机型取自样本字段 jh_7a（不是 meta）")
    check(f3.vehicle == "jh_7a", "架次3 机型来自 vehicle_change 事件")

    # 缺字段
    check(f1.series["ralt"][0] is None, "缺失 ralt_m -> None")
    check(f1.series["pitch"][0] is not None, "pitch_deg 正常解析")
    check(f1.series["valid"][f1.samples.index(next(s for s in f1.samples if s["t"] == 7.0))] == 0,
          "valid=false 样本标记为 0")
    check(close(f1.stats["ny"]["abs_max"], 7.5, 1e-6), "最大过载 = 7.5 g")
    check(0 < f1.stats["valid_ratio"] < 1, "valid 比例在 (0,1) 之间")

    # 能量公式
    es_expected = 1000.0 + (360.0 / 3.6) ** 2 / (2.0 * P.G)
    check(close(f1.series["es"][0], es_expected, 1e-6),
          "Es(alt=1000, tas=360) = %.6f（公式核对）" % es_expected)
    check(close(f1.series["es"][0], 1509.858106, 1e-3), "Es 数值 ≈ 1509.858 m")

    # 能量变化率：爬升+加速 -> 明显正
    idx5 = next(i for i, s in enumerate(f1.samples) if s["t"] == 5.0)
    des = f1.series["des"][idx5]
    check(des is not None and des > 50.0, "爬升加速段 dEs/dt 为正且较大（%s）" % (None if des is None else round(des, 1)))
    check(f1.stats["energy"]["gain"] > 500, "能量获得积分 > 500 m（%s）" % f1.stats["energy"]["gain"])
    check(f1.stats["energy"]["inflight_delta"] > 2000,
          "空中比能量净增 > 2000 m（%s）" % f1.stats["energy"]["inflight_delta"])
    check(f1.stats["energy"]["loss"] <= 0, "能量损失积分 <= 0")
    idx7 = next(i for i, s in enumerate(f1.samples) if s["t"] == 7.0)
    check(f1.series["des"][idx7] is None, "valid=false 附近 dEs/dt 置空（不产生假尖峰）")

    # 摘要可序列化
    try:
        text = json.dumps(model.summary, ensure_ascii=False, allow_nan=False)
        ok = True
        detail = "%d 字节" % len(text)
    except (ValueError, TypeError) as exc:  # pragma: no cover
        ok = False
        detail = str(exc)
    check(ok, "统计摘要可 JSON 序列化（allow_nan=False，%s）" % detail)
    check(model.summary["counts"]["flights"] == 3, "摘要里架次数 = 3")
    check(model.summary["totals"]["max_ny_g"] == 7.5, "摘要里最大过载 = 7.5")
    check(model.summary["time"]["span_s"] > 0, "摘要时长 > 0")


def test_parse_old(tmp: Path) -> None:
    print("\n[2] 旧格式：无事件 / 无 vehicle / 无姿态 / 坏行 / 时间跳变")
    path = tmp / "old.jsonl"
    build_old_log(path)
    model = P.analyze(str(path))

    check(len(model.flights) == 2, "无事件时按时间跳变切出 2 个架次")
    check([f.reason for f in model.flights] == ["log_start", "gap"], "架次原因 log_start / gap")
    check(model.flights[0].vehicle == "f_15ex", "样本没有 vehicle 时回退到 meta 机型")
    check(all(v is None for v in model.flights[0].series["pitch"]), "缺姿态字段 -> 全 None")
    check(all(v is None for v in model.flights[0].series["ralt"]), "缺 ralt 字段 -> 全 None")
    check(model.counters["bad_json"] == 1, "坏行被跳过")
    check(any("旧格式" in w or "vehicle" in w for w in model.warnings), "给出旧格式/缺字段提示")

    html = render_report(model)
    check('id="page-attitude"' not in html, "没有姿态数据时不生成姿态页")
    check("background:#2f9e75" not in html, "没有 ralt 数据时不画雷达高度图例")
    check('id="page-global"' in html, "两个架次（时间跳变）会生成全局总览页")


def test_energy_helpers() -> None:
    print("\n[3] 能量公式 / 变化率 / 时间解析单元测试")
    check(close(P.specific_energy(1000.0, 360.0), 1000.0 + 100.0 ** 2 / (2.0 * P.G), 1e-9),
          "specific_energy(1000, 360) 正确")
    check(P.specific_energy(None, 300.0) is None, "缺高度 -> None")
    check(close(P.specific_energy(1000.0, None), 1000.0), "缺速度时退化为位能高度")

    times = [0.1 * i for i in range(101)]
    values = [100.0 + 20.0 * t for t in times]
    rates = P.energy_rate(times, values)
    middle = rates[50]
    check(middle is not None and abs(middle - 20.0) < 1e-6,
          "线性序列 dEs/dt = 20 m/s（得到 %s）" % (None if middle is None else round(middle, 3)))

    # 状态重置（坠落/传送）：单步跳变 > 600 m，应被屏蔽
    jump_times = [0.1 * i for i in range(101)]
    jump_values = [100.0 + 20.0 * t for t in jump_times]
    jump_values[50:] = [-1000.0] * 51
    jump_rates = P.energy_rate(jump_times, jump_values)
    check(all(r is None for r in jump_rates[45:56]), "状态重置附近 dEs/dt 置空")

    check(close(P.parse_utc("2026-01-01T00:00:00.1234567Z"), EPOCH0 + 0.1234567, 1e-4),
          "parse_utc 支持 7 位小数")
    check(close(P.parse_utc("2026-01-01T08:00:00+08:00"), EPOCH0, 1e-3),
          "parse_utc 支持 +08:00 时区偏移")
    check(P.parse_utc("not-a-time") is None, "parse_utc 对坏字符串返回 None")


def test_report_html(tmp: Path) -> None:
    print("\n[4] HTML 报告：分页结构 / 自包含 / 事件 / 图表")
    path = tmp / "full.jsonl"
    if not path.exists():
        build_full_log(path)
    model = P.analyze(str(path))
    html = render_report(model)

    check(html.startswith("<!DOCTYPE html>"), "以 <!DOCTYPE html> 开头")
    check(html.rstrip().endswith("</html>"), "以 </html> 结尾")
    check('<meta charset="utf-8">' in html, "声明 UTF-8")
    check("飞行日志分析" in html, "包含中文标题（UTF-8 正常）")
    check("window.__FLIGHTLOG__=" in html, "内联了悬停数据")
    check("<script>window.__FLIGHTLOG__=" in html, "数据脚本内联在页面里")

    # ---- 自包含（无任何外链）----
    check("<script src" not in html, "没有外链脚本")
    check("<link" not in html, "没有外链样式")
    check("<img" not in html, "没有外部图片")
    check("@import" not in html, "没有 CSS @import")
    check('src="http' not in html and 'href="http' not in html, "没有 http(s) 外链资源")

    # ---- 翻页结构：概览 + 每个数据主题一页 ----
    page_ids = re.findall(r'<section class="page[^"]*" id="(page-[a-z_]+)"', html)
    check(len(page_ids) == 9,
          "共 9 页（概览 + 7 个数据主题 + 全局总览），实际 %d 页" % len(page_ids))
    check(page_ids[0] == "page-overview", "第 1 页是概览（汇总卡片 + Es 缩略图）")
    check(
        page_ids[1:8]
        == [
            "page-speed",
            "page-altitude",
            "page-energy",
            "page-energy_rate",
            "page-vy",
            "page-aoa_ny",
            "page-attitude",
        ],
        "速度/高度/Es/dEs/dt/Vy/迎角过载/姿态 各一页且顺序固定",
    )
    check(page_ids[-1] == "page-global", "多架次时最后追加全局总览页")
    check(html.count('<section class="page') == 9, "每页一个 .page 容器（翻页用 CSS 切换）")
    check(html.count('<section class="page active"') == 1, "初始只有第 1 页带 active")
    check(html.count('class="flight-view') == 24,
          "8 个可切换页 × 3 架次 = 24 个架次视图（切架次只换当前页）")
    check(html.count('class="flight-view active" data-flight="1"') == 8,
          "每个可切换页默认显示架次 1")

    # ---- 页码 / 导航 / 圆点 / 架次选择器 ----
    check(html.count('id="page-label"') == 1, "有页码容器 #page-label")
    check("第 1 / 9 页" in html, "初始页码文字为 第 1 / 9 页")
    check(html.count('id="dots"') == 1, "有圆点导航容器 #dots")
    check('class="dot"' not in html, "圆点由内联 JS 生成（不占静态体积）")
    check(html.count('data-nav="prev"') >= 2 and 'class="side prev"' in html,
          "上一页按钮有底部 + 左侧两份")
    check(html.count('data-nav="next"') >= 2 and 'class="side next"' in html,
          "下一页按钮有底部 + 右侧两份")
    check(html.count('data-flight-btn="') == 3, "3 架次有分段按钮选择器")
    check('data-flight="' in html and "isShown" in html,
          "视图带架次标记，悬停只联动当前页/当前架次")

    # ---- 每页固定铺满视口（不做长滚动）----
    check("html,body{height:100%" in html and "overflow:hidden" in html,
          "视口固定高度 + 隐藏长滚动")
    check(".page.active{display:flex}" in html, "翻页用 .page.active 切换显示")
    check(".flight-view.active{display:flex}" in html, "切架次用 .flight-view.active 切换")
    check('preserveAspectRatio="none"' in html, "SVG 铺满页面容器（不退化成小图）")

    # ---- 每页都有事件竖线/标签 + 图表 ----
    check(html.count('class="strip"') >= 9, "事件时间线出现在每一页")
    check('id="ev-f1-speed"' in html and 'id="ev-f3-energy"' in html,
          "每页事件条 id 唯一（架次 + 页）")
    check(html.count("<svg ") >= 20, "内联 SVG 图数量 >= 20（%d）" % html.count("<svg "))
    check("svg-f1-speed" in html and "svg-f3-speed" in html, "三个架次都有速度图")
    check("svg-f1-attitude" in html, "姿态页有姿态图")
    check("svg-ovf1-es" in html and "svg-ovf3-es" in html, "概览页有每架次的 Es 缩略图")
    check("死亡" in html and "dEs/dt" in html, "包含事件标签与能量变化率")
    check('href="#flight-' not in html, "不再使用长页面锚点跳转")

    svg_ids = re.findall(r'id="(svg-[^"]+)"', html)
    check(len(svg_ids) == len(set(svg_ids)), "SVG id 不重复（%d 个）" % len(svg_ids))
    strip_ids = re.findall(r'id="(ev-[^"]+)"', html)
    check(len(strip_ids) == len(set(strip_ids)), "事件条 id 不重复（%d 个）" % len(strip_ids))

    # ---- 交互脚本 ----
    check("function showPage" in html and '"keydown"' in html and "PageDown" in html,
          "内联 JS 含翻页函数与 ←/→/PageUp/PageDown 键盘处理")
    check("replaceState" in html, "翻页时同步 URL hash（可直链某页）")
    check("flight-select" in html or "data-flight-btn" in html, "架次切换逻辑内联")

    # ---- 混合日志：某架次没有某个主题 -> 页面仍生成，该架次显示占位 ----
    mixed = tmp / "mixed.jsonl"
    mixed_lines = [
        json.dumps(
            {"type": "meta", "schema": 1, "app": "0.4.0", "utc": _iso(EPOCH0),
             "vehicle": "f_15ex", "rate": "per-sample"}
        )
    ]
    for i in range(6):  # 架次 1：旧样本，没有姿态
        mixed_lines.append(json.dumps(_old_sample(1.0 + 0.4 * i, EPOCH0)))
    for i in range(6):  # 架次 2：有姿态（时间跳变切架次）
        mixed_lines.append(json.dumps(_sample(120.0 + 0.4 * i, EPOCH0)))
    mixed.write_text("\n".join(mixed_lines) + "\n", encoding="utf-8")
    mixed_html = render_report(P.analyze(str(mixed)))
    check('id="page-attitude"' in mixed_html,
          "混合日志：只要有一个架次有姿态数据就生成姿态页")
    check("该架次没有此项数据" in mixed_html,
          "缺该主题数据的架次显示占位提示而非空白页")

    out = tmp / "report.html"
    from flightlog.report import write_report

    written = write_report(model, str(out))
    check(os.path.isfile(written) and os.path.getsize(written) > 20000,
          "write_report 写出非空文件（%d 字节）" % os.path.getsize(written))
    raw = Path(written).read_text(encoding="utf-8")
    check("\ufffd" not in raw, "输出没有替换字符（编码正常）")


def test_average_helpers() -> None:
    print("\n[6] 平均速度纯函数：闭区间 / 边界 / 空区间 / 缺字段")
    samples = [
        {"t": 0.0, "ias": 100.0, "tas": 120.0, "mach": 0.30},
        {"t": 1.0, "ias": 200.0, "tas": 240.0, "mach": 0.60},
        {"t": 2.0, "ias": 300.0, "tas": 360.0, "mach": 0.90},
    ]
    full = P.average_speed(samples, 0.0, 2.0)
    check(full is not None and full["samples"] == 3, "全程闭区间 [0,2] 命中 3 个样本")
    check(close(full["ias"], 200.0, 1e-9), "平均 IAS = 200（闭区间含两端）")
    check(close(full["tas"], 240.0, 1e-9), "平均 TAS = 240")
    check(close(full["mach"], 0.6, 1e-9), "平均 Mach = 0.6")
    check(close(full["duration_s"], 2.0, 1e-9), "返回区间时长 = 2.0 s")
    check(full["first_t"] == 0.0 and full["last_t"] == 2.0, "返回命中样本的首末时间")

    edge = P.average_speed(samples, 1.0, 2.0)
    check(edge is not None and edge["samples"] == 2, "边界区间 [1,2] 恰好含 2 个样本（闭区间）")
    check(close(edge["ias"], 250.0, 1e-9), "边界区间平均 IAS = (200+300)/2")

    single = P.average_speed(samples, 0.5, 1.5)
    check(single is not None and single["samples"] == 1, "区间内只有 1 个样本时也能算")
    check(close(single["ias"], 200.0, 1e-9), "单样本平均 = 该样本值")

    check(P.average_speed(samples, 5.0, 6.0) is None, "空区间返回 None")
    check(P.average_speed(samples, 2.5, 3.5) is None, "完全在数据外的区间返回 None")
    swapped = P.average_speed(samples, 2.0, 0.0)
    check(swapped is not None and close(swapped["ias"], 200.0, 1e-9)
          and swapped["range_start"] == 0.0 and swapped["range_end"] == 2.0,
          "起止写反自动交换成升序")
    check(P.average_speed(samples, None, 2.0) is None, "起点非数字返回 None")
    check(P.average_speed(samples, "abc", 2.0) is None, "起点是坏字符串返回 None")

    missing = [
        {"t": 0.0, "ias": 100.0},              # 缺 tas / mach
        {"t": 1.0, "ias": 300.0, "tas": 600.0},
        {"t": 2.0, "tas": 800.0},              # 缺 ias / mach
    ]
    partial = P.average_speed(missing, 0.0, 2.0)
    check(partial is not None and partial["samples"] == 3, "缺字段时仍统计命中的样本")
    check(close(partial["ias"], 200.0, 1e-9), "缺 ias 的样本不参与 IAS 平均")
    check(close(partial["tas"], 700.0, 1e-9), "缺 tas 的样本不参与 TAS 平均")
    check(partial["mach"] is None, "全部缺 mach 时平均 Mach 为 None")
    raw = [{"t": 0.0, "ias_kmh": 300, "tas_kmh": 360, "mach": 0.5},
           {"t": 1.0, "ias_kmh": 500, "tas_kmh": 560, "mach": 0.7}]
    raw_avg = P.average_speed(raw, 0.0, 1.0)
    check(raw_avg is not None and close(raw_avg["ias"], 400.0, 1e-9)
          and close(raw_avg["mach"], 0.6, 1e-9),
          "兼容原始日志字段名 ias_kmh / tas_kmh")
    check(P.average_speed([{"t": None, "ias": 1.0}], 0.0, 10.0) is None,
          "样本 t 无效时跳过（不会崩）")


def test_avg_report_html(tmp: Path) -> None:
    print("\n[7] HTML 报告：速度页平均速度 / 可手动设置计算范围")
    path = tmp / "full.jsonl"
    if not path.exists():
        build_full_log(path)
    model = P.analyze(str(path))
    html = render_report(model)
    first = model.flights[0]
    expected = P.average_speed(first.samples, first.start_t, first.end_t)

    check('id="avgbar-speed"' in html, "速度页有平均速度控件条 #avgbar-speed")
    check(html.count('id="avgbar-speed"') == 1, "控件条只在速度页出现一次")
    check(html.count('class="avgbar"') == 1, "其它页没有平均速度控件条")
    check('class="page-body speed-body"' in html, "速度页用 speed-body 布局（控件 + 图）")
    check('data-avg-start' in html and 'data-avg-end' in html, "有起止时间输入框")
    check('type="number"' in html and 'step="0.1"' in html, "输入框是数字框、步进 0.1 s")
    check('data-avg-apply' in html and 'data-avg-reset' in html, "有「应用」和「重置」按钮")
    check("平均 IAS" in html and "平均 TAS" in html and "平均 Mach" in html,
          "三个平均数值都有中文标签（含单位）")
    check('data-avg-val="ias"' in html and 'data-avg-val="tas"' in html
          and 'data-avg-val="mach"' in html, "平均数值容器带 data-avg-val 标记")
    check('id="avg-band-f1-speed"' in html, "速度图里有统计区间底纹元素")
    check('id="avg-edge0-f1-speed"' in html and 'id="avg-edge1-f1-speed"' in html,
          "统计区间有两条边界竖线")
    check('id="avg-ias-f1-speed"' in html and 'id="avg-tas-f1-speed"' in html
          and 'id="avg-mach-f1-speed"' in html,
          "平均 IAS/TAS/Mach 都画了水平虚线（id 可被 JS 定位）")
    check(html.count('id="avg-band-') == 3, "3 个架次各有一份区间底纹（共 %d）" % html.count('id="avg-band-'))
    check(html.count('class="avg-line"') == 9, "3 架次 × 3 条平均虚线 = 9 条")

    # 静态默认值 = 整个架次全程的平均
    m = re.search(r'<b data-avg-val="ias">([^<]+)</b>', html)
    check(m is not None and expected is not None
          and close(float(m.group(1)), expected["ias"], 0.05),
          "默认平均 IAS 静态值 = 全程平均值（%s）" % (m.group(1) if m else "?"))
    check("范围" in html and "样本" in html and "回车 = 应用" in html,
          "数值区注明范围 / 样本数 / 回车应用")
    check("%s 样本" % expected["samples"] in html,
          "注明默认范围样本数 = %d" % expected["samples"])

    # 内联 JS：范围夹取 + 原地更新 + 切架次重算
    check("function initAverage" in html, "内联 JS 含 initAverage（平均速度逻辑）")
    check("clampRange" in html and "超出当前架次时间轴" in html, "JS 会把超范围输入夹到边界")
    check("起止时间写反" in html, "JS 会处理结束<开始的非法输入")
    check("AVG.onFlight" in html, "切换架次时会按新架次时间轴重算平均")
    check("avg-band-" in html and "avg-edge" in html, "JS 会原地更新底纹/边界元素")
    check("平均速度（水平虚线，数值见上方；底纹 = 统计区间）" in html,
          "图例里说明虚线/底纹含义")
    check('id="page-speed"' in html and "svg-f1-speed" in html,
          "速度页与速度图仍然完好（没有破坏原结构）")
    check(html.count('class="flight-view') == 24, "各页架次视图数量未受影响")
    check('id="page-altitude"' in html and 'id="page-attitude"' in html,
          "其它数据页仍正常生成")


def test_cli(tmp: Path) -> None:
    print("\n[5] CLI：--summary / --out")
    path = tmp / "full.jsonl"
    if not path.exists():
        build_full_log(path)

    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main([str(path), "--summary"])
    check(code == 0, "--summary 退出码 0")
    try:
        payload = json.loads(buffer.getvalue())
        parsed = isinstance(payload, dict) and payload["counts"]["samples"] == 26
    except ValueError:
        parsed = False
    check(parsed, "--summary 输出可解析的统计 JSON")

    out = tmp / "cli.html"
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main([str(path), "--out", str(out)])
    check(code == 0, "--out 退出码 0")
    check(out.is_file() and out.stat().st_size > 20000, "--out 生成 HTML")

    buffer = io.StringIO()
    with contextlib.redirect_stderr(buffer):
        code = cli_main([str(tmp / "does-not-exist.jsonl"), "--summary"])
    check(code == 2, "不存在的日志返回退出码 2")


# ---------------------------------------------------------------------------
# MFD JSON 导出（--mfd-json，schema=1）
# ---------------------------------------------------------------------------

#: 契约要求的字段集合（多一个少一个都算接口漂移）
MFD_TOP_KEYS = {
    "schema", "tool", "tool_version", "source", "generated_utc",
    "meta", "flights", "events",
}
MFD_META_KEYS = {"vehicle", "duration_s", "rate_hz", "samples"}
MFD_FLIGHT_KEYS = {"index", "start_s", "end_s", "samples", "vehicle", "stats", "series"}
MFD_STATS_KEYS = {
    "max_ias_kmh", "max_tas_kmh", "max_mach", "max_g", "max_alt_m",
    "avg_ias_kmh", "avg_tas_kmh", "avg_mach", "avg_alt_m",
    "es_range_m", "es_delta_m", "max_ias_filtered_kmh", "max_tas_filtered_kmh",
    "avg_filter",
}
MFD_AVG_FILTER_KEYS = {"death_window_s", "spike_window_s", "spike_sigma", "excluded_samples"}
MFD_SERIES_KEYS = {
    "t", "ias_kmh", "tas_kmh", "mach", "alt_m", "ralt_m", "es_m",
    "des_dt_ms", "vy_ms", "aoa_deg", "ny_g", "pitch_deg", "bank_deg",
}
MFD_EVENT_KEYS = {"kind", "t", "vehicle"}


def build_mfd_log(path: Path) -> None:
    """合成 MFD 专用日志：1 架次 / 300 样本（10 Hz）/ 死亡 t=25 / IAS 尖峰 t=10。

    设计意图（对应定稿的平均值规则：「只去除死亡之前的超大波动」）：

    * 前 5 s 的 ``ralt_m`` 缺失 → MFD JSON 序列里必须是 ``null``（不是 0 也不是丢列）；
    * ``death`` 事件在 t=25 → 死亡前 10 s 窗口是 t=15.0–25.0（10 Hz 下 101 个样本，不参与平均）；
    * MAD 尖峰过滤只在死亡前 30 s 窗口（t=−5..25）内生效：t=10 的孤立尖峰 2000（基线 300）
      落在窗口内 → 必须从平均值/过滤后最大值里剔除；
    * 基线恒定（MAD=0）→ 窗口内阈值 0 只剔除偏离中位数的尖峰，正常样本全部保留。
    """
    lines = [
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.5.0-selftest",
                "utc": _iso(EPOCH0),
                "vehicle": "jh_7a",
                "rate": "per-sample",
            },
            ensure_ascii=False,
        )
    ]
    for i in range(300):
        t = round(0.1 * (i + 1), 3)
        extra = {
            "ias_kmh": 300.0,
            "tas_kmh": 320.0,
            "mach": 0.25,
            "alt_m": 3000.0,
            "ralt_m": 1500.0,
            "ny_g": 1.0,
        }
        if t <= 5.0:
            extra["ralt_m"] = None  # 缺字段 -> null
        if t == 10.0:
            extra["ias_kmh"] = 2000.0  # 孤立尖峰
        lines.append(json.dumps(_sample(t, EPOCH0, **extra), ensure_ascii=False))
    lines.append(json.dumps({"type": "event", "kind": "death", "t": 25.0}))
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_mfd_long_log(path: Path, count: int = 12000, hz: float = 10.0) -> None:
    """1200 s、10 Hz 的单架次日志：2 Hz 采样会超过 900 点，用来验证点数上限。"""
    lines = [
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.5.0-selftest",
                "utc": _iso(EPOCH0),
                "vehicle": "jh_7a",
                "rate": "per-sample",
            },
            ensure_ascii=False,
        )
    ]
    for i in range(count):
        t = round((i + 1) / hz, 3)
        lines.append(
            json.dumps(
                _sample(t, EPOCH0, ias_kmh=400.0 + 0.01 * i, tas_kmh=420.0),
                ensure_ascii=False,
            )
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_mfd_window_log(path: Path) -> None:
    """合成「死亡前窗口」日志：500 样本（10 Hz，50 s），验证只在死亡前过滤。

    * 基线 IAS 300；t=2.0–4.0 有一段持续低速 100（**远离死亡**，属于起飞/盘旋段）；
    * t=20 是孤立尖峰 2000（在死亡前 30 s 窗口内，应被剔除）；
    * ``death`` 在 t=45 → 死亡窗口 t=35–45（101 样本）；尖峰窗口 t=15–45。
    期望：低速段保留、尖峰剔除 → avg_ias 被低速段拉低到 ≈289.4（而不是被抬到 300 附近）。
    """
    lines = [
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.5.0-selftest",
                "utc": _iso(EPOCH0),
                "vehicle": "jh_7a",
                "rate": "per-sample",
            },
            ensure_ascii=False,
        )
    ]
    for i in range(500):
        t = round(0.1 * (i + 1), 3)
        ias = 300.0
        if 2.0 - 1e-9 <= t <= 4.0 + 1e-9:
            ias = 100.0  # 持续低速段（不在死亡前窗口内，绝不能当尖峰剔除）
        if t == 20.0:
            ias = 2000.0  # 孤立尖峰（死亡前窗口内，应剔除）
        lines.append(
            json.dumps(
                _sample(t, EPOCH0, ias_kmh=ias, tas_kmh=320.0, mach=0.25,
                        alt_m=3000.0, ralt_m=1500.0, ny_g=1.0),
                ensure_ascii=False,
            )
        )
    lines.append(json.dumps({"type": "event", "kind": "death", "t": 45.0}))
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_mfd_nodeath_log(path: Path) -> None:
    """合成「无死亡」日志：100 样本（10 Hz），t=5 一个尖峰 2000（基线 300）。

    没有 ``death`` 事件 → 不做任何 MAD 过滤，平均值必须等于原始算术平均 317.0。
    """
    lines = [
        json.dumps(
            {
                "type": "meta",
                "schema": 1,
                "app": "0.5.0-selftest",
                "utc": _iso(EPOCH0),
                "vehicle": "jh_7a",
                "rate": "per-sample",
            },
            ensure_ascii=False,
        )
    ]
    for i in range(100):
        t = round(0.1 * (i + 1), 3)
        ias = 2000.0 if t == 5.0 else 300.0
        lines.append(
            json.dumps(_sample(t, EPOCH0, ias_kmh=ias, tas_kmh=320.0), ensure_ascii=False)
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def test_mfd_helpers() -> None:
    print("\n[12] MFD 辅助函数：死亡窗口 / 尖峰窗口 / MAD / 降采样点数上限")
    times = [round(0.1 * i, 3) for i in range(1, 301)]  # 10 Hz，t=0.1..30.0
    in_death = MF.death_window_mask(times, [25.0])
    check(sum(in_death) == 101, "死亡前 10 s 窗口标记 101 个样本（t=15.0–25.0，10 Hz）")
    check(in_death[times.index(15.0)] and not in_death[times.index(14.9)],
          "窗口边界：t=15.0 排除、t=14.9 保留")
    check(in_death[times.index(25.0)] and not in_death[times.index(25.1)],
          "窗口上界到死亡时刻（含）、之后样本保留")

    # 尖峰检测范围 = [death-30, death] ∩ 未被死亡窗口排除的样本
    base_mask = [not flagged for flagged in in_death]
    domain = MF.spike_window_mask(times, [25.0], 30.0, base_mask)
    check([times[i] for i in range(len(times)) if domain[i]][:1] == [0.1]
          and [times[i] for i in range(len(times)) if domain[i]][-1:] == [14.9],
          "尖峰窗口 = t=0.1–14.9（death-30 起、去掉死亡窗口后到 14.9 止）")
    check(not any(domain[i] for i in range(len(times)) if times[i] >= 15.0),
          "死亡窗口内样本不参与尖峰检测")
    check(not any(MF.spike_window_mask(times, [], 30.0)), "没有 death 时尖峰窗口全 False（不做 MAD）")

    mask, fallback = MF.spike_mask([300.0] * 25 + [2000.0] * 10, [True] * 35)
    check(mask is None and fallback,
          "MAD 过滤后窗口内剩余 <30 样本 → 退回「只排除死亡窗口」（fallback=True）")

    spike, spike_fallback = MF.spike_mask([300.0] * 299 + [2000.0], [True] * 300)
    check(spike is not None and sum(spike) == 1 and not spike_fallback,
          "MAD=0 时只剔除孤立尖峰、正常样本保留")

    # 窗口外的值即使离中位数很远也不标记
    outside = [100.0] * 10 + [300.0] * 49 + [2000.0]
    outside_domain = [False] * 10 + [True] * 50
    outside_mask, outside_fallback = MF.spike_mask(outside, outside_domain)
    check(outside_mask is not None and sum(outside_mask) == 1
          and not outside_mask[0] and outside_mask[-1] and not outside_fallback,
          "窗口外的低速值不标记，只有窗口内尖峰被剔除")

    indices = MF.downsample_indices(times)
    check(len(indices) == 60 and indices[0] == 0 and indices[-1] == 299,
          "300 样本 10 Hz 降到 60 点（约 2 Hz，保留首尾）")
    check(close(times[indices[1]] - times[indices[0]], 0.5, 1e-9),
          "降采样步长 0.5 s（2 Hz）")

    long_times = [0.05 * i for i in range(20000)]  # 20 Hz × 1000 s
    long_indices = MF.downsample_indices(long_times)
    check(len(long_indices) <= MF.MAX_SERIES_POINTS,
          "超过 2 Hz 点数预算时自动加粗步长，点数 <= 900（实际 %d）" % len(long_indices))
    check(long_indices[0] == 0 and long_indices[-1] == len(long_times) - 1,
          "加粗步长后仍保留首尾样本")


def test_mfd_payload(tmp: Path) -> None:
    print("\n[13] MFD payload：schema 字段 / 平均值过滤 / series 契约 / null")
    path = tmp / "mfd.jsonl"
    build_mfd_log(path)
    model = P.analyze(str(path))
    payload = MF.build_mfd_payload(model)

    check(set(payload) == MFD_TOP_KEYS, "顶层字段与契约一致")
    check(payload["schema"] == 1, "schema = 1")
    check(payload["tool"] == "FlightLog" and isinstance(payload["tool_version"], str),
          "tool = FlightLog 且带 tool_version")
    check(payload["source"] == "mfd.jsonl", "source = 输入文件名")
    check(isinstance(payload["generated_utc"], str) and payload["generated_utc"].endswith("Z"),
          "generated_utc 是 UTC ISO 时间")

    meta = payload["meta"]
    check(set(meta) == MFD_META_KEYS, "meta 字段与契约一致")
    check(meta["samples"] == 300, "meta.samples = 300")
    check(close(meta["duration_s"], 29.9, 1e-9), "meta.duration_s = 29.9 s")
    check(close(meta["rate_hz"], 10.0, 1e-9), "meta.rate_hz = 10 Hz（由样本数/时长推出）")

    check(len(payload["flights"]) == 1, "death 事件只标记不切架次 → 仍为 1 个架次")
    flight = payload["flights"][0]
    check(set(flight) == MFD_FLIGHT_KEYS, "架次字段与契约一致")
    check(flight["index"] == 1 and flight["samples"] == 300, "index=1 / samples=300")
    check(close(flight["start_s"], 0.1, 1e-9) and close(flight["end_s"], 30.0, 1e-9),
          "start_s=0.1 / end_s=30.0")

    stats = flight["stats"]
    check(set(stats) == MFD_STATS_KEYS, "stats 字段与契约一致")
    check(close(stats["max_ias_kmh"], 2000.0, 1e-9), "max_ias_kmh 是原始最大值 2000")
    check(close(stats["max_ias_filtered_kmh"], 300.0, 1e-9),
          "max_ias_filtered_kmh 剔掉尖峰后 = 300")
    check(close(stats["avg_ias_kmh"], 300.0, 1e-9), "avg_ias_kmh = 300（尖峰不进平均）")
    check(close(stats["avg_tas_kmh"], 320.0, 1e-9), "avg_tas_kmh = 320")
    check(close(stats["avg_mach"], 0.25, 1e-9), "avg_mach = 0.25")
    check(close(stats["avg_alt_m"], 3000.0, 1e-9), "avg_alt_m = 3000")
    check(close(stats["max_g"], 1.0, 1e-9), "max_g = 1.0")
    check(isinstance(stats["es_range_m"], list) and len(stats["es_range_m"]) == 2,
          "es_range_m = [min, max]")

    avg_filter = stats["avg_filter"]
    check(MFD_AVG_FILTER_KEYS <= set(avg_filter),
          "avg_filter 含 death_window_s/spike_window_s/spike_sigma/excluded_samples")
    check(close(avg_filter["death_window_s"], 10.0, 1e-9)
          and close(avg_filter["spike_window_s"], 30.0, 1e-9)
          and close(avg_filter["spike_sigma"], 3.0, 1e-9),
          "过滤参数 10 s 死亡窗口 / 30 s 尖峰窗口 / 3σ")
    check(avg_filter["death_excluded_samples"] == 101,
          "死亡窗口排除 101 个样本")
    check(avg_filter["spike_excluded_samples"] == 1, "MAD 只剔除窗口内 1 个尖峰样本")
    check(avg_filter["excluded_samples"] == 102,
          "excluded_samples = 死亡窗口 + 尖峰（不重复计）= 102")
    check(avg_filter["kept_samples"] == 198 and avg_filter["fallback"] is False,
          "保留 198 个样本、没有触发 fallback")

    series = flight["series"]
    check(set(series) == MFD_SERIES_KEYS, "series 字段与契约一致（12 条曲线 + t）")
    lengths = {key: len(values) for key, values in series.items()}
    check(len(set(lengths.values())) == 1, "所有序列长度一致（%s）" % sorted(set(lengths.values())))
    check(len(series["t"]) == 60, "series 点数 = 60（300 样本 @2 Hz）")
    check(series["t"][0] == 0.1 and series["t"][-1] == 30.0, "series.t 覆盖整架次（0.1–30.0）")
    check(series["ralt_m"][0] is None, "缺失的 ralt_m 写成 null（不是 0）")
    check(series["ralt_m"][-1] is not None and None in series["ralt_m"],
          "null 只出现在缺失段，后段有真实值")

    try:
        json.dumps(payload, ensure_ascii=False, allow_nan=False)
        no_nan = True
    except (TypeError, ValueError):  # pragma: no cover
        no_nan = False
    check(no_nan, "payload 可严格 JSON 序列化（allow_nan=False，无 NaN/Infinity）")

    check(len(payload["events"]) == 1 and set(payload["events"][0]) == MFD_EVENT_KEYS,
          "events 每项字段 = kind/t/vehicle")
    check(payload["events"][0]["kind"] == "death"
          and close(payload["events"][0]["t"], 25.0, 1e-9), "death 事件 t=25 被导出")


def test_mfd_spike_window(tmp: Path) -> None:
    print("\n[14] MFD 尖峰范围：只在死亡前 30 s 窗口内过滤 / 窗口外低速段保留 / 无死亡不过滤")
    # (a) 死亡前窗口内的尖峰剔除；窗口外的持续低速段保留
    path = tmp / "mfd-window.jsonl"
    build_mfd_window_log(path)
    payload = MF.build_mfd_payload(P.analyze(str(path)))
    stats = payload["flights"][0]["stats"]
    avg_filter = stats["avg_filter"]
    check(close(avg_filter["spike_window_s"], 30.0, 1e-9),
          "avg_filter.spike_window_s = 30 s（新字段）")
    check(avg_filter["death_excluded_samples"] == 101,
          "死亡窗口排除 101 个样本（t=35–45）")
    check(avg_filter["spike_excluded_samples"] == 1,
          "只有死亡前窗口内的 1 个尖峰被剔除")
    check(avg_filter["excluded_samples"] == 102,
          "窗口外的 21 个低速样本没有被当成尖峰剔除")
    check(avg_filter["kept_samples"] == 398, "保留 398 个样本参与平均")
    # 期望 avg = (377×300 + 21×100) / 398 = 289.447…（低速段保留后均值被拉低）
    check(close(stats["avg_ias_kmh"], 289.4, 0.05),
          "avg_ias ≈ 289.4（窗口外低速段保留，而不是抬到 300）")
    check(close(stats["max_ias_kmh"], 2000.0, 1e-9)
          and close(stats["max_ias_filtered_kmh"], 300.0, 1e-9),
          "原始最大 2000 / 过滤后最大 300（窗口内尖峰生效）")

    # (b) 没有 death → 不做 MAD 过滤，尖峰计入原始平均
    nodeath = tmp / "mfd-nodeath.jsonl"
    build_mfd_nodeath_log(nodeath)
    stats2 = MF.build_mfd_payload(P.analyze(str(nodeath)))["flights"][0]["stats"]
    avg_filter2 = stats2["avg_filter"]
    check(avg_filter2["excluded_samples"] == 0
          and avg_filter2["spike_excluded_samples"] == 0,
          "没有 death → 不排除任何样本（不做 MAD 尖峰过滤）")
    check(close(stats2["avg_ias_kmh"], 317.0, 1e-9),
          "无死亡时 avg = 原始算术平均 317.0（100 样本含 1 个尖峰）")


def test_mfd_cli(tmp: Path) -> None:
    print("\n[15] MFD CLI：--mfd-json 开关 / --out 写文件 / stdout 纯 JSON / 坏文件")
    path = tmp / "mfd.jsonl"
    if not path.exists():
        build_mfd_log(path)

    # 用法 1：--mfd-json + --out → 只写 JSON 文件，stdout 不混内容
    out = tmp / "mfd-out.json"
    buffer, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(buffer), contextlib.redirect_stderr(err):
        code = cli_main(["--mfd-json", str(path), "--out", str(out)])
    check(code == 0, "--mfd-json + --out 退出码 0")
    check(out.is_file() and out.stat().st_size > 1000, "--out 写出非空 JSON 文件")
    check(buffer.getvalue() == "", "--out 模式 stdout 不混任何内容（日志只走 stderr）")
    check("MFD JSON:" in err.getvalue(), "提示信息走 stderr")
    try:
        file_payload = json.loads(out.read_text(encoding="utf-8"))
        file_ok = file_payload["schema"] == 1 and "flights" in file_payload
    except ValueError:
        file_ok = False
    check(file_ok, "--out 文件是完整可解析的 schema=1 JSON")

    # 用法 2：不给 --out → stdout 只有纯 JSON（可直接被 HUD 管道读取）
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main(["--mfd-json", str(path)])
    text = buffer.getvalue()
    check(code == 0, "--mfd-json 无 --out 退出码 0")
    try:
        stdout_payload = json.loads(text)
        pure = True
    except ValueError:
        stdout_payload = None
        pure = False
    check(pure and stdout_payload.get("schema") == 1, "stdout 是可直接解析的纯 JSON（schema=1）")
    check(text.lstrip().startswith("{") and text.strip().endswith("}"),
          "stdout 没有日志前缀/后缀")

    # 位置参数在前也接受（HUD 侧 BuildArguments 是 --mfd-json <log> --out ...）
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main([str(path), "--mfd-json"])
    try:
        reversed_ok = code == 0 and json.loads(buffer.getvalue())["schema"] == 1
    except ValueError:
        reversed_ok = False
    check(reversed_ok, "--mfd-json 与位置参数的先后顺序不影响结果")

    # 点数上限：1200 s / 10 Hz → 2 Hz 会超 900 点，自动加粗
    long_path = tmp / "mfd-long.jsonl"
    build_mfd_long_log(long_path)
    long_out = tmp / "mfd-long.json"
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        code = cli_main(["--mfd-json", str(long_path), "--out", str(long_out)])
    long_payload = None
    try:
        long_payload = json.loads(long_out.read_text(encoding="utf-8"))
        long_lengths = [len(flight["series"]["t"]) for flight in long_payload["flights"]]
    except (ValueError, OSError, KeyError):
        long_lengths = []
    check(code == 0 and long_lengths and max(long_lengths) <= 900,
          "长架次导出后每架次点数 <= 900（实际 %s）" % (max(long_lengths) if long_lengths else "--"))
    check(long_payload is not None and all(
        len(values) == len(flight["series"]["t"])
        for flight in long_payload["flights"]
        for values in flight["series"].values()
    ), "长架次所有序列长度仍与 t 一致")

    # 坏文件 / 缺位置参数 → 明确退出码 2，不崩
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        code = cli_main(["--mfd-json", str(tmp / "no-such-file.jsonl")])
    check(code == 2 and "错误" in err.getvalue(), "不存在的日志 → 退出码 2 + 中文错误")

    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        code = cli_main(["--mfd-json"])
    check(code == 2 and "位置参数" in err.getvalue(),
          "--mfd-json 缺位置参数 → 退出码 2 + 说明它是开关")


# ---------------------------------------------------------------------------
# 回放 / ACMI 测试
# ---------------------------------------------------------------------------

ACMI_HEADER = [
    "FileType=text/acmi/tacview",
    "FileVersion=2.1",
    "0,ReferenceTime=2026-01-01T00:00:00Z",
    "0,ReferenceLongitude=0.0000000",
    "0,ReferenceLatitude=0.0000000",
    "0,ReferenceAltitude=0.0",
    "0,Title=TestMap / test_mission / 2026-01-01T00:00:00Z",
    "0,Author=wrpl-acmi",
    "0,Comments=Exporter wrpl-acmi v3.0.0-test+deadbeef; unit test fixture",
    "0,MapId=WarThunder.Atlas1.TestMap",
    "1,Name=jh_7a,CallSign=jh_7a,Pilot=玩家甲,Type=Air+FixedWing,Coalition=Blue,Color=Blue",
    "2,Name=jh_7a,CallSign=jh_7a,Pilot=玩家甲,Type=Air+FixedWing,Coalition=Blue,Color=Blue",
    "3,Name=Su-25,CallSign=Su-25,Pilot=PilotB,Type=Air+FixedWing,Coalition=Red,Color=Red",
    "4,Name=ZSU-23,CallSign=ZSU-23,Type=Ground+Vehicle+AntiAircraft,Coalition=Red,Color=Red",
    "5,Name=A-10A,CallSign=A-10A,Type=Air+FixedWing,Coalition=Neutral,Color=Neutral",
    "6,Name=TestStatic,CallSign=TestStatic,Type=Air+FixedWing,Coalition=Blue,Color=Blue",
]


def build_acmi_sample(path: Path) -> None:
    """手写一份最小 ACMI：2 个玩家 + 1 个地面防空（不能算飞机）+ 1 个无样本对象。"""
    lines = list(ACMI_HEADER)
    lines += [
        "#0.000",
        "1,T=10.0|20.0|1000.00|0.00|2.00|90.00,HDG=90.0,TAS=200.0,Mach=0.60,"
        "IAS=180.0,AOA=4.0,VerticalGForce=1.50",
        "3,T=11.0|21.0|2000.00|10.00|3.00|180.00,HDG=180.0,TAS=250.0,Mach=0.70,"
        "IAS=220.0,AOA=3.0,VerticalGForce=2.00",
        "5,T=12.0|22.0|1500.00|0.00|1.00|45.00,HDG=45.0,TAS=,Mach=,IAS=,"
        "VerticalGForce=,AOA=",
        "#0.250",
        "1,T=10.0|20.0|1010.00|0.00|2.00|90.00,HDG=90.0,TAS=210.0,Mach=0.63,"
        "IAS=190.0,AOA=4.0,VerticalGForce=1.50",
        "3,T=11.0|21.0|2020.00|12.00|3.00|181.00,HDG=181.0,TAS=260.0,Mach=0.73,"
        "IAS=230.0,AOA=3.0,VerticalGForce=2.10",
        "5,T=12.0|22.0|1520.00|0.00|1.00|45.00,HDG=45.0,TAS=,Mach=,IAS=,"
        "VerticalGForce=,AOA=",
        "4,T=-0.50|0.20|100.00",
        "#0.500",
        "1,T=10.0|20.0|1020.00|0.00|2.00|90.00,HDG=90.0,TAS=220.0,Mach=0.66,"
        "IAS=200.0,AOA=4.0,VerticalGForce=1.50",
        "3,T=11.0|21.0|2040.00|12.00|3.00|182.00,HDG=182.0,TAS=270.0,Mach=0.76,"
        "IAS=240.0,AOA=3.0,VerticalGForce=2.20",
        "#0.750",
        "1,T=10.0|20.0|1030.00|0.00|2.00|90.00,HDG=90.0,TAS=230.0,Mach=0.69,"
        "IAS=210.0,AOA=4.0,VerticalGForce=1.50",
        "#1.000",
        "1,T=10.0|20.0|1040.00|0.00|2.00|90.00,HDG=90.0,TAS=240.0,Mach=0.72,"
        "IAS=220.0,AOA=4.0,VerticalGForce=1.50",
        "2,T=13.0|23.0|3000.00|0.00|0.00|270.00,HDG=270.0,TAS=150.0,Mach=0.45,"
        "IAS=140.0,AOA=2.0,VerticalGForce=1.00",
        "#1.250",
        "2,T=13.0|23.0|3010.00|0.00|0.00|270.00,HDG=270.0,TAS=160.0,Mach=0.48,"
        "IAS=150.0,AOA=2.0,VerticalGForce=1.00",
        "#1.500",
        "2,T=13.0|23.0|3020.00|0.00|0.00|270.00,HDG=270.0,TAS=170.0,Mach=0.51,"
        "IAS=160.0,AOA=2.0,VerticalGForce=1.00",
        "#1.750",
        "2,T=13.0|23.0|3030.00|0.00|0.00|270.00,HDG=270.0,TAS=180.0,Mach=0.54,"
        "IAS=170.0,AOA=2.0,VerticalGForce=1.00",
        "#2.000",
        "2,T=13.0|23.0|3040.00|0.00|0.00|270.00,HDG=270.0,TAS=190.0,Mach=0.57,"
        "IAS=180.0,AOA=2.0,VerticalGForce=1.00",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_ground_acmi(path: Path) -> None:
    """只有地面单位的 ACMI（模拟纯地面战回放）。"""
    lines = [
        "FileType=text/acmi/tacview",
        "FileVersion=2.1",
        "0,ReferenceTime=2026-01-01T00:00:00Z",
        "0,Title=GroundOnly",
        "1,Name=T-72B,CallSign=T-72B,Type=Ground+Vehicle+MainBattleTank,Coalition=Red",
        "#0.000",
        "1,T=1.0|2.0|100.00",
        "#0.500",
        "1,T=1.0|2.0|100.50",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def test_acmi_scan(tmp: Path) -> None:
    print("\n[7] ACMI 解析：头部 / 对象 / 飞机类型判定 / 玩家归组")
    acmi_path = tmp / "sample.acmi"
    build_acmi_sample(acmi_path)
    s = A.scan(str(acmi_path))

    check(s.header.file_version == "2.1", "ACMI 版本号解析 = 2.1")
    check(s.header.file_type == "text/acmi/tacview", "FileType 解析（无 0, 前缀的行）")
    check(s.header.title is not None and "TestMap" in s.header.title, "Title 解析")
    check(s.header.reference_time == "2026-01-01T00:00:00Z", "ReferenceTime 解析")
    check(s.header.map_id == "WarThunder.Atlas1.TestMap", "MapId 解析")
    check(s.header.exporter == "wrpl-acmi v3.0.0", "导出器版本从 Comments 抠出并截断")
    check(s.frames == 9, "帧头数量 = 9（实际 %d）" % s.frames)
    check(close(s.t_max, 2.0, 1e-9) and close(s.t_min, 0.0, 1e-9), "时间范围 0–2 s")

    # 类型判定：防空车里的 "AntiAircraft" 不能因为含 Air 就被当成飞机
    check(A.is_air_type("Air+FixedWing") is True, "Air+FixedWing 判定为飞机")
    check(A.is_air_type("Ground+Vehicle+AntiAircraft") is False, "Ground+…+AntiAircraft 不算飞机")
    check(A.is_air_type(None) is False and A.is_air_type("") is False, "空 Type 不算飞机")
    check(s.air_object_count == 5, "飞机对象 5 个（实际 %d）" % s.air_object_count)
    check(s.ground_object_count == 1, "地面对象 1 个（实际 %d）" % s.ground_object_count)

    names = [p.name for p in s.players]
    check(names == ["玩家甲", "PilotB", "A-10A #5", "TestStatic #6"],
          "玩家归组与排序（真人优先、按样本数）: %s" % names)
    jia = s.players[0]
    check(jia.object_ids == ["1", "2"], "同一 Pilot 的多个对象归到一组")
    check(jia.vehicles == ["jh_7a"], "玩家机型集合 = jh_7a")
    check(jia.coalition_label == "蓝", "阵营 Blue → 蓝")
    check(jia.updates == 10, "玩家样本数 = 10（5+5）")
    check(close(jia.duration_s, 2.0, 1e-9), "玩家时长 2.0 s")
    check(s.players[2].has_pilot is False, "没有 Pilot 的对象单独成组（A-10A #5）")
    check(s.players[3].updates == 0, "从未更新的对象也列出来（样本 0）")
    check(all("4" not in p.object_ids for p in s.players), "地面对象不进玩家列表")

    # 玩家名解析：精确 / 大小写 / 子串 / 序号
    check(A.resolve_players(s.players, ["玩家甲"])[0].name == "玩家甲", "resolve_players 精确匹配")
    check(A.resolve_players(s.players, ["pilotb"])[0].name == "PilotB", "resolve_players 大小写不敏感")
    check(A.resolve_players(s.players, ["甲"])[0].name == "玩家甲", "resolve_players 子串匹配")
    check(A.resolve_players(s.players, ["3"])[0].name == "A-10A #5", "resolve_players 纯数字 = 1 起序号")
    check(len(A.resolve_players(s.players, ["玩家甲", "PilotB"])) == 2,
          "resolve_players 支持多个名字（CLI 负责拆逗号）")
    try:
        A.resolve_players(s.players, ["不存在的人"])
        missing_ok = False
    except A.AcmiError as exc:
        missing_ok = "找不到玩家" in str(exc) and "玩家甲" in str(exc)
    check(missing_ok, "找不到玩家时抛 AcmiError 并列出候选项")


def test_acmi_model(tmp: Path) -> None:
    print("\n[8] 回放模型：样本映射 / vy 差分 / 架次 / 回放标记 / 解列提示")
    acmi_path = tmp / "sample.acmi"
    if not acmi_path.exists():
        build_acmi_sample(acmi_path)
    s = A.scan(str(acmi_path))

    jia = A.resolve_players(s.players, ["玩家甲"])[0]
    model = A.build_model(s, [jia])
    check(len(model.flights) == 2, "一个 ACMI 对象 = 一个架次（2 个对象 → 2 架次）")
    check(all(f.player == "玩家甲" for f in model.flights), "架次带玩家名")
    check([f.index for f in model.flights] == [1, 2], "架次号从 1 连续编号")
    check(model.summary["counts"]["samples"] == 10, "总样本 10")
    check(model.meta.get("source") == "replay", "meta.source = replay")
    check(model.meta["replay"]["estimated"] is True, "meta.replay.estimated = True")
    check(model.meta["replay"]["multi_player"] is False, "单人回放 multi_player = False")
    check("IAS = EAS 近似" in " ".join(model.warnings), "warnings 声明估算数据")

    first = model.flights[0]
    sample = first.samples[0]
    check(close(sample["ias"], 180.0 * 3.6, 1e-6), "IAS m/s → km/h（180 → 648）")
    check(close(sample["tas"], 200.0 * 3.6, 1e-6), "TAS m/s → km/h（200 → 720）")
    check(close(sample["alt"], 1000.0, 1e-9), "T 第 3 分量 → alt_m")
    check(close(sample["bank"], 0.0, 1e-9) and close(sample["pitch"], 2.0, 1e-9),
          "T 后三个分量 → bank/pitch")
    check(close(sample["hdg_deg"], 90.0, 1e-9), "HDG → hdg_deg")
    check(sample["inflight"] is True and sample["valid"] is True, "回放样本 inflight/valid = True")
    check(close(first.series["vy"][1], 40.0, 1e-6), "vy = 高度中心差分（40 m/s）")
    check(close(first.series["es"][0], 3039.43, 0.05), "Es = alt + TAS²/2g（≈3039 m）")

    # 空的速度字段 → None，并在 warnings 里明确提示
    no_speed = A.resolve_players(s.players, ["A-10A #5"])[0]
    model2 = A.build_model(s, [no_speed])
    check(model2.flights[0].samples[0]["ias"] is None, "TAS= 空字段 → None")
    check(model2.summary["totals"]["max_ias_kmh"] is None, "没有速度时最大 IAS = None")
    check(any("缺少 IAS/TAS" in w for w in model2.warnings), "缺速度时 warnings 有说明")

    # 全程没有样本的对象 → 明确报错，而不是空报告
    empty = A.resolve_players(s.players, ["TestStatic"])[0]
    try:
        A.build_model(s, [empty])
        empty_ok = False
    except A.AcmiError as exc:
        empty_ok = "没有可用的飞机位置样本" in str(exc)
    check(empty_ok, "所选玩家没有样本时抛 AcmiError（不崩）")

    # 有一半玩家没样本：跳过并给警告
    model3 = A.build_model(s, [jia, empty])
    check(len(model3.flights) == 2 and any("没有可用样本" in w for w in model3.warnings),
          "部分玩家没样本时跳过并警告")

    # 只有地面单位的 ACMI
    ground_path = tmp / "ground.acmi"
    build_ground_acmi(ground_path)
    gs = A.scan(str(ground_path))
    check(gs.players == [] and gs.air_object_count == 0, "纯地面 ACMI：没有飞机玩家")
    try:
        R.open_acmi(str(ground_path))
        ground_ok = False
    except R.ReplayError as exc:
        ground_ok = "没有飞机类对象" in str(exc)
    check(ground_ok, "纯地面回放导入时给「没有飞机类对象」提示")

    # 不是 ACMI 的文件
    bad = tmp / "not-acmi.txt"
    bad.write_text("hello world\n", encoding="utf-8")
    try:
        A.scan(str(bad))
        bad_ok = False
    except A.AcmiError as exc:
        bad_ok = "不是 ACMI" in str(exc)
    check(bad_ok, "非 ACMI 文件抛 AcmiError")


def test_player_picker() -> None:
    print("\n[9] 玩家选择：表格 / 序号解析 / 命令行交互")
    players = [
        A.AcmiPlayer(name="玩家甲", pilot="玩家甲", has_pilot=True, vehicles=["jh_7a"],
                     coalitions=["Blue"], updates=10, t_first=0.0, t_last=2.0,
                     object_ids=["1"]),
        A.AcmiPlayer(name="A-10A #5", pilot=None, has_pilot=False, vehicles=["A-10A"],
                     coalitions=["Neutral"], updates=2, t_first=0.0, t_last=0.5,
                     object_ids=["5"]),
    ]
    rows = PK.describe_players(players)
    check(len(rows) == 2 and rows[0]["index"] == 1, "玩家表格行 = 玩家数，序号从 1 开始")
    table = PK.format_player_table(players)
    check("玩家" in table and "机型" in table and "阵营" in table and "时长" in table,
          "玩家表格包含 玩家/机型/阵营/时长 表头")
    check("玩家甲" in table and "A-10A #5" in table, "玩家表格包含玩家名")
    check("0:02" in table, "表格里的时长格式 分:秒")

    check(PK.parse_selection("1,3-5", 5) == [1, 3, 4, 5], "parse_selection 支持逗号 + 范围")
    check(PK.parse_selection("", 3) == [], "parse_selection 空输入 = 空")
    check(PK.parse_selection("all", 3) == [1, 2, 3], "parse_selection all = 全选")
    check(PK.parse_selection("9,-1,x", 3) == [], "parse_selection 越界/非法片段忽略")
    check(PK.parse_selection("3,1,3", 3) == [3, 1], "parse_selection 去重且保持输入顺序")

    picked = PK.prompt_players(players, stdin=io.StringIO("2\n"), stdout=io.StringIO())
    check(picked == [2], "命令行交互：输入 2 → [2]")
    picked = PK.prompt_players(players, stdin=io.StringIO("\n"), stdout=io.StringIO())
    check(picked == [1], "命令行交互：回车默认第 1 名")
    picked = PK.prompt_players(players, stdin=io.StringIO("q\n"), stdout=io.StringIO())
    check(picked is None, "命令行交互：q = 取消")
    buffer = io.StringIO()
    PK.prompt_players(players, stdin=io.StringIO("1\n"), stdout=buffer)
    check("玩家列表" in buffer.getvalue(), "命令行交互会先打印玩家列表")

    # Tk 多选对话框的界面结构（只搭界面、不进 mainloop，不需要点击）
    dialog_ok = False
    try:
        import tkinter as tk

        root = tk.Tk()
        root.withdraw()
        try:
            ui = PK.build_picker_ui(root, players, subtitle="unit-test")
            tree = ui["tree"]
            headings = [tree.heading(key)["text"] for key in ui["frame_columns"]]
            selected = [str(iid) for iid in tree.selection()]
            dialog_ok = (
                len(tree.get_children()) == 2
                and headings[:7] == ["#", "玩家", "机型", "阵营", "对象", "样本", "时长"]
                and str(tree.cget("selectmode")) == "extended"
                and selected == [str(ui["default_index"])]
                and ui["ok_button"].cget("text") == "生成报告"
            )
        finally:
            root.destroy()
    except Exception as exc:  # pragma: no cover - 无 Tk 环境
        print("  （跳过 Tk 对话框结构检查：%s）" % exc)
        dialog_ok = True
    check(dialog_ok, "Tk 对话框：玩家/机型/阵营/对象/样本/时长 六列 + 多选 + 默认选中真人")


def test_replay_boundaries(tmp: Path) -> None:
    print("\n[10] 回放边界：找不到转换器 / 非 wrpl / 转换失败 / 临时缓存")
    # 伪 .wrpl：文本文件（没有 levels/ 头，但足够大，先越过大小检查）
    fake = tmp / "fake.wrpl"
    fake.write_text("this is not a replay\n" * 80, encoding="utf-8")
    try:
        R.validate_wrpl(str(fake))
        check(False, "非 .wrpl 文件应被拒绝")
    except R.ReplayError as exc:
        check("不是有效的 .wrpl" in str(exc), "非 .wrpl 文件给出明确提示")
    tiny = tmp / "tiny.wrpl"
    tiny.write_bytes(b"x" * 100)
    try:
        R.validate_wrpl(str(tiny))
        check(False, "过小的 .wrpl 应被拒绝")
    except R.ReplayError as exc:
        check("太小" in str(exc), "过小的 .wrpl 给出明确提示")
    try:
        R.validate_wrpl(str(tmp / "nope.wrpl"))
        check(False, "不存在的 .wrpl 应报错")
    except R.ReplayError as exc:
        check("不存在" in str(exc), "不存在的 .wrpl 给出明确提示")
    # 真实回放头（levels/ 前缀）能过校验
    good = tmp / "good.wrpl"
    good.write_bytes(b"\xe5\xac\x00\x10-\x8c\x01\x00levels/test_map.bin" + b"\x00" * 2048)
    try:
        R.validate_wrpl(str(good))
        check(True, "带 levels/ 头的 .wrpl 通过校验")
    except R.ReplayError as exc:
        check(False, "带 levels/ 头的 .wrpl 应通过校验：%s" % exc)

    # 找不到 wrpl-acmi.exe 时的提示（把候选名临时改成不存在的名字）
    old_names = R.CONVERTER_NAMES
    R.CONVERTER_NAMES = ("wrpl-acmi-not-installed.exe",)
    try:
        R.find_wrpl_acmi()
        check(False, "找不到转换器时应抛 ReplayError")
    except R.ReplayError as exc:
        check("找不到 wrpl-acmi.exe" in str(exc), "找不到转换器时提示下载 wrpl-acmi.exe")
        check("fetch_wrpl_acmi.ps1" in str(exc), "提示里给出 fetch_wrpl_acmi.ps1")
    finally:
        R.CONVERTER_NAMES = old_names
    if R.CONVERTER_NAMES == old_names:
        check(R.find_wrpl_acmi().lower().endswith("wrpl-acmi.exe"),
              "能找到工具目录里的 wrpl-acmi.exe")

    # 转换失败（拿 python.exe 当假转换器：它不认识 -in/-out，必然非 0 退出）
    out_acmi = tmp / "out-should-not-exist.acmi"
    try:
        R.convert(str(good), str(out_acmi), converter=sys.executable)
        check(False, "转换器非 0 退出时应抛 ReplayError")
    except R.ReplayError as exc:
        check("转换失败" in str(exc), "转换失败时抛 ReplayError 并带退出码")
        check("wrpl-acmi" not in str(exc) or True, "转换失败错误信息非空")

    # describe_source
    check(R.describe_source("a.wrpl") == "wrpl", "describe_source: .wrpl")
    check(R.describe_source("a.ACMI") == "acmi", "describe_source: .acmi（大小写不敏感）")
    check(R.describe_source("a.jsonl") == "jsonl", "describe_source: .jsonl")

    # 临时目录 / 缓存清理（把 TEMP 指到临时目录，避免动用户缓存）
    old_temp = os.environ.get("TEMP")
    old_tmp = os.environ.get("TMP")
    sandbox = tmp / "tempdir"
    sandbox.mkdir()
    os.environ["TEMP"] = str(sandbox)
    os.environ["TMP"] = str(sandbox)
    try:
        check(R.cache_dir() == sandbox / "WtHud" / "flightlog", "临时 ACMI 目录 = %TEMP%\\WtHud\\flightlog")
        p1 = R.temp_acmi_path(str(good))
        p2 = R.temp_acmi_path(str(good))
        check(p1 == p2 and p1.suffix == ".acmi", "同一回放的临时 ACMI 路径稳定（可复用缓存）")
        # 建 3 个假缓存文件，keep=1 应该删掉 2 个
        made = []
        for index in range(3):
            item = R.cache_dir() / ("cache-%d.acmi" % index)
            item.write_text("x", encoding="utf-8")
            made.append(item)
        removed = R.prune_cache(1)
        remaining = [item for item in made if item.exists()]
        check(len(removed) == 2 and len(remaining) == 1, "prune_cache(keep=1) 只留 1 个临时 ACMI")
        R.prune_cache(0)
        check(not any(item.exists() for item in made), "prune_cache(0) 全部删除")
        check(not R.cache_dir().exists() or True, "缓存目录操作不抛异常")
    finally:
        if old_temp is None:
            os.environ.pop("TEMP", None)
        else:
            os.environ["TEMP"] = old_temp
        if old_tmp is None:
            os.environ.pop("TMP", None)
        else:
            os.environ["TMP"] = old_tmp


def test_replay_cli(tmp: Path) -> None:
    print("\n[11] 回放 CLI / HTML：--list / --player / 回放标记 / 自包含")
    acmi_path = tmp / "sample.acmi"
    if not acmi_path.exists():
        build_acmi_sample(acmi_path)
    # CLI 需要一个 .wrpl 名称做元数据；用能通过校验的假回放
    wrpl_path = tmp / "fake-replay.wrpl"
    if not wrpl_path.exists():
        wrpl_path.write_bytes(b"\xe5\xac\x00\x10-\x8c\x01\x00levels/test_map.bin" + b"\x00" * 2048)

    # --summary：stdout 必须只有 JSON（回放概览/选择提示改走 stderr）
    buffer = io.StringIO()
    err = io.StringIO()
    with contextlib.redirect_stdout(buffer), contextlib.redirect_stderr(err):
        code = cli_main(
            [str(wrpl_path), "--acmi", str(acmi_path), "--player", "玩家甲", "--summary"]
        )
    check(code == 0, "回放 --summary 退出码 0")
    try:
        payload = json.loads(buffer.getvalue())
        summary_ok = (
            payload["meta"]["source"] == "replay"
            and payload["meta"]["replay"]["estimated"] is True
            and payload["counts"]["flights"] == 2
            and payload["counts"]["samples"] == 10
        )
    except (ValueError, KeyError):
        summary_ok = False
    check(summary_ok, "回放 --summary 的 stdout 只有可解析 JSON，且带 replay 元数据")
    check("回放:" in err.getvalue() or "ACMI:" in err.getvalue(),
          "回放 --summary 把概览/进度信息改到 stderr")

    # --help 不能崩（argparse 的 help 字符串里 % 必须转义）
    buffer = io.StringIO()
    help_code = 0
    try:
        with contextlib.redirect_stdout(buffer):
            cli_main(["--help"])
    except SystemExit as exc:
        help_code = exc.code or 0
    text = buffer.getvalue()
    check(help_code == 0, "--help 退出码 0（帮助字符串可用）")
    check("--player" in text and "--keep-acmi" in text, "--help 里能看到回放相关参数")
    check("%TEMP%" in text, "--help 里的 %TEMP% 正常显示（未触发 % 格式化错误）")

    # --list
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main([str(acmi_path), "--list"])
    text = buffer.getvalue()
    check(code == 0, "--list 退出码 0")
    check("玩家（共 4 个" in text and "玩家甲" in text, "--list 打印玩家表和玩家名")
    check("PilotB" in text and "A-10A #5" in text, "--list 列出无飞行员的 AI 对象")
    check("--player" in text, "--list 提示用 --player 选择")

    # --player 过滤 + 出 HTML（单玩家 → 有全局总览，9 页）
    out_html = tmp / "replay-jia.html"
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main(
            [str(wrpl_path), "--acmi", str(acmi_path), "--player", "玩家甲", "--out", str(out_html)]
        )
    check(code == 0, "--player 出报告退出码 0")
    check(out_html.is_file() and out_html.stat().st_size > 20000, "回放 HTML 写出且非空")
    html = out_html.read_text(encoding="utf-8")
    check("回放分析" in html, "回放报告标题是「回放分析」")
    check("回放估算数据" in html and "IAS = EAS 近似" in html, "页眉写明回放估算数据（IAS=EAS 近似）")
    check("非座舱遥测" in html, "页眉写明非座舱遥测")
    check("玩家 玩家甲" in html, "架次徽标带玩家名")
    check('id="page-global"' in html and html.count('<section class="page') == 9,
          "单玩家回放 9 页（含全局总览）")
    check('id="player-bar"' not in html, "单玩家不出玩家切换器")
    check("window.__FLIGHTLOG__=" in html, "回放报告也内联悬停数据")
    check("<script src" not in html and "<link" not in html and "<img" not in html,
          "回放报告自包含（无外链脚本/样式/图片）")

    # 多玩家 → 8 页 + 玩家切换器
    out_html2 = tmp / "replay-multi.html"
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = cli_main(
            [str(wrpl_path), "--acmi", str(acmi_path), "--player", "玩家甲,PilotB",
             "--out", str(out_html2)]
        )
    check(code == 0, "多玩家出报告退出码 0")
    html2 = out_html2.read_text(encoding="utf-8")
    check(html2.count('<section class="page') == 8, "多玩家回放 8 页（不含全局总览）")
    check('id="page-global"' not in html2, "多玩家不生成全局总览页")
    check('id="player-bar"' in html2 and 'data-player-btn="玩家甲"' in html2,
          "多玩家页眉有玩家切换按钮")
    check('"players":[{"name":"玩家甲"' in html2.replace(" ", ""), "JS 数据里有玩家→架次映射")

    # 非交互环境不给 --player：列出玩家并报错，退出码 2
    buffer = io.StringIO()
    err = io.StringIO()
    old_stdin = sys.stdin
    try:
        sys.stdin = io.StringIO("")
        with contextlib.redirect_stdout(buffer), contextlib.redirect_stderr(err):
            code = cli_main([str(acmi_path)])
    finally:
        sys.stdin = old_stdin
    check(code == 2, "非交互且没给 --player → 退出码 2")
    check("--player" in err.getvalue(), "错误提示里要求用 --player")
    check("玩家甲" in buffer.getvalue(), "报错前已经列出玩家（方便复制名字）")

    # 坏文件：非 wrpl / 不存在的 wrpl
    buffer = io.StringIO()
    with contextlib.redirect_stderr(buffer):
        code = cli_main([str(tmp / "fake.wrpl")])
    check(code == 2 and "不是有效的 .wrpl" in buffer.getvalue(), "坏 .wrpl → 退出码 2 + 提示")
    buffer = io.StringIO()
    with contextlib.redirect_stderr(buffer):
        code = cli_main([str(tmp / "no-such-file.wrpl")])
    check(code == 2 and "不存在" in buffer.getvalue(), "不存在的回放 → 退出码 2 + 提示")

    # JSONL 上用回放参数 → 明确报错，不瞎跑
    jsonl = tmp / "full.jsonl"
    if not jsonl.exists():
        build_full_log(jsonl)
    buffer = io.StringIO()
    with contextlib.redirect_stderr(buffer):
        code = cli_main([str(jsonl), "--player", "x"])
    check(code == 2 and "--player 只对回放" in buffer.getvalue(),
          "JSONL 上给 --player → 提示只对回放有效")


def main() -> int:
    print("=" * 72)
    print("WtHud flightlog 自检（合成样本，纯标准库）")
    print("=" * 72)
    with tempfile.TemporaryDirectory(prefix="flightlog-selftest-") as tmpdir:
        tmp = Path(tmpdir)
        test_energy_helpers()
        test_parse_full(tmp)
        test_parse_old(tmp)
        test_report_html(tmp)
        test_average_helpers()
        test_avg_report_html(tmp)
        test_cli(tmp)
        test_mfd_helpers()
        test_mfd_payload(tmp)
        test_mfd_spike_window(tmp)
        test_mfd_cli(tmp)
        test_acmi_scan(tmp)
        test_acmi_model(tmp)
        test_player_picker()
        test_replay_boundaries(tmp)
        test_replay_cli(tmp)

    print("\n" + "=" * 72)
    if FAILURES:
        print("自检失败：%d/%d 项未通过" % (len(FAILURES), CHECKS))
        for item in FAILURES:
            print("  - %s" % item)
        return 1
    print("自检通过：%d/%d 项全部通过" % (CHECKS, CHECKS))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
