#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""可选的 PNG 预览（依赖 Pillow；没装 Pillow 时优雅降级，不影响 HTML 报告）。

画一张三联图（能量 / 速度 / 高度）并叠加事件竖线，用于快速看效果或在聊天里
直接预览。命令行通过 ``python -m flightlog <log> --png out/preview.png`` 触发。
"""

from __future__ import annotations

import math
import os
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import EVENT_LABELS, Flight, LogModel
from .report import _iter_segments, _nice_ticks, _time_ticks

# 与 HTML 报告一致的配色
BG = (10, 14, 19)
PANEL_BG = (15, 21, 29)
GRID = (30, 42, 54)
TEXT = (221, 230, 240)
MUTED = (142, 161, 180)
COLORS = {
    "es": (255, 209, 102),
    "ias": (74, 163, 255),
    "tas": (255, 179, 71),
    "alt": (70, 211, 154),
    "ralt": (47, 158, 117),
    "death": (255, 92, 92),
    "spawn": (74, 222, 128),
    "vehicle_change": (96, 165, 250),
    "kill": (56, 217, 201),
    "other": (147, 164, 184),
}

FONT_CANDIDATES = (
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
    r"C:\Windows\Fonts\simsun.ttc",
    r"C:\Windows\Fonts\Deng.ttf",
    "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
    "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
    "/System/Library/Fonts/PingFang.ttc",
)

WIDTH = 1600
HEIGHT = 1000
MARGIN_L = 118
MARGIN_R = 96
MARGIN_T = 168
MARGIN_B = 70
PANEL_GAP = 24


class PreviewUnavailable(RuntimeError):
    """当前环境无法生成 PNG 预览（通常是缺少 Pillow 或中文字体）。"""


def _load_pillow():
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError as exc:  # pragma: no cover - 取决于环境
        raise PreviewUnavailable(
            "未安装 Pillow，跳过 PNG 预览（HTML 报告不受影响；装 Pillow 后可用 --png）"
        ) from exc
    return Image, ImageDraw, ImageFont


def _font(size: int):
    _Image, _ImageDraw, ImageFont = _load_pillow()
    for path in FONT_CANDIDATES:
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def _pick_flight(model: LogModel) -> Flight:
    if not model.flights:
        raise PreviewUnavailable("日志里没有可绘制的架次")
    return max(model.flights, key=lambda f: len(f.samples))


def _panel(
    draw,
    fonts: Dict[str, Any],
    x_map,
    box: Tuple[int, int, int, int],
    title: str,
    unit: str,
    series: Sequence[Tuple[str, str, Sequence[Optional[float]], Tuple[int, int, int], bool]],
    times: Sequence[float],
    t_min: float,
    t_max: float,
    events: Sequence[Dict[str, Any]],
    xticks: Sequence[float],
    tstep: float,
    label_x: bool,
) -> None:
    left, top, right, bottom = box
    draw.rounded_rectangle(box, radius=8, fill=PANEL_BG, outline=GRID)
    draw.text((left + 12, top + 8), title, font=fonts["panel"], fill=TEXT)
    draw.text((left + 12, top + 30), unit, font=fonts["small"], fill=MUTED)

    values: List[float] = []
    for _key, _label, vals, _color, _fill in series:
        values.extend(v for v in vals if v is not None)
    if not values:
        draw.text((left + 12, top + 54), "（无数据）", font=fonts["small"], fill=MUTED)
        return
    lo, hi, step, ticks = _nice_ticks(min(values), max(values), target=5)
    plot_top = top + 58
    plot_bottom = bottom - 22
    plot_left = left + 62

    def y_map(value: float) -> float:
        frac = (value - lo) / (hi - lo)
        return plot_bottom - frac * (plot_bottom - plot_top)

    # 网格与 y 轴刻度
    for tick in ticks:
        y = y_map(tick)
        color = (60, 78, 96) if abs(tick) < 1e-9 else GRID
        draw.line([(plot_left, y), (right - 12, y)], fill=color, width=1)
        digits = 0 if step >= 1 else (1 if step >= 0.1 else 2)
        draw.text(
            (left + 54, y - 8),
            ("%." + str(digits) + "f") % tick,
            font=fonts["small"],
            fill=MUTED,
            anchor="ra",
        )

    # x 网格
    for tick in xticks:
        x = x_map(tick)
        draw.line([(x, plot_top), (x, plot_bottom)], fill=(22, 31, 41), width=1)
        if label_x:
            draw.text((x, plot_bottom + 6), "%d" % round(tick), font=fonts["small"],
                      fill=MUTED, anchor="ma")

    # 面积填充（能量）
    for _key, _label, vals, color, fill in series:
        if not fill:
            continue
        for seg in _iter_segments(times, vals):
            points = [(x_map(t), y_map(v)) for t, v in seg]
            if len(points) < 2:
                continue
            polygon = [(points[0][0], plot_bottom)] + points + [(points[-1][0], plot_bottom)]
            draw.polygon(polygon, fill=tuple(int(c * 0.22 + b * 0.78) for c, b in zip(color, PANEL_BG)))
            break

    # 曲线
    for _key, _label, vals, color, _fill in series:
        for seg in _iter_segments(times, vals):
            points = [(x_map(t), y_map(v)) for t, v in seg]
            if len(points) >= 2:
                draw.line(points, fill=color, width=2, joint="curve")
            elif points:
                x, y = points[0]
                draw.ellipse([x - 2, y - 2, x + 2, y + 2], fill=color)

    # 事件竖线 + 标签
    for event in events:
        t = event.get("t")
        if t is None or t < t_min or t > t_max:
            continue
        x = x_map(t)
        color = COLORS.get(event.get("kind", ""), COLORS["other"])
        _dashed_vline(draw, x, top + 46, plot_bottom, color)
        label = event.get("label") or event.get("kind", "")
        draw.text(
            (min(max(x, left + 30), right - 30), top + 62),
            "%s %.1fs" % (label, t),
            font=fonts["small"],
            fill=color,
            anchor="ma",
        )


def _dashed_vline(draw, x: float, y0: float, y1: float, color, dash: int = 6, gap: int = 5) -> None:
    y = y0
    while y < y1:
        draw.line([(x, y), (x, min(y + dash, y1))], fill=color, width=1)
        y += dash + gap


def render_preview(model: LogModel, out_path: str) -> str:
    """渲染 PNG 预览，返回绝对路径；缺 Pillow 时抛 :class:`PreviewUnavailable`。"""
    Image, ImageDraw, _ImageFont = _load_pillow()
    flight = _pick_flight(model)
    series = flight.series
    times = series["t"]
    if not times:
        raise PreviewUnavailable("架次里没有样本")

    t_min, t_max = flight.start_t, flight.end_t
    if t_max - t_min < 0.5:
        t_max = t_min + 0.5
    x_left, x_right = MARGIN_L, WIDTH - MARGIN_R

    def x_map(t: float) -> float:
        return x_left + (t - t_min) / (t_max - t_min) * (x_right - x_left)

    tstep, xticks = _time_ticks(t_min, t_max, target=10)

    image = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(image)
    fonts = {
        "title": _font(27),
        "subtitle": _font(16),
        "panel": _font(17),
        "legend": _font(15),
        "small": _font(13),
    }

    # 标题
    draw.text((MARGIN_L, 22), "飞行日志预览 · 能量 / 速度 / 高度", font=fonts["title"], fill=TEXT)
    meta = model.meta or {}
    subtitle = "文件 %s · 机型 %s · 架次 %d/%d（最长架次）· %s – %s · 样本 %d" % (
        os.path.basename(model.path),
        flight.vehicle or meta.get("vehicle") or "未知",
        flight.index,
        len(model.flights),
        "%.1fs" % t_min,
        "%.1fs" % flight.end_t,
        len(times),
    )
    draw.text((MARGIN_L, 60), subtitle, font=fonts["subtitle"], fill=MUTED)

    # 图例
    legend = [
        ("比能量高度 Es", COLORS["es"], True),
        ("IAS", COLORS["ias"], True),
        ("TAS", COLORS["tas"], True),
        ("气压高度", COLORS["alt"], True),
        ("雷达高度", COLORS["ralt"], False),
        ("事件竖线", COLORS["death"], True),
    ]
    x = MARGIN_L
    for label, color, solid in legend:
        draw.line([(x, 94), (x + 22, 94)], fill=color, width=3)
        draw.text((x + 28, 86), label, font=fonts["legend"], fill=TEXT)
        x += 30 + int(draw.textlength(label, font=fonts["legend"])) + 26

    # 事件（只画落在架次内的）
    events = [
        {"kind": e.kind, "t": e.t, "label": EVENT_LABELS.get(e.kind, e.kind)}
        for e in flight.events
        if e.t is not None and t_min <= e.t <= t_max
    ]

    panels = [
        (
            "比能量高度 Es（米）",
            "Es = alt + v²/2g   能量变化率见 HTML 报告",
            [("es", "Es", series["es"], COLORS["es"], True)],
        ),
        (
            "速度（km/h）",
            "IAS 指示空速 / TAS 真空速",
            [
                ("tas", "TAS", series["tas"], COLORS["tas"], False),
                ("ias", "IAS", series["ias"], COLORS["ias"], False),
            ],
        ),
        (
            "高度（米）",
            "气压高度 / 雷达高度（虚线）",
            [
                ("alt", "高度", series["alt"], COLORS["alt"], False),
                ("ralt", "雷达高度", series["ralt"], COLORS["ralt"], False),
            ],
        ),
    ]
    available = HEIGHT - MARGIN_T - MARGIN_B - PANEL_GAP * (len(panels) - 1)
    panel_h = available // len(panels)

    for i, (title, unit, curve_specs) in enumerate(panels):
        top = MARGIN_T + i * (panel_h + PANEL_GAP)
        bottom = top + panel_h
        _panel(
            draw,
            fonts,
            x_map,
            (MARGIN_L - 66, top, WIDTH - MARGIN_R + 12, bottom),
            title,
            unit,
            curve_specs,
            times,
            t_min,
            t_max,
            [],
            xticks,
            tstep,
            label_x=(i == len(panels) - 1),
        )

    # 事件竖线跨越三块面板 + 顶部标签（避免与曲线内标签重复）
    for event in events:
        t = event["t"]
        x = x_map(t)
        color = COLORS.get(event["kind"], COLORS["other"])
        _dashed_vline(draw, x, MARGIN_T, MARGIN_T + panel_h * 3 + PANEL_GAP * 2, color)
        draw.text(
            (min(max(x, MARGIN_L + 40), WIDTH - MARGIN_R - 40), 128),
            "%s %.1fs" % (event["label"], t),
            font=fonts["legend"],
            fill=color,
            anchor="ma",
        )

    # 底部说明
    draw.text(
        (MARGIN_L, HEIGHT - 40),
        "由 wt-hud/tools/flightlog 生成 · 横轴为日志内秒数 t(s) · 能量正值=获得能量",
        font=fonts["small"],
        fill=MUTED,
    )
    draw.text(
        (WIDTH - MARGIN_R, HEIGHT - 40),
        "预览图 · 完整交互报告见同名 HTML",
        font=fonts["small"],
        fill=MUTED,
        anchor="ra",
    )

    out_path = os.path.abspath(out_path)
    directory = os.path.dirname(out_path)
    if directory:
        os.makedirs(directory, exist_ok=True)
    image.save(out_path, "PNG")
    return out_path
