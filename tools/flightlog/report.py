#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""把解析后的飞行日志渲染成一个**自包含单文件 HTML（翻页式）**。

特点：

* **翻页视图**：第 1 页是概览（汇总卡片 + 比能量缩略图），之后每个数据主题一页
  （速度 / 高度 / 比能量 Es / dEs/dt / Vy / 迎角与过载 / 姿态），多架次时最后追加
  "全局总览"页；每页固定铺满视口，不做长滚动。
* 翻页交互：底部 ``‹ 上一页`` / ``下一页 ›``、页码 ``第 3 / 8 页``、圆点导航、
  左右两侧大按钮，键盘 ←/→、PageUp/PageDown、Home/End 均可翻页；
* 多架次：页眉的架次选择器切换后，**当前页**的曲线换成该架次（事件竖线/标签保留）；
* 速度页顶部显示**平均 IAS / TAS / Mach**，起止时间可手动设置（默认整个架次），
  所选区间在图上用浅色底纹 + 边界虚线标出、平均值画成对应颜色的水平虚线；
* 图表全部用内联 SVG 在 Python 侧画好，**浏览器禁用脚本也能看到全部图**；
* 内联 JS 只负责"锦上添花"：翻页、架次切换、鼠标悬停十字线 + 数值浮窗；
* 事件（死亡 / 复活 / 换机型）以竖线 + 标签画在每条时间轴上方，并贯穿全部图表；
* 不依赖任何外部资源（无 CDN / 图片 / 第三方 JS），离线双击即看。

