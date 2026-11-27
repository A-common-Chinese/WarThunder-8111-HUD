#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""MFD 数据导出（``--mfd-json``）：把飞行日志压成 HUD 内嵌 MFD 面板可直接消费的 JSON。

设计目标（与 HTML 报告解耦，不经过 report/SVG）：

* **机器可读、版本化**：``schema = 1``，单位写进字段名（``_kmh`` / ``_m`` / ``_g`` …），
  字段只增不减；HUD 侧按 ``schema`` 判断兼容性；
* **面向几百像素的小屏**：每个架次内嵌一条独立降采样序列（约 2 Hz、单架次 ≤ 900 点），
  多架次时 MFD 切架次直接换用 ``flights[i].series``，不用在一条曲线上做范围裁剪；
* **平均值自动去噪**：不做手动范围；只排除「死亡前 10 s 窗口」样本，并**只在死亡前 30 s
  窗口内**用 MAD 剔除超大波动（窗口外不做过离群过滤，平均接近原始均值）；
* **快**：只读 JSONL（或 ACMI），不生成 HTML/SVG，8k 样本 1 s 内完成；
* **缺失值写 ``null``**（不是 0），数组长度与 ``series.t`` 严格一致。

字段与规则说明见 README 的「MFD 数据导出（--mfd-json）」一节。
"""

from __future__ import annotations

import bisect
import datetime as _dt
import json
import math
import os
import statistics
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import LogModel, build_series

# ---------------------------------------------------------------------------
# 常量（HUD 侧契约）
# ---------------------------------------------------------------------------

#: 输出 JSON 的 schema 版本；HUD 只认这个数字，字段变化时递增
MFD_SCHEMA = 1

#: ``tool`` 固定为 FlightLog；``tool_version`` 与 WtHud 发布版本对齐
MFD_TOOL = "FlightLog"
MFD_TOOL_VERSION = "0.5.10"

#: 平均值过滤：死亡事件前多少秒的样本不参与平均
DEATH_WINDOW_S = 10.0
#: MAD 尖峰过滤的生效窗口：``[death - SPIKE_WINDOW_S, death]``（窗口外一律不做过离群过滤）
SPIKE_WINDOW_S = 30.0
#: 平均值过滤：鲁棒尖峰阈值 = spike_sigma × 1.4826 × MAD
SPIKE_SIGMA = 3.0
#: MAD -> 标准差的换算系数（正态分布下）
MAD_TO_SIGMA = 1.4826
#: 尖峰过滤后至少保留多少样本，否则退回「只排除死亡窗口」
MIN_FILTER_SAMPLES = 30

#: 单个架次降采样后的最大点数（MFD 屏幕只有几百像素）
MAX_SERIES_POINTS = 900
#: 降采样目标采样率（Hz）；实际约 1–2 Hz
SERIES_TARGET_HZ = 2.0

#: 事件只导出落在样本时间范围 ± 该容差内的（记录结束后的事件没有时间轴可放）
EVENT_MARGIN_S = 1.0

#: 序列字段：(内部序列键, 输出键, 保留小数位)
#: 速度 0.1、Mach 0.001、G 0.01，其余见括号
SERIES_FIELDS: Tuple[Tuple[str, str, int], ...] = (
    ("ias", "ias_kmh", 1),
    ("tas", "tas_kmh", 1),
    ("mach", "mach", 3),
    ("alt", "alt_m", 1),
    ("ralt", "ralt_m", 1),
    ("es", "es_m", 1),
    ("des", "des_dt_ms", 2),
    ("vy", "vy_ms", 2),
    ("aoa", "aoa_deg", 2),
    ("ny", "ny_g", 2),
    ("pitch", "pitch_deg", 2),
    ("bank", "bank_deg", 2),
)

#: 统计值小数位
_DIGITS = {
    "max_ias_kmh": 1,
    "max_tas_kmh": 1,
    "max_mach": 3,
    "max_g": 2,
    "max_alt_m": 1,
    "avg_ias_kmh": 1,
    "avg_tas_kmh": 1,
    "avg_mach": 3,
    "avg_alt_m": 1,
    "es_range_m": 1,
    "es_delta_m": 1,
    "max_ias_filtered_kmh": 1,
    "max_tas_filtered_kmh": 1,
}


# ---------------------------------------------------------------------------
# 小工具
# ---------------------------------------------------------------------------


def _round(value: Any, digits: int) -> Optional[float]:
    """四舍五入成 JSON 友好的 float；None / 非有限值一律 None（写进 JSON 是 null）。"""
    if value is None or isinstance(value, bool):
        return None
    try:
        out = float(value)
    except (TypeError, ValueError):
        return None
    if not math.isfinite(out):
        return None
    return round(out, digits)


def _iso_now_utc() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _median_mad(values: Sequence[float]) -> Tuple[Optional[float], Optional[float]]:
    """返回 ``(median, MAD)``；空序列返回 ``(None, None)``。"""
    if not values:
        return None, None
    median = statistics.median(values)
    mad = statistics.median([abs(value - median) for value in values])
    return median, mad


def _mean_on(values: Sequence[Optional[float]], mask: Sequence[bool]) -> Optional[float]:
    total = 0.0
    count = 0
    for index, keep in enumerate(mask):
        if not keep:
            continue
        value = values[index]
        if value is None:
            continue
        total += value
        count += 1
    return (total / count) if count else None


def _max_on(values: Sequence[Optional[float]], mask: Sequence[bool]) -> Optional[float]:
    best: Optional[float] = None
    for index, keep in enumerate(mask):
        if not keep:
            continue
        value = values[index]
        if value is None:
            continue
        if best is None or value > best:
            best = value
    return best


def _max_abs(values: Sequence[Optional[float]]) -> Optional[float]:
    best: Optional[float] = None
    for value in values:
        if value is None:
            continue
        magnitude = abs(value)
        if best is None or magnitude > best:
            best = magnitude
    return best


def _minmax(values: Sequence[Optional[float]]) -> Tuple[Optional[float], Optional[float]]:
    present = [value for value in values if value is not None]
    if not present:
        return None, None
    return min(present), max(present)


# ---------------------------------------------------------------------------
# 平均值过滤（死亡窗口 + MAD 尖峰）
# ---------------------------------------------------------------------------


def death_times(flight) -> List[float]:
    """架次内所有死亡时刻（事件 + ``Flight.death_t``，去重升序）。"""
    found = {event.t for event in flight.events if event.kind == "death" and event.t is not None}
    if flight.death_t is not None:
        found.add(flight.death_t)
    return sorted(found)


def death_window_mask(
    times: Sequence[float], deaths: Sequence[float], window_s: float = DEATH_WINDOW_S
) -> List[bool]:
    """``[death - window_s, death]`` 闭区间内的样本标记为 True（不参与平均）。"""
    mask = [False] * len(times)
    for death in deaths:
        lo = bisect.bisect_left(times, death - window_s)
        hi = bisect.bisect_right(times, death)
        for index in range(lo, hi):
            mask[index] = True
    return mask


def spike_window_mask(
    times: Sequence[float],
    deaths: Sequence[float],
    window_s: float = SPIKE_WINDOW_S,
    base_mask: Optional[Sequence[bool]] = None,
) -> List[bool]:
    """``[death - window_s, death]`` 内、且 ``base_mask`` 为 True 的样本标记为 True。

    这是 MAD 尖峰检测的**唯一**生效范围：只处理死亡前的窗口，窗口外（起飞/巡航/盘旋）
    一律不做离群过滤。死亡窗口内已经排除的样本不再参与尖峰统计（``base_mask`` 交集）。

    没有 death 事件时返回全 False —— 即「无死亡 = 不做 MAD 过滤」。
    """
    count = len(times)
    mask = [False] * count
    for death in deaths:
        lo = bisect.bisect_left(times, death - window_s)
        hi = bisect.bisect_right(times, death)
        for index in range(lo, hi):
            mask[index] = True
    if base_mask is not None:
        mask = [mask[index] and bool(base_mask[index]) for index in range(count)]
    return mask


def spike_mask(
    values: Sequence[Optional[float]],
    window_mask: Sequence[bool],
    sigma: float = SPIKE_SIGMA,
    min_samples: int = MIN_FILTER_SAMPLES,
) -> Tuple[Optional[List[bool]], bool]:
    """对 ``window_mask`` 内（死亡前窗口）的样本做 MAD 尖峰检测。

    返回 ``(mask, fell_back)``：

    * ``mask`` 与 ``values`` 等长的 True/False 掩码（True = 尖峰要排除）；
      ``None`` 表示该通道**不做**尖峰过滤（检测范围内有效值少于 3 个，MAD 无意义，
      或没有死亡窗口 —— 窗口外的样本永远不标记）；
    * ``fell_back`` 只在「按公式过滤后窗口内剩余样本 < ``min_samples``」时为 True ——
      此时该通道退回「只排除死亡窗口」，调用方应记录 ``avg_filter.fallback``。

    注：量化数据里 MAD 可能正好是 0（大部分样本同值），此时阈值 = 0，
    ``|x - median| > 0`` 会把窗口内的孤立尖峰排除，正常样本（同值）保留，符合预期。
    """
    count = len(values)
    window_indices = [index for index in range(count) if window_mask[index]]
    present = [values[index] for index in window_indices if values[index] is not None]
    if len(present) < 3:
        return None, False
    median, mad = _median_mad(present)
    if median is None or mad is None:
        return None, False
    threshold = sigma * MAD_TO_SIGMA * mad
    mask = [False] * count
    excluded = 0
    for index in window_indices:
        value = values[index]
        if value is None:
            continue
        if abs(value - median) > threshold:
            mask[index] = True
            excluded += 1
    if len(window_indices) - excluded < min_samples:
        return None, True  # 窗口内样本太少 -> 退回只排除死亡窗口
    return mask, False


def _apply_spike(base_mask: Sequence[bool], spike: Optional[Sequence[bool]]) -> List[bool]:
    """``base_mask`` 去掉尖峰后的掩码；``spike is None`` 表示该通道不滤。"""
    if spike is None:
        return list(base_mask)
    return [bool(base_mask[index]) and not bool(spike[index]) for index in range(len(base_mask))]


def flight_averages(
    flight,
    *,
    death_window_s: float = DEATH_WINDOW_S,
    spike_window_s: float = SPIKE_WINDOW_S,
    spike_sigma: float = SPIKE_SIGMA,
    min_samples: int = MIN_FILTER_SAMPLES,
) -> Dict[str, Any]:
    """自动去噪后的架次平均值 + 过滤后的最大值（MFD 左侧统计区数据）。

    规则（用户定稿：「只会去除死亡之前的超大波动来平均」）：

    1. 先排除每个 ``death`` 事件前 ``death_window_s`` 秒窗口内的样本（这部分不参与平均）；
    2. **只在死亡前 ``spike_window_s`` 秒窗口内**（``[death-30, death]``，且去掉已经排除的
       死亡窗口样本）按 IAS / TAS **各自**用 MAD 判尖峰：
       ``|x - median| > 3 × 1.4826 × MAD``；窗口外（起飞/巡航/盘旋等）**不做任何**离群过滤；
    3. 没有 ``death`` 事件时**不做** MAD 过滤，平均值就是原始样本均值（仍排除死亡窗口＝无）；
    4. Mach / 高度取 IAS/TAS 两个通道尖峰集合的并集；
    5. 某通道过滤后窗口内剩余 < ``min_samples`` 时该通道退回「只排除死亡窗口」。

    返回 dict（含 ``avg_filter`` 子字典，记录参数与排除样本数）。
    """
    series = flight.series
    times: List[float] = series["t"]
    count = len(times)

    deaths = death_times(flight)
    in_death = death_window_mask(times, deaths, death_window_s)
    base_mask = [not flagged for flagged in in_death]
    death_excluded = sum(1 for flagged in in_death if flagged)

    # 尖峰检测范围 = 死亡前窗口 ∩ 未被死亡窗口排除的样本；没有 death 时全 False
    spike_domain = spike_window_mask(times, deaths, spike_window_s, base_mask)

    ias_spike, ias_fallback = spike_mask(series["ias"], spike_domain, spike_sigma, min_samples)
    tas_spike, tas_fallback = spike_mask(series["tas"], spike_domain, spike_sigma, min_samples)
    fallback = ias_fallback or tas_fallback

    ias_mask = _apply_spike(base_mask, ias_spike)
    tas_mask = _apply_spike(base_mask, tas_spike)

    union_spike: List[bool] = [False] * count
    for spike in (ias_spike, tas_spike):
        if spike is None:
            continue
        for index, flagged in enumerate(spike):
            if flagged:
                union_spike[index] = True
    union_mask = [base_mask[index] and not union_spike[index] for index in range(count)]

    spike_excluded = sum(1 for flagged in union_spike if flagged)
    excluded = death_excluded + spike_excluded
    kept = count - excluded

    result: Dict[str, Any] = {
        "avg_ias_kmh": _mean_on(series["ias"], ias_mask),
        "avg_tas_kmh": _mean_on(series["tas"], tas_mask),
        "avg_mach": _mean_on(series["mach"], union_mask),
        "avg_alt_m": _mean_on(series["alt"], union_mask),
        "max_ias_filtered_kmh": _max_on(series["ias"], ias_mask),
        "max_tas_filtered_kmh": _max_on(series["tas"], tas_mask),
        "avg_filter": {
            "death_window_s": float(death_window_s),
            # MAD 尖峰过滤只在这个窗口内生效（death - spike_window_s ～ death）
            "spike_window_s": float(spike_window_s),
            "spike_sigma": float(spike_sigma),
            # 被排除样本总数 = 死亡窗口 + 尖峰（并集）
            "excluded_samples": excluded,
            # 下面三项是给调试/自检多给的细分，HUD 可忽略
            "death_excluded_samples": death_excluded,
            "spike_excluded_samples": spike_excluded,
            "kept_samples": kept,
            "fallback": fallback,
        },
    }
    return result


# ---------------------------------------------------------------------------
# 架次统计 + 降采样
# ---------------------------------------------------------------------------


def downsample_indices(
    times: Sequence[float],
    *,
    max_points: int = MAX_SERIES_POINTS,
    target_hz: float = SERIES_TARGET_HZ,
) -> List[int]:
    """按目标采样率挑出保留的样本下标（时间越均匀越好，点数 ≤ max_points）。

    默认 2 Hz；如果 2 Hz 点太多（超过 ``max_points``），自动加粗步长。
    最后一个点固定换成架次末样本，保证曲线画到架次结束（点数不增加）。
    """
    count = len(times)
    if count <= 2:
        return list(range(count))

    span = float(times[-1] - times[0])
    deltas = [times[index + 1] - times[index] for index in range(count - 1)]
    median_dt = statistics.median(deltas) if deltas else span
    stride = 1
    if median_dt and median_dt > 0.0 and target_hz > 0.0:
        stride = max(1, int(round((1.0 / target_hz) / median_dt)))
    while (count + stride - 1) // stride > max_points:
        stride += 1

    indices = list(range(0, count, stride))
    if indices[-1] != count - 1:
        indices[-1] = count - 1
    return indices


def _series_payload(flight) -> Dict[str, List[Optional[float]]]:
    """降采样后的列式序列（键按 HUD 契约命名，长度全部等于 ``t``）。"""
    series = flight.series or build_series(flight)
    indices = downsample_indices(series["t"])
    payload: Dict[str, List[Optional[float]]] = {"t": [_round(series["t"][i], 3) for i in indices]}
    for source_key, output_key, digits in SERIES_FIELDS:
        column = series.get(source_key) or []
        payload[output_key] = [
            _round(column[i], digits) if i < len(column) else None for i in indices
        ]
    return payload


def flight_stats(flight) -> Dict[str, Any]:
    """原始最大值（不过滤）+ 自动过滤后的平均值/过滤后最大值。"""
    series = flight.series or build_series(flight)
    ias = series["ias"]
    tas = series["tas"]
    mach = series["mach"]
    alt = series["alt"]
    ny = series["ny"]

    es = series["es"]
    es_min, es_max = _minmax(es)
    # 能量变化优先用「空中样本首尾差」（与 HTML 报告的 energy.inflight_delta 一致），
    # 没有空中样本时退回全样本首尾差
    inflight = series.get("inflight") or []
    inflight_es = [
        es[index]
        for index in range(len(es))
        if (inflight[index] if index < len(inflight) else True) and es[index] is not None
    ]
    if len(inflight_es) >= 2:
        es_delta: Optional[float] = inflight_es[-1] - inflight_es[0]
    else:
        es_delta = (es_max - es_min) if (es_min is not None and es_max is not None) else None

    ias_min, ias_max = _minmax(ias)
    tas_min, tas_max = _minmax(tas)
    _mach_min, mach_max = _minmax(mach)
    _alt_min, alt_max = _minmax(alt)

    averaged = flight_averages(flight)

    stats: Dict[str, Any] = {
        # 原始最大值（不过滤，跟 HTML 报告一致）
        "max_ias_kmh": _round(ias_max, _DIGITS["max_ias_kmh"]),
        "max_tas_kmh": _round(tas_max, _DIGITS["max_tas_kmh"]),
        "max_mach": _round(mach_max, _DIGITS["max_mach"]),
        "max_g": _round(_max_abs(ny), _DIGITS["max_g"]),
        "max_alt_m": _round(alt_max, _DIGITS["max_alt_m"]),
        # 自动去噪后的平均值（MFD 直接显示这个）
        "avg_ias_kmh": _round(averaged["avg_ias_kmh"], _DIGITS["avg_ias_kmh"]),
        "avg_tas_kmh": _round(averaged["avg_tas_kmh"], _DIGITS["avg_tas_kmh"]),
        "avg_mach": _round(averaged["avg_mach"], _DIGITS["avg_mach"]),
        "avg_alt_m": _round(averaged["avg_alt_m"], _DIGITS["avg_alt_m"]),
        "es_range_m": [
            _round(es_min, _DIGITS["es_range_m"]),
            _round(es_max, _DIGITS["es_range_m"]),
        ],
        "es_delta_m": _round(es_delta, _DIGITS["es_delta_m"]),
        # 过滤后的最大值（MFD 想显示"去掉坠机俯冲后"的极值就用这个）
        "max_ias_filtered_kmh": _round(
            averaged["max_ias_filtered_kmh"], _DIGITS["max_ias_filtered_kmh"]
        ),
        "max_tas_filtered_kmh": _round(
            averaged["max_tas_filtered_kmh"], _DIGITS["max_tas_filtered_kmh"]
        ),
        "avg_filter": averaged["avg_filter"],
    }
    return stats


def _flight_payload(flight) -> Dict[str, Any]:
    series = flight.series or build_series(flight)
    times = series["t"]
    return {
        "index": flight.index,
        "start_s": _round(times[0], 3) if times else None,
        "end_s": _round(times[-1], 3) if times else None,
        "samples": len(times),
        "vehicle": flight.vehicle,
        "stats": flight_stats(flight),
        "series": _series_payload(flight),
    }


# ---------------------------------------------------------------------------
# 顶层 payload
# ---------------------------------------------------------------------------


def _meta_payload(model: LogModel) -> Dict[str, Any]:
    all_times = [sample["t"] for flight in model.flights for sample in flight.samples]
    samples = len(all_times)
    t_min = min(all_times) if all_times else None
    t_max = max(all_times) if all_times else None
    duration = (t_max - t_min) if (t_min is not None and t_max is not None) else None
    rate = ((samples - 1) / duration) if (duration and duration > 0.0) else None

    vehicle = model.meta.get("vehicle")
    if not vehicle:
        vehicle = next(
            (flight.vehicle for flight in model.flights if flight.vehicle), None
        )
    return {
        "vehicle": vehicle,
        "duration_s": _round(duration, 1),
        "rate_hz": _round(rate, 1),
        "samples": samples,
    }


def _events_payload(model: LogModel) -> List[Dict[str, Any]]:
    all_times = [sample["t"] for flight in model.flights for sample in flight.samples]
    if not all_times:
        return []
    lo = min(all_times) - EVENT_MARGIN_S
    hi = max(all_times) + EVENT_MARGIN_S
    events: List[Dict[str, Any]] = []
    for event in model.events:
        if event.t is None or event.kind == "end":
            continue
        if event.kind == "gap":
            # 数据中断（gap）是录制侧的诊断事件，不是"游戏事件"：MFD 的
            # 事件时间轴只画死亡 / 击落 / 复活 / 换机型，导出时不带 gap，
            # 老消费者看到的字段集合不变（0.5.3 起日志里才会有 gap）。
            continue
        if event.t < lo or event.t > hi:
            continue  # 记录结束后才发生的事件没有时间轴可放
        events.append(
            {
                "kind": event.kind,
                "t": _round(event.t, 3),
                "vehicle": event.vehicle,
                # 0.5.2 击落事件带被击落目标；其它事件不写这个可选字段（schema 仍为 1，
                # HUD 的 MFD 解析忽略未知字段，图表渲染也不改）。
                **({"target": event.target} if event.target else {}),
            }
        )
    events.sort(key=lambda item: (item["t"], item["kind"]))
    return events


def build_mfd_payload(
    model: LogModel,
    *,
    source: Optional[str] = None,
    generated_utc: Optional[str] = None,
) -> Dict[str, Any]:
    """把一个 :class:`~flightlog.parse.LogModel` 组装成 MFD JSON（schema=1）。"""
    if source is None:
        source = os.path.basename(model.path or "")
    return {
        "schema": MFD_SCHEMA,
        "tool": MFD_TOOL,
        "tool_version": MFD_TOOL_VERSION,
        "source": source,
        "generated_utc": generated_utc or _iso_now_utc(),
        "meta": _meta_payload(model),
        "flights": [_flight_payload(flight) for flight in model.flights],
        "events": _events_payload(model),
    }


# ---------------------------------------------------------------------------
# 序列化 / 落盘
# ---------------------------------------------------------------------------


def dump_mfd_json(payload: Dict[str, Any]) -> str:
    """紧凑 JSON 文本（UTF-8 中文不转义；无缩进以控制体积；不允许 NaN/Infinity）。

    ``allow_nan=False`` 是契约的一部分：缺失值必须写 ``null``，不能出现 ``NaN``
    （C# ``System.Text.Json`` 默认不认这些字面量，会直接把整份结果判成坏 JSON）。
    """
    return json.dumps(payload, ensure_ascii=False, allow_nan=False, separators=(",", ":"))


def write_mfd_json(payload: Dict[str, Any], path: str) -> str:
    """写文件并返回绝对路径；父目录不存在会自动创建。"""
    target = os.path.abspath(path)
    parent = os.path.dirname(target)
    if parent and not os.path.isdir(parent):
        os.makedirs(parent, exist_ok=True)
    data = dump_mfd_json(payload) + "\n"
    with open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(data)
    return target
