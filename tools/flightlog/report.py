#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""把解析后的飞行日志渲染成一个**自包含单文件 HTML**。

特点：

* 不依赖任何外部资源（无 CDN / 无图片 / 无第三方 JS），离线可直接双击打开；
* 图表全部用内联 SVG 在 Python 侧绘制好，**即使浏览器禁用脚本也能看到图**；
* 内联一小段 JS 只做"锦上添花"：鼠标悬停十字线 + 数值浮窗、架次跳转；
* 事件（死亡 / 复活 / 换机型）以竖线 + 标签画在每条时间轴上方，并叠加在全部图上；
* 多架次分段展示（每次 spawn / 换机型 / 时间跳变一节），顶栏还有全局总览。

坐标/颜色/面板配置都集中在文件开头的常量里，方便调整。
"""

from __future__ import annotations

import html
import json
import math
import os
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import EVENT_LABELS, Flight, LogModel

# ---------------------------------------------------------------------------
# 版式与配色
# ---------------------------------------------------------------------------

VIEW_W = 1180.0
MARGIN_L = 68.0
MARGIN_R = 82.0
PLOT_LEFT = MARGIN_L
PLOT_RIGHT = VIEW_W - MARGIN_R
PLOT_W = PLOT_RIGHT - PLOT_LEFT
PAD_TOP = 14.0
PAD_BOTTOM = 26.0

#: 单条曲线最多画多少个点（超过就等距抽稀；悬停数据仍是全量）
MAX_SVG_POINTS = 1800
#: 全局总览抽稀目标点数
MAX_OVERVIEW_POINTS = 2600
#: 序列在两条曲线之间断开的时间阈值（秒）
PATH_GAP_S = 2.5

COLORS = {
    "ias": "#4aa3ff",
    "tas": "#ffb347",
    "mach": "#d97bff",
    "alt": "#46d39a",
    "ralt": "#2f9e75",
    "es": "#ffd166",
    "des_pos": "#48d597",
    "des_neg": "#ff6b6b",
    "vy": "#7dd3fc",
    "aoa": "#f472b6",
    "ny": "#fbbf24",
    "pitch": "#60a5fa",
    "bank": "#a78bfa",
    "death": "#ff5c5c",
    "spawn": "#4ade80",
    "vehicle_change": "#60a5fa",
    "other": "#93a4b8",
    "grid": "#ffffff12",
    "axis": "#8fa1b3",
}

EVENT_COLORS = {
    "death": COLORS["death"],
    "spawn": COLORS["spawn"],
    "vehicle_change": COLORS["vehicle_change"],
    "end": COLORS["other"],
}

SERIES_ROUND = {
    "t": 3,
    "mach": 2,
    "valid": 0,
    "inflight": 0,
    "es": 1,
    "des": 1,
}


def _series_digits(key: str) -> int:
    return SERIES_ROUND.get(key, 1)


def _round_series(values: Sequence[Optional[float]], digits: int) -> List[Optional[float]]:
    out: List[Optional[float]] = []
    for value in values:
        if value is None:
            out.append(None)
        else:
            out.append(round(float(value), digits))
    return out


# ---------------------------------------------------------------------------
# 数值 / 坐标工具
# ---------------------------------------------------------------------------


def _fmt_number(value: Optional[float], digits: int = 1) -> str:
    if value is None:
        return "—"
    text = ("%." + str(digits) + "f") % value
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return text


def _fmt_coord(value: float) -> str:
    return "%.1f" % value


def _nice_ticks(lo: float, hi: float, target: int = 5) -> Tuple[float, float, float, List[float]]:
    """给出一个"好看"的坐标范围与刻度。"""
    if lo is None or hi is None or not math.isfinite(lo) or not math.isfinite(hi):
        lo, hi = 0.0, 1.0
    if hi - lo < 1e-9:
        # 常数序列：围绕数值展开成以 0 为基准的可读范围
        if abs(hi) < 1e-9:
            lo, hi = -1.0, 1.0
        else:
            pad = max(abs(hi) * 0.1, 1.0)
            lo, hi = hi - pad, hi + pad
    raw = (hi - lo) / max(1, target)
    if raw <= 0:
        raw = 1.0
    mag = 10.0 ** math.floor(math.log10(raw))
    norm = raw / mag
    if norm <= 1.0:
        step = 1.0 * mag
    elif norm <= 2.0:
        step = 2.0 * mag
    elif norm <= 2.5:
        step = 2.5 * mag
    elif norm <= 5.0:
        step = 5.0 * mag
    else:
        step = 10.0 * mag
    nlo = math.floor(lo / step) * step
    nhi = math.ceil(hi / step) * step
    ticks: List[float] = []
    v = nlo
    guard = 0
    while v <= nhi + step * 1e-6 and guard < 200:
        ticks.append(round(v, 10))
        v += step
        guard += 1
    return nlo, nhi, step, ticks


def _tick_digits(step: float) -> int:
    if step >= 10:
        return 0
    if step >= 1:
        return 0
    if step >= 0.1:
        return 1
    return 2


def _time_ticks(t0: float, t1: float, target: int = 8) -> Tuple[float, List[float]]:
    """时间轴刻度：只给"整"步长（1/2/5/10/15/30/60/120... 秒）。"""
    span = max(t1 - t0, 1e-6)
    raw = span / max(2, target)
    candidates = (0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600)
    step = candidates[-1]
    for cand in candidates:
        if raw <= cand:
            step = cand
            break
    start = math.ceil(t0 / step) * step
    ticks: List[float] = []
    v = start
    while v <= t1 + 1e-9:
        ticks.append(round(v, 6))
        v += step
    return step, ticks


def _fmt_time(value: float, step: float) -> str:
    if step >= 1:
        return "%d" % round(value)
    return ("%." + str(_tick_digits(step)) + "f") % value


def _iter_segments(
    times: Sequence[float], values: Sequence[Optional[float]], max_gap_s: float = PATH_GAP_S
) -> List[List[Tuple[float, float]]]:
    """把序列切成若干连续段（None 或大时间缺口处断开）。"""
    segments: List[List[Tuple[float, float]]] = []
    current: List[Tuple[float, float]] = []
    last_t: Optional[float] = None
    for t, v in zip(times, values):
        if t is None or v is None:
            if current:
                segments.append(current)
                current = []
            last_t = None
            continue
        if last_t is not None and (t - last_t) > max_gap_s:
            if current:
                segments.append(current)
                current = []
        current.append((t, v))
        last_t = t
    if current:
        segments.append(current)
    return segments


def _decimate(points: List[Tuple[float, float]], limit: int) -> List[Tuple[float, float]]:
    if len(points) <= limit:
        return points
    step = len(points) / float(limit)
    out = [points[0]]
    pos = step
    last_index = 0
    while pos < len(points) - 1:
        idx = int(round(pos))
        if idx != last_index:
            out.append(points[idx])
            last_index = idx
        pos += step
    out.append(points[-1])
    return out


class _Axis:
    """一条 y 轴：数据范围 → 画布坐标。刻度在创建时算好，保证刻度和坐标一致。"""

    def __init__(
        self,
        lo: float,
        hi: float,
        cmin: float,
        cmax: float,
        step: float = 0.0,
        ticks: Optional[Sequence[float]] = None,
    ) -> None:
        self.lo = lo
        self.hi = hi
        self.cmin = cmin
        self.cmax = cmax
        self.step = step
        self.ticks = list(ticks) if ticks is not None else []

    def __call__(self, value: Optional[float]) -> Optional[float]:
        if value is None:
            return None
        frac = (value - self.lo) / (self.hi - self.lo)
        # lo 在底部（cmin = y1），hi 在顶部（cmax = y0）
        return self.cmin - frac * (self.cmin - self.cmax)

    def as_dict(self) -> Dict[str, float]:
        return {"min": self.lo, "max": self.hi}


def _make_axis(values: Sequence[float], cmin: float, cmax: float) -> _Axis:
    lo, hi, step, ticks = _nice_ticks(min(values), max(values), target=5)
    return _Axis(lo, hi, cmin, cmax, step, ticks)


class _XAxis:
    def __init__(self, t0: float, t1: float, cmin: float, cmax: float) -> None:
        self.t0 = t0
        self.t1 = t1
        self.cmin = cmin
        self.cmax = cmax

    def __call__(self, t: float) -> float:
        frac = (t - self.t0) / (self.t1 - self.t0)
        return self.cmin + frac * (self.cmax - self.cmin)


# ---------------------------------------------------------------------------
# 面板配置
# ---------------------------------------------------------------------------


def _series(key: str, label: str, color: str, unit: str, axis: str = "left",
            decimals: int = 1, dash: bool = False, width: float = 1.7,
            clip: Optional[str] = None, fill: Optional[str] = None,
            tooltip: bool = True, signed: bool = False,
            tooltip_label: Optional[str] = None) -> Dict[str, Any]:
    return {
        "key": key,
        "label": label,
        "color": color,
        "unit": unit,
        "axis": axis,
        "decimals": decimals,
        "dash": dash,
        "width": width,
        "clip": clip,
        "fill": fill,
        "tooltip": tooltip,
        "signed": signed,
        "tooltip_label": tooltip_label,
    }


def _flight_panel_specs() -> List[Dict[str, Any]]:
    return [
        {
            "key": "speed",
            "title": "速度",
            "height": 250,
            "left_unit": "km/h",
            "right_unit": "Mach",
            "series": [
                _series("ias", "IAS 指示空速", COLORS["ias"], "km/h", decimals=0),
                _series("tas", "TAS 真空速", COLORS["tas"], "km/h", decimals=0),
                _series("mach", "Mach", COLORS["mach"], "", axis="right", decimals=2, width=1.4),
            ],
        },
        {
            "key": "altitude",
            "title": "高度",
            "height": 195,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.8),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.3),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es",
            "subtitle": "Es = 高度 + 速度² / 2g（米）",
            "height": 215,
            "left_unit": "m",
            "series": [
                _series("es", "比能量高度 Es", COLORS["es"], "m", decimals=0, width=2.0, fill="min"),
            ],
        },
        {
            "key": "energy_rate",
            "title": "能量变化率 dEs/dt",
            "subtitle": "正 = 获得能量（爬升或加速），负 = 损失能量",
            "height": 205,
            "left_unit": "m/s",
            "series": [
                _series("des", "获得能量", COLORS["des_pos"], "m/s", decimals=1, width=1.4,
                        clip="pos", fill="zero", tooltip=True, signed=True,
                        tooltip_label="dEs/dt"),
                _series("des", "损失能量", COLORS["des_neg"], "m/s", decimals=1, width=1.4,
                        clip="neg", fill="zero", tooltip=False, signed=True,
                        tooltip_label="dEs/dt"),
            ],
        },
        {
            "key": "vy",
            "title": "垂直速度 Vy",
            "height": 175,
            "left_unit": "m/s",
            "series": [
                _series("vy", "Vy 垂直速度", COLORS["vy"], "m/s", decimals=1, width=1.6,
                        fill="zero"),
            ],
        },
        {
            "key": "aoa_ny",
            "title": "迎角与过载",
            "height": 200,
            "left_unit": "°",
            "right_unit": "g",
            "series": [
                _series("aoa", "AoA 迎角", COLORS["aoa"], "°", decimals=1, width=1.6),
                _series("ny", "Ny 过载", COLORS["ny"], "g", axis="right", decimals=2),
            ],
        },
        {
            "key": "attitude",
            "title": "姿态",
            "height": 195,
            "left_unit": "°",
            "right_unit": "°",
            "series": [
                _series("pitch", "Pitch 俯仰", COLORS["pitch"], "°", decimals=1, width=1.6),
                _series("bank", "Bank 滚转", COLORS["bank"], "°", axis="right", decimals=1,
                        width=1.4),
            ],
        },
    ]


def _overview_panel_specs() -> List[Dict[str, Any]]:
    return [
        {
            "key": "speed",
            "title": "速度（全局）",
            "height": 145,
            "left_unit": "km/h",
            "right_unit": "Mach",
            "series": [
                _series("ias", "IAS", COLORS["ias"], "km/h", decimals=0, width=1.3),
                _series("tas", "TAS", COLORS["tas"], "km/h", decimals=0, width=1.3),
                _series("mach", "Mach", COLORS["mach"], "", axis="right", decimals=2, width=1.0),
            ],
        },
        {
            "key": "altitude",
            "title": "高度（全局）",
            "height": 140,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.3),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.0),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es（全局）",
            "height": 155,
            "left_unit": "m",
            "series": [
                _series("es", "Es", COLORS["es"], "m", decimals=0, width=1.5, fill="min"),
            ],
        },
    ]


# ---------------------------------------------------------------------------
# SVG 片段
# ---------------------------------------------------------------------------


def _svg_series(
    defn: Dict[str, Any],
    times: Sequence[float],
    values: Sequence[Optional[float]],
    xaxis: _XAxis,
    yaxis: _Axis,
) -> str:
    """渲染一条曲线（含正值/负值裁剪与面积填充）。"""
    key = defn["key"]
    clip = defn.get("clip")
    fill = defn.get("fill")
    if clip == "pos":
        series = [None if v is None else max(0.0, v) for v in values]
    elif clip == "neg":
        series = [None if v is None else min(0.0, v) for v in values]
    else:
        series = list(values)

    segments = _iter_segments(times, series)
    if not segments:
        return ""

    parts: List[str] = []
    for segment in segments:
        segment = _decimate(segment, MAX_SVG_POINTS)
        if len(segment) == 1:
            px = xaxis(segment[0][0])
            py = yaxis(segment[0][1])
            if py is None:
                continue
            parts.append(
                '<circle cx="%s" cy="%s" r="2.2" fill="%s"/>'
                % (_fmt_coord(px), _fmt_coord(py), defn["color"])
            )
            continue
        line = "M" + " L".join(
            "%s,%s" % (_fmt_coord(xaxis(t)), _fmt_coord(yaxis(v) or 0.0)) for t, v in segment
        )
        if fill:
            if fill == "zero":
                baseline = yaxis(0.0)
            else:
                baseline = yaxis(yaxis.lo)
            if baseline is not None:
                first_x = xaxis(segment[0][0])
                last_x = xaxis(segment[-1][0])
                area = (
                    "M%s,%s L"
                    % (_fmt_coord(first_x), _fmt_coord(baseline))
                    + " L".join(
                        "%s,%s" % (_fmt_coord(xaxis(t)), _fmt_coord(yaxis(v) or 0.0))
                        for t, v in segment
                    )
                    + " L%s,%s Z" % (_fmt_coord(last_x), _fmt_coord(baseline))
                )
                opacity = "0.16" if fill == "min" else "0.22"
                parts.append(
                    '<path d="%s" fill="%s" fill-opacity="%s" stroke="none"/>'
                    % (area, defn["color"], opacity)
                )
        dash = ' stroke-dasharray="5 4"' if defn.get("dash") else ""
        parts.append(
            '<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linejoin="round"'
            ' stroke-linecap="round"%s vector-effect="non-scaling-stroke"/>'
            % (line, defn["color"], defn.get("width", 1.7), dash)
        )
    return "".join(parts)


def _svg_axes(
    left: Optional[_Axis],
    right: Optional[_Axis],
    xstep: float,
    xticks: Sequence[float],
    xaxis: _XAxis,
    t_label: bool,
    height: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    # 水平网格 + 左轴刻度
    if left is not None:
        step = left.step
        digits = _tick_digits(step)
        for tick in left.ticks:
            py = left(tick)
            if py is None:
                continue
            is_zero = abs(tick) < 1e-9
            parts.append(
                '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="%s"/>'
                % (
                    _fmt_coord(PLOT_LEFT),
                    _fmt_coord(py),
                    _fmt_coord(PLOT_RIGHT),
                    _fmt_coord(py),
                    "#ffffff2a" if is_zero else COLORS["grid"],
                    1.0 if is_zero else 1.0,
                )
            )
            parts.append(
                '<text x="%s" y="%s" text-anchor="end" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(PLOT_LEFT - 8),
                    _fmt_coord(py + 3.6),
                    COLORS["axis"],
                    _fmt_number(tick, digits),
                )
            )
    # 右轴刻度
    if right is not None:
        step = right.step
        digits = _tick_digits(step)
        for tick in right.ticks:
            py = right(tick)
            if py is None:
                continue
            parts.append(
                '<text x="%s" y="%s" text-anchor="start" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(PLOT_RIGHT + 8),
                    _fmt_coord(py + 3.6),
                    COLORS["axis"],
                    _fmt_number(tick, digits),
                )
            )
    # 竖向网格 + 底部时间刻度
    for tick in xticks:
        px = xaxis(tick)
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1"/>'
            % (
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                COLORS["grid"],
            )
        )
        if t_label:
            parts.append(
                '<text x="%s" y="%s" text-anchor="middle" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(px),
                    _fmt_coord(height - 8),
                    COLORS["axis"],
                    _fmt_time(tick, xstep),
                )
            )
    # 边框
    parts.append(
        '<rect x="%s" y="%s" width="%s" height="%s" fill="none" stroke="#ffffff14"'
        ' stroke-width="1"/>'
        % (
            _fmt_coord(PLOT_LEFT),
            _fmt_coord(y0),
            _fmt_coord(PLOT_W),
            _fmt_coord(y1 - y0),
        )
    )
    return "".join(parts)


def _svg_events(
    events: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for event in events:
        t = event.get("t")
        if t is None or t < t0 - 0.05 or t > t1 + 0.05:
            continue
        px = min(max(xaxis(t), PLOT_LEFT), PLOT_RIGHT)
        color = EVENT_COLORS.get(event.get("kind", ""), COLORS["other"])
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1.4"'
            ' stroke-dasharray="5 4" opacity="0.85"/>'
            % (_fmt_coord(px), _fmt_coord(y0), _fmt_coord(px), _fmt_coord(y1), color)
        )
    return "".join(parts)


def _svg_regions(
    regions: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for region in regions:
        start = max(region["t0"], t0)
        end = min(region["t1"], t1)
        if end - start <= 1e-9:
            continue
        x = xaxis(start)
        width = xaxis(end) - x
        parts.append(
            '<rect x="%s" y="%s" width="%s" height="%s" fill="%s" fill-opacity="%s"/>'
            % (
                _fmt_coord(x),
                _fmt_coord(y0),
                _fmt_coord(width),
                _fmt_coord(y1 - y0),
                region.get("color", "#ff5c5c"),
                region.get("opacity", 0.08),
            )
        )
    return "".join(parts)


def _svg_vlines(
    lines: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for line in lines:
        t = line.get("t")
        if t is None or t < t0 or t > t1:
            continue
        px = xaxis(t)
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1"'
            ' stroke-dasharray="2 4" opacity="0.65"/>'
            % (
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                line.get("color", "#cbd5e1"),
            )
        )
    return "".join(parts)


def _render_chart(
    panel: Dict[str, Any],
    times: Sequence[float],
    series: Dict[str, Sequence[Optional[float]]],
    t0: float,
    t1: float,
    chart_id: str,
    t_label: bool,
    events: Sequence[Dict[str, Any]] = (),
    regions: Sequence[Dict[str, Any]] = (),
    vlines: Sequence[Dict[str, Any]] = (),
) -> Optional[Dict[str, Any]]:
    """画一张图，返回 (svg, 图例, 悬停几何)。没有数据时返回 None。"""
    height = float(panel["height"])
    y0, y1 = PAD_TOP, height - PAD_BOTTOM
    xaxis = _XAxis(t0, t1, PLOT_LEFT, PLOT_RIGHT)

    defs: List[Dict[str, Any]] = []
    for defn in panel["series"]:
        values = series.get(defn["key"])
        if values is None:
            continue
        if not any(v is not None for v in values):
            continue
        defs.append(defn)
    if not defs:
        return None

    left_values: List[float] = []
    right_values: List[float] = []
    for defn in defs:
        values = series.get(defn["key"], [])
        bucket = right_values if defn["axis"] == "right" else left_values
        bucket.extend(v for v in values if v is not None)
    left_axis: Optional[_Axis] = None
    right_axis: Optional[_Axis] = None
    if left_values:
        left_axis = _make_axis(left_values, y1, y0)
    if right_values:
        right_axis = _make_axis(right_values, y1, y0)

    xstep, xticks = _time_ticks(t0, t1, target=8)

    parts: List[str] = [
        '<svg id="svg-%s" viewBox="0 0 %.0f %.1f" class="chart-svg"'
        ' preserveAspectRatio="none" role="img" aria-label="%s">'
        % (chart_id, VIEW_W, height, html.escape(panel["title"]))
    ]
    parts.append(_svg_regions(regions, xaxis, t0, t1, y0, y1))
    parts.append(_svg_vlines(vlines, xaxis, t0, t1, y0, y1))
    parts.append(_svg_axes(left_axis, right_axis, xstep, xticks, xaxis, t_label, height, y0, y1))
    parts.append(_svg_events(events, xaxis, t0, t1, y0, y1))
    # 悬停层放在曲线下面，曲线再覆盖上去；十字线由 JS 追加在最上层
    hover_group: List[str] = []
    for defn in defs:
        axis = right_axis if defn["axis"] == "right" else left_axis
        if axis is None:
            continue
        values = series.get(defn["key"], [])
        if not any(v is not None for v in values):
            continue
        hover_group.append(_svg_series(defn, times, values, xaxis, axis))
    parts.append("".join(hover_group))
    parts.append("</svg>")

    legend_parts: List[str] = []
    for defn in defs:
        dash = ' class="dashed"' if defn.get("dash") else ""
        legend_parts.append(
            '<span class="lg"><i style="background:%s"%s></i>%s</span>'
            % (defn["color"], dash, html.escape(defn["label"]))
        )

    geometry = {
        "id": chart_id,
        "title": panel["title"],
        "x0": PLOT_LEFT,
        "x1": PLOT_RIGHT,
        "y0": y0,
        "y1": y1,
        "left": left_axis.as_dict() if left_axis is not None else None,
        "right": right_axis.as_dict() if right_axis is not None else None,
        "series": [
            {
                "key": defn["key"],
                "label": defn["label"],
                "color": defn["color"],
                "axis": defn["axis"],
                "clip": defn.get("clip"),
                "decimals": defn.get("decimals", 1),
                "unit": defn.get("unit", ""),
                "tooltip": defn.get("tooltip", True),
                "signed": bool(defn.get("signed")),
                "tooltipLabel": defn.get("tooltip_label"),
            }
            for defn in defs
        ],
    }
    return {
        "svg": "".join(parts),
        "legend": "".join(legend_parts),
        "geometry": geometry,
        "left_unit": panel.get("left_unit"),
        "right_unit": panel.get("right_unit"),
    }


def _render_event_strip(
    events: Sequence[Dict[str, Any]], t0: float, t1: float, strip_id: str
) -> Optional[str]:
    visible = [
        e
        for e in events
        if e.get("t") is not None and t0 - 0.05 <= e["t"] <= t1 + 0.05
    ]
    if not visible:
        return None
    height = 38.0
    xaxis = _XAxis(t0, t1, PLOT_LEFT, PLOT_RIGHT)
    parts: List[str] = [
        '<svg id="%s" viewBox="0 0 %.0f %.1f" class="event-strip-svg"'
        ' preserveAspectRatio="none" role="img" aria-label="事件时间线">'
        % (strip_id, VIEW_W, height)
    ]
    row_last_x = [-1e9, -1e9]
    for event in visible:
        t = event["t"]
        px = min(max(xaxis(t), PLOT_LEFT), PLOT_RIGHT)
        kind = event.get("kind", "")
        color = EVENT_COLORS.get(kind, COLORS["other"])
        label = event.get("label") or EVENT_LABELS.get(kind, kind)
        text = "%s %s" % (label, _fmt_number(t, 1) + "s")
        parts.append(
            '<line x1="%s" y1="10" x2="%s" y2="%.0f" stroke="%s" stroke-width="1.4"'
            ' opacity="0.9"/>'
            % (_fmt_coord(px), _fmt_coord(px), height, color)
        )
        parts.append(
            '<circle cx="%s" cy="10" r="2.6" fill="%s"/>' % (_fmt_coord(px), color)
        )
        row = 0
        if px - row_last_x[0] < 88 and px - row_last_x[1] >= 88:
            row = 1
        row_last_x[row] = px
        text_y = 12.0 if row == 0 else 25.0
        anchor = "middle"
        text_x = px
        if px < PLOT_LEFT + 40:
            anchor = "start"
            text_x = px + 3
        elif px > PLOT_RIGHT - 40:
            anchor = "end"
            text_x = px - 3
        parts.append(
            '<text x="%s" y="%s" text-anchor="%s" font-size="11" fill="%s">%s</text>'
            % (
                _fmt_coord(text_x),
                _fmt_coord(text_y),
                anchor,
                color,
                html.escape(text),
            )
        )
    parts.append("</svg>")
    return "".join(parts)


# ---------------------------------------------------------------------------
# 数据准备
# ---------------------------------------------------------------------------


def _events_dicts(flight: Flight) -> List[Dict[str, Any]]:
    return [
        {
            "kind": event.kind,
            "label": EVENT_LABELS.get(event.kind, event.kind),
            "t": event.t,
        }
        for event in flight.events
        if event.t is not None
    ]


def _visible_events(events: Sequence[Dict[str, Any]], t0: float, t1: float) -> List[Dict[str, Any]]:
    return [e for e in events if e["t"] is not None and t0 - 0.05 <= e["t"] <= t1 + 0.05]


def _invalid_regions(series: Dict[str, Sequence[Optional[float]]]) -> List[Dict[str, Any]]:
    """valid=false 的连续区间（>=0.25 s 才画）。"""
    times = series["t"]
    valid = series["valid"]
    regions: List[Dict[str, Any]] = []
    start: Optional[float] = None
    for t, v in zip(times, valid):
        if v:
            if start is not None:
                regions.append({"t0": start, "t1": t, "color": "#9aa7b8", "opacity": 0.10})
                start = None
        else:
            if start is None:
                start = t
    if start is not None and times:
        regions.append({"t0": start, "t1": times[-1], "color": "#9aa7b8", "opacity": 0.10})
    return [r for r in regions if r["t1"] - r["t0"] >= 0.25]


def _post_death_regions(flight: Flight) -> List[Dict[str, Any]]:
    if flight.death_t is None or not flight.samples:
        return []
    end_t = flight.samples[-1]["t"]
    if end_t <= flight.death_t + 0.1:
        return []
    return [{"t0": flight.death_t, "t1": end_t, "color": "#ff5c5c", "opacity": 0.10}]


def _render_all_panels(
    specs: Sequence[Dict[str, Any]],
    times: Sequence[float],
    series: Dict[str, Sequence[Optional[float]]],
    t0: float,
    t1: float,
    id_prefix: str,
    events: Sequence[Dict[str, Any]] = (),
    regions: Sequence[Dict[str, Any]] = (),
    vlines: Sequence[Dict[str, Any]] = (),
) -> List[Tuple[Dict[str, Any], Dict[str, Any]]]:
    """渲染一组面板；丢弃没有数据的面板，并让最后一个面板显示时间刻度。"""
    rendered: List[Tuple[Dict[str, Any], Dict[str, Any]]] = []
    for spec in specs:
        chart_id = "%s-%s" % (id_prefix, spec["key"])
        block = _render_chart(
            spec, times, series, t0, t1, chart_id, False, events, regions, vlines
        )
        if block is not None:
            rendered.append((spec, block))
    if not rendered:
        return []
    spec, _block = rendered[-1]
    chart_id = "%s-%s" % (id_prefix, spec["key"])
    last = _render_chart(spec, times, series, t0, t1, chart_id, True, events, regions, vlines)
    if last is not None:
        rendered[-1] = (spec, last)
    return rendered


def _build_flight_payload(flight: Flight) -> Dict[str, Any]:
    series = flight.series
    times = series["t"]
    t0, t1 = flight.start_t, flight.end_t
    if t1 - t0 < 0.5:
        t1 = t0 + 0.5

    payload_series: Dict[str, List[Optional[float]]] = {}
    for key, values in series.items():
        payload_series[key] = _round_series(values, _series_digits(key))

    events = _visible_events(_events_dicts(flight), t0, t1)
    regions = _invalid_regions(series) + _post_death_regions(flight)

    kept = _render_all_panels(
        _flight_panel_specs(),
        times,
        series,
        t0,
        t1,
        "f%d" % flight.index,
        events=events,
        regions=regions,
    )
    return {
        "index": flight.index,
        "title": "架次 %d · %s · %s – %s"
        % (
            flight.index,
            flight.vehicle or "未知机型",
            _fmt_number(t0, 1) + "s",
            _fmt_number(flight.end_t, 1) + "s",
        ),
        "reason_label": flight.stats.get("reason_label"),
        "t0": t0,
        "t1": t1,
        "times": payload_series["t"],
        "series": payload_series,
        "panels": [block["geometry"] for _spec, block in kept],
        "html": {
            "strip": _render_event_strip(events, t0, t1, "ev-f%d" % flight.index),
            "blocks": kept,
            "events": events,
        },
    }


def _overview_payload(model: LogModel) -> Optional[Dict[str, Any]]:
    flights = [f for f in model.flights if f.samples]
    if len(flights) < 2:
        return None
    times: List[float] = []
    merged: Dict[str, List[Optional[float]]] = {
        key: [] for key in ("ias", "tas", "mach", "alt", "ralt", "es", "valid", "inflight")
    }
    for flight in flights:
        times.extend(flight.series["t"])
        for key in merged:
            merged[key].extend(flight.series.get(key, []))
    if not times:
        return None
    # 抽稀：总览只需要形状，悬停用抽稀点也够用
    keep: List[int] = []
    if len(times) > MAX_OVERVIEW_POINTS:
        step = len(times) / float(MAX_OVERVIEW_POINTS)
        pos = 0.0
        seen = -1
        while pos < len(times):
            idx = min(len(times) - 1, int(round(pos)))
            if idx != seen:
                keep.append(idx)
                seen = idx
            pos += step
        if not keep or keep[-1] != len(times) - 1:
            keep.append(len(times) - 1)
    else:
        keep = list(range(len(times)))
    times_f = [times[i] for i in keep]
    series_f = {key: [vals[i] for i in keep] for key, vals in merged.items()}
    series_f["t"] = times_f

    t0 = min(times_f)
    t1 = max(times_f)
    if t1 - t0 < 0.5:
        t1 = t0 + 0.5
    events = [
        {"kind": e.kind, "label": EVENT_LABELS.get(e.kind, e.kind), "t": e.t}
        for e in model.events
        if e.t is not None and t0 - 0.05 <= e.t <= t1 + 0.05
    ]
    vlines: List[Dict[str, Any]] = []
    for flight in flights[1:]:
        vlines.append(
            {
                "t": flight.start_t,
                "label": "架次 %d" % flight.index,
                "color": "#cbd5e1",
            }
        )
    regions = _invalid_regions(series_f)
    for flight in flights:
        regions.extend(_post_death_regions(flight))

    kept = _render_all_panels(
        _overview_panel_specs(),
        times_f,
        series_f,
        t0,
        t1,
        "ov",
        events=events,
        regions=regions,
        vlines=vlines,
    )
    if not kept:
        return None
    rounded_times = _round_series(times_f, 3)
    return {
        "t0": t0,
        "t1": t1,
        "times": rounded_times,
        "series": {key: _round_series(vals, _series_digits(key)) for key, vals in series_f.items()},
        "panels": [block["geometry"] for _spec, block in kept],
        "html": {
            "blocks": kept,
            "strip": _render_event_strip(events, t0, t1, "ev-overview"),
            "events": events,
        },
    }


# ---------------------------------------------------------------------------
# HTML 组装
# ---------------------------------------------------------------------------


def _cards(model: LogModel) -> List[Dict[str, str]]:
    totals = model.summary["totals"]
    counts = model.summary["counts"]
    cards = [
        {"label": "总时长", "value": _fmt_number(totals["duration_s"], 1) + " s",
         "sub": "%s 个样本" % counts["samples"]},
        {"label": "架次", "value": str(counts["flights"]),
         "sub": "%s 个事件" % counts["events"]},
        {"label": "最大 IAS", "value": _fmt_number(totals["max_ias_kmh"], 0) + " km/h",
         "sub": "TAS %s km/h" % _fmt_number(totals["max_tas_kmh"], 0)},
        {"label": "最大 Mach", "value": _fmt_number(totals["max_mach"], 2),
         "sub": "最高 %s m" % _fmt_number(totals["max_alt_m"], 0)},
        {"label": "最大过载", "value": _fmt_number(totals["max_ny_g"], 2) + " g",
         "sub": "最大滚转 %s°" % _fmt_number(totals["max_bank_deg"], 1)},
        {"label": "比能量范围",
         "value": "%s – %s m" % (
             _fmt_number(totals["energy_min_m"], 0),
             _fmt_number(totals["energy_max_m"], 0),
         ),
         "sub": "空中能量变化 %s m"
         % _fmt_number(totals.get("energy_delta_inflight_m"), 0)},
    ]
    return cards


def _flight_badges(flight: Flight) -> List[str]:
    stats = flight.stats
    energy = stats["energy"]
    badges = [
        "时长 %s s" % _fmt_number(stats["duration_s"], 1),
        "样本 %d" % stats["samples"],
        "最大 IAS %s km/h" % _fmt_number(stats["ias"]["max"], 0),
        "最大 TAS %s km/h" % _fmt_number(stats["tas"]["max"], 0),
        "最大过载 %s g" % _fmt_number(stats["ny"]["abs_max"], 2),
        "Es %s – %s m" % (
            _fmt_number(energy["min"], 0),
            _fmt_number(energy["max"], 0),
        ),
        "能量获得 %s / 损失 %s m" % (
            _fmt_number(energy["gain"], 0),
            _fmt_number(energy["loss"], 0),
        ),
        "空中能量变化 %s m" % _fmt_number(energy.get("inflight_delta"), 0),
    ]
    if stats.get("death_t") is not None:
        badges.append("死亡于 %s s" % _fmt_number(stats["death_t"], 1))
    return badges


def _html_escape(value: Any) -> str:
    return html.escape("" if value is None else str(value))


CSS = """
:root{
  --bg:#0a0e13; --panel:#0f151d; --panel2:#121a24; --line:#1e2a36;
  --text:#dde6f0; --muted:#8ea1b4; --accent:#4aa3ff;
}
*{box-sizing:border-box}
html,body{margin:0;padding:0}
body{
  background:var(--bg); color:var(--text);
  font-family:"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",
    "Noto Sans CJK SC",sans-serif;
  font-size:14px; line-height:1.5;
  -webkit-font-smoothing:antialiased;
}
.wrap{max-width:1240px;margin:0 auto;padding:26px 20px 60px}
header h1{font-size:24px;margin:0 0 4px;font-weight:650;letter-spacing:.3px}
.subline{color:var(--muted);font-size:13px;word-break:break-all}
.subline code{background:#16202b;border:1px solid var(--line);padding:1px 5px;border-radius:4px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(158px,1fr));gap:10px;margin:16px 0 6px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px 13px}
.card b{display:block;font-size:18px;font-weight:650;letter-spacing:.2px;font-variant-numeric:tabular-nums}
.card span{display:block;font-size:12px;color:var(--muted);margin-top:1px}
.card .k{font-size:12px;color:var(--muted)}
.nav{display:flex;flex-wrap:wrap;gap:8px;margin:16px 0 18px;align-items:center}
.nav a{
  color:var(--text);text-decoration:none;font-size:13px;
  background:var(--panel2);border:1px solid var(--line);border-radius:999px;
  padding:5px 12px;transition:.15s;
}
.nav a:hover{border-color:var(--accent);color:#fff}
.nav .navlabel{color:var(--muted);font-size:12px;margin-right:2px}
.warn{
  background:#2c2113;border:1px solid #6a4a1c;color:#ffd79b;border-radius:9px;
  padding:9px 13px;font-size:13px;margin:12px 0;
}
.warn ul{margin:4px 0 0;padding-left:20px}
section.flight{
  background:var(--panel);border:1px solid var(--line);border-radius:13px;
  padding:16px 16px 10px;margin:18px 0;scroll-margin-top:14px;
}
section.flight:target{border-color:#31527a;box-shadow:0 0 0 1px #31527a inset}
.fhead{display:flex;flex-wrap:wrap;align-items:baseline;gap:10px;margin-bottom:2px}
.fhead h2{font-size:17px;margin:0;font-weight:650}
.reason{font-size:12px;color:#0a0e13;background:#7dd3fc;border-radius:4px;padding:1px 7px;font-weight:600}
.badges{display:flex;flex-wrap:wrap;gap:7px;margin:9px 0 4px}
.badges span{
  font-size:12px;color:#bfcedd;background:#141d27;border:1px solid var(--line);
  border-radius:6px;padding:2px 8px;font-variant-numeric:tabular-nums;
}
.panel{margin-top:14px}
.phead{display:flex;flex-wrap:wrap;align-items:baseline;gap:10px;justify-content:space-between}
.ptitle{font-size:14px;font-weight:650}
.psub{font-size:12px;color:var(--muted)}
.punit{font-size:12px;color:var(--muted);margin-left:auto}
.legend{display:flex;flex-wrap:wrap;gap:13px;font-size:12px;color:#b8c7d6;margin:3px 0 2px}
.lg{display:inline-flex;align-items:center;gap:5px;white-space:nowrap}
.lg i{display:inline-block;width:15px;height:3px;border-radius:2px}
.lg i.dashed{background-image:linear-gradient(90deg,#fff 0 60%,transparent 60% 100%)!important;background-size:7px 3px;background-repeat:repeat-x}
.chart-svg,.event-strip-svg{width:100%;height:auto;display:block;overflow:visible}
.event-strip-svg{margin-top:2px}
.chart-svg text{font-family:inherit}
svg .dot{pointer-events:none}
.note{font-size:12px;color:var(--muted);margin-top:18px;line-height:1.7}
.note code{background:#16202b;border:1px solid var(--line);padding:1px 5px;border-radius:4px}
.tip{
  position:fixed;z-index:999;display:none;pointer-events:none;
  background:#080c11f2;border:1px solid #2e4054;border-radius:9px;
  padding:8px 11px;font-size:12px;color:var(--text);
  box-shadow:0 10px 30px #00000066;min-width:180px;
}
.tip .tip-t{font-weight:650;margin-bottom:5px;color:#fff;font-variant-numeric:tabular-nums}
.tip table{border-collapse:collapse;width:100%}
.tip td{padding:1px 0;vertical-align:top}
.tip td.v{text-align:right;padding-left:14px;font-variant-numeric:tabular-nums;white-space:nowrap}
.tip td i{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:6px}
footer{color:var(--muted);font-size:12px;margin-top:28px;line-height:1.7}
a{color:var(--accent)}
@media (max-width:640px){
  .wrap{padding:16px 10px 40px}
  .cards{grid-template-columns:repeat(auto-fit,minmax(130px,1fr))}
  section.flight{padding:12px 10px 6px}
}
@media print{
  body{background:#fff;color:#111}
  section.flight{break-inside:avoid;border-color:#ccc;background:#fff}
  .card,.badges span{background:#f6f8fa;border-color:#ddd;color:#111}
  .subline,.card span,.badges span,.legend,.note,footer,.psub,.punit{color:#555}
  .nav{display:none}
}
"""

JS = r"""
(function(){
  "use strict";
  var DATA = window.__FLIGHTLOG__ || {};
  var NS = "http://www.w3.org/2000/svg";
  var tip = document.getElementById("tip");
  if (!tip) return;

  function nearest(times, t){
    var lo = 0, hi = times.length - 1;
    if (hi < 0) return -1;
    if (t <= times[0]) return 0;
    if (t >= times[hi]) return hi;
    while (lo < hi - 1){
      var mid = (lo + hi) >> 1;
      if (times[mid] <= t) lo = mid; else hi = mid;
    }
    return (t - times[lo] <= times[hi] - t) ? lo : hi;
  }

  function fmt(v, decimals){
    if (v === null || v === undefined || !isFinite(v)) return "—";
    if (decimals === undefined || decimals === null){
      var a = Math.abs(v);
      decimals = a >= 100 ? 0 : (a >= 10 ? 1 : 2);
    }
    return v.toFixed(decimals);
  }

  function signed(v, decimals){
    var s = fmt(Math.abs(v), decimals);
    if (v > 0) return "+" + s;
    if (v < 0) return "-" + s;
    return s;
  }

  function setup(desc){
    if (!desc || !desc.panels || !desc.panels.length) return;
    var panels = [];
    desc.panels.forEach(function(p){
      var svg = document.getElementById("svg-" + p.id);
      if (!svg || !svg.viewBox) return;
      var dots = [];
      (p.series || []).forEach(function(s){
        var c = document.createElementNS(NS, "circle");
        c.setAttribute("r", "3.2");
        c.setAttribute("fill", s.color);
        c.setAttribute("stroke", "#0a0e13");
        c.setAttribute("stroke-width", "0.9");
        c.setAttribute("opacity", "0");
        svg.appendChild(c);
        dots.push({el: c, s: s});
      });
      var line = document.createElementNS(NS, "line");
      line.setAttribute("stroke", "#e6eef7");
      line.setAttribute("stroke-width", "0.9");
      line.setAttribute("stroke-dasharray", "3 3");
      line.setAttribute("opacity", "0");
      svg.appendChild(line);
      panels.push({p: p, svg: svg, dots: dots, line: line});
    });
    if (!panels.length) return;

    function xFor(p, t){
      return p.x0 + (t - desc.t0) / (desc.t1 - desc.t0) * (p.x1 - p.x0);
    }
    function yFor(p, axisName, v){
      var b = axisName === "right" ? p.right : p.left;
      if (!b) return null;
      var frac = (v - b.min) / (b.max - b.min);
      return p.y1 - frac * (p.y1 - p.y0);
    }
    function hide(){
      tip.style.display = "none";
      panels.forEach(function(P){
        P.line.setAttribute("opacity", "0");
        P.dots.forEach(function(D){ D.el.setAttribute("opacity", "0"); });
      });
    }
    function show(index, clientX, clientY){
      var t = desc.times[index];
      var rows = [];
      panels.forEach(function(P){
        var px = xFor(P.p, t);
        P.line.setAttribute("x1", px);
        P.line.setAttribute("x2", px);
        P.line.setAttribute("y1", P.p.y0);
        P.line.setAttribute("y2", P.p.y1);
        P.line.setAttribute("opacity", "0.7");
        P.dots.forEach(function(D){
          var raw = desc.series[D.s.key] ? desc.series[D.s.key][index] : null;
          if (raw === null || raw === undefined){
            D.el.setAttribute("opacity", "0");
            return;
          }
          var v = raw;
          if (D.s.clip === "pos") v = Math.max(0, raw);
          else if (D.s.clip === "neg") v = Math.min(0, raw);
          if (D.s.clip && Math.abs(v) < 1e-9 && Math.abs(raw) > 1e-9){
            D.el.setAttribute("opacity", "0");
          } else {
            var cy = yFor(P.p, D.s.axis, v);
            if (cy === null){
              D.el.setAttribute("opacity", "0");
            } else {
              D.el.setAttribute("cx", px);
              D.el.setAttribute("cy", cy);
              D.el.setAttribute("opacity", "1");
            }
          }
          if (D.s.tooltip !== false && D.s.clip !== "neg"){
            var text = D.s.signed ? signed(raw, D.s.decimals) : fmt(raw, D.s.decimals);
            var unit = D.s.unit ? (" " + D.s.unit) : "";
            rows.push({label: D.s.tooltipLabel || D.s.label, color: D.s.color, text: text + unit});
          }
        });
      });
      var html = '<div class="tip-t">t = ' + t.toFixed(2) + " s</div><table>";
      rows.forEach(function(r){
        html += '<tr><td><i style="background:' + r.color + '"></i>' +
          r.label + '</td><td class="v">' + r.text + "</td></tr>";
      });
      html += "</table>";
      tip.innerHTML = html;
      tip.style.display = "block";
      var x = clientX + 16, y = clientY + 16;
      var w = tip.offsetWidth, h = tip.offsetHeight;
      if (x + w > window.innerWidth - 8) x = clientX - w - 16;
      if (y + h > window.innerHeight - 8) y = clientY - h - 16;
      tip.style.left = Math.max(4, x) + "px";
      tip.style.top = Math.max(4, y) + "px";
    }
    panels.forEach(function(P){
      P.svg.addEventListener("mousemove", function(ev){
        var rect = P.svg.getBoundingClientRect();
        if (!rect.width) return;
        var vb = P.svg.viewBox.baseVal.width || 1180;
        var vx = (ev.clientX - rect.left) / rect.width * vb;
        var t = desc.t0 + (vx - P.p.x0) / (P.p.x1 - P.p.x0) * (desc.t1 - desc.t0);
        var idx = nearest(desc.times, t);
        if (idx < 0) return;
        show(idx, ev.clientX, ev.clientY);
      });
      P.svg.addEventListener("mouseleave", hide);
      P.svg.addEventListener("touchstart", function(ev){
        var touch = ev.touches && ev.touches[0];
        if (!touch) return;
        var rect = P.svg.getBoundingClientRect();
        var vb = P.svg.viewBox.baseVal.width || 1180;
        var vx = (touch.clientX - rect.left) / rect.width * vb;
        var t = desc.t0 + (vx - P.p.x0) / (P.p.x1 - P.p.x0) * (desc.t1 - desc.t0);
        var idx = nearest(desc.times, t);
        if (idx >= 0) show(idx, touch.clientX, touch.clientY);
      }, {passive: true});
    });
  }

  if (DATA.overview) setup(DATA.overview);
  (DATA.flights || []).forEach(setup);

  // 架次导航：当前可见架次高亮
  var links = Array.prototype.slice.call(document.querySelectorAll(".nav a[data-target]"));
  if (links.length && "IntersectionObserver" in window){
    var map = {};
    links.forEach(function(a){ map[a.getAttribute("data-target")] = a; });
    var obs = new IntersectionObserver(function(entries){
      entries.forEach(function(entry){
        var a = map[entry.target.id];
        if (!a) return;
        if (entry.isIntersecting){
          links.forEach(function(x){ x.style.borderColor = ""; x.style.color = ""; });
          a.style.borderColor = "#4aa3ff";
          a.style.color = "#fff";
        }
      });
    }, {rootMargin: "-20% 0px -70% 0px"});
    Object.keys(map).forEach(function(id){
      var el = document.getElementById(id);
      if (el) obs.observe(el);
    });
  }
})();
"""


def _render_panel_html(
    block: Dict[str, Any], panel_spec: Dict[str, Any], is_last: bool = False
) -> str:
    units = []
    if block.get("left_unit"):
        units.append("左轴 %s" % _html_escape(block["left_unit"]))
    if block.get("right_unit"):
        units.append("右轴 %s" % _html_escape(block["right_unit"]))
    if is_last:
        units.append("时间轴 秒 (s)")
    subtitle = panel_spec.get("subtitle")
    parts = ['<div class="panel">', '<div class="phead">', '<div>']
    parts.append('<div class="ptitle">%s</div>' % _html_escape(panel_spec["title"]))
    if subtitle:
        parts.append('<div class="psub">%s</div>' % _html_escape(subtitle))
    parts.append("</div>")
    if units:
        parts.append('<div class="punit">%s</div>' % " · ".join(units))
    parts.append("</div>")
    parts.append('<div class="legend">%s</div>' % block["legend"])
    parts.append(block["svg"])
    parts.append("</div>")
    return "".join(parts)


def _build_payload(model: LogModel) -> Dict[str, Any]:
    flights_payload: List[Dict[str, Any]] = []
    sections: List[str] = []
    nav: List[str] = []

    for flight in model.flights:
        payload = _build_flight_payload(flight)
        flights_payload.append(
            {
                "index": payload["index"],
                "title": payload["title"],
                "t0": payload["t0"],
                "t1": payload["t1"],
                "times": payload["times"],
                "series": payload["series"],
                "panels": payload["panels"],
            }
        )
        specs = _flight_panel_specs()
        blocks = payload["html"]["blocks"]
        panel_html = "".join(
            _render_panel_html(block, spec, is_last=(i == len(blocks) - 1))
            for i, (spec, block) in enumerate(blocks)
        )
        badges = "".join("<span>%s</span>" % _html_escape(b) for b in _flight_badges(flight))
        strip = payload["html"]["strip"]
        section = [
            '<section class="flight" id="flight-%d">' % flight.index,
            '<div class="fhead">',
            "<h2>%s</h2>" % _html_escape(payload["title"]),
            '<span class="reason">%s</span>' % _html_escape(payload["reason_label"]),
            "</div>",
            '<div class="badges">%s</div>' % badges,
        ]
        if strip:
            section.append(strip)
        section.append(panel_html)
        section.append("</section>")
        sections.append("".join(section))
        nav.append(
            '<a href="#flight-%d" data-target="flight-%d">架次 %d</a>'
            % (flight.index, flight.index, flight.index)
        )

    overview = _overview_payload(model)
    overview_html = ""
    if overview is not None:
        blocks = overview["html"]["blocks"]
        panel_html = "".join(
            _render_panel_html(block, spec, is_last=(i == len(blocks) - 1))
            for i, (spec, block) in enumerate(blocks)
        )
        strip = overview["html"]["strip"] or ""
        overview_html = (
            '<section class="flight" id="overview">'
            '<div class="fhead"><h2>全局总览</h2>'
            '<span class="psub">全部架次拼在同一条时间轴上，竖虚线为架次边界</span></div>'
            + strip
            + panel_html
            + "</section>"
        )
        nav.insert(0, '<a href="#overview" data-target="overview">全局总览</a>')

    overview_data = None
    if overview is not None:
        overview_data = {key: value for key, value in overview.items() if key != "html"}

    return {
        "title": "飞行日志分析 · %s" % os.path.basename(model.path),
        "file": os.path.basename(model.path),
        "meta": model.meta,
        "cards": _cards(model),
        "warnings": model.warnings,
        "overview": overview_data,
        "flights": flights_payload,
        "html": {
            "overview": overview_html,
            "sections": sections,
            "nav": "".join(nav),
        },
    }


def render_report(model: LogModel, title: Optional[str] = None) -> str:
    """把模型渲染成完整 HTML 字符串。"""
    payload = _build_payload(model)
    page_title = title or payload["title"]

    cards_html = "".join(
        '<div class="card"><div class="k">%s</div><b>%s</b><span>%s</span></div>'
        % (_html_escape(card["label"]), _html_escape(card["value"]), _html_escape(card["sub"]))
        for card in payload["cards"]
    )

    warnings_html = ""
    if payload["warnings"]:
        items = "".join("<li>%s</li>" % _html_escape(w) for w in payload["warnings"])
        warnings_html = '<div class="warn"><b>解析提示</b><ul>%s</ul></div>' % items

    meta = model.meta or {}
    meta_bits = [
        "文件 <code>%s</code>" % _html_escape(payload["file"]),
        "机型 %s" % _html_escape(meta.get("vehicle") or "未知"),
        "WtHud %s" % _html_escape(meta.get("app") or "未知"),
        "schema %s" % _html_escape(meta.get("schema") if meta.get("schema") is not None else "?"),
        "采样 %s" % _html_escape(meta.get("rate") or "?"),
        "生成于 %s" % _html_escape(model.summary.get("generated_utc", "")),
    ]
    subline = " · ".join(meta_bits)

    data_json = json.dumps(
        {
            "title": page_title,
            "overview": payload["overview"],
            "flights": payload["flights"],
        },
        ensure_ascii=False,
        allow_nan=False,
        separators=(",", ":"),
    )
    # 避免数据里出现 </script> 提前闭合
    data_json = data_json.replace("</", "<\\/")

    nav_html = (
        '<div class="nav"><span class="navlabel">跳转</span>%s</div>' % payload["html"]["nav"]
        if payload["html"]["nav"]
        else ""
    )

    footer = (
        '<footer>'
        "比能量高度 <code>Es = alt_m + (tas_kmh/3.6)&sup2; / (2&times;9.80665)</code>（米）；"
        "能量变化率 dEs/dt 为滑动窗口线性回归，正值表示获得能量。"
        "<br>图表由 Python 内联 SVG 生成，悬停查看该时刻各项数值；本文件不依赖网络与任何外部资源。"
        "</footer>"
    )

    return (
        "<!DOCTYPE html>\n"
        '<html lang="zh-CN">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
        "<title>%s</title>\n<style>%s</style>\n</head>\n<body>\n"
        '<div class="wrap">\n'
        "<header>\n<h1>飞行日志分析</h1>\n"
        '<div class="subline">%s</div>\n'
        '<div class="cards">%s</div>\n'
        "%s\n%s\n"
        "</header>\n<main>\n%s\n%s\n</main>\n"
        "%s\n</div>\n"
        '<div id="tip" class="tip"></div>\n'
        "<script>window.__FLIGHTLOG__=%s;</script>\n"
        "<script>%s</script>\n"
        "</body>\n</html>\n"
        % (
            _html_escape(page_title),
            CSS,
            subline,
            cards_html,
            warnings_html,
            nav_html,
            payload["html"]["overview"],
            "\n".join(payload["html"]["sections"]),
            footer,
            data_json,
            JS,
        )
    )


def write_report(model: LogModel, out_path: str, title: Optional[str] = None) -> str:
    """渲染并写盘（UTF-8，自动建目录），返回绝对路径。"""
    out_path = os.path.abspath(out_path)
    directory = os.path.dirname(out_path)
    if directory:
        os.makedirs(directory, exist_ok=True)
    content = render_report(model, title=title)
    with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(content)
    return out_path
