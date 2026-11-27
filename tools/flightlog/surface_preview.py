# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""舵面 / 配平 / 指令轴 —— 单页交互预览（实验页，不进正式 9 页报告）。

为什么单开一页而不是加进 report.py：
正式报告的页码是 10 页制（封面 + 9 页内容），散在 CSS、B 路径进度、设置页文案等 6 处；
这页还在验数据，不该把页码体系一起搅动。等数据确认有价值再决定是否并入。

v0.5.10 第二版（正式页）相对第一版（静态 SVG）的改动：

* 一个 SVG 里叠 5 张图（副翼 / 升降 / 方向 / 配平 / 差值），共用一条时间轴；
* 悬停十字线 + tooltip 读数；框选缩放（所有图同步）、双击复位、轴开关、
  颜色图例 chip、事件竖线（death / spawn / kill / gap，有事件才出现）；
* 导出 PNG（SVG → canvas 栅格化，无外部库）、打印 / 导出 PDF（window.print + @media print）；
* 缺字段（schema < 2 或 7 个字段全缺）给明确空态，绝不把 ``None`` 画成 0；
* 保留：默认只画空中连续段、1400 点抽稀、配平分位范围、统计口径、单文件无外链。

实现约定（重要）：

* 页面里只有一个 ``<svg>``，所有图都在里面，避免多 SVG 的坐标/缩放各写一套；
* 纵向布局（哪些图可见）由 JS 用 ``transform="translate(0,y)"`` 重排，SVG 高度一起改；
* 横向缩放由 JS 给每张图的 ``.zoomer`` 组加 ``translate+scale``，折线不重算；
* SVG 的视觉样式全部写成**表现属性**（``stroke=`` / ``font-size=`` 等），不依赖页面 CSS ——
  否则导出的 PNG 会把 CSS 丢掉、变成黑块；
* 整页用 ``_fill()`` 做 ``@@TOKEN@@`` 替换，而不是 ``%`` 格式化：模板里有大量 CSS 百分比
  和正文「1%」「2%–98%」，用 ``%`` 格式化需要逐个写成 ``%%``，历史上同类 bug 连踩三次。

用法：
    python -m flightlog --surface-preview "<log.jsonl>" --out "<out.html>"
    python -m flightlog --surface-preview "<log.jsonl>" --out "<out.html>" --surface-preview-all