坐标/颜色/面板配置都集中在文件开头的常量里，方便调整。
"""

from __future__ import annotations

import html
import json
import math
import os
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import EVENT_LABELS, Flight, LogModel, average_speed

# ---------------------------------------------------------------------------
# 版式与配色
# ---------------------------------------------------------------------------

#: SVG 内部坐标系宽度。页面用 preserveAspectRatio="none" 铺满容器，
#: 因此这个值只决定"文字/线宽在 1500 单位里多大"，不决定实际像素。
VIEW_W = 1500.0
MARGIN_L = 78.0
MARGIN_R = 96.0
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

#: 单页图表的 viewBox 高度（页面铺满时按容器比例拉伸）
PAGE_CHART_H = 650.0
#: 概览页比能量缩略图的 viewBox 高度
THUMB_CHART_H = 470.0
#: 全局总览页每张堆叠图的高度
GLOBAL_CHART_H = 200.0

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
    # 平均速度统计区间的底纹（中性浅蓝，避免和三条曲线颜色混淆）
    "avg_band": "#6ea8ff",
    "avg_edge": "#9dc4ff",
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
# 面板配置（每个 key 对应报告里的一页）
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
    """单架次的 7 个数据主题（顺序 = 报告里的页面顺序）。"""
    height = PAGE_CHART_H
    return [
        {
            "key": "speed",
            "title": "速度",
            "subtitle": "IAS / TAS（左轴 km/h）与 Mach（右轴）",
            "height": height,
            "left_unit": "km/h",
            "right_unit": "Mach",
            "series": [
                _series("ias", "IAS 指示空速", COLORS["ias"], "km/h", decimals=0),
                _series("tas", "TAS 真空速", COLORS["tas"], "km/h", decimals=0),
                _series("mach", "Mach", COLORS["mach"], "", axis="right", decimals=2, width=1.2),
            ],
        },
        {
            "key": "altitude",
            "title": "高度",
            "subtitle": "气压高度 + 雷达高度（虚线，有数据才画）",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.8),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.3),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es",
            "subtitle": "Es = 高度 + 速度² / 2g（米），面积填充",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("es", "比能量高度 Es", COLORS["es"], "m", decimals=0, width=2.0, fill="min"),
            ],
        },
        {
            "key": "energy_rate",
            "title": "能量变化率 dEs/dt",
            "subtitle": "绿色 = 获得能量（爬升或加速），红色 = 损失能量（m/s）",
            "height": height,
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
            "subtitle": "爬升率为正、下降率为负（m/s）",
            "height": height,
            "left_unit": "m/s",
            "series": [
                _series("vy", "Vy 垂直速度", COLORS["vy"], "m/s", decimals=1, width=1.6,
                        fill="zero"),
            ],
        },
        {
            "key": "aoa_ny",
            "title": "迎角与过载",
            "subtitle": "AoA 迎角（左轴 °）+ Ny 过载（右轴 g）",
            "height": height,
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
            "subtitle": "Pitch 俯仰（左轴 °）+ Bank 滚转（右轴 °）",
            "height": height,
            "left_unit": "°",
            "right_unit": "°",
            "series": [
                _series("pitch", "Pitch 俯仰", COLORS["pitch"], "°", decimals=1, width=1.6),
                _series("bank", "Bank 滚转", COLORS["bank"], "°", axis="right", decimals=1,
                        width=1.2),
            ],
        },
    ]


def _thumb_panel_spec() -> Dict[str, Any]:
    """概览页的比能量缩略图。"""
    return {
        "key": "es_thumb",
        "title": "比能量高度 Es",
        "subtitle": "Es = 高度 + 速度² / 2g（米）",
        "height": THUMB_CHART_H,
        "left_unit": "m",
        "series": [
            _series("es", "比能量高度 Es", COLORS["es"], "m", decimals=0, width=2.0, fill="min"),
        ],
    }


def _overview_panel_specs() -> List[Dict[str, Any]]:
    """多架次"全局总览"页：三张堆叠的合并时间轴图。"""
    height = GLOBAL_CHART_H
    return [
        {
            "key": "speed",
            "title": "速度（全局）",
            "height": height,
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
            "height": height,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.3),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.0),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es（全局）",
            "height": height,
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


def _svg_avg_overlay(
    avg: Dict[str, Any],
    chart_id: str,
    xaxis: _XAxis,
    left: Optional[_Axis],
    right: Optional[_Axis],
    y0: float,
    y1: float,
) -> str:
    """速度页的"平均速度统计区间"图层：半透明竖条 + 两条边界线 + 平均速度虚线。

    id 规则固定为 ``avg-<kind>-<chart_id>``（chart_id 形如 ``f1-speed``），
    内联 JS 在用户改范围后按同样的 id 原地更新几何，不需要重画整张 SVG。
    """
    start = max(float(avg["range_start"]), xaxis.t0)
    end = min(float(avg["range_end"]), xaxis.t1)
    if end < start:
        return ""
    parts: List[str] = []
    x1 = xaxis(start)
    x2 = xaxis(end)
    band_w = max(0.0, x2 - x1)
    # 统计区间浅色底纹（在曲线之上、但只有 7% 不透明度，不影响看曲线）
    parts.append(
        '<rect id="avg-band-%s" class="avg-band" x="%s" y="%s" width="%s" height="%s"'
        ' fill="%s" fill-opacity="0.09"/>'
        % (
            chart_id,
            _fmt_coord(x1),
            _fmt_coord(y0),
            _fmt_coord(band_w),
            _fmt_coord(y1 - y0),
            COLORS["avg_band"],
        )
    )
    # 区间两条边界：竖向细虚线
    for slot, px in (("edge0", x1), ("edge1", x2)):
        parts.append(
            '<line id="avg-%s-%s" class="avg-edge" x1="%s" y1="%s" x2="%s" y2="%s"'
            ' stroke="%s" stroke-width="1" stroke-dasharray="3 4" opacity="0.6"/>'
            % (
                slot,
                chart_id,
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                COLORS["avg_edge"],
            )
        )
    # 平均 IAS / 平均 TAS（左轴）与平均 Mach（右轴）的水平虚线
    for key, axis, dash in (
        ("ias", left, "8 5"),
        ("tas", left, "8 5"),
        ("mach", right, "2 5"),
    ):
        value = avg.get(key)
        if value is None or axis is None:
            continue
        py = axis(value)
        if py is None:
            continue
        parts.append(
            '<line id="avg-%s-%s" class="avg-line" data-avg-line="%s" x1="%s" y1="%s"'
            ' x2="%s" y2="%s" stroke="%s" stroke-width="1.5" stroke-dasharray="%s"'
            ' opacity="0.85"/>'
            % (
                key,
                chart_id,
                key,
                _fmt_coord(PLOT_LEFT),
                _fmt_coord(py),
                _fmt_coord(PLOT_RIGHT),
                _fmt_coord(py),
                COLORS[key],
                dash,
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
    avg: Optional[Dict[str, Any]] = None,
) -> Optional[Dict[str, Any]]:
    """画一张图，返回 (svg, 图例, 悬停几何)。没有数据时返回 None。

    ``avg`` 非空时（只有速度页用）额外画"平均速度统计区间"图层：半透明竖条 +
    边界虚线 + 平均 IAS/TAS/Mach 的水平虚线；默认范围 = 整个架次。
    """
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
    if avg is not None:
        parts.append(_svg_avg_overlay(avg, chart_id, xaxis, left_axis, right_axis, y0, y1))
    parts.append("</svg>")

    legend_parts: List[str] = []
    for defn in defs:
        dash = ' class="dashed"' if defn.get("dash") else ""
        legend_parts.append(
            '<span class="lg"><i style="background:%s"%s></i>%s</span>'
            % (defn["color"], dash, html.escape(defn["label"]))
        )
    if avg is not None:
        legend_parts.append(
            '<span class="lg avg-legend"><i class="avgdash"></i>'
            "平均速度（水平虚线，数值见上方；底纹 = 统计区间）</span>"
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
        '<svg id="%s" viewBox="0 0 %.0f %.1f" class="strip-svg"'
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

    # 每个数据主题渲染一张独立的 SVG（每页一张，都带时间刻度）
    blocks: Dict[str, Dict[str, Any]] = {}
    panels: List[Dict[str, Any]] = []
    default_avg: Optional[Dict[str, Any]] = None
    for spec in _flight_panel_specs():
        chart_id = "f%d-%s" % (flight.index, spec["key"])
        chart_avg: Optional[Dict[str, Any]] = None
        if spec["key"] == "speed":
            # 默认统计范围 = 整个当前架次（用户可在页面上手动改）
            chart_avg = average_speed(flight.samples, t0, t1)
            default_avg = chart_avg
        block = _render_chart(
            spec, times, series, t0, t1, chart_id, True,
            events=events, regions=regions, avg=chart_avg,
        )
        if block is None:
            continue
        blocks[spec["key"]] = block
        panels.append(block["geometry"])

    thumb_spec = _thumb_panel_spec()
    thumb = _render_chart(
        thumb_spec,
        times,
        series,
        t0,
        t1,
        "ovf%d-es" % flight.index,
        True,
        events=events,
        regions=regions,
    )
    if thumb is not None:
        panels.append(thumb["geometry"])

    return {
        "index": flight.index,
        "title": "架次 %d · %s · %s – %s"
        % (
            flight.index,
            flight.vehicle or "未知机型",
            _fmt_number(t0, 1) + "s",
            _fmt_number(flight.end_t, 1) + "s",
        ),
        "vehicle": flight.vehicle or "未知机型",
        "reason_label": flight.stats.get("reason_label"),
        "t0": t0,
        "t1": t1,
        "times": payload_series["t"],
        "series": payload_series,
        "panels": panels,
        "blocks": blocks,
        "thumb": thumb,
        "avg_default": default_avg,
        "events": events,
        "badges": _flight_badges(flight),
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
# 卡片 / 徽标
# ---------------------------------------------------------------------------


def _cards(model: LogModel) -> List[Dict[str, str]]:
    totals = model.summary["totals"]
    counts = model.summary["counts"]
    time = model.summary.get("time") or {}
    span = time.get("span_s") or 0.0
    samples = counts.get("samples") or 0
    rate = (samples - 1) / span if span > 0 and samples > 1 else None

    event_counts = model.summary.get("event_counts") or {}
    ev_bits = []
    for kind, label in (("death", "死亡"), ("spawn", "复活"), ("vehicle_change", "换机型")):
        if event_counts.get(kind):
            ev_bits.append("%s %d" % (label, event_counts[kind]))

    ralt_max: Optional[float] = None
    for flight in model.flights:
        value = (flight.stats.get("ralt") or {}).get("max")
        if value is not None and (ralt_max is None or value > ralt_max):
            ralt_max = value

    cards = [
        {
            "label": "总时长",
            "value": _fmt_number(totals["duration_s"], 1) + " s",
            "sub": "%s 个样本" % samples,
        },
        {
            "label": "采样率",
            "value": (_fmt_number(rate, 1) + " Hz") if rate else "—",
            "sub": "采样 %s" % (model.meta.get("rate") or "?"),
        },
        {
            "label": "最大 IAS",
            "value": _fmt_number(totals["max_ias_kmh"], 0) + " km/h",
            "sub": "TAS %s km/h" % _fmt_number(totals["max_tas_kmh"], 0),
        },
        {
            "label": "最大 Mach",
            "value": _fmt_number(totals["max_mach"], 2),
            "sub": "最大高度 %s m" % _fmt_number(totals["max_alt_m"], 0),
        },
        {
            "label": "最大高度",
            "value": _fmt_number(totals["max_alt_m"], 0) + " m",
            "sub": "雷达 %s m" % _fmt_number(ralt_max, 0),
        },
        {
            "label": "最大过载",
            "value": _fmt_number(totals["max_ny_g"], 2) + " g",
            "sub": "最大滚转 %s°" % _fmt_number(totals["max_bank_deg"], 1),
        },
        {
            "label": "比能量范围",
            "value": "%s – %s m"
            % (
                _fmt_number(totals["energy_min_m"], 0),
                _fmt_number(totals["energy_max_m"], 0),
            ),
            "sub": "空中能量变化 %s m"
            % _fmt_number(totals.get("energy_delta_inflight_m"), 0),
        },
        {
            "label": "事件数",
            "value": str(counts["events"]),
            "sub": " · ".join(ev_bits) if ev_bits else "无事件",
        },
    ]
    return cards


def _flight_badges(flight: Flight) -> List[str]:
    stats = flight.stats
    energy = stats["energy"]
    badges = [
        "机型 %s" % (flight.vehicle or "未知"),
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


# ---------------------------------------------------------------------------
# 页面组装
# ---------------------------------------------------------------------------


def _strip_html(flight_payload: Dict[str, Any], page_key: str) -> str:
    """一页一份事件时间线（id 按 架次+页 唯一，避免重复 id）。"""
    strip = _render_event_strip(
        flight_payload["events"],
        flight_payload["t0"],
        flight_payload["t1"],
        "ev-f%d-%s" % (flight_payload["index"], page_key),
    )
    return strip or ""


def _flight_view_html(
    flight_payload: Dict[str, Any],
    page_key: str,
    block: Optional[Dict[str, Any]],
    is_active: bool,
    badges_html: str = "",
) -> str:
    """一页里"某个架次"的可切换内容：事件条 + 图例 + 图。"""
    parts = [
        '<div class="flight-view%s" data-flight="%d">'
        % (" active" if is_active else "", flight_payload["index"])
    ]
    if badges_html:
        parts.append(badges_html)
    strip = _strip_html(flight_payload, page_key)
    if strip:
        parts.append('<div class="strip">%s</div>' % strip)
    if block is None:
        parts.append('<div class="empty">该架次没有此项数据</div>')
    else:
        parts.append('<div class="legend">%s</div>' % block["legend"])
        parts.append('<div class="chart-wrap">%s</div>' % block["svg"])
    parts.append("</div>")
    return "".join(parts)


def _fmt_fixed(value: Any, digits: int) -> str:
    """固定小数位（不做去尾零），保证静态数值和 JS 更新后的格式一致。"""
    if value is None:
        return "—"
    return ("%." + str(digits) + "f") % float(value)


def _fmt_seconds_input(value: float) -> str:
    """秒数输入框的值：最多 3 位小数、去掉尾零（0.342 / 30 / 293.58）。"""
    text = "%.3f" % float(value)
    text = text.rstrip("0").rstrip(".")
    return text or "0"


def _avg_bar_html(flight_payload: Dict[str, Any]) -> str:
    """速度页顶部的"平均速度 + 可手动设置计算范围"控件条。

    静态 HTML 里放的是**默认范围（整个当前架次）**的平均值；内联 JS 加载后
    会用同样的数据重算一遍，并在用户点"应用"时原地更新数值和 SVG 图层。
    """
    avg = flight_payload.get("avg_default") or {}
    t0 = float(flight_payload["t0"])
    t1 = float(flight_payload["t1"])
    flight_index = int(flight_payload["index"])
    channels = (("ias", "IAS", "km/h", 1), ("tas", "TAS", "km/h", 1), ("mach", "Mach", "", 2))
    value_parts: List[str] = []
    for key, label, unit, digits in channels:
        unit_text = (" · " + unit) if unit else ""
        value_parts.append(
            '<div class="avg-val"><span class="avg-k"><i class="avgdash"'
            ' style="color:%s"></i>平均 %s%s</span>'
            '<b data-avg-val="%s">%s</b></div>'
            % (COLORS[key], label, unit_text, key, _fmt_fixed(avg.get(key), digits))
        )
    range_start = avg.get("range_start", t0) if avg else t0
    range_end = avg.get("range_end", t1) if avg else t1
    sample_count = avg.get("samples") if avg else None
    if avg:
        meta_text = "范围 %s–%s s · %s 样本 · 底纹 = 统计区间，虚线 = 平均值（回车 = 应用）" % (
            _fmt_fixed(range_start, 1),
            _fmt_fixed(range_end, 1),
            sample_count,
        )
    else:
        meta_text = "该架次没有速度数据"
    disabled = "" if avg else " disabled"
    return (
        '<div class="avgbar" id="avgbar-speed" data-avg-flight="%d"'
        ' data-avg-t0="%s" data-avg-t1="%s">'
        '<div class="avg-title">平均速度<b>可手动设置计算范围</b></div>'
        '<div class="avg-vals">%s</div>'
        '<div class="avg-ctl">'
        '<label class="avg-range">范围'
        '<input id="avg-start-speed" class="avg-input" type="number" data-avg-start'
        ' step="0.1" min="%s" max="%s" value="%s" inputmode="decimal"'
        ' aria-label="平均速度统计开始时间（秒）"%s>'
        '<span>s</span><span class="avg-dash">–</span>'
        '<input id="avg-end-speed" class="avg-input" type="number" data-avg-end'
        ' step="0.1" min="%s" max="%s" value="%s" inputmode="decimal"'
        ' aria-label="平均速度统计结束时间（秒）"%s>'
        "<span>s</span></label>"
        '<button type="button" class="avg-btn" data-avg-apply>应用</button>'
        '<button type="button" class="avg-btn ghost" data-avg-reset>重置</button>'
        "</div>"
        '<div class="avg-meta"><span data-avg-meta>%s</span>'
        '<span class="avg-msg" data-avg-msg role="status" aria-live="polite"></span></div>'
        "</div>"
        % (
            flight_index,
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            "".join(value_parts),
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            _fmt_seconds_input(range_start),
            disabled,
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            _fmt_seconds_input(range_end),
            disabled,
            _html_escape(meta_text),
        )
    )


def _overview_page_html(
    flight_payloads: Sequence[Dict[str, Any]],
    default_index: int,
    cards_html: str,
    warnings_html: str,
) -> str:
    views: List[str] = []
    for flight_payload in flight_payloads:
        badges = "".join("<span>%s</span>" % _html_escape(b) for b in flight_payload["badges"])
        views.append(
            _flight_view_html(
                flight_payload,
                "overview",
                flight_payload["thumb"],
                flight_payload["index"] == default_index,
                badges_html='<div class="badges">%s</div>' % badges,
            )
        )
    return (
        '<section class="page active" id="page-overview" data-page="overview">'
        '<div class="page-head"><div class="page-title">概览</div>'
        '<div class="page-sub">汇总指标 + 比能量缩略图；多架次时用右上角切换</div>'
        '<div class="page-right">按 ← → 或 PageUp/PageDown 翻页</div></div>'
        '<div class="page-body"><div class="ovwrap">'
        '<div class="cards">%s</div>'
        "%s"
        '<div class="ovchart">%s</div>'
        "</div></div></section>"
        % (
            cards_html,
            warnings_html,
            "".join(views),
        )
    )


def _data_page_html(spec: Dict[str, Any], views_html: str) -> str:
    key = spec["key"]
    units = []
    if spec.get("left_unit"):
        units.append("左轴 %s" % spec["left_unit"])
    if spec.get("right_unit"):
        units.append("右轴 %s" % spec["right_unit"])
    units.append("时间轴 秒 (s)")
    return (
        '<section class="page" id="page-%s" data-page="%s">'
        '<div class="page-head"><div class="page-title">%s</div>'
        '<div class="page-sub">%s</div>'
        '<div class="page-right">%s</div></div>'
        '<div class="page-body single">%s</div>'
        "</section>"
        % (
            key,
            key,
            _html_escape(spec["title"]),
            _html_escape(spec.get("subtitle") or ""),
            _html_escape(" · ".join(units)),
            views_html,
        )
    )


def _speed_page_html(
    spec: Dict[str, Any], views_html: str, default_payload: Dict[str, Any]
) -> str:
    """速度页：顶部多一条"平均速度 / 手动范围"控件条，其余与普通数据页一致。"""
    key = spec["key"]
    units = []
    if spec.get("left_unit"):
        units.append("左轴 %s" % spec["left_unit"])
    if spec.get("right_unit"):
        units.append("右轴 %s" % spec["right_unit"])
    units.append("时间轴 秒 (s)")
    return (
        '<section class="page" id="page-%s" data-page="%s">'
        '<div class="page-head"><div class="page-title">%s</div>'
        '<div class="page-sub">%s</div>'
        '<div class="page-right">%s</div></div>'
        '<div class="page-body speed-body">%s'
        '<div class="speed-views">%s</div>'
        "</div></section>"
        % (
            key,
            key,
            _html_escape(spec["title"]),
            _html_escape(spec.get("subtitle") or ""),
            _html_escape(" · ".join(units)),
            _avg_bar_html(default_payload),
            views_html,
        )
    )


def _global_page_html(overview: Dict[str, Any]) -> str:
    items: List[str] = []
    for spec, block in overview["html"]["blocks"]:
        units = []
        if block.get("left_unit"):
            units.append("左轴 %s" % block["left_unit"])
        if block.get("right_unit"):
            units.append("右轴 %s" % block["right_unit"])
        items.append(
            '<div class="stack-item">'
            '<div class="chart-label">%s<span>%s</span></div>'
            '<div class="legend">%s</div>'
            '<div class="chart-wrap">%s</div>'
            "</div>"
            % (
                _html_escape(spec["title"]),
                _html_escape(" · ".join(units)),
                block["legend"],
                block["svg"],
            )
        )
    strip = overview["html"].get("strip")
    return (
        '<section class="page" id="page-global" data-page="global">'
        '<div class="page-head"><div class="page-title">全局总览</div>'
        '<div class="page-sub">全部架次拼在同一条时间轴上，竖虚线为架次边界</div>'
        '<div class="page-right">时间轴 秒 (s)</div></div>'
        '<div class="page-body"><div class="stackwrap">'
        "%s%s"
        "</div></div></section>"
        % (
            ('<div class="strip">%s</div>' % strip) if strip else "",
            "".join(items),
        )
    )


def _flight_selector_html(
    flight_payloads: Sequence[Dict[str, Any]], default_index: int
) -> str:
    """架次选择器：<=6 架次用分段按钮，更多用下拉框（避免页眉被撑爆）。"""
    if len(flight_payloads) < 2:
        return ""
    if len(flight_payloads) <= 6:
        buttons: List[str] = []
        for flight_payload in flight_payloads:
            active = " active" if flight_payload["index"] == default_index else ""
            buttons.append(
                '<button type="button" class="fbtn%s" data-flight-btn="%d" title="%s">'
                "架次 %d · %s</button>"
                % (
                    active,
                    flight_payload["index"],
                    _html_escape(flight_payload["title"]),
                    flight_payload["index"],
                    _html_escape(flight_payload["vehicle"]),
                )
            )
        return '<div class="flights"><span class="flabel">架次</span>%s</div>' % "".join(buttons)
    options: List[str] = []
    for flight_payload in flight_payloads:
        selected = " selected" if flight_payload["index"] == default_index else ""
        options.append(
            '<option value="%d"%s>架次 %d · %s</option>'
            % (
                flight_payload["index"],
                selected,
                flight_payload["index"],
                _html_escape(flight_payload["vehicle"]),
            )
        )
    return (
        '<div class="flights"><label class="flabel" for="flight-select">架次</label>'
        '<select id="flight-select" class="fselect">%s</select></div>' % "".join(options)
    )


def _build_payload(model: LogModel) -> Dict[str, Any]:
    flight_payloads: List[Dict[str, Any]] = [
        _build_flight_payload(flight) for flight in model.flights
    ]
    default_index = flight_payloads[0]["index"] if flight_payloads else 1

    cards_html = "".join(
        '<div class="card"><div class="k">%s</div><b>%s</b><span>%s</span></div>'
        % (_html_escape(card["label"]), _html_escape(card["value"]), _html_escape(card["sub"]))
        for card in _cards(model)
    )

    warnings = model.warnings or []
    warnings_html = ""
    if warnings:
        items = "".join("<li>%s</li>" % _html_escape(w) for w in warnings)
        warnings_html = '<div class="warn"><b>解析提示</b><ul>%s</ul></div>' % items

    pages: List[str] = [
        _overview_page_html(flight_payloads, default_index, cards_html, warnings_html)
    ]
    for spec in _flight_panel_specs():
        views: List[str] = []
        any_data = False
        for flight_payload in flight_payloads:
            block = flight_payload["blocks"].get(spec["key"])
            if block is not None:
                any_data = True
            views.append(
                _flight_view_html(
                    flight_payload,
                    spec["key"],
                    block,
                    flight_payload["index"] == default_index,
                )
            )
        if not any_data:
            continue
        if spec["key"] == "speed":
            pages.append(_speed_page_html(spec, "".join(views), flight_payloads[0]))
        else:
            pages.append(_data_page_html(spec, "".join(views)))

    overview = _overview_payload(model)
    if overview is not None:
        pages.append(_global_page_html(overview))

    payload_flights = [
        {
            "index": flight_payload["index"],
            "title": flight_payload["title"],
            "t0": flight_payload["t0"],
            "t1": flight_payload["t1"],
            "times": flight_payload["times"],
            "series": flight_payload["series"],
            "panels": flight_payload["panels"],
        }
        for flight_payload in flight_payloads
    ]

    overview_data = None
    if overview is not None:
        overview_data = {key: value for key, value in overview.items() if key != "html"}

    return {
        "title": "飞行日志分析 · %s" % os.path.basename(model.path),
        "file": os.path.basename(model.path),
        "meta": model.meta,
        "cards": _cards(model),
        "warnings": warnings,
        "overview": overview_data,
        "flights": payload_flights,
        "html": {
            "pages": pages,
            "page_count": len(pages),
            "flight_selector": _flight_selector_html(flight_payloads, default_index),
        },
    }


# ---------------------------------------------------------------------------
# CSS / JS（内联，无外链）
# ---------------------------------------------------------------------------

CSS = """
:root{
  --bg:#0a0e13; --panel:#0f151d; --panel2:#121a24; --line:#1e2a36;
  --text:#dde6f0; --muted:#8ea1b4; --accent:#4aa3ff;
}
*{box-sizing:border-box}
html,body{height:100%;margin:0;padding:0;overflow:hidden}
body{
  background:var(--bg); color:var(--text);
  font-family:"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",
    "Noto Sans CJK SC",sans-serif;
  font-size:14px; line-height:1.5;
  -webkit-font-smoothing:antialiased;
}
.app{height:100%;display:flex;flex-direction:column;min-height:0}
header{
  flex:0 0 auto;display:flex;flex-direction:column;
  padding:11px 22px 8px;border-bottom:1px solid var(--line);
  background:linear-gradient(180deg,#0d141c,#0a0e13);
}
.hrow{display:flex;align-items:baseline;gap:14px;min-width:0;flex-wrap:wrap}
header h1{font-size:19px;margin:0;font-weight:650;letter-spacing:.3px;white-space:nowrap}
.subline{
  color:var(--muted);font-size:12px;margin-top:3px;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
.subline code{background:#16202b;border:1px solid var(--line);padding:1px 5px;border-radius:4px}
.flights{margin-left:auto;display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.flights .flabel{font-size:12px;color:var(--muted);margin-right:2px;white-space:nowrap}
.fbtn{
  font-family:inherit;font-size:12px;color:#c3d2e1;background:var(--panel2);
  border:1px solid var(--line);border-radius:999px;padding:3px 11px;cursor:pointer;
  transition:.15s;white-space:nowrap;
}
.fbtn:hover{border-color:var(--accent);color:#fff}
.fbtn.active{background:#12324f;border-color:var(--accent);color:#fff}
.fselect{
  font-family:inherit;font-size:12px;color:#c3d2e1;background:var(--panel2);
  border:1px solid var(--line);border-radius:8px;padding:3px 8px;cursor:pointer;max-width:240px;
}
.fselect:hover{border-color:var(--accent)}
main{position:relative;flex:1 1 auto;min-height:0}
.page{position:absolute;inset:0;display:none;flex-direction:column;padding:9px 26px 4px;min-height:0}
.page.active{display:flex}
.page-head{flex:0 0 auto;display:flex;align-items:baseline;gap:12px;flex-wrap:wrap;margin-bottom:4px}
.page-title{font-size:16px;font-weight:650}
.page-sub{font-size:12px;color:var(--muted)}
.page-right{margin-left:auto;font-size:12px;color:var(--muted);font-variant-numeric:tabular-nums;white-space:nowrap}
.page-body{flex:1 1 auto;min-height:0;position:relative}
.page-body.single>.flight-view{position:absolute;inset:0}
.flight-view{display:none;flex-direction:column;min-height:0}
.flight-view.active{display:flex}
.badges{flex:0 0 auto;display:flex;flex-wrap:wrap;gap:6px;margin-bottom:5px;overflow:hidden;max-height:56px}
.badges span{
  font-size:11.5px;color:#bfcedd;background:#141d27;border:1px solid var(--line);
  border-radius:6px;padding:2px 8px;font-variant-numeric:tabular-nums;
}
.strip{flex:0 0 auto;margin-bottom:1px}
.strip svg{width:100%;height:34px;display:block;overflow:hidden}
.legend{flex:0 0 auto;display:flex;flex-wrap:wrap;gap:13px;font-size:12px;color:#b8c7d6;margin:2px 0 3px}
.lg{display:inline-flex;align-items:center;gap:5px;white-space:nowrap}
.lg i{display:inline-block;width:15px;height:3px;border-radius:2px}
.lg i.dashed{background-image:linear-gradient(90deg,#fff 0 60%,transparent 60% 100%)!important;background-size:7px 3px;background-repeat:repeat-x}
.chart-wrap{flex:1 1 auto;min-height:0;position:relative}
.chart-svg{width:100%;height:100%;display:block;overflow:hidden}
.chart-svg text{font-family:inherit}
.empty{flex:1 1 auto;display:flex;align-items:center;justify-content:center;color:var(--muted);font-size:13px}
/* 速度页：平均速度 / 手动统计范围（只在第 2 页出现） */
.speed-body{display:flex;flex-direction:column;min-height:0}
.speed-views{position:relative;flex:1 1 auto;min-height:0}
.speed-views>.flight-view{position:absolute;inset:0}
.avgbar{
  flex:0 0 auto;display:flex;align-items:center;gap:20px;flex-wrap:wrap;
  background:var(--panel);border:1px solid var(--line);border-radius:10px;
  padding:6px 13px;margin-bottom:5px;font-variant-numeric:tabular-nums;
}
.avgbar .avg-title{font-size:12px;color:var(--muted);line-height:1.35;white-space:nowrap}
.avgbar .avg-title b{display:block;font-size:13px;color:var(--text);font-weight:600}
.avg-vals{display:flex;gap:18px;flex-wrap:wrap}
.avg-val{display:flex;flex-direction:column;line-height:1.15}
.avg-val b{font-size:19px;font-weight:650;letter-spacing:.2px;white-space:nowrap}
.avg-k{display:inline-flex;align-items:center;gap:5px;font-size:10.5px;color:var(--muted);white-space:nowrap}
.avgdash{
  display:inline-block;width:13px;height:3px;border-radius:2px;
  background-image:linear-gradient(90deg,currentColor 0 60%,transparent 60% 100%);
  background-size:7px 3px;background-repeat:repeat-x;
}
.avg-ctl{display:flex;align-items:center;gap:8px;margin-left:auto;flex-wrap:wrap}
.avg-range{display:flex;align-items:center;gap:5px;font-size:12px;color:var(--muted);white-space:nowrap}
.avg-dash{color:#6d7f92}
.avg-input{
  width:82px;font-family:inherit;font-size:12.5px;color:var(--text);background:var(--panel2);
  border:1px solid var(--line);border-radius:6px;padding:3px 6px;font-variant-numeric:tabular-nums;
}
.avg-input:focus{outline:none;border-color:var(--accent)}
.avg-input:disabled{opacity:.45}
.avg-btn{
  font-family:inherit;font-size:12.5px;color:#fff;background:#16406b;border:1px solid #2b6aa8;
  border-radius:7px;padding:4px 12px;cursor:pointer;transition:.15s;
}
.avg-btn:hover{background:#1b4f84;border-color:var(--accent)}
.avg-btn.ghost{background:var(--panel2);border-color:var(--line);color:#c3d2e1}
.avg-btn.ghost:hover{border-color:var(--accent);color:#fff}
.avg-meta{
  flex-basis:100%;display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;
  font-size:11.5px;color:var(--muted);margin-top:1px;min-height:14px;
}
.avg-msg{color:#ffd79b}
.avg-msg.warn{color:#ffb4a8}
/* SVG 里的平均速度图层不拦截悬停 */
.avg-band,.avg-edge,.avg-line{pointer-events:none}
.lg i.avgdash{background-image:linear-gradient(90deg,#cbd5e1 0 60%,transparent 60% 100%);
  background-size:7px 3px;background-repeat:repeat-x}
/* 概览页 */
.ovwrap{position:absolute;inset:0;display:flex;flex-direction:column;gap:9px;min-height:0}
.cards{flex:0 0 auto;display:grid;grid-template-columns:repeat(auto-fit,minmax(168px,1fr));gap:8px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:7px 11px}
.card .k{font-size:12px;color:var(--muted)}
.card b{
  display:block;font-size:17px;font-weight:650;letter-spacing:.2px;
  font-variant-numeric:tabular-nums;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
.card span{
  display:block;font-size:11.5px;color:var(--muted);margin-top:1px;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
.warn{
  flex:0 0 auto;background:#2c2113;border:1px solid #6a4a1c;color:#ffd79b;
  border-radius:9px;padding:6px 12px;font-size:12.5px;max-height:64px;overflow:auto;
}
.warn ul{margin:3px 0 0;padding-left:18px}
.ovchart{flex:1 1 auto;min-height:0;position:relative}
.ovchart>.flight-view{position:absolute;inset:0}
/* 全局总览页（多图堆叠） */
.stackwrap{position:absolute;inset:0;display:flex;flex-direction:column;gap:5px;min-height:0}
.stack-item{flex:1 1 0;min-height:0;display:flex;flex-direction:column}
.chart-label{flex:0 0 auto;font-size:12.5px;font-weight:650;color:#cfdcea}
.chart-label span{font-weight:400;color:var(--muted);margin-left:8px;font-size:11.5px}
.stack-item .legend{margin:0 0 2px;font-size:11.5px}
/* 翻页控件 */
.pager{
  flex:0 0 auto;display:flex;align-items:center;justify-content:center;gap:14px;
  padding:7px 22px 9px;border-top:1px solid var(--line);background:#0b1118;position:relative;
}
.pbtn{
  font-family:inherit;font-size:13px;color:var(--text);background:var(--panel2);
  border:1px solid var(--line);border-radius:999px;padding:5px 15px;cursor:pointer;transition:.15s;
}
.pbtn:hover:not(:disabled){border-color:var(--accent);color:#fff}
.pbtn:disabled{opacity:.35;cursor:default}
.page-label{font-size:13px;color:var(--text);font-variant-numeric:tabular-nums;min-width:104px;text-align:center}
.dots{display:flex;gap:7px;align-items:center}
.dot{width:9px;height:9px;padding:0;border:0;border-radius:50%;background:#2b3c4f;cursor:pointer;transition:.15s}
.dot:hover{background:#5b7a99}
.dot.active{background:var(--accent);box-shadow:0 0 0 3px #4aa3ff2e}
.hint{position:absolute;right:16px;bottom:12px;font-size:11.5px;color:#6d7f92;white-space:nowrap}
.side{
  position:absolute;top:50%;transform:translateY(-50%);z-index:6;width:32px;height:76px;
  display:flex;align-items:center;justify-content:center;font-size:22px;line-height:1;
  color:#bcd0e4;background:#0d151ef2;border:1px solid var(--line);cursor:pointer;
  font-family:inherit;border-radius:8px;transition:.15s;
}
.side:hover:not(:disabled){border-color:var(--accent);color:#fff;background:#12324f}
.side:disabled{opacity:.22;cursor:default}
.side.prev{left:3px}
.side.next{right:3px}
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
a{color:var(--accent)}
@media (max-width:1000px){
  .hint{display:none}
  .side{width:26px;height:60px;font-size:18px}
}
@media (max-width:760px){
  .side{display:none}
  header{padding:9px 12px 6px}
  .page{padding:7px 12px 3px}
  .cards{grid-template-columns:repeat(auto-fit,minmax(140px,1fr))}
}
@media (max-height:660px){
  .strip svg{height:26px}
  .legend{font-size:11px;margin:0 0 2px}
  .badges{max-height:28px}
  .cards .card b{font-size:15px}
}
@media print{
  html,body{height:auto;overflow:visible;background:#fff;color:#111}
  .app{height:auto}
  header{background:#fff;border-color:#ccc}
  main{position:static}
  .page{position:relative;inset:auto;display:flex!important;height:auto;min-height:72vh;
        page-break-after:always;border-top:1px solid #ddd}
  .page-body{min-height:420px}
  .flight-view{display:none!important}
  .flight-view.active{display:flex!important}
  .pager,.side,.hint{display:none}
  .card,.badges span{background:#f6f8fa;border-color:#ddd;color:#111}
  .subline,.card span,.badges span,.legend,.psub,.punit,.page-sub,.page-right,.chart-label{color:#555}
  .warn{background:#fff7e6;color:#5a3d00;border-color:#e0c080}
}
"""

JS = r"""
(function(){
  "use strict";
  var DATA = window.__FLIGHTLOG__ || {};
  var NS = "http://www.w3.org/2000/svg";
  var tip = document.getElementById("tip");
  var hiders = [];

  function qsa(sel, root){
    return Array.prototype.slice.call((root || document).querySelectorAll(sel));
  }

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

  // 只对"当前页 + 当前架次"里可见的图表做十字线联动
  function isShown(el){
    if (!el || !el.closest) return true;
    var page = el.closest(".page");
    if (page && !page.classList.contains("active")) return false;
    var view = el.closest(".flight-view");
    if (view && !view.classList.contains("active")) return false;
    return true;
  }

  function hideAll(){
    for (var i = 0; i < hiders.length; i++) hiders[i]();
  }

  // 屏幕坐标 -> SVG 用户坐标（兼容 preserveAspectRatio="none" 的拉伸）
  function localPoint(svg, clientX, clientY){
    if (svg.getScreenCTM){
      var m = svg.getScreenCTM();
      if (m && m.inverse){
        var pt = svg.createSVGPoint();
        pt.x = clientX;
        pt.y = clientY;
        return pt.matrixTransform(m.inverse());
      }
    }
    var rect = svg.getBoundingClientRect();
    var vb = (svg.viewBox && svg.viewBox.baseVal && svg.viewBox.baseVal.width) || 1500;
    return {x: rect.width ? (clientX - rect.left) / rect.width * vb : 0, y: 0};
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
        if (!isShown(P.svg)) return;
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
      if (!rows.length){ hide(); return; }
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
    function hover(ev){
      var clientX = ev.clientX, clientY = ev.clientY;
      var q = localPoint(ev.currentTarget, clientX, clientY);
      if (!isFinite(q.x)) return;
      var t = desc.t0 + (q.x - panels[0].p.x0) / (panels[0].p.x1 - panels[0].p.x0) *
        (desc.t1 - desc.t0);
      var idx = nearest(desc.times, t);
      if (idx >= 0) show(idx, clientX, clientY);
    }
    function touchHover(ev){
      var touch = ev.touches && ev.touches[0];
      if (!touch) return;
      var q = localPoint(ev.currentTarget, touch.clientX, touch.clientY);
      if (!isFinite(q.x)) return;
      var t = desc.t0 + (q.x - panels[0].p.x0) / (panels[0].p.x1 - panels[0].p.x0) *
        (desc.t1 - desc.t0);
      var idx = nearest(desc.times, t);
      if (idx >= 0) show(idx, touch.clientX, touch.clientY);
    }
    panels.forEach(function(P){
      P.svg.addEventListener("mousemove", hover);
      P.svg.addEventListener("mouseleave", hide);
      P.svg.addEventListener("touchstart", touchHover, {passive: true});
      P.svg.addEventListener("touchmove", touchHover, {passive: true});
    });
    hiders.push(hide);
  }

  if (DATA.overview) setup(DATA.overview);
  (DATA.flights || []).forEach(setup);

  // ---------------- 翻页 ----------------
  var pages = qsa(".page");
  var labelEl = document.getElementById("page-label");
  var dotsBox = document.getElementById("dots");
  var prevBtns = qsa('[data-nav="prev"]');
  var nextBtns = qsa('[data-nav="next"]');
  var current = 0;
  var dots = [];

  pages.forEach(function(page, i){
    if (!dotsBox) return;
    var dot = document.createElement("button");
    dot.type = "button";
    dot.className = "dot";
    dot.title = "第 " + (i + 1) + " 页 · " + (page.querySelector(".page-title") || {}).textContent;
    dot.setAttribute("aria-label", "第 " + (i + 1) + " 页");
    dot.addEventListener("click", function(){ showPage(i, true); });
    dotsBox.appendChild(dot);
    dots.push(dot);
  });

  function showPage(index, updateHash){
    if (!pages.length) return;
    index = Math.max(0, Math.min(pages.length - 1, index));
    current = index;
    pages.forEach(function(page, i){
      page.classList.toggle("active", i === index);
    });
    if (labelEl) labelEl.textContent = "第 " + (index + 1) + " / " + pages.length + " 页";
    dots.forEach(function(dot, i){
      dot.classList.toggle("active", i === index);
    });
    prevBtns.forEach(function(btn){ btn.disabled = index === 0; });
    nextBtns.forEach(function(btn){ btn.disabled = index === pages.length - 1; });
    hideAll();
    if (updateHash){
      try { history.replaceState(null, "", "#" + pages[index].id); } catch (err) { /* file:// */ }
    }
  }

  prevBtns.forEach(function(btn){
    btn.addEventListener("click", function(){ showPage(current - 1, true); });
  });
  nextBtns.forEach(function(btn){
    btn.addEventListener("click", function(){ showPage(current + 1, true); });
  });

  document.addEventListener("keydown", function(ev){
    if (ev.defaultPrevented || ev.ctrlKey || ev.metaKey || ev.altKey) return;
    var tag = (ev.target && ev.target.tagName) || "";
    if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
    var key = ev.key;
    if (key === "ArrowLeft" || key === "PageUp"){
      ev.preventDefault();
      showPage(current - 1, true);
    } else if (key === "ArrowRight" || key === "PageDown"){
      ev.preventDefault();
      showPage(current + 1, true);
    } else if (key === "Home"){
      ev.preventDefault();
      showPage(0, true);
    } else if (key === "End"){
      ev.preventDefault();
      showPage(pages.length - 1, true);
    }
  });

  // ---------------- 平均速度 / 手动统计范围（速度页） ----------------
  // 静态 HTML 里已经画好默认（全程）的平均值和 SVG 图层；这里加载后按同一份
  // 数据重算一遍，用户改起止时间点"应用"时只原地更新数值和图层几何。
  var AVG = null;

  function initAverage(){
    var bar = document.getElementById("avgbar-speed");
    if (!bar) return null;
    var startEl = bar.querySelector("[data-avg-start]");
    var endEl = bar.querySelector("[data-avg-end]");
    var applyEl = bar.querySelector("[data-avg-apply]");
    var resetEl = bar.querySelector("[data-avg-reset]");
    var metaEl = bar.querySelector("[data-avg-meta]");
    var msgEl = bar.querySelector("[data-avg-msg]");
    var viewEls = {
      ias: bar.querySelector('[data-avg-val="ias"]'),
      tas: bar.querySelector('[data-avg-val="tas"]'),
      mach: bar.querySelector('[data-avg-val="mach"]')
    };
    var byIndex = {};
    (DATA.flights || []).forEach(function(fl){ byIndex[fl.index] = fl; });
    var state = {a: null, b: null};

    function toNum(v, fallback){
      var n = parseFloat(v);
      return isFinite(n) ? n : fallback;
    }
    function f1(t){ return (Math.round(t * 10) / 10).toFixed(1); }
    function fsec(t){
      var s = t.toFixed(3);
      s = s.replace(/0+$/, "").replace(/\.$/, "");
      return s || "0";
    }
    function fixed(v, d){
      if (v === null || v === undefined || !isFinite(v)) return "—";
      return v.toFixed(d);
    }
    function setMsg(text, warn){
      if (!msgEl) return;
      msgEl.textContent = text || "";
      msgEl.className = "avg-msg" + (warn ? " warn" : "");
    }
    function currentFlight(){
      var el = document.querySelector(".flight-view.active[data-flight]");
      if (!el) return null;
      var idx = parseInt(el.getAttribute("data-flight"), 10);
      return byIndex[idx] || null;
    }
    // 夹取到 [t0, t1]：写反交换、超界夹边界、完全不相交则退回全程
    function clampRange(fl, rawA, rawB){
      var t0 = fl.t0, t1 = fl.t1;
      var a = toNum(rawA, t0);
      var b = toNum(rawB, t1);
      var notes = [];
      if (a > b){ var tmp = a; a = b; b = tmp; notes.push("起止时间写反，已交换"); }
      var ca = a, cb = b;
      if (ca < t0) ca = t0;
      if (cb > t1) cb = t1;
      if (ca !== a || cb !== b) notes.push("超出当前架次时间轴，已夹到边界");
      if (ca > cb){ ca = t0; cb = t1; notes.push("范围与当前架次不相交，已重置为全程"); }
      return {a: ca, b: cb, notes: notes};
    }
    function slice(fl, a, b){
      var times = fl.times, s = fl.series;
      var n = 0, si = 0, st = 0, sm = 0, ci = 0, ct = 0, cm = 0, v;
      for (var i = 0; i < times.length; i++){
        var t = times[i];
        if (t < a) continue;
        if (t > b) break;
        n++;
        v = s.ias ? s.ias[i] : null;
        if (v !== null && v !== undefined){ si += v; ci++; }
        v = s.tas ? s.tas[i] : null;
        if (v !== null && v !== undefined){ st += v; ct++; }
        v = s.mach ? s.mach[i] : null;
        if (v !== null && v !== undefined){ sm += v; cm++; }
      }
      return {
        samples: n,
        ias: ci ? si / ci : null,
        tas: ct ? st / ct : null,
        mach: cm ? sm / cm : null
      };
    }
    function panelFor(fl){
      var panels = fl.panels || [];
      for (var i = 0; i < panels.length; i++){
        if (panels[i].id === "f" + fl.index + "-speed") return panels[i];
      }
      return null;
    }
    function yFor(p, axis, v){
      if (!axis) return null;
      var span = axis.max - axis.min;
      if (!isFinite(span) || span === 0) return null;
      return p.y1 - (v - axis.min) / span * (p.y1 - p.y0);
    }
    function find(id){ return document.getElementById(id); }
    function updateOverlay(fl, a, b, avg){
      var p = panelFor(fl);
      if (!p) return;
      var suffix = "f" + fl.index + "-speed";
      var span = (fl.t1 - fl.t0) || 1;
      var xa = p.x0 + (a - fl.t0) / span * (p.x1 - p.x0);
      var xb = p.x0 + (b - fl.t0) / span * (p.x1 - p.x0);
      xa = Math.max(p.x0, Math.min(p.x1, xa));
      xb = Math.max(p.x0, Math.min(p.x1, xb));
      if (xb < xa){ var tmp = xa; xa = xb; xb = tmp; }
      var band = find("avg-band-" + suffix);
      if (band){
        band.setAttribute("x", xa.toFixed(1));
        band.setAttribute("width", (xb - xa).toFixed(1));
      }
      var edge0 = find("avg-edge0-" + suffix);
      if (edge0){ edge0.setAttribute("x1", xa.toFixed(1)); edge0.setAttribute("x2", xa.toFixed(1)); }
      var edge1 = find("avg-edge1-" + suffix);
      if (edge1){ edge1.setAttribute("x1", xb.toFixed(1)); edge1.setAttribute("x2", xb.toFixed(1)); }
      [["ias", p.left], ["tas", p.left], ["mach", p.right]].forEach(function(pair){
        var line = find("avg-" + pair[0] + "-" + suffix);
        if (!line) return;
        var y = yFor(p, pair[1], avg[pair[0]]);
        if (y === null){
          line.setAttribute("opacity", "0");
        } else {
          line.setAttribute("y1", y.toFixed(1));
          line.setAttribute("y2", y.toFixed(1));
          line.setAttribute("opacity", "0.85");
        }
      });
    }
    function render(fl){
      if (!fl) return;
      var result = slice(fl, state.a, state.b);
      if (viewEls.ias) viewEls.ias.textContent = fixed(result.ias, 1);
      if (viewEls.tas) viewEls.tas.textContent = fixed(result.tas, 1);
      if (viewEls.mach) viewEls.mach.textContent = fixed(result.mach, 2);
      if (metaEl){
        metaEl.textContent = "范围 " + f1(state.a) + "–" + f1(state.b) + " s · " +
          result.samples + " 样本 · 底纹 = 统计区间，虚线 = 平均值（回车 = 应用）";
      }
      updateOverlay(fl, state.a, state.b, result);
    }
    function syncInputs(fl){
      var lo = fsec(fl.t0), hi = fsec(fl.t1);
      if (startEl){
        startEl.min = lo; startEl.max = hi;
        startEl.value = fsec(state.a);
      }
      if (endEl){
        endEl.min = lo; endEl.max = hi;
        endEl.value = fsec(state.b);
      }
    }
    function apply(){
      var fl = currentFlight();
      if (!fl) return;
      var out = clampRange(fl, startEl ? startEl.value : state.a,
                           endEl ? endEl.value : state.b);
      state.a = out.a;
      state.b = out.b;
      syncInputs(fl);
      render(fl);
      setMsg(out.notes.join("；"), out.notes.length > 0);
    }
    function reset(){
      var fl = currentFlight();
      if (!fl) return;
      state.a = fl.t0;
      state.b = fl.t1;
      syncInputs(fl);
      render(fl);
      setMsg("已重置为当前架次全程", false);
    }
    function onFlight(value){
      var fl = byIndex[parseInt(value, 10)];
      if (!fl) return;
      var out = clampRange(fl, state.a === null ? fl.t0 : state.a,
                           state.b === null ? fl.t1 : state.b);
      state.a = out.a;
      state.b = out.b;
      syncInputs(fl);
      render(fl);
      if (out.notes.length) setMsg("切换架次后：" + out.notes.join("；"), true);
      else setMsg("", false);
    }

    if (applyEl) applyEl.addEventListener("click", apply);
    if (resetEl) resetEl.addEventListener("click", reset);
    if (startEl) startEl.addEventListener("keydown", function(ev){
      if (ev.key === "Enter"){ ev.preventDefault(); apply(); }
    });
    if (endEl) endEl.addEventListener("keydown", function(ev){
      if (ev.key === "Enter"){ ev.preventDefault(); apply(); }
    });

    var first = currentFlight();
    if (first){
      state.a = first.t0;
      state.b = first.t1;
      syncInputs(first);
      render(first);
    }
    return {apply: apply, reset: reset, onFlight: onFlight};
  }

  AVG = initAverage();

  // ---------------- 架次切换（只换当前页的曲线） ----------------
  var flightBtns = qsa("[data-flight-btn]");
  var flightViews = qsa(".flight-view[data-flight]");
  var flightSelect = document.getElementById("flight-select");
  function selectFlight(value){
    flightViews.forEach(function(view){
      view.classList.toggle("active", view.getAttribute("data-flight") === value);
    });
    flightBtns.forEach(function(btn){
      btn.classList.toggle("active", btn.getAttribute("data-flight-btn") === value);
    });
    if (flightSelect && flightSelect.value !== value) flightSelect.value = value;
    if (AVG) AVG.onFlight(value);
    hideAll();
  }
  flightBtns.forEach(function(btn){
    btn.addEventListener("click", function(){
      selectFlight(btn.getAttribute("data-flight-btn"));
    });
  });
  if (flightSelect){
    flightSelect.addEventListener("change", function(){
      selectFlight(flightSelect.value);
    });
  }

  // ---------------- 初始页（支持 #page-speed 直链） ----------------
  var startPage = 0;
  var hash = (window.location.hash || "").replace(/^#/, "");
  if (hash){
    for (var i = 0; i < pages.length; i++){
      if (pages[i].id === hash){ startPage = i; break; }
    }
  }
  showPage(startPage, false);
})();
"""

NOSCRIPT = """<noscript><style>
html,body{height:auto;overflow:auto}
.app{height:auto}
main{position:static}
.page{position:relative;inset:auto;display:flex!important;flex-direction:column;
  height:auto;min-height:70vh;border-top:1px solid #1e2a36;margin-top:8px}
.page-body{min-height:420px}
.pager,.side,.hint,.flights{display:none}
</style><p style="color:#8ea1b4;font-size:12px;padding:0 22px">
本报告用脚本实现翻页；当前浏览器禁用了脚本，已改为纵向平铺显示全部页面。
</p></noscript>"""


def render_report(model: LogModel, title: Optional[str] = None) -> str:
    """把模型渲染成完整 HTML 字符串（翻页式、单文件自包含）。"""
    payload = _build_payload(model)
    page_title = title or payload["title"]
    page_count = payload["html"]["page_count"]
    pages_html = "\n".join(payload["html"]["pages"])

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

    pager = (
        '<nav class="pager" aria-label="翻页">'
        '<button type="button" class="pbtn" data-nav="prev" id="nav-prev"'
        ' aria-label="上一页" disabled>&#8249; 上一页</button>'
        '<span class="page-label" id="page-label">第 1 / %d 页</span>'
        '<span class="dots" id="dots"></span>'
        '<button type="button" class="pbtn" data-nav="next" id="nav-next"'
        ' aria-label="下一页">下一页 &#8250;</button>'
        '<span class="hint">&#8592; &#8594; / PageUp PageDown 翻页 · 鼠标悬停看数值</span>'
        "</nav>" % page_count
    )

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

    return (
        "<!DOCTYPE html>\n"
        '<html lang="zh-CN">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
        "<title>%s</title>\n<style>%s</style>\n</head>\n<body>\n"
        '<div class="app">\n'
        "<header>\n"
        '<div class="hrow"><h1>飞行日志分析</h1>%s</div>\n'
        '<div class="subline">%s</div>\n'
        "</header>\n"
        "%s\n"
        '<main id="pages">\n%s\n'
        '<button type="button" class="side prev" data-nav="prev" aria-label="上一页" disabled>'
        "&#8249;</button>"
        '<button type="button" class="side next" data-nav="next" aria-label="下一页">'
        "&#8250;</button>\n"
        "</main>\n"
        "%s\n"
        "</div>\n"
        '<div id="tip" class="tip"></div>\n'
        "<script>window.__FLIGHTLOG__=%s;</script>\n"
        "<script>%s</script>\n"
        "</body>\n</html>\n"
        % (
            _html_escape(page_title),
            CSS,
            payload["html"]["flight_selector"],
            subline,
            NOSCRIPT,
            pages_html,
            pager,
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