"""

from __future__ import annotations

import datetime
import html
import json
import math
import os
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import EVENT_LABELS, LogError, analyze

#: 每条曲线最多画多少个点（超出按等距抽稀，保留首末）
MAX_POINTS = 1400

#: 三根轴的展示配置：(短 id, 中文名, 舵面字段, 指令轴字段, 颜色)
AXES: Tuple[Tuple[str, str, str, str, str], ...] = (
    ("ail", "副翼", "aileron_pct", "stick_ailerons", "#2563eb"),
    ("ele", "升降舵", "elevator_pct", "stick_elevator", "#c2410c"),
    ("rud", "方向舵", "rudder_pct", "pedals", "#047857"),
)

#: 页面会画的事件种类（``end`` 是记录结束，不画；``vehicle_change`` 暂不进这页）
EVENT_KINDS = ("death", "spawn", "kill", "gap")

EVENT_COLORS = {
    "death": "#dc2626",
    "spawn": "#059669",
    "kill": "#7c3aed",
    "gap": "#64748b",
}

EVENT_FALLBACK_COLOR = "#64748b"

# ---------------------------------------------------------------------------
# 配色（与 _CSS 里的 CSS 变量是同一套；改一边记得改另一边）
# ---------------------------------------------------------------------------

INK = "#1f2937"
MUTED = "#6b7280"
PAGE_BG = "#f4f1e7"
CARD = "#fffdf9"
LINE = "#e3ddcc"
GRID_X = "#ece7d9"
GRID_Y = "#ece7d9"
GRID_STRONG = "#cfc7b4"
PLOT_BG = "#fffefa"
SURFACE_COLOR = "#111827"
TRIM_COLOR = "#b45309"

# ---------------------------------------------------------------------------
# 几何（viewBox 1000 宽；所有图共用一个坐标空间）
# ---------------------------------------------------------------------------

VB_W = 1000
LEFT = 64
RIGHT = 12
PLOT_W = VB_W - LEFT - RIGHT

TOP_PAD = 8
BOTTOM_PAD = 12

PLOT_TOP = 30           # 块内：标题（基线 15）之后，绘图区顶边
AXIS_PLOT_H = 110
TRIM_PLOT_H = 84
DIFF_PLOT_H = 104
BLOCK_TAIL = 30         # 绘图区底部到下一块标题的空隙

BLOCK_H_AXIS = PLOT_TOP + AXIS_PLOT_H + BLOCK_TAIL      # 170
BLOCK_H_TRIM = PLOT_TOP + TRIM_PLOT_H + BLOCK_TAIL      # 144
BLOCK_H_DIFF = PLOT_TOP + DIFF_PLOT_H + BLOCK_TAIL      # 164
XAXIS_H = 26

LANE_ROW_H = 17
LANE_TOP = 7
LANE_BOTTOM = 5

# ---------------------------------------------------------------------------
# 小工具
# ---------------------------------------------------------------------------


def _esc(value: Any) -> str:
    return html.escape(str(value), quote=True)


def _sgn(value: Optional[float], digits: int = 1) -> str:
    """带符号数字；``None`` → 破折号（缺字段，不是 0）。"""
    if value is None:
        return "—"
    if digits <= 0:
        return "%+d" % int(round(float(value)))
    return ("%+." + str(digits) + "f") % float(value)


def _num(value: Optional[float], digits: int = 1) -> str:
    if value is None:
        return "—"
    return ("%." + str(digits) + "f") % float(value)


def _thousands(value: int) -> str:
    return "{:,}".format(int(value))


def _pct_of(ordered: Sequence[float], q: float) -> float:
    """已排序序列的分位数（nearest-rank，口径与旧版一致）。"""
    if not ordered:
        return 0.0
    idx = int(len(ordered) * q)
    return ordered[min(len(ordered) - 1, max(0, idx))]


def _fill(template: str, **values: str) -> str:
    """``@@KEY@@`` 替换。值里再出现 ``@@`` 的概率可以忽略，比 % 格式化安全得多。"""
    for key, value in values.items():
        template = template.replace("@@%s@@" % key.upper(), value)
    return template


# ---------------------------------------------------------------------------
# 数据收集 / 统计（口径与 v1 一致，别改）
# ---------------------------------------------------------------------------


def _collect(model) -> List[Dict[str, Any]]:
    """把所有架次的样本拼成一条时间轴（t 直接用日志原始秒数）。"""
    rows: List[Dict[str, Any]] = []
    for flight in model.flights:
        for sample in flight.samples:
            rows.append(sample)
    rows.sort(key=lambda s: s.get("t") or 0.0)
    return rows


def _thin(values: Sequence[Optional[float]], limit: int = MAX_POINTS) -> List[int]:
    """返回要保留的下标（等距抽稀，首末必留）。"""
    n = len(values)
    if n <= limit:
        return list(range(n))
    step = n / float(limit)
    idx = sorted({int(i * step) for i in range(limit)} | {0, n - 1})
    return [i for i in idx if 0 <= i < n]


def _corr(xs: Sequence[float], ys: Sequence[float]) -> Optional[float]:
    if len(xs) < 8:
        return None
    mx = sum(xs) / len(xs)
    my = sum(ys) / len(ys)
    num = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    dx = math.sqrt(sum((x - mx) ** 2 for x in xs))
    dy = math.sqrt(sum((y - my) ** 2 for y in ys))
    return num / (dx * dy) if dx and dy else None


def _axis_stats(rows: Sequence[Dict[str, Any]], surf_key: str, stick_key: str) -> Dict[str, Any]:
    surf, stick, diff = [], [], []
    quant = set()
    for row in rows:
        s = row.get(surf_key)
        k = row.get(stick_key)
        if s is None or k is None:
            continue
        surf.append(float(s))
        stick.append(float(k))
        diff.append(float(s) - float(k) * 100.0)
        quant.add(round(float(s), 3))
    if not surf:
        return {"n": 0}
    # 把舵面前移一个采样再比：/indicators 隔轮取 + 复用缓存，指令轴本身滞后约 1 个采样。
    # 对齐后剩下的主要是舵面 1% 量化，这个数能说明「残差是不是采样问题」。
    aligned = []
    for i in range(1, len(rows)):
        sp = rows[i - 1].get(surf_key)
        kc = rows[i].get(stick_key)
        if sp is None or kc is None:
            continue
        aligned.append(abs(float(sp) - float(kc) * 100.0))
    aligned.sort()
    abs_diff = sorted(abs(d) for d in diff)
    return {
        "n": len(surf),
        "surf_min": min(surf),
        "surf_max": max(surf),
        "stick_min": min(stick),
        "stick_max": max(stick),
        "corr": _corr(stick, surf),
        "max_abs_diff": abs_diff[-1],
        "p50": _pct_of(abs_diff, 0.5),
        "p90": _pct_of(abs_diff, 0.9),
        "steps": len(quant),
        "aligned_p50": _pct_of(aligned, 0.5) if aligned else None,
        "aligned_p90": _pct_of(aligned, 0.9) if aligned else None,
    }


def _trim_stats(rows: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    vals = [float(r["trimmer"]) for r in rows if r.get("trimmer") is not None]
    if not vals:
        return {"n": 0}
    nz = [v for v in vals if abs(v) > 1e-9]
    return {"n": len(vals), "min": min(vals), "max": max(vals), "nz": len(nz)}


def _pick_window(rows: Sequence[Dict[str, Any]], min_seconds: float = 5.0):
    """挑最长的一段连续空中数据：地面段会把动作压扁，默认只看飞行部分。

    返回 (起始下标, 结束下标, 说明文字)；没有空中数据时退回全段。
    """
    best = (0, len(rows) - 1)
    best_len = 0.0
    i = 0
    n = len(rows)
    while i < n:
        if not rows[i].get("inflight"):
            i += 1
            continue
        j = i
        while j + 1 < n and rows[j + 1].get("inflight"):
            j += 1
        span = (rows[j].get("t") or 0.0) - (rows[i].get("t") or 0.0)
        if span > best_len:
            best_len = span
            best = (i, j)
        i = j + 1
    if best_len < min_seconds:
        return 0, n - 1, "全程（日志里没有足够长的空中段）"
    t0 = rows[best[0]].get("t") or 0.0
    t1 = rows[best[1]].get("t") or 0.0
    return best[0], best[1], "空中 %.1f–%.1f s（共 %.1f s），前后地面段已略去" % (t0, t1, t1 - t0)


# ---------------------------------------------------------------------------
# 坐标 / 刻度
# ---------------------------------------------------------------------------


def _nice_step(span: float, target: int = 5) -> float:
    raw = span / max(1, target)
    if raw <= 0:
        return 1.0
    mag = 10.0 ** math.floor(math.log10(raw))
    for mult in (1.0, 2.0, 2.5, 5.0, 10.0):
        if raw <= mult * mag:
            return mult * mag
    return 10.0 * mag


def _y_ticks(lo: float, hi: float, target: int = 5) -> Tuple[float, List[float]]:
    if not (math.isfinite(lo) and math.isfinite(hi)) or hi - lo < 1e-9:
        lo, hi = -1.0, 1.0
    step = _nice_step(hi - lo, target)
    start = math.ceil(lo / step - 1e-9) * step
    ticks: List[float] = []
    v = start
    guard = 0
    while v <= hi + step * 1e-6 and guard < 24:
        ticks.append(round(v, 10))
        v += step
        guard += 1
    if not ticks:
        ticks = [round(lo, 6), round(hi, 6)]
    return step, ticks


def _time_ticks(t0: float, t1: float, target: int = 7) -> Tuple[float, List[float]]:
    span = max(t1 - t0, 1e-6)
    raw = span / max(2, target)
    candidates = (0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200)
    step = candidates[-1]
    for cand in candidates:
        if raw <= cand:
            step = cand
            break
    start = math.ceil(t0 / step - 1e-9) * step
    ticks: List[float] = []
    v = start
    guard = 0
    while v <= t1 + 1e-9 and guard < 40:
        ticks.append(round(v, 6))
        v += step
        guard += 1
    if not ticks:
        step = max((t1 - t0) / 2.0, 0.05)
        ticks = [round(t0, 6), round((t0 + t1) / 2.0, 6), round(t1, 6)]
    return step, ticks


def _time_label(value: float, step: float) -> str:
    if step < 1:
        minutes = int(value // 60)
        seconds = value - minutes * 60
        return "%d:%04.1f" % (minutes, seconds)
    total = int(round(value))
    return "%d:%02d" % (total // 60, total % 60)


def _x_of(t: float, t0: float, t1: float) -> float:
    return LEFT + (t - t0) / max(t1 - t0, 1e-9) * PLOT_W


def _plot_y(value: float, lo: float, hi: float, top: float, height: float) -> float:
    if hi - lo < 1e-9:
        hi = lo + 1e-9
    clamped = min(max(value, lo), hi)
    return top + height - (clamped - lo) / (hi - lo) * height


def _axis_domain(rows, idx, surf_key: str, stick_key: str) -> Tuple[float, float]:
    vals: List[float] = []
    for i in idx:
        s = rows[i].get(surf_key)
        k = rows[i].get(stick_key)
        if s is not None:
            vals.append(float(s))
        if k is not None:
            vals.append(float(k) * 100.0)
    if not vals:
        return -110.0, 110.0
    lo = min(min(vals), -5.0)
    hi = max(max(vals), 5.0)
    pad = (hi - lo) * 0.08 or 5.0
    return lo - pad, hi + pad


def _trim_domain(rows, idx) -> Tuple[float, float]:
    vals = [float(rows[i]["trimmer"]) for i in idx if rows[i].get("trimmer") is not None]
    if not vals:
        return -1.0, 1.0
    ordered = sorted(vals)
    lo, hi = _pct_of(ordered, 0.02), _pct_of(ordered, 0.98)
    if hi - lo < 0.02:
        mid = (lo + hi) / 2.0
        lo, hi = mid - 0.02, mid + 0.02
    pad = (hi - lo) * 0.15 or 0.01
    return lo - pad, hi + pad


def _diff_domain(rows, idx) -> Tuple[float, float]:
    vals: List[float] = []
    for i in idx:
        for _axis_id, _label, surf_key, stick_key, _color in AXES:
            s = rows[i].get(surf_key)
            k = rows[i].get(stick_key)
            if s is None or k is None:
                continue
            vals.append(abs(float(s) - float(k) * 100.0))
    lim = max(max(vals), 1.0) * 1.05 if vals else 1.0
    return -lim, lim


# ---------------------------------------------------------------------------
# 折线
# ---------------------------------------------------------------------------


def _path_str(points: Sequence[Tuple[float, float]], step: bool = False) -> str:
    if len(points) < 2:
        return ""
    if step:
        parts = ["M%.1f,%.1f" % points[0]]
        for (px, py), (cx, cy) in zip(points, points[1:]):
            parts.append("L%.1f,%.1f" % (cx, py))
            parts.append("L%.1f,%.1f" % (cx, cy))
        return "".join(parts)
    return "M" + "L".join("%.1f,%.1f" % p for p in points)


def _series_points(rows, idx, key: str, scale: float, t0: float, t1: float,
                   lo: float, hi: float, top: float, height: float) -> List[Tuple[float, float]]:
    points: List[Tuple[float, float]] = []
    for i in idx:
        value = rows[i].get(key)
        t = rows[i].get("t")
        if value is None or t is None:
            continue
        points.append((_x_of(float(t), t0, t1),
                       _plot_y(float(value) * scale, lo, hi, top, height)))
    return points


def _diff_points(rows, idx, surf_key: str, stick_key: str, t0: float, t1: float,
                 lo: float, hi: float, top: float, height: float) -> List[Tuple[float, float]]:
    points: List[Tuple[float, float]] = []
    for i in idx:
        row = rows[i]
        s = row.get(surf_key)
        k = row.get(stick_key)
        t = row.get("t")
        if s is None or k is None or t is None:
            continue
        points.append((_x_of(float(t), t0, t1),
                       _plot_y(float(s) - float(k) * 100.0, lo, hi, top, height)))
    return points


# ---------------------------------------------------------------------------
# 事件
# ---------------------------------------------------------------------------


def _event_payload(model_events, t0: float, t1: float) -> List[Dict[str, Any]]:
    out: List[Dict[str, Any]] = []
    for event in model_events:
        if event.t is None or event.kind not in EVENT_KINDS:
            continue
        t = float(event.t)
        if t < t0 - 0.001 or t > t1 + 0.001:
            continue
        detail = ""
        if event.kind == "gap" and event.gap is not None:
            detail = "%.1f s" % float(event.gap)
        elif event.kind == "kill" and event.target:
            detail = event.target
        out.append({
            "t": round(t, 3),
            "kind": event.kind,
            "label": EVENT_LABELS.get(event.kind, event.kind),
            "detail": detail,
            "color": EVENT_COLORS.get(event.kind, EVENT_FALLBACK_COLOR),
        })
    out.sort(key=lambda item: item["t"])
    return out


def _lane_items(events: Sequence[Dict[str, Any]], t0: float, t1: float):
    """事件标签的排布（最多两行，避免重叠）。Python 与 JS 用同一套规则。"""
    items: List[Dict[str, Any]] = []
    for event in events:
        x = _x_of(float(event["t"]), t0, t1)
        text = event["label"] + ((" " + event["detail"]) if event["detail"] else "")
        width = len(text) * 10.5 + 14.0
        x0 = x + 4.0
        if x0 + width > LEFT + PLOT_W:
            x0 = x - 4.0 - width
        items.append({"ev": event, "x": x, "x0": x0, "text": text, "width": width, "row": 0})
    items.sort(key=lambda item: item["x"])
    row_ends: List[float] = []
    for item in items:
        placed = None
        for r in range(2):
            if r >= len(row_ends):
                row_ends.append(-1e9)
            if item["x0"] > row_ends[r] + 8:
                placed = r
                break
        if placed is None:
            placed = 0
        item["row"] = placed
        row_ends[placed] = max(row_ends[placed], item["x0"] + item["width"])
    rows = max([item["row"] for item in items], default=-1) + 1
    height = (LANE_TOP + rows * LANE_ROW_H + LANE_BOTTOM) if items else 0
    return items, rows, height


# ---------------------------------------------------------------------------
# SVG 片段
# ---------------------------------------------------------------------------


def _clip_defs(blocks: Sequence[Dict[str, Any]]) -> str:
    parts = ["<defs>"]
    for block in blocks:
        parts.append(
            '<clipPath id="clip-%s"><rect x="%d" y="%d" width="%d" height="%d"/></clipPath>'
            % (block["id"], LEFT, PLOT_TOP, PLOT_W, block["plot_h"]))
    parts.append("</defs>")
    return "".join(parts)


def _xgrid_svg(ticks: Sequence[float], t0: float, t1: float, top: float, height: float) -> str:
    parts = []
    for tick in ticks:
        x = _x_of(tick, t0, t1)
        parts.append('<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="%s" stroke-width="1"/>'
                     % (x, top, x, top + height, GRID_X))
    return "".join(parts)


def _y_axis_svg(ticks: Sequence[float], lo: float, hi: float, top: float, height: float,
                digits: int) -> str:
    parts = []
    for value in ticks:
        y = _plot_y(value, lo, hi, top, height)
        strong = abs(value) < 1e-9
        parts.append('<line x1="%d" y1="%.1f" x2="%d" y2="%.1f" stroke="%s" stroke-width="%s"/>'
                     % (LEFT, y, LEFT + PLOT_W, y, GRID_STRONG if strong else GRID_Y,
                        "1.3" if strong else "1"))
        label = "0" if strong else _sgn(value, digits)
        parts.append('<text x="%d" y="%.1f" font-size="10.5" fill="%s" text-anchor="end">%s</text>'
                     % (LEFT - 8, y + 3.5, MUTED, label))
    return "".join(parts)


def _events_svg(events: Sequence[Dict[str, Any]], t0: float, t1: float,
                top: float, height: float) -> str:
    if not events:
        return ""
    parts = ['<g data-role="events">']
    for event in events:
        x = _x_of(float(event["t"]), t0, t1)
        dash = ' stroke-dasharray="3 3"' if event["kind"] == "gap" else ""
        parts.append(
            '<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="%s" stroke-width="1.1" '
            'stroke-opacity="0.55" vector-effect="non-scaling-stroke"%s/>'
            % (x, top, x, top + height, event["color"], dash))
    parts.append("</g>")
    return "".join(parts)


def _block_head(block: Dict[str, Any], extra_dots: Optional[str] = None) -> str:
    parts = []
    if extra_dots:
        parts.append(extra_dots)
    else:
        parts.append('<circle cx="6" cy="11" r="3.4" fill="%s"/>' % block["color"])
    parts.append('<text x="%d" y="15" font-size="13" font-weight="600" fill="%s">%s</text>'
                 % (16 if not extra_dots else 52, INK, _esc(block["title"])))
    parts.append('<text class="blk-note" x="%d" y="15" font-size="11" fill="%s" text-anchor="end">%s</text>'
                 % (VB_W, MUTED, _esc(block["note"])))
    return "".join(parts)


def _block_axis(block: Dict[str, Any], rows, idx, t0: float, t1: float,
                events: Sequence[Dict[str, Any]]) -> str:
    lo, hi = block["lo"], block["hi"]
    top, height = PLOT_TOP, block["plot_h"]
    out = ['<g class="blk" data-block="%s" data-kind="%s" data-h="%d" transform="translate(0,%d)">'
           % (block["id"], block["kind"], block["block_h"], block["offset"])]
    out.append('<g class="layout">')
    out.append(_block_head(block))
    out.append('<rect x="%d" y="%d" width="%d" height="%d" rx="3" fill="%s" stroke="%s"/>'
               % (LEFT, top, PLOT_W, height, PLOT_BG, LINE))
    out.append('<g class="xgrid" data-xgrid="%s">%s</g>'
               % (block["id"], _xgrid_svg(block["xticks"], t0, t1, top, height)))
    out.append(_y_axis_svg(block["yticks"], lo, hi, top, height, block["digits"]))
    out.append('<g clip-path="url(#clip-%s)">' % block["id"])
    out.append('<g class="zoomer" data-zoom="%s">' % block["id"])

    surf_pts = _series_points(rows, idx, block["surf_key"], 1.0, t0, t1, lo, hi, top, height)
    stick_pts = _series_points(rows, idx, block["stick_key"], 100.0, t0, t1, lo, hi, top, height)
    surface_d = _path_str(surf_pts, step=True)
    stick_d = _path_str(stick_pts)
    if surface_d:
        out.append('<path data-role="surface" d="%s" fill="none" stroke="%s" stroke-width="3.2" '
                   'stroke-opacity="0.30" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>'
                   % (surface_d, SURFACE_COLOR))
    if stick_d:
        out.append('<path data-role="stick" d="%s" fill="none" stroke="%s" stroke-width="1.3" '
                   'stroke-linejoin="round" vector-effect="non-scaling-stroke"/>'
                   % (stick_d, block["color"]))
    if not surface_d and not stick_d:
        out.append('<text x="%d" y="%.1f" font-size="12" fill="%s" text-anchor="middle">%s</text>'
                   % (LEFT + PLOT_W // 2, top + height / 2.0, MUTED,
                      _esc(block["empty_text"])))
    out.append(_events_svg(events, t0, t1, top, height))
    out.append('</g></g>')
    out.append('</g></g>')
    return "".join(out)


def _block_trim(block: Dict[str, Any], rows, idx, t0: float, t1: float,
                events: Sequence[Dict[str, Any]]) -> str:
    lo, hi = block["lo"], block["hi"]
    top, height = PLOT_TOP, block["plot_h"]
    out = ['<g class="blk" data-block="%s" data-kind="trim" data-h="%d" transform="translate(0,%d)">'
           % (block["id"], block["block_h"], block["offset"])]
    out.append('<g class="layout">')
    out.append(_block_head(block))
    out.append('<rect x="%d" y="%d" width="%d" height="%d" rx="3" fill="%s" stroke="%s"/>'
               % (LEFT, top, PLOT_W, height, PLOT_BG, LINE))
    out.append('<g class="xgrid" data-xgrid="trim">%s</g>'
               % _xgrid_svg(block["xticks"], t0, t1, top, height))
    out.append(_y_axis_svg(block["yticks"], lo, hi, top, height, block["digits"]))
    out.append('<g clip-path="url(#clip-%s)">' % block["id"])
    out.append('<g class="zoomer" data-zoom="%s">' % block["id"])
    pts = _series_points(rows, idx, "trimmer", 1.0, t0, t1, lo, hi, top, height)
    trim_d = _path_str(pts)
    if trim_d:
        out.append('<path data-role="trim" d="%s" fill="none" stroke="%s" stroke-width="1.7" '
                   'stroke-linejoin="round" vector-effect="non-scaling-stroke"/>'
                   % (trim_d, TRIM_COLOR))
    else:
        out.append('<text x="%d" y="%.1f" font-size="12" fill="%s" text-anchor="middle">%s</text>'
                   % (LEFT + PLOT_W // 2, top + height / 2.0, MUTED, _esc(block["empty_text"])))
    out.append(_events_svg(events, t0, t1, top, height))
    out.append('</g></g>')
    out.append('</g></g>')
    return "".join(out)


def _block_diff(block: Dict[str, Any], rows, idx, t0: float, t1: float,
                events: Sequence[Dict[str, Any]]) -> str:
    lo, hi = block["lo"], block["hi"]
    top, height = PLOT_TOP, block["plot_h"]
    out = ['<g class="blk" data-block="diff" data-kind="diff" data-h="%d" transform="translate(0,%d)">'
           % (block["block_h"], block["offset"])]
    out.append('<g class="layout">')
    dots = "".join('<circle cx="%d" cy="11" r="3.4" fill="%s"/>' % (6 + n * 12, color)
                   for n, color in enumerate(block["dot_colors"]))
    out.append(_block_head(block, dots))
    out.append('<rect x="%d" y="%d" width="%d" height="%d" rx="3" fill="%s" stroke="%s"/>'
               % (LEFT, top, PLOT_W, height, PLOT_BG, LINE))
    out.append('<g class="xgrid" data-xgrid="diff">%s</g>'
               % _xgrid_svg(block["xticks"], t0, t1, top, height))
    out.append(_y_axis_svg(block["yticks"], lo, hi, top, height, block["digits"]))
    out.append('<g clip-path="url(#clip-diff)">')
    out.append('<g class="zoomer" data-zoom="diff">')
    any_drawn = False
    for axis_id, _label, surf_key, stick_key, color in AXES:
        pts = _diff_points(rows, idx, surf_key, stick_key, t0, t1, lo, hi, top, height)
        path = _path_str(pts)
        if not path:
            continue
        any_drawn = True
        out.append('<path data-role="diff-%s" d="%s" fill="none" stroke="%s" stroke-width="1.05" '
                   'stroke-opacity="0.85" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>'
                   % (axis_id, path, color))
    if not any_drawn:
        out.append('<text x="%d" y="%.1f" font-size="12" fill="%s" text-anchor="middle">%s</text>'
                   % (LEFT + PLOT_W // 2, top + height / 2.0, MUTED, _esc(block["empty_text"])))
    out.append(_events_svg(events, t0, t1, top, height))
    out.append('</g></g>')
    out.append('</g></g>')
    return "".join(out)


def _lane_svg(items: Sequence[Dict[str, Any]], height: int, offset: int) -> str:
    if not items:
        return ""
    out = ['<g class="blk" data-block="evtlane" data-kind="evtlane" data-h="%d" transform="translate(0,%d)">'
           % (height, offset)]
    out.append('<g class="layout"><g id="event-labels">')
    for item in items:
        event = item["ev"]
        y = LANE_TOP + item["row"] * LANE_ROW_H
        out.append('<g transform="translate(%.1f,%d)">' % (item["x0"], y))
        out.append('<rect x="0" y="0" width="%.1f" height="15" rx="7.5" fill="%s" fill-opacity="0.12"/>'
                   % (item["width"], event["color"]))
        out.append('<text x="7" y="11" font-size="10.5" font-weight="600" fill="%s">%s</text>'
                   % (event["color"], _esc(item["text"])))
        out.append('</g>')
    out.append('</g></g></g>')
    return "".join(out)


def _xaxis_svg(step: float, ticks: Sequence[float], t0: float, t1: float) -> str:
    parts = []
    for tick in ticks:
        x = _x_of(tick, t0, t1)
        parts.append('<line x1="%.1f" y1="0" x2="%.1f" y2="5" stroke="%s" stroke-width="1"/>'
                     % (x, x, GRID_STRONG))
        parts.append('<text x="%.1f" y="18" font-size="10.5" fill="%s" text-anchor="middle">%s</text>'
                     % (x, MUTED, _time_label(tick, step)))
    return "".join(parts)


def _xaxis_block(offset: int, content: str) -> str:
    return ('<g class="blk" data-block="xaxis" data-kind="xaxis" data-h="%d" transform="translate(0,%d)">'
            '<g class="layout"><g id="xaxis">%s</g></g></g>' % (XAXIS_H, offset, content))


# ---------------------------------------------------------------------------
# 统计卡 / 表格 / 文案片段
# ---------------------------------------------------------------------------


def _stat_card(cls: str, label: str, number: str, caption: str,
               line1: str, line2: str) -> str:
    return ('<article class="stat-card acc-%s">'
            '<div class="stat-title"><i class="dot %s"></i>%s</div>'
            '<div class="stat-main"><span class="stat-num">%s</span>'
            '<span class="stat-cap">%s</span></div>'
            '<div class="stat-line">%s</div>'
            '<div class="stat-line sub">%s</div>'
            '</article>'
            % (cls, cls, _esc(label), _esc(number), _esc(caption), line1, line2))


def _stat_grid(stats: Dict[str, Dict[str, Any]], trim: Dict[str, Any],
               has_any: bool) -> str:
    if not has_any:
        cards = []
        for axis_id, label, _s, _k, _c in AXES:
            cards.append(_stat_card(axis_id, label, "—", "相关 r",
                                    '<span>对齐后 p90 <b>—</b></span><span>p50 <b>—</b></span>',
                                    '<span>样本 —</span>'))
        cards.append(_stat_card("trim", "配平", "—", "非零样本",
                                '<span>全程 <b>—</b></span>',
                                '<span>仅关闭增稳/教练模式时有值</span>'))
        return '<section class="stat-grid">%s</section>' % "".join(cards)

    cards = []
    for axis_id, label, surf_key, _stick_key, _color in AXES:
        st = stats[surf_key]
        if not st.get("n"):
            cards.append(_stat_card(axis_id, label, "—", "相关 r",
                                    '<span>对齐后 p90 <b>—</b></span><span>p50 <b>—</b></span>',
                                    '<span>该窗口没有%s数据</span>' % _esc(label)))
            continue
        corr = "—" if st["corr"] is None else "%.4f" % st["corr"]
        cards.append(_stat_card(
            axis_id, label, corr, "相关 r",
            '<span>对齐后 p90 <b>%s</b></span><span>p50 <b>%s</b></span>'
            % (_num(st["aligned_p90"], 2), _num(st["aligned_p50"], 2)),
            '<span>%s 样本</span><span>舵面 %s…%s</span><span>%s 台阶</span>'
            % (_thousands(st["n"]), _sgn(st["surf_min"], 0), _sgn(st["surf_max"], 0),
               _thousands(st["steps"]))))
    if trim.get("n"):
        cards.append(_stat_card(
            "trim", "配平", _thousands(trim["nz"]), "非零样本",
            '<span>全程 <b>%s…%s</b></span>' % (_sgn(trim["min"], 3), _sgn(trim["max"], 3)),
            '<span>共 %s 样本</span><span>仅关闭增稳/教练模式时有值</span>'
            % _thousands(trim["n"])))
    else:
        cards.append(_stat_card("trim", "配平", "—", "非零样本",
                                '<span>全程 <b>—</b></span>',
                                '<span>该日志没有配平数据</span>'))
    return '<section class="stat-grid">%s</section>' % "".join(cards)


def _stat_table(stats: Dict[str, Dict[str, Any]], trim: Dict[str, Any],
                has_any: bool) -> str:
    head = ("<tr><th>轴</th><th>样本</th><th>舵面范围</th><th>指令轴范围</th>"
            "<th>相关 r</th><th>最大残差</th><th>对齐后 p50</th><th>对齐后 p90</th>"
            "<th>量化台阶</th></tr>")
    rows: List[str] = []
    for axis_id, label, surf_key, _stick_key, _color in AXES:
        st = stats.get(surf_key, {"n": 0})
        if not has_any or not st.get("n"):
            rows.append('<tr><td>%s</td>%s</tr>'
                        % (_esc(label), "<td>—</td>" * 8))
            continue
        rows.append(
            '<tr><td>%s</td><td>%s</td><td>%s … %s</td><td>%s … %s</td><td>%s</td>'
            '<td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>'
            % (_esc(label), _thousands(st["n"]),
               _sgn(st["surf_min"], 0), _sgn(st["surf_max"], 0),
               "%.3f" % st["stick_min"], "%.3f" % st["stick_max"],
               "—" if st["corr"] is None else "%.4f" % st["corr"],
               _num(st["max_abs_diff"], 2),
               _num(st["aligned_p50"], 2), _num(st["aligned_p90"], 2),
               _thousands(st["steps"])))
    if not has_any or not trim.get("n"):
        rows.append('<tr><td>配平</td>%s</tr>' % ("<td>—</td>" * 8))
    else:
        rows.append(
            '<tr><td>配平</td><td>%s</td><td colspan="3">%s … %s（非零 %s）</td>'
            '<td colspan="4" class="muted">只在关闭「航电增稳 / 教练模式」时有值；'
            '显示范围按 2%%–98%% 分位</td></tr>'
            % (_thousands(trim["n"]), "%.4f" % trim["min"], "%.4f" % trim["max"],
               _thousands(trim["nz"])))
    return '<table class="stats"><thead>%s</thead><tbody>%s</tbody></table>' % (
        head, "".join(rows))


# ---------------------------------------------------------------------------
# 页面骨架片段
# ---------------------------------------------------------------------------


def _hero_html(facts: Sequence[Tuple[str, str]], title: str, subtitle: str) -> str:
    items = "".join('<div class="fact"><span class="k">%s</span><span class="v">%s</span></div>'
                    % (_esc(key), value) for key, value in facts)
    return ('<header class="hero">'
            '<div class="hero-text">'
            '<div class="eyebrow">WtHud FlightLog · 舵面分析 · 实验页</div>'
            '<h1>%s</h1>'
            '<p class="lede">%s</p>'
            '</div>'
            '<div class="facts">%s</div>'
            '</header>' % (_esc(title), subtitle, items))


def _toolbar_html(events: Sequence[Dict[str, Any]]) -> str:
    event_button = ""
    if events:
        event_button = ('<button class="chip" type="button" id="evt-toggle" aria-pressed="true">'
                        '<i class="dot evt"></i>事件标记</button>')
    chips = []
    for axis_id, label, _s, _k, _c in AXES:
        chips.append('<button class="chip" type="button" data-axis="%s" aria-pressed="true">'
                     '<i class="dot %s"></i>%s</button>' % (axis_id, axis_id, label))
    chips.append('<button class="chip" type="button" data-axis="trim" aria-pressed="true">'
                 '<i class="dot trim"></i>配平</button>')
    chips.append('<button class="chip" type="button" data-axis="diff" aria-pressed="true">'
                 '<i class="dot diff"></i>差值</button>')
    return ('<section class="toolbar" id="toolbar">'
            '<div class="tb-group">'
            '<span class="tb-label">显示</span>'
            '%s'
            '<span class="tb-sep" aria-hidden="true"></span>'
            '<button class="chip preset" type="button" data-preset="all">全部显示</button>'
            '<button class="chip preset" type="button" data-preset="diff">只看差值</button>'
            '%s'
            '</div>'
            '<div class="tb-group tb-right">'
            '<span class="zoom-state" id="zoom-state">全程</span>'
            '<button class="btn" type="button" id="zoom-reset" disabled>复位缩放</button>'
            '<button class="btn" type="button" id="export-png">导出 PNG</button>'
            '<button class="btn primary" type="button" id="print-pdf">打印 / PDF</button>'
            '</div>'
            '</section>' % ("".join(chips), event_button))


def _empty_card_html(schema: str, app: str, vehicle: str) -> str:
    return ('<section class="card empty-state">'
            '<div class="empty-badge" aria-hidden="true">—</div>'
            '<div class="empty-body">'
            '<h2>这份日志没有舵面字段（0.5.10 之前录制）</h2>'
            '<p>schema <code>%s</code> · 应用 <code>%s</code> · 机型 <code>%s</code>。'
            '日志里没有 <code>aileron_pct</code> / <code>elevator_pct</code> / '
            '<code>rudder_pct</code> / <code>trimmer</code> / <code>stick_ailerons</code> / '
            '<code>stick_elevator</code> / <code>pedals</code> 这 7 个字段，'
            '所以这里不画舵面曲线（也不画 0 线），统计表只保留字段行并写 <code>—</code>。'
            '该日志没有配平数据（trimmer）。</p>'
            '<p>用 WtHud 0.5.10 或更新版本重新录制即可看到；'
            'ACMI（<code>.wrpl</code> 回放）来源同样没有这些字段，也是这个空态。</p>'
            '</div></section>' % (_esc(schema), _esc(app), _esc(vehicle)))


def _stats_card_html(table: str) -> str:
    return ('<section class="card">'
            '<div class="card-head"><h2>统计口径</h2>'
            '<span class="hint">样本数按当前窗口统计；缺字段写 — 而不是 0</span></div>'
            '<div class="table-wrap">%s</div>'
            '<div class="defs">'
            '<p><b>相关 r</b>：指令轴（×100）与舵面的皮尔逊相关系数。</p>'
            '<p><b>残差</b>：<code>舵面 − 指令轴×100</code> 的绝对值；'
            '最大残差会被采样滞后造成的尖峰抬高。</p>'
            '<p><b>对齐后 p50 / p90</b>：把舵面前移 1 个采样再比，残差绝对值的 50 / 90 分位（%%）。'
            '对齐后 p90 掉到 2–3 个百分点，说明剩下的基本都是舵面 1%% 量化台阶。</p>'
            '<p><b>量化台阶</b>：窗口内出现过的不同舵面百分数个数（游戏只给 1%% 整数）。</p>'
            '</div>'
            '</section>' % table)


def _notes_html() -> str:
    return ('<section class="card notes">'
            '<div class="card-head"><h2>怎么读这份图</h2></div>'
            '<ol>'
            '<li><b>指令轴 = 配平 + 增稳后的最终指令</b>，不是原始杆量；'
            '实测它恒等于「舵面 ÷ 100」。两条线是同源、粗细两版：'
            '舵面是 <code>/state</code> 的 1% 整数，指令轴是 <code>/indicators</code> 的 0.001 级，'
            '价值在细腻度，不在对比谁大谁小。</li>'
            '<li><b>差值图的尖峰是采样滞后，不是操纵差动。</b>'
            '<code>/indicators</code> 隔轮采样并复用缓存，比 <code>/state</code> 晚约 1 个采样'
            '（≈24 ms）。把舵面前移 1 个采样再比，p90 从 4–6% 降到 2–3%'
            '（见表格「对齐后 p90」），剩下的基本是 1% 量化台阶 —— '
            '所以不要把尖峰解读成「输入与响应不一致」。</li>'
            '<li><b>trimmer 只在关闭「航电增稳 / 教练模式」时有值</b>，开着时恒为 0；'
            '0 = 这局没有配平在生效，不是字段坏了。配平图按 2%–98% 分位定显示范围，'
            '超出的尖峰贴在边上，避免开局尖峰把整张图压扁。</li>'
            '<li><b>A-10 系列要单独看</b>：分裂副翼（deceleron）会让 '
            '<code>airbrake, %</code> 跟随副翼偏转，A-10 的副翼/舵面数据不能和常规机型直接比。</li>'
            '<li><b>这是实验页</b>：单文件、无外链、不并入正式 9 页报告；'
            '默认只画最长一段空中连续数据（地面段会把动作压扁），每条曲线抽稀到 ≤1400 点。'
            '数据只来自 JSONL 记录，FlightLog 本身不采集数据。</li>'
            '</ol></section>')


def _footer_html(generated: str, source_file: str, schema: str) -> str:
    return ('<footer><span>由 <code>tools/flightlog/surface_preview.py</code> 生成 · '
            '单文件无外链 · 数据源 JSONL</span>'
            '<span>%s · schema %s · %s</span></footer>'
            % (_esc(generated), _esc(schema), _esc(source_file)))


# ---------------------------------------------------------------------------
# 样式
# ---------------------------------------------------------------------------

_CSS = r"""
:root{
  color-scheme: light;
  --bg:#f4f1e7;
  --card:#fffdf9;
  --soft:#faf7ee;
  --line:#e3ddcc;
  --line-2:#eee9db;
  --ink:#1f2937;
  --muted:#6b7280;
  --faint:#9ca3af;
  --ail:#2563eb;
  --ele:#c2410c;
  --rud:#047857;
  --trim:#b45309;
  --diff:#64748b;
  --evt:#94a3b8;
  --radius:14px;
  --shadow:0 1px 2px rgba(31,41,55,.05), 0 10px 26px rgba(31,41,55,.06);
}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%}
body{
  margin:0;background:var(--bg);color:var(--ink);
  font:14px/1.62 "Segoe UI","Microsoft YaHei","PingFang SC",system-ui,-apple-system,sans-serif;
  -webkit-font-smoothing:antialiased;
}
.wrap{max-width:1280px;margin:0 auto;padding:30px 24px 60px}
h1,h2{margin:0;font-weight:640;letter-spacing:-.01em}
h1{font-size:30px;line-height:1.22;margin:8px 0 10px}
h2{font-size:15.5px}
code{
  font-family:ui-monospace,"Cascadia Mono",Consolas,"Courier New",monospace;
  font-size:.9em;background:#f1ecdd;border-radius:4px;padding:1px 5px;color:#374151;
  white-space:nowrap;
}
.muted{color:var(--muted)}

/* ---------- 页头 ---------- */
.hero{display:block;padding:6px 4px 22px}
.hero-text{min-width:0}
.eyebrow{
  font-size:11.5px;font-weight:650;letter-spacing:.16em;text-transform:uppercase;
  color:#8b8574;
}
.lede{margin:0;max-width:720px;color:var(--muted);font-size:13.5px}
.facts{
  display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px 24px;
  margin-top:18px;padding:14px 18px;background:rgba(255,253,249,.6);
  border:1px solid var(--line);border-radius:var(--radius);
}
.fact{display:flex;flex-direction:column;gap:1px;min-width:0}
.fact .k{font-size:10.5px;letter-spacing:.06em;color:var(--faint);text-transform:uppercase}
.fact .v{
  font-size:12.5px;color:#374151;font-variant-numeric:tabular-nums;
  overflow-wrap:anywhere;
}

/* ---------- 卡片 ---------- */
.card{
  background:var(--card);border:1px solid var(--line);border-radius:var(--radius);
  box-shadow:var(--shadow);padding:18px 20px;margin:0 0 16px;
}
.card-head{display:flex;flex-wrap:wrap;align-items:baseline;gap:6px 16px;margin-bottom:10px}
.hint{font-size:11.5px;color:var(--faint)}

/* ---------- 统计卡 ---------- */
.stat-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px;margin:0 0 16px}
.stat-card{
  position:relative;overflow:hidden;background:var(--card);border:1px solid var(--line);
  border-radius:var(--radius);box-shadow:var(--shadow);padding:14px 16px 12px;
}
.stat-card::before{
  content:"";position:absolute;left:0;top:0;bottom:0;width:3px;background:var(--accent);
}
.acc-ail{--accent:var(--ail)} .acc-ele{--accent:var(--ele)}
.acc-rud{--accent:var(--rud)} .acc-trim{--accent:var(--trim)}
.stat-title{display:flex;align-items:center;gap:7px;font-size:13px;font-weight:640;color:#374151}
.dot{display:inline-block;width:8px;height:8px;border-radius:50%;background:var(--faint)}
.dot.ail{background:var(--ail)} .dot.ele{background:var(--ele)}
.dot.rud{background:var(--rud)} .dot.trim{background:var(--trim)}
.dot.diff{background:var(--diff)} .dot.evt{background:var(--evt)}
.stat-main{display:flex;align-items:baseline;gap:8px;margin-top:8px}
.stat-num{
  font-size:27px;font-weight:650;letter-spacing:-.02em;line-height:1.1;
  font-variant-numeric:tabular-nums;color:#111827;
}
.stat-cap{font-size:11.5px;color:var(--faint)}
.stat-line{
  display:flex;flex-wrap:wrap;gap:2px 14px;
  margin-top:4px;font-size:11.5px;color:var(--muted);
}
.stat-line b{color:#374151;font-weight:640;font-variant-numeric:tabular-nums}
.stat-line.sub{color:var(--faint);margin-top:2px}

/* ---------- 工具条 ---------- */
.toolbar{
  display:flex;flex-wrap:wrap;gap:10px 18px;align-items:center;
  justify-content:space-between;background:var(--card);border:1px solid var(--line);
  border-radius:var(--radius);box-shadow:var(--shadow);padding:10px 14px;margin:0 0 16px;
}
.tb-group{display:flex;flex-wrap:wrap;align-items:center;gap:7px}
.tb-right{margin-left:auto}
.tb-label{font-size:11px;letter-spacing:.08em;color:var(--faint);text-transform:uppercase}
.tb-sep{width:1px;height:18px;background:var(--line);margin:0 3px}
.chip{
  appearance:none;display:inline-flex;align-items:center;gap:6px;
  border:1px solid var(--line);background:#fffdf9;border-radius:999px;
  padding:5px 11px;font:inherit;font-size:12.5px;color:#374151;cursor:pointer;
  transition:background .15s ease,border-color .15s ease,opacity .15s ease;
  white-space:nowrap;
}
.chip:hover{border-color:#c9c1ac;background:#fffdf3}
.chip[aria-pressed="false"]{
  opacity:.5;background:transparent;border-style:dashed;
}
.chip.preset{color:var(--muted);font-size:12px;background:var(--soft)}
.chip.preset:hover{color:#374151;background:#fbf6e6}
.zoom-state{
  font-size:12px;color:var(--muted);font-variant-numeric:tabular-nums;
  padding:0 4px;white-space:nowrap;
}
.btn{
  appearance:none;border:1px solid var(--line);background:#fffdf9;border-radius:9px;
  padding:6px 12px;font:inherit;font-size:12.5px;color:#374151;cursor:pointer;
  transition:background .15s ease,border-color .15s ease,opacity .15s ease;
  white-space:nowrap;
}
.btn:hover:not(:disabled){border-color:#c9c1ac;background:#fffdf3}
.btn:disabled{opacity:.45;cursor:default}
.btn.primary{background:var(--ink);border-color:var(--ink);color:#fffdf9}
.btn.primary:hover{background:#111827;border-color:#111827}

/* ---------- 图表 ---------- */
.chart-card{padding:18px 18px 12px}
.legend{display:inline-flex;align-items:center;gap:6px;font-size:11.5px;color:var(--muted)}
.sw{display:inline-block;width:16px;height:4px;border-radius:2px;background:var(--faint)}
.sw.surface{background:#111827;opacity:.28;height:4px}
.sw.stick{background:linear-gradient(90deg,var(--ail),var(--ele) 55%,var(--rud))}
.svg-wrap{position:relative;user-select:none;-webkit-user-select:none;touch-action:none}
#chart{display:block;width:100%;height:auto}
.tooltip{
  position:absolute;z-index:30;pointer-events:none;
  background:rgba(24,32,44,.94);color:#f9fafb;border-radius:10px;
  padding:9px 11px;font-size:12px;line-height:1.55;
  box-shadow:0 12px 30px rgba(15,23,42,.28);min-width:196px;max-width:300px;
}
.tooltip[hidden]{display:none}
.tt-time{font-weight:640;font-size:12.5px;margin-bottom:5px;font-variant-numeric:tabular-nums}
.tt-row{display:flex;align-items:center;justify-content:space-between;gap:14px;white-space:nowrap}
.tt-row .k{display:inline-flex;align-items:center;gap:6px;color:#cbd5e1}
.tt-row .v{font-variant-numeric:tabular-nums;color:#fff}
.tt-foot{
  margin-top:6px;padding-top:5px;border-top:1px solid rgba(255,255,255,.14);
  color:#94a3b8;font-size:10.5px;line-height:1.45;white-space:normal;
}
.tt-dot{width:7px;height:7px;border-radius:50%;display:inline-block}

/* ---------- 表格 ---------- */
.table-wrap{overflow-x:auto;margin:0 -3px}
table.stats{
  border-collapse:collapse;width:100%;font-size:12.5px;
  font-variant-numeric:tabular-nums;
}
table.stats th,table.stats td{
  padding:8px 10px;border-bottom:1px solid var(--line-2);
  text-align:right;white-space:nowrap;
}
table.stats th:first-child,table.stats td:first-child{text-align:left}
table.stats thead th{
  color:var(--muted);font-weight:640;font-size:11.5px;
  background:var(--soft);border-bottom:1px solid var(--line);
}
table.stats tbody tr:hover{background:#fdfaf1}
.defs{margin-top:10px;color:var(--muted);font-size:12px}
.defs p{margin:5px 0}
.defs b{color:#374151}

/* ---------- 说明 ---------- */
.notes ol{margin:0;padding-left:20px}
.notes li{margin:8px 0}
.notes b{color:#111827}

/* ---------- 空态 ---------- */
.empty-state{display:flex;gap:16px;align-items:flex-start;border-left:4px solid #d97706}
.empty-badge{
  flex:0 0 auto;width:40px;height:40px;border-radius:50%;
  background:#fef3c7;color:#b45309;display:grid;place-items:center;
  font-size:20px;font-weight:600;line-height:1;
}
.empty-body h2{font-size:16px;margin-bottom:8px}
.empty-body p{margin:6px 0;color:var(--muted)}
.empty-body code{white-space:normal}

/* ---------- 页脚 / toast ---------- */
footer{
  display:flex;flex-wrap:wrap;gap:6px 20px;justify-content:space-between;
  color:var(--faint);font-size:11.5px;padding:6px 4px 0;
}
footer code{font-size:.94em;background:transparent;padding:0;color:var(--muted)}
.toast{
  position:fixed;left:50%;bottom:26px;transform:translateX(-50%);
  background:rgba(24,32,44,.94);color:#f9fafb;border-radius:10px;
  padding:9px 16px;font-size:12.5px;box-shadow:0 12px 30px rgba(15,23,42,.3);
  z-index:60;
}
.toast[hidden]{display:none}

/* ---------- 窄屏 ---------- */
@media (max-width:1080px){
  .facts{grid-template-columns:repeat(2,minmax(0,1fr))}
  .stat-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
}
@media (max-width:720px){
  .wrap{padding:20px 12px 40px}
  h1{font-size:23px}
  .facts{grid-template-columns:repeat(2,minmax(0,1fr));gap:8px 16px;margin-top:14px}
  .stat-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
  .stat-num{font-size:23px}
  .toolbar{padding:10px}
  .tb-right{margin-left:0;width:100%}
  .card{padding:15px 14px}
  .chart-card{padding:15px 10px 10px}
}
@media (max-width:460px){
  .stat-grid{grid-template-columns:minmax(0,1fr)}
}

/* ---------- 打印 / 导出 PDF ---------- */
@page{margin:11mm}
@media print{
  body{background:#fff}
  .wrap{max-width:none;padding:0}
  .toolbar,.tooltip,.toast,.hint,.no-print{display:none !important}
  .hero{padding:0 0 10px}
  h1{font-size:23px;margin:4px 0 6px}
  .lede{font-size:12px}
  .facts{grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 18px;padding:8px 10px;
         margin-top:10px;background:transparent;border-color:#ddd8ca}
  .fact .v{font-size:11.5px}
  .card,.stat-card{box-shadow:none;border-color:#dcd7ca;break-inside:avoid}
  .stat-grid{grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin-bottom:10px}
  .stat-card{padding:10px 12px}
  .stat-num{font-size:20px}
  .stat-line{font-size:10.5px}
  .chart-card{break-inside:avoid;padding:12px 10px 6px}
  .blk-note{display:none}
  #chart{width:100% !important;height:auto !important}
  table.stats thead th{background:#f6f3ea}
  .notes ol{padding-left:16px}
  .notes li{break-inside:avoid}
}
"""


# ---------------------------------------------------------------------------
# 交互脚本（纯内联，无外部库）
# ---------------------------------------------------------------------------

_JS = r"""
(function () {
  "use strict";

  var svg = document.getElementById("chart");
  var payloadEl = document.getElementById("surface-data");
  if (!svg || !payloadEl) { return; }

  var D;
  try { D = JSON.parse(payloadEl.textContent); } catch (err) { return; }

  var NS = svg.namespaceURI || "";
  var layout = D.layout;
  var L = layout.left;
  var PW = layout.plotW;
  var VBW = layout.vbw;
  var topPad = layout.topPad;
  var bottomPad = layout.bottomPad;
  var xaxisH = layout.xaxisH;
  var t0 = D.meta.t0;
  var t1 = D.meta.t1;
  var AXIS_KINDS = ["ail", "ele", "rud"];
  var ALL_KINDS = ["ail", "ele", "rud", "trim", "diff"];

  var state = {
    a: t0,
    b: t1,
    visible: { ail: true, ele: true, rud: true, trim: true, diff: true },
    events: true
  };

  function el(id) { return document.getElementById(id); }
  function q(sel, root) { return (root || document).querySelector(sel); }
  function qa(sel, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(sel));
  }
  function svgEl(name) { return document.createElementNS(NS, name); }
  function attrs(node, values) {
    for (var key in values) {
      if (Object.prototype.hasOwnProperty.call(values, key)) {
        node.setAttribute(key, String(values[key]));
      }
    }
    return node;
  }
  function clamp(value, lo, hi) { return value < lo ? lo : (value > hi ? hi : value); }
  function clear(node) {
    while (node && node.firstChild) { node.removeChild(node.firstChild); }
  }

  /* ---------- 运行时块 ---------- */
  var chartBlocks = [];
  layout.blocks.forEach(function (spec) {
    var group = q('[data-block="' + spec.id + '"]');
    if (!group) { return; }
    chartBlocks.push({
      spec: spec,
      kind: spec.kind,
      id: spec.id,
      group: group,
      top: spec.top,
      h: spec.h,
      lo: spec.lo,
      hi: spec.hi,
      blockH: spec.blockH,
      screenTop: 0,
      zoomer: q('[data-zoom="' + spec.id + '"]', group),
      xgrid: q('[data-xgrid="' + spec.id + '"]', group)
    });
  });
  var xaxisGroup = q('[data-block="xaxis"]');
  var laneGroup = q('[data-block="evtlane"]');
  var laneLabels = el("event-labels");
  var lane = laneGroup ? { group: laneGroup, labels: laneLabels, visible: false, h: 0 } : null;

  var totalH = 0;

  /* ---------- 坐标 ---------- */
  function xScreen(t) {
    return L + (t - state.a) / Math.max(state.b - state.a, 1e-9) * PW;
  }
  function timeAtX(x) {
    return state.a + (x - L) / PW * (state.b - state.a);
  }
  function yOf(block, value) {
    var lo = block.lo;
    var hi = block.hi;
    if (hi - lo < 1e-9) { hi = lo + 1e-9; }
    var v = clamp(value, lo, hi);
    return block.top + block.h - (v - lo) / (hi - lo) * block.h;
  }
  function screenYOf(block, value) {
    return block.screenTop + yOf(block, value);
  }

  function valueOf(kind, key, i) {
    if (key === "trim") {
      return D.trim ? D.trim[i] : null;
    }
    if (key.indexOf("d-") === 0) {
      var axis = D.axes[key.slice(2)];
      if (!axis) { return null; }
      var surf = axis.surf[i];
      var stick = axis.stick[i];
      if (surf === null || surf === undefined || stick === null || stick === undefined) {
        return null;
      }
      return surf - stick;
    }
    var source = D.axes[kind];
    if (!source || !source[key]) { return null; }
    var value = source[key][i];
    return (value === null || value === undefined) ? null : value;
  }

  /* ---------- 时间轴 ---------- */
  function timeTicks(a, b) {
    var span = Math.max(b - a, 1e-6);
    var raw = span / 7;
    var cands = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200];
    var step = cands[cands.length - 1];
    for (var c = 0; c < cands.length; c++) {
      if (raw <= cands[c]) { step = cands[c]; break; }
    }
    var start = Math.ceil(a / step - 1e-9) * step;
    var ticks = [];
    for (var v = start; v <= b + 1e-9 && ticks.length < 40; v += step) {
      ticks.push(v);
    }
    if (!ticks.length) {
      step = Math.max((b - a) / 2, 0.05);
      ticks = [a, (a + b) / 2, b];
    }
    return { step: step, ticks: ticks };
  }
  function pad2(value, width) {
    var text = String(value);
    while (text.length < width) { text = "0" + text; }
    return text;
  }
  function timeLabel(value, step) {
    if (step < 1) {
      var minutes = Math.floor(value / 60);
      var seconds = value - minutes * 60;
      return minutes + ":" + pad2(seconds.toFixed(1), 4);
    }
    var total = Math.round(value);
    return Math.floor(total / 60) + ":" + pad2(total % 60, 2);
  }

  function renderTimeAxis() {
    var info = timeTicks(state.a, state.b);
    chartBlocks.forEach(function (block) {
      if (!state.visible[block.kind] || !block.xgrid) { return; }
      clear(block.xgrid);
      info.ticks.forEach(function (tick) {
        var x = xScreen(tick);
        block.xgrid.appendChild(attrs(svgEl("line"), {
          x1: x, y1: block.top, x2: x, y2: block.top + block.h,
          stroke: D.colors.gridX, "stroke-width": 1
        }));
      });
    });
    var axis = el("xaxis");
    if (!axis) { return; }
    clear(axis);
    info.ticks.forEach(function (tick) {
      var x = xScreen(tick);
      axis.appendChild(attrs(svgEl("line"), {
        x1: x, y1: 0, x2: x, y2: 5, stroke: D.colors.gridStrong, "stroke-width": 1
      }));
      var label = attrs(svgEl("text"), {
        x: x, y: 18, "font-size": 10.5, fill: D.colors.muted, "text-anchor": "middle"
      });
      label.textContent = timeLabel(tick, info.step);
      axis.appendChild(label);
    });
  }

  /* ---------- 事件标签 ---------- */
  function renderEvents() {
    if (!lane) { return; }
    var on = state.events && D.events.length > 0;
    var items = [];
    if (on) {
      D.events.forEach(function (event) {
        var x = xScreen(event.t);
        if (x < L - 40 || x > L + PW + 40) { return; }
        var text = event.label + (event.detail ? " " + event.detail : "");
        var width = text.length * 10.5 + 14.0;
        var x0 = x + 4.0;
        if (x0 + width > L + PW) { x0 = x - 4.0 - width; }
        items.push({ ev: event, x: x, x0: x0, text: text, width: width, row: 0 });
      });
    }
    items.sort(function (a, b) { return a.x - b.x; });
    var rowEnds = [];
    var maxRow = -1;
    items.forEach(function (item) {
      var placed = -1;
      for (var r = 0; r < 2; r++) {
        if (r >= rowEnds.length) { rowEnds.push(-1e9); }
        if (item.x0 > rowEnds[r] + 8) { placed = r; break; }
      }
      if (placed < 0) { placed = 0; }
      item.row = placed;
      rowEnds[placed] = Math.max(rowEnds[placed], item.x0 + item.width);
      maxRow = Math.max(maxRow, placed);
    });
    if (!items.length) {
      lane.visible = false;
      lane.h = 0;
      if (lane.labels) { clear(lane.labels); }
      return;
    }
    lane.visible = true;
    lane.h = 7 + (maxRow + 1) * 17 + 5;
    if (!lane.labels) { return; }
    clear(lane.labels);
    items.forEach(function (item) {
      var group = attrs(svgEl("g"), {
        transform: "translate(" + item.x0.toFixed(1) + "," + (7 + item.row * 17) + ")"
      });
      group.appendChild(attrs(svgEl("rect"), {
        x: 0, y: 0, width: item.width.toFixed(1), height: 15, rx: 7.5,
        fill: item.ev.color, "fill-opacity": 0.12
      }));
      var text = attrs(svgEl("text"), {
        x: 7, y: 11, "font-size": 10.5, "font-weight": 600, fill: item.ev.color
      });
      text.textContent = item.text;
      group.appendChild(text);
      lane.labels.appendChild(group);
    });
  }

  /* ---------- 纵向布局 ---------- */
  function layoutAll() {
    var y = topPad;
    if (lane) {
      if (lane.visible) {
        lane.group.removeAttribute("display");
        lane.group.setAttribute("transform", "translate(0," + y + ")");
        lane.group.setAttribute("data-h", String(lane.h));
        y += lane.h;
      } else {
        lane.group.setAttribute("display", "none");
      }
    }
    chartBlocks.forEach(function (block) {
      if (!state.visible[block.kind]) {
        block.group.setAttribute("display", "none");
        return;
      }
      block.group.removeAttribute("display");
      block.group.setAttribute("transform", "translate(0," + y + ")");
      block.screenTop = y;
      y += block.blockH;
    });
    if (xaxisGroup) {
      xaxisGroup.setAttribute("transform", "translate(0," + y + ")");
      y += xaxisH;
    }
    totalH = y + bottomPad;
    svg.setAttribute("viewBox", "0 0 " + VBW + " " + totalH);
    svg.setAttribute("height", String(totalH));
  }

  /* ---------- 横向缩放 ---------- */
  function applyZoom() {
    var dataSpan = Math.max(t1 - t0, 1e-9);
    var shown = Math.max(state.b - state.a, 1e-9);
    var sx = dataSpan / shown;
    var tx = xScreen(t0) - sx * L;
    var transform = "translate(" + tx.toFixed(3) + ",0) scale(" + sx.toFixed(6) + ",1)";
    chartBlocks.forEach(function (block) {
      if (block.zoomer) { block.zoomer.setAttribute("transform", transform); }
    });
  }

  function updateZoomUI() {
    var node = el("zoom-state");
    var reset = el("zoom-reset");
    var full = Math.abs((state.b - state.a) - (t1 - t0)) < 1e-6;
    var text;
    if (full) {
      text = "全程 " + timeLabel(state.a, 0.1) + " – " + timeLabel(state.b, 0.1) +
        " · " + (state.b - state.a).toFixed(1) + " s";
    } else {
      text = "区间 " + timeLabel(state.a, 0.1) + " – " + timeLabel(state.b, 0.1) +
        " · " + (state.b - state.a).toFixed(1) + " s / 全程 " + (t1 - t0).toFixed(1) + " s";
    }
    if (node) { node.textContent = text; }
    if (reset) { reset.disabled = full; }
  }

  function zoomTo(a, b) {
    var lo = clamp(a, t0, t1);
    var hi = clamp(b, t0, t1);
    if (hi - lo < 0.2) { return; }
    if (hi - lo > (t1 - t0) - 0.05) {
      state.a = t0;
      state.b = t1;
    } else {
      state.a = lo;
      state.b = hi;
    }
    refresh();
  }
  function resetZoom() {
    state.a = t0;
    state.b = t1;
    refresh();
  }

  /* ---------- 可见性 / 刷新 ---------- */
  function syncChips() {
    qa("[data-axis]").forEach(function (chip) {
      var kind = chip.getAttribute("data-axis");
      chip.setAttribute("aria-pressed", state.visible[kind] ? "true" : "false");
    });
  }

  function refresh() {
    applyZoom();
    renderEvents();
    layoutAll();
    renderTimeAxis();
    updateZoomUI();
    hideHover();
  }

  /* ---------- 悬停 ---------- */
  var hoverLayer = el("hover-layer");
  var hoverV = el("hv-v");
  var hoverH = el("hv-h");
  var hoverDots = el("hv-dots");
  var tip = el("tooltip");
  var wrap = el("svg-wrap");

  function pointInSvg(ev) {
    var rect = svg.getBoundingClientRect();
    if (!rect.width) { return { x: 0, y: 0 }; }
    var scale = VBW / rect.width;
    return {
      x: (ev.clientX - rect.left) * scale,
      y: (ev.clientY - rect.top) * scale
    };
  }
  function nearestIndex(t) {
    var lo = 0;
    var hi = D.t.length - 1;
    while (lo < hi) {
      var mid = (lo + hi) >> 1;
      if (D.t[mid] < t) { lo = mid + 1; } else { hi = mid; }
    }
    var best = lo;
    if (lo > 0 && Math.abs(D.t[lo - 1] - t) <= Math.abs(D.t[lo] - t)) { best = lo - 1; }
    return best;
  }
  function blockAtY(y) {
    for (var i = 0; i < chartBlocks.length; i += 1) {
      var block = chartBlocks[i];
      if (!state.visible[block.kind]) { continue; }
      var top = block.screenTop + block.top;
      if (y >= top && y <= top + block.h) { return block; }
    }
    return null;
  }
  function visibleRange() {
    var top = Infinity;
    var bottom = -Infinity;
    chartBlocks.forEach(function (block) {
      if (!state.visible[block.kind]) { return; }
      top = Math.min(top, block.screenTop + block.top);
      bottom = Math.max(bottom, block.screenTop + block.top + block.h);
    });
    return { top: top, bottom: bottom };
  }
  function hideHover() {
    if (hoverLayer) { hoverLayer.setAttribute("display", "none"); }
    if (tip) { tip.hidden = true; }
  }
  function fmtSigned(value, digits) {
    if (value === null || value === undefined) { return "—"; }
    var v = Math.abs(value) < Math.pow(10, -digits) / 2 ? 0 : value;
    return (v > 0 ? "+" : "") + v.toFixed(digits);
  }
  function fmtClock(value) {
    var minutes = Math.floor(value / 60);
    var seconds = value - minutes * 60;
    return minutes + ":" + pad2(seconds.toFixed(1), 4);
  }
  function tooltipRow(label, color, valueText) {
    var row = document.createElement("div");
    row.className = "tt-row";
    var key = document.createElement("span");
    key.className = "k";
    var dot = document.createElement("i");
    dot.className = "tt-dot";
    dot.style.background = color;
    key.appendChild(dot);
    key.appendChild(document.createTextNode(label));
    var value = document.createElement("span");
    value.className = "v";
    value.textContent = valueText;
    row.appendChild(key);
    row.appendChild(value);
    return row;
  }
  function placeTooltip(ev) {
    var wrapRect = wrap.getBoundingClientRect();
    tip.hidden = false;
    var x = ev.clientX - wrapRect.left + 18;
    var y = ev.clientY - wrapRect.top + 16;
    var tw = tip.offsetWidth;
    var th = tip.offsetHeight;
    if (x + tw > wrapRect.width - 6) { x = ev.clientX - wrapRect.left - tw - 18; }
    if (y + th > wrapRect.height - 6) { y = wrapRect.height - th - 6; }
    if (x < 0) { x = 0; }
    if (y < 0) { y = 0; }
    tip.style.left = x + "px";
    tip.style.top = y + "px";
  }
  function showTooltip(ev, index) {
    if (!tip) { return; }
    var timeRow = el("tt-time");
    var rows = el("tt-rows");
    if (timeRow) {
      timeRow.textContent = "t = " + fmtClock(D.t[index]) + "（" + D.t[index].toFixed(2) + " s）";
    }
    if (rows) {
      clear(rows);
      AXIS_KINDS.forEach(function (kind) {
        if (!state.visible[kind]) { return; }
        var axis = D.axes[kind];
        if (!axis) { return; }
        var surf = axis.surf[index];
        var stick = axis.stick[index];
        var text = "—";
        if (surf !== null && surf !== undefined && stick !== null && stick !== undefined) {
          text = fmtSigned(surf, 0) + "% · " + fmtSigned(stick, 1) + " · " +
            fmtSigned(surf - stick, 1);
        }
        rows.appendChild(tooltipRow(axis.label, axis.color, text));
      });
      if (state.visible.trim && D.trim && D.trim[index] !== null && D.trim[index] !== undefined) {
        rows.appendChild(tooltipRow("配平", D.colors.trim, fmtSigned(D.trim[index], 3)));
      }
    }
    placeTooltip(ev);
  }

  function hover(ev) {
    var point = pointInSvg(ev);
    if (point.x < L || point.x > L + PW) { hideHover(); return; }
    var block = blockAtY(point.y);
    if (!block) { hideHover(); return; }
    var index = nearestIndex(timeAtX(point.x));
    var range = visibleRange();
    if (!isFinite(range.top)) { hideHover(); return; }
    var x = xScreen(D.t[index]);
    if (hoverV) {
      attrs(hoverV, { x1: x, x2: x, y1: range.top, y2: range.bottom });
    }
    if (hoverH) {
      var y = clamp(point.y, block.screenTop + block.top, block.screenTop + block.top + block.h);
      attrs(hoverH, { x1: L, x2: L + PW, y1: y, y2: y });
    }
    if (hoverDots) {
      clear(hoverDots);
      chartBlocks.forEach(function (item) {
        if (!state.visible[item.kind]) { return; }
        item.spec.series.forEach(function (series) {
          var value = valueOf(item.kind, series.key, index);
          if (value === null || value === undefined) { return; }
          hoverDots.appendChild(attrs(svgEl("circle"), {
            cx: x, cy: screenYOf(item, value), r: 3.1,
            fill: series.color, stroke: "#fffdf9", "stroke-width": 1.2
          }));
        });
      });
    }
    if (hoverLayer) { hoverLayer.removeAttribute("display"); }
    showTooltip(ev, index);
  }

  /* ---------- 框选 ---------- */
  var brushLayer = el("brush-layer");
  var brushRect = el("brush-rect");
  var dragging = null;

  function drawBrush() {
    if (!dragging || !brushRect) { return; }
    var range = visibleRange();
    var x0 = Math.min(dragging.x0, dragging.x1);
    var x1 = Math.max(dragging.x0, dragging.x1);
    attrs(brushRect, {
      x: x0, y: range.top, width: Math.max(x1 - x0, 1),
      height: Math.max(range.bottom - range.top, 1)
    });
    if (brushLayer) { brushLayer.removeAttribute("display"); }
  }
  function hideBrush() {
    if (brushLayer) { brushLayer.setAttribute("display", "none"); }
  }

  if (wrap) {
    wrap.addEventListener("pointerdown", function (ev) {
      if (ev.button !== 0) { return; }
      var point = pointInSvg(ev);
      if (point.x < L || point.x > L + PW) { return; }
      dragging = { x0: point.x, x1: point.x, moved: false };
      hideHover();
      try { wrap.setPointerCapture(ev.pointerId); } catch (err) { /* 老浏览器 */ }
      ev.preventDefault();
    });
    wrap.addEventListener("pointermove", function (ev) {
      if (dragging) {
        var point = pointInSvg(ev);
        dragging.x1 = clamp(point.x, L, L + PW);
        if (Math.abs(dragging.x1 - dragging.x0) > 4) { dragging.moved = true; }
        drawBrush();
        return;
      }
      hover(ev);
    });
    wrap.addEventListener("pointerup", function (ev) {
      if (!dragging) { return; }
      var drag = dragging;
      dragging = null;
      hideBrush();
      try { wrap.releasePointerCapture(ev.pointerId); } catch (err) { /* 忽略 */ }
      if (!drag.moved) { return; }
      var a = timeAtX(Math.min(drag.x0, drag.x1));
      var b = timeAtX(Math.max(drag.x0, drag.x1));
      zoomTo(a, b);
    });
    wrap.addEventListener("pointercancel", function () {
      dragging = null;
      hideBrush();
    });
    wrap.addEventListener("pointerleave", function () {
      if (!dragging) { hideHover(); }
    });
    wrap.addEventListener("dblclick", function () { resetZoom(); });
  }

  /* ---------- 轴开关 / 预设 / 事件开关 ---------- */
  qa("[data-axis]").forEach(function (chip) {
    chip.addEventListener("click", function () {
      var kind = chip.getAttribute("data-axis");
      if (!(kind in state.visible)) { return; }
      var next = !state.visible[kind];
      var anyAfter = false;
      ALL_KINDS.forEach(function (item) {
        if (item !== kind && state.visible[item]) { anyAfter = true; }
      });
      if (!next && !anyAfter) { return; }
      state.visible[kind] = next;
      syncChips();
      refresh();
    });
  });
  qa("[data-preset]").forEach(function (button) {
    button.addEventListener("click", function () {
      var preset = button.getAttribute("data-preset");
      ALL_KINDS.forEach(function (kind) {
        state.visible[kind] = (preset === "all") ? true : (kind === "diff");
      });
      syncChips();
      refresh();
    });
  });
  var eventButton = el("evt-toggle");
  if (eventButton) {
    eventButton.addEventListener("click", function () {
      state.events = !state.events;
      eventButton.setAttribute("aria-pressed", state.events ? "true" : "false");
      qa('[data-role="events"]').forEach(function (group) {
        group.setAttribute("display", state.events ? "inline" : "none");
      });
      refresh();
    });
  }

  /* ---------- 导出 PNG ---------- */
  var toastTimer = null;
  function toast(text) {
    var node = el("toast");
    if (!node) { return; }
    node.textContent = text;
    node.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { node.hidden = true; }, 3600);
  }
  function downloadBlob(blob) {
    var url = URL.createObjectURL(blob);
    var link = document.createElement("a");
    link.href = url;
    link.download = D.meta.exportName + ".png";
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(function () { URL.revokeObjectURL(url); }, 5000);
  }
  function drawAndDownload(image) {
    var pad = 28;
    var headerH = 96;
    var footH = 34;
    var canvasW = VBW + pad * 2;
    var canvasH = pad + headerH + totalH + footH + pad;
    var scale = 2;
    var canvas = document.createElement("canvas");
    canvas.width = Math.round(canvasW * scale);
    canvas.height = Math.round(canvasH * scale);
    var ctx = canvas.getContext("2d");
    if (!ctx) { toast("导出失败：浏览器不支持 canvas"); return; }
    ctx.scale(scale, scale);
    ctx.fillStyle = D.colors.card;
    ctx.fillRect(0, 0, canvasW, canvasH);
    ctx.fillStyle = D.colors.ink;
    ctx.font = '600 22px "Segoe UI", "Microsoft YaHei", sans-serif';
    ctx.fillText(D.meta.title, pad, pad + 24);
    ctx.fillStyle = D.colors.muted;
    ctx.font = '12.5px "Segoe UI", "Microsoft YaHei", sans-serif';
    ctx.fillText(D.meta.subtitle, pad, pad + 48);
    ctx.fillText(D.meta.footer, pad, pad + 68);
    ctx.drawImage(image, pad, pad + headerH, VBW, totalH);
    ctx.fillStyle = D.colors.faint;
    ctx.font = '11.5px "Segoe UI", "Microsoft YaHei", sans-serif';
    ctx.fillText(D.meta.note, pad, pad + headerH + totalH + 22);
    canvas.toBlob(function (blob) {
      if (!blob) { toast("导出失败：canvas 转 PNG 失败"); return; }
      downloadBlob(blob);
      toast("已导出 " + D.meta.exportName + ".png");
    }, "image/png");
  }
  function exportPng() {
    try {
      var clone = svg.cloneNode(true);
      ["hover-layer", "brush-layer"].forEach(function (id) {
        var node = q("#" + id, clone);
        if (node && node.parentNode) { node.parentNode.removeChild(node); }
      });
      if (!state.events) {
        qa('[data-role="events"]', clone).forEach(function (node) {
          if (node.parentNode) { node.parentNode.removeChild(node); }
        });
      }
      clone.setAttribute("width", String(VBW));
      clone.setAttribute("height", String(totalH));
      clone.setAttribute("viewBox", "0 0 " + VBW + " " + totalH);
      var background = attrs(svgEl("rect"), {
        x: 0, y: 0, width: VBW, height: totalH, fill: D.colors.card
      });
      clone.insertBefore(background, clone.firstChild);
      var xml = new XMLSerializer().serializeToString(clone);
      if (xml.indexOf("xmlns=") < 0) {
        xml = xml.replace(/^<([A-Za-z][A-Za-z0-9]*)/, '<$1 xmlns="' + NS + '"');
      }
      var image = new Image();
      image.onload = function () { drawAndDownload(image); };
      image.onerror = function () { toast("导出失败：SVG 转图片被浏览器拒绝"); };
      image.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(xml);
    } catch (err) {
      toast("导出失败：" + (err && err.message ? err.message : err));
    }
  }
  var exportButton = el("export-png");
  if (exportButton) { exportButton.addEventListener("click", exportPng); }
  var printButton = el("print-pdf");
  if (printButton) {
    printButton.addEventListener("click", function () { window.print(); });
  }

  /* ---------- 调试入口（selftest / 人工核对用，不影响交互） ---------- */
  window.__surfacePreview = {
    state: state,
    meta: D.meta,
    chartBlocks: chartBlocks,
    zoomTo: zoomTo,
    resetZoom: resetZoom,
    exportPng: exportPng,
    get totalH() { return totalH; }
  };

  syncChips();
  refresh();
})();
"""


# ---------------------------------------------------------------------------
# 页面模板
# ---------------------------------------------------------------------------

_DOC = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>@@TITLE@@</title>
<style>
@@STYLE@@
</style>
</head>
<body>
<div class="wrap">
@@HERO@@
@@TOOLBAR@@
@@STATS@@
@@CHART@@
@@TABLE@@
@@NOTES@@
@@FOOTER@@
</div>
<div class="toast" id="toast" hidden></div>
<script type="application/json" id="surface-data">@@DATA@@</script>
<script>
@@SCRIPT@@
</script>
</body>
</html>
"""


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------


def _fmt_utc(utc: Any) -> str:
    if not isinstance(utc, str) or len(utc) < 16:
        return "—"
    text = utc.replace("T", " ")
    return text[:16] + " UTC"


def _json_safe_float(value: float, digits: int) -> Any:
    rounded = round(float(value), digits)
    if digits <= 1 and float(rounded).is_integer():
        return int(rounded)
    return rounded


def _build_payload(*, rows, idx, t0: float, t1: float, events: Sequence[Dict[str, Any]],
                   layout: Dict[str, Any], meta: Dict[str, Any]) -> Dict[str, Any]:
    times = [round(float(rows[i].get("t") or 0.0), 3) for i in idx]
    axes_payload: Dict[str, Any] = {}
    for axis_id, label, surf_key, stick_key, color in AXES:
        surf: List[Optional[Any]] = []
        stick: List[Optional[Any]] = []
        for i in idx:
            row = rows[i]
            s = row.get(surf_key)
            k = row.get(stick_key)
            surf.append(None if s is None else _json_safe_float(float(s), 1))
            stick.append(None if k is None else _json_safe_float(float(k) * 100.0, 2))
        axes_payload[axis_id] = {
            "label": label, "color": color, "surf": surf, "stick": stick,
        }
    trim: List[Optional[Any]] = []
    for i in idx:
        value = rows[i].get("trimmer")
        trim.append(None if value is None else _json_safe_float(float(value), 4))
    return {
        "meta": meta,
        "t": times,
        "axes": axes_payload,
        "trim": trim,
        "events": list(events),
        "colors": {
            "ink": INK, "muted": MUTED, "faint": "#9ca3af", "card": CARD,
            "gridX": GRID_X, "gridStrong": GRID_STRONG, "trim": TRIM_COLOR,
        },
        "layout": layout,
    }


def build_preview(log_path: str, out_path: Optional[str] = None, title: Optional[str] = None,
                  whole_log: bool = False) -> str:
    """生成预览页，返回写出的 HTML 路径。"""
    if not os.path.isfile(log_path):
        raise LogError("日志文件不存在: %s" % log_path)
    model = analyze(log_path)
    rows = _collect(model)
    if not rows:
        raise LogError("日志里没有可用样本: %s" % log_path)

    if whole_log:
        rows_used = rows
        window_note = "全程（含地面段）"
    else:
        start, finish, window_note = _pick_window(rows)
        rows_used = rows[start:finish + 1]

    idx = _thin(rows_used)
    t0 = float(rows_used[idx[0]].get("t") or 0.0)
    t1 = float(rows_used[idx[-1]].get("t") or 0.0)
    span = max(t1 - t0, 0.0)
    rate = (len(rows_used) / span) if span > 0 else 0.0

    vehicle = model.meta.get("vehicle") or (model.flights[0].vehicle if model.flights else "") or "—"
    schema = model.meta.get("schema")
    app = model.meta.get("app") or "—"
    recorded = _fmt_utc(model.meta.get("utc"))
    source_file = os.path.basename(log_path)
    generated = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    page_title = title or ("舵面 / 配平 / 指令轴 — %s" % source_file)
    hero_title = "舵面 / 配平 / 指令轴"
    subtitle = ("把 <code>/state</code> 的 1% 舵面、<code>/indicators</code> 的 0.001 级指令轴"
                "和配平放在同一条时间轴上：悬停读数、框选缩放、轴开关、导出 PNG / PDF。"
                "缺字段时给空态，不把 <code>None</code> 画成 0。")

    has_any = any(row.get(key) is not None for row in rows_used
                  for key in ("aileron_pct", "elevator_pct", "rudder_pct", "trimmer",
                              "stick_ailerons", "stick_elevator", "pedals"))

    stats: Dict[str, Dict[str, Any]] = {}
    for _axis_id, _label, surf_key, stick_key, _color in AXES:
        stats[surf_key] = _axis_stats(rows_used, surf_key, stick_key)
    trim_stats = _trim_stats(rows_used)

    events = _event_payload(model.events, t0, t1)

    # ---- 布局 ----
    blocks: List[Dict[str, Any]] = []
    has_lane = bool(events)
    lane_items, _lane_rows, lane_h = _lane_items(events, t0, t1) if has_lane else ([], 0, 0)
    lane_offset = TOP_PAD
    offset = TOP_PAD + lane_h

    xticks_step, xticks = _time_ticks(t0, t1)

    for axis_id, label, surf_key, stick_key, color in AXES:
        lo, hi = _axis_domain(rows_used, idx, surf_key, stick_key)
        _step, yticks = _y_ticks(lo, hi, 5)
        note = "灰阶梯 = 舵面 %（1% 量化）· 细线 = 指令轴 ×100"
        blocks.append({
            "id": axis_id, "kind": axis_id, "plot_h": AXIS_PLOT_H,
            "block_h": BLOCK_H_AXIS, "offset": offset,
            "title": label, "note": note, "color": color,
            "lo": lo, "hi": hi, "yticks": yticks, "digits": 0,
            "xticks": xticks, "surf_key": surf_key, "stick_key": stick_key,
            "empty_text": "该窗口没有%s数据" % label,
            "series": [
                {"key": "surf", "color": SURFACE_COLOR},
                {"key": "stick", "color": color},
            ],
        })
        offset += BLOCK_H_AXIS

    trim_lo, trim_hi = _trim_domain(rows_used, idx)
    _step, trim_ticks = _y_ticks(trim_lo, trim_hi, 4)
    if trim_stats.get("n"):
        trim_note = ("全程 %s … %s · 显示范围按 2%%–98%% 分位"
                     % (_sgn(trim_stats["min"], 3), _sgn(trim_stats["max"], 3)))
        trim_empty = ""
    else:
        trim_note = "该日志没有配平数据（0.5.10 之前录制，或全程未开增稳）"
        trim_empty = "该日志没有配平数据"
    blocks.append({
        "id": "trim", "kind": "trim", "plot_h": TRIM_PLOT_H,
        "block_h": BLOCK_H_TRIM, "offset": offset,
        "title": "配平（trimmer，−1…1）", "note": trim_note, "color": TRIM_COLOR,
        "lo": trim_lo, "hi": trim_hi, "yticks": trim_ticks, "digits": 2,
        "xticks": xticks, "empty_text": trim_empty,
        "series": [{"key": "trim", "color": TRIM_COLOR}],
    })
    offset += BLOCK_H_TRIM

    diff_lo, diff_hi = _diff_domain(rows_used, idx)
    _step, diff_ticks = _y_ticks(diff_lo, diff_hi, 4)
    blocks.append({
        "id": "diff", "kind": "diff", "plot_h": DIFF_PLOT_H,
        "block_h": BLOCK_H_DIFF, "offset": offset,
        "title": "差值（舵面 − 指令轴×100）",
        "note": "尖峰 = 指令轴滞后约 1 个采样（≈24 ms），不是操纵差动",
        "color": "#64748b",
        "lo": diff_lo, "hi": diff_hi, "yticks": diff_ticks, "digits": 0,
        "xticks": xticks, "empty_text": "该窗口没有残差数据",
        "dot_colors": [color for _axis_id, _label, _s, _k, color in AXES],
        "series": [
            {"key": "d-%s" % axis_id, "color": color}
            for axis_id, _label, _s, _k, color in AXES
        ],
    })
    offset += BLOCK_H_DIFF

    xaxis_offset = offset
    total_h = xaxis_offset + XAXIS_H + BOTTOM_PAD

    layout: Dict[str, Any] = {
        "vbw": VB_W, "left": LEFT, "right": RIGHT, "plotW": PLOT_W,
        "topPad": TOP_PAD, "bottomPad": BOTTOM_PAD, "xaxisH": XAXIS_H,
        "blocks": [
            {
                "id": block["id"], "kind": block["kind"],
                "top": PLOT_TOP, "h": block["plot_h"], "blockH": block["block_h"],
                "lo": block["lo"], "hi": block["hi"],
                "series": block["series"],
            }
            for block in blocks
        ],
        "lane": {"blockH": lane_h} if has_lane else None,
    }

    meta = {
        "title": hero_title,
        "subtitle": "机型 %s · schema %s · 窗口内 %s 样本 / %.1f s · %s"
                    % (vehicle, schema, _thousands(len(rows_used)), span, window_note),
        "footer": "WtHud FlightLog · surface_preview · 生成 %s · 单文件无外链" % generated,
        "note": "差值 = 舵面 − 指令轴×100；尖峰多来自 /indicators 采样滞后（约 1 个采样 / 24 ms），"
                "不是操纵差动；配平只在关闭增稳/教练模式时有值。",
        "exportName": "舵面预览-%s-%s" % (vehicle, generated.replace("-", "").replace(" ", "-").replace(":", "")),
        "t0": t0, "t1": t1,
    }

    # ---- SVG ----
    svg_parts: List[str] = []
    svg_parts.append(
        '<svg id="chart" viewBox="0 0 %d %d" role="img" '
        'aria-label="舵面、指令轴、配平与残差的时间序列" '
        'font-family="Segoe UI, Microsoft YaHei, sans-serif">' % (VB_W, total_h))
    svg_parts.append(_clip_defs(blocks))
    if has_lane:
        svg_parts.append(_lane_svg(lane_items, lane_h, lane_offset))
    for block in blocks:
        if block["kind"] == "trim":
            svg_parts.append(_block_trim(block, rows_used, idx, t0, t1, events))
        elif block["kind"] == "diff":
            svg_parts.append(_block_diff(block, rows_used, idx, t0, t1, events))
        else:
            svg_parts.append(_block_axis(block, rows_used, idx, t0, t1, events))
    svg_parts.append(_xaxis_block(xaxis_offset, _xaxis_svg(xticks_step, xticks, t0, t1)))
    svg_parts.append(
        '<g id="hover-layer" display="none" pointer-events="none">'
        '<line id="hv-v" x1="0" y1="0" x2="0" y2="0" stroke="#6b7280" stroke-width="1" '
        'stroke-dasharray="3 3"/>'
        '<line id="hv-h" x1="0" y1="0" x2="0" y2="0" stroke="#9ca3af" stroke-width="1" '
        'stroke-opacity="0.75"/>'
        '<g id="hv-dots"></g></g>'
        '<g id="brush-layer" display="none" pointer-events="none">'
        '<rect id="brush-rect" x="0" y="0" width="0" height="0" fill="#2563eb" '
        'fill-opacity="0.09" stroke="#2563eb" stroke-opacity="0.4" stroke-width="1"/></g>')
    svg_parts.append('</svg>')
    svg_html = "".join(svg_parts)

    payload = _build_payload(rows=rows_used, idx=idx, t0=t0, t1=t1, events=events,
                             layout=layout, meta=meta)
    data_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    data_json = data_json.replace("</", "<\\/")

    # ---- HTML ----
    facts = [
        ("机型", vehicle),
        ("录制", recorded),
        ("schema", "%s · app %s" % (schema, app)),
        ("样本", "%s 窗口内 / %s 整份" % (_thousands(len(rows_used)), _thousands(len(rows)))),
        ("时长", "%.1f s · 平均 %.1f Hz" % (span, rate)),
        ("抽稀", "%s / %s 点" % (_thousands(len(idx)), _thousands(len(rows_used)))),
        ("窗口", window_note),
        ("文件", source_file),
    ]
    hero = _hero_html(facts, hero_title, subtitle)

    if has_any:
        toolbar = _toolbar_html(events)
        chart_card = (
            '<section class="card chart-card">'
            '<div class="card-head"><h2>时间序列</h2>'
            '<span class="hint">拖动框选缩放 · 双击复位 · 悬停读数</span>'
            '<span class="legend"><i class="sw surface"></i>舵面 %%（灰阶梯，1%% 量化）</span>'
            '<span class="legend"><i class="sw stick"></i>指令轴 ×100（彩细线，0.001 级）</span>'
            '</div>'
            '<div class="svg-wrap" id="svg-wrap">%s'
            '<div class="tooltip" id="tooltip" hidden>'
            '<div class="tt-time" id="tt-time">t = 0:00.0</div>'
            '<div id="tt-rows"></div>'
            '<div class="tt-foot">舵面 %% · 指令轴×100 · 残差 %%</div>'
            '</div></div></section>' % svg_html)
    else:
        toolbar = ""
        chart_card = _empty_card_html(schema, app, vehicle)

    doc = (_DOC
           .replace("@@TITLE@@", _esc(page_title))
           .replace("@@STYLE@@", _CSS)
           .replace("@@HERO@@", hero)
           .replace("@@TOOLBAR@@", toolbar)
           .replace("@@STATS@@", _stat_grid(stats, trim_stats, has_any))
           .replace("@@CHART@@", chart_card)
           .replace("@@TABLE@@", _stats_card_html(_stat_table(stats, trim_stats, has_any)))
           .replace("@@NOTES@@", _notes_html())
           .replace("@@FOOTER@@", _footer_html(generated, source_file, schema))
           .replace("@@SCRIPT@@", _JS)
           .replace("@@DATA@@", data_json))

    if out_path is None:
        stem = os.path.splitext(os.path.basename(log_path))[0]
        out_path = os.path.join("out", "%s-surface.html" % stem)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(doc)
    return out_path
