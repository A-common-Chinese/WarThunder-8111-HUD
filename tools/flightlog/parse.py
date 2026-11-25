#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""WtHud 飞行日志解析器（独立工具，不修改 WtHud 本体）。

读取 ``wtlog-*.jsonl``（一行一个 JSON 记录），产出结构化模型：

* ``meta``      —— 文件头信息（schema / app / vehicle / rate）
* ``events``    —— death / spawn / vehicle_change / kill / end 等事件，统一换算成秒
                   （``kill`` 带 ``target`` = 被击落目标名，旧日志没有该字段）
* ``flights``   —— 按 spawn / death / vehicle_change / 机型字段变化 / 时间跳变
                   切出来的"架次"，每个架次带逐样本序列与统计摘要
* ``summary``   —— 可直接 ``json.dumps`` 的统计摘要（供 ``--summary`` 使用）

设计要点（也是这个工具的"容错契约"）：

* 坏行（非 JSON、非对象、缺 t、t 非数字）只计数跳过，不抛异常；
* 样本按键 ``t`` 稳定排序，允许日志乱序；
* 所有可选字段缺失时得到 ``None``，不会 KeyError；
* 事件可能只有 ``utc`` 没有 ``t``：用样本的 ``utc - t`` 中位数反推；
* 旧日志可能完全没有 event 记录：此时按时间跳变 / 样本里的 vehicle 字段切分。

能量定义（比能量高度，单位米）::

    Es = alt_m + (tas_kmh / 3.6) ** 2 / (2 * g)      g = 9.80665 m/s^2

其时间导数 ``dEs/dt`` 单位 m/s，正值表示获得能量（爬升或加速）。
原始日志的速度/高度是整数量化值，直接差分会有剧烈锯齿，因此导数用
"滑动窗口线性回归"平滑（默认 ±1.5 s），跨越坏样本或大时间缺口时置 ``None``。
"""

from __future__ import annotations

import datetime as _dt
import json
import math
import os
import re
import statistics
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Sequence, Tuple

# ---------------------------------------------------------------------------
# 常量
# ---------------------------------------------------------------------------

G = 9.80665  # 标准重力加速度 m/s^2

#: 样本时间间隔超过这个值就认为发生了断档/换对局，切新架次
DEFAULT_GAP_SPLIT_S = 30.0

#: 能量变化率的滑动窗口半宽（秒）
ENERGY_RATE_WINDOW_S = 1.5
#: 计算斜率所需的最小时间跨度（秒）
ENERGY_RATE_MIN_SPAN_S = 0.8
#: 窗口内允许的最大相邻样本间隔（秒），超过则认为跨越缺口
ENERGY_RATE_MAX_DT_S = 2.5
#: 窗口内允许的单步比能量跳变（米）；超过视为状态重置（坠机/传送/死亡），
#: 不参与求导（否则会出现 -3000 m/s 之类的假尖峰）
ENERGY_RATE_MAX_STEP_M = 600.0
#: 窗口内至少需要的样本数
ENERGY_RATE_MIN_POINTS = 5

#: 样本数值字段（原始键名 -> 序列键名）
NUMERIC_SAMPLE_FIELDS = (
    ("ias_kmh", "ias"),
    ("tas_kmh", "tas"),
    ("mach", "mach"),
    ("alt_m", "alt"),
    ("ralt_m", "ralt"),
    ("vy_ms", "vy"),
    ("aoa_deg", "aoa"),
    ("ny_g", "ny"),
    ("pitch_deg", "pitch"),
    ("bank_deg", "bank"),
)

#: 允许出现在 sample 里的其它数值字段（只是解析出来备用，不进入图表）
EXTRA_NUMERIC_FIELDS = (
    "aos_deg",
    "roll_rate_dps",
    "hdg_deg",
    "gear_pct",
    "flaps_pct",
    "airbrake_pct",
    "fuel_kg",
    "fuel0_kg",
)

#: 进入报告序列的键（report.py 会按需取用）
SERIES_KEYS = tuple(k for _, k in NUMERIC_SAMPLE_FIELDS) + ("es", "des", "valid", "inflight")

#: 事件种类 -> 中文名（报告里也用）
EVENT_LABELS = {
    "death": "死亡",
    "spawn": "复活/生成",
    "vehicle_change": "换机型",
    "kill": "击落",
    "end": "记录结束",
}

_FLIGHT_REASON_LABELS = {
    "log_start": "日志起点",
    "spawn": "复活生成",
    "vehicle_change": "换机型",
    "vehicle_field": "机型字段变化",
    "gap": "时间跳变",
}

_ISO_RE = re.compile(
    r"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})"
    r"(?:\.(\d+))?(Z|[+-]\d{2}:?\d{2})?$"
)


class LogError(Exception):
    """日志无法使用（文件不存在 / 不是飞行日志 / 没有样本）。"""


# ---------------------------------------------------------------------------
# 基础类型
# ---------------------------------------------------------------------------


@dataclass
class Event:
    """一条事件记录。``t`` 为日志内秒数，可能为 None（无法换算）。

    ``target`` 只对击落（``kill``）事件有意义 = 被击落的目标名；旧日志没有该字段，
    统一留空串，序列化时也不写空字段（保持旧摘要格式不变）。
    """

    kind: str
    t: Optional[float]
    vehicle: Optional[str] = None
    t_source: Optional[str] = None  # "t" / "utc" / None
    utc_epoch: Optional[float] = None
    target: str = ""

    def as_dict(self) -> Dict[str, Any]:
        data: Dict[str, Any] = {
            "kind": self.kind,
            "label": EVENT_LABELS.get(self.kind, self.kind),
            "t": None if self.t is None else round(self.t, 3),
            "vehicle": self.vehicle,
        }
        if self.target:
            data["target"] = self.target
        return data


@dataclass
class Flight:
    """一个架次（spawn → death 之间的一组样本）。"""

    index: int
    reason: str
    vehicle: Optional[str]
    start_t: float
    end_t: float
    samples: List[Dict[str, Any]] = field(default_factory=list)
    events: List[Event] = field(default_factory=list)
    series: Dict[str, List[Optional[float]]] = field(default_factory=dict)
    stats: Dict[str, Any] = field(default_factory=dict)
    death_t: Optional[float] = None
    ended: bool = False
    #: 回放导入时该架次所属玩家（JSONL 日志恒为 None）
    player: Optional[str] = None


@dataclass
class LogModel:
    """解析后的完整模型。"""

    path: str
    meta: Dict[str, Any]
    flights: List[Flight]
    events: List[Event]
    counters: Dict[str, Any]
    warnings: List[str]
    summary: Dict[str, Any] = field(default_factory=dict)


# ---------------------------------------------------------------------------
# 字段转换
# ---------------------------------------------------------------------------


def parse_utc(text: Any) -> Optional[float]:
    """把 ISO8601 UTC 时间转成 epoch 秒；失败返回 None。

    兼容 ``2026-11-17T05:10:39.4499212Z``（7 位小数）与带时区偏移的写法。
    """
    if not isinstance(text, str):
        return None
    m = _ISO_RE.match(text.strip())
    if not m:
        return None
    try:
        year, month, day, hour, minute, second = (int(x) for x in m.groups()[:6])
        frac = m.group(7) or ""
        micro = int((frac + "000000")[:6]) if frac else 0
        base = _dt.datetime(
            year, month, day, hour, minute, second, micro, tzinfo=_dt.timezone.utc
        )
    except ValueError:
        return None
    tz = m.group(8)
    if tz and tz != "Z":
        sign = 1 if tz[0] == "+" else -1
        body = tz[1:].replace(":", "")
        try:
            offset_min = sign * (int(body[:2]) * 60 + int(body[2:4]))
        except ValueError:
            return None
        base = base - _dt.timedelta(minutes=offset_min)
    try:
        return base.timestamp()
    except (OverflowError, OSError, ValueError):
        return None


def _num(value: Any) -> Optional[float]:
    """宽松转 float；bool / 空串 / nan / inf 一律返回 None。"""
    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, (int, float)):
        out = float(value)
    elif isinstance(value, str):
        text = value.strip()
        if not text:
            return None
        try:
            out = float(text)
        except ValueError:
            return None
    else:
        return None
    return out if math.isfinite(out) else None


def _bool(value: Any) -> Optional[bool]:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)) and math.isfinite(float(value)):
        return bool(value)
    if isinstance(value, str):
        low = value.strip().lower()
        if low in ("true", "1", "yes"):
            return True
        if low in ("false", "0", "no"):
            return False
    return None


def _text(value: Any) -> Optional[str]:
    if isinstance(value, str):
        value = value.strip()
        return value or None
    return None


def _coerce_sample(obj: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    """把一行 sample 转成内部样本；缺 t 时返回 None（该行丢弃）。"""
    t = _num(obj.get("t"))
    if t is None:
        return None
    sample: Dict[str, Any] = {
        "t": t,
        "utc": parse_utc(obj.get("utc")),
        "valid": _bool(obj.get("valid")),
        "inflight": _bool(obj.get("inflight")),
        "vehicle": _text(obj.get("vehicle")),
    }
    for src, key in NUMERIC_SAMPLE_FIELDS:
        sample[key] = _num(obj.get(src))
    for src in EXTRA_NUMERIC_FIELDS:
        sample[src] = _num(obj.get(src))
    sample["has_hdg"] = _bool(obj.get("has_hdg"))
    sample["att_src"] = _text(obj.get("att_src"))
    sample["att_conf"] = _num(obj.get("att_conf"))
    return sample


def _coerce_event(obj: Dict[str, Any]) -> Optional[Event]:
    kind = _text(obj.get("kind")) or _text(obj.get("type_name"))
    if kind is None:
        return None
    t = _num(obj.get("t"))
    source = "t" if t is not None else None
    return Event(
        kind=kind,
        t=t,
        vehicle=_text(obj.get("vehicle")),
        t_source=source,
        utc_epoch=parse_utc(obj.get("utc")),
        target=_text(obj.get("target")) or "",
    )


# ---------------------------------------------------------------------------
# 读取
# ---------------------------------------------------------------------------


def load_log(path: str) -> Dict[str, Any]:
    """读取 JSONL，返回原始解析结果（未切架次）。"""
    if not os.path.isfile(path):
        raise LogError("日志文件不存在: %s" % path)

    samples: List[Dict[str, Any]] = []
    events: List[Event] = []
    metas: List[Dict[str, Any]] = []
    counters: Dict[str, Any] = {
        "lines": 0,
        "samples": 0,
        "events": 0,
        "meta": 0,
        "bad_json": 0,
        "bad_record": 0,
        "bad_sample": 0,
        "skipped_event": 0,
        "unknown_type": 0,
    }
    unsorted = False
    last_t: Optional[float] = None

    try:
        fh = open(path, "r", encoding="utf-8-sig", errors="replace")
    except OSError as exc:  # pragma: no cover - 一般由 isfile 拦住
        raise LogError("无法打开日志: %s (%s)" % (path, exc))

    with fh:
        for raw in fh:
            line = raw.strip()
            if not line:
                continue
            counters["lines"] += 1
            try:
                obj = json.loads(line)
            except (ValueError, TypeError):
                counters["bad_json"] += 1
                continue
            if not isinstance(obj, dict):
                counters["bad_record"] += 1
                continue

            kind = obj.get("type")
            if kind is None and "t" in obj:
                kind = "sample"  # 旧日志可能漏写 type

            if kind == "meta":
                metas.append(obj)
                counters["meta"] += 1
            elif kind == "sample":
                sample = _coerce_sample(obj)
                if sample is None:
                    counters["bad_sample"] += 1
                    continue
                if last_t is not None and sample["t"] < last_t:
                    unsorted = True
                last_t = sample["t"]
                samples.append(sample)
                counters["samples"] += 1
            elif kind == "event":
                event = _coerce_event(obj)
                if event is None:
                    counters["skipped_event"] += 1
                    continue
                events.append(event)
                counters["events"] += 1
            elif kind == "end":
                events.append(
                    Event(
                        kind="end",
                        t=_num(obj.get("t")),
                        t_source="t" if _num(obj.get("t")) is not None else None,
                        utc_epoch=parse_utc(obj.get("utc")),
                    )
                )
            else:
                counters["unknown_type"] += 1

    meta: Dict[str, Any] = {}
    if metas:
        first = metas[0]
        meta = {
            "schema": first.get("schema"),
            "app": first.get("app"),
            "utc": first.get("utc"),
            "vehicle": _text(first.get("vehicle")),
            "rate": first.get("rate"),
        }
        meta["utc_epoch"] = parse_utc(first.get("utc"))

    return {
        "samples": samples,
        "events": events,
        "metas": metas,
        "meta": meta,
        "counters": counters,
        "unsorted": unsorted,
    }


# ---------------------------------------------------------------------------
# 事件时间换算
# ---------------------------------------------------------------------------


def _utc_offset(samples: Sequence[Dict[str, Any]]) -> Optional[float]:
    """估算 ``utc_epoch - t`` 的偏移（中位数，抗异常值）。"""
    pairs = [s["utc"] - s["t"] for s in samples if s.get("utc") is not None]
    if not pairs:
        return None
    if len(pairs) > 400:  # 均匀抽样，控制开销
        step = max(1, len(pairs) // 400)
        pairs = pairs[::step]
    return statistics.median(pairs)


def _resolve_event_times(
    events: Sequence[Event], samples: Sequence[Dict[str, Any]], warnings: List[str]
) -> None:
    """给只有 utc 的事件补出秒数 t；补不出来的计数并剔除。"""
    offset = _utc_offset(samples)
    missing = 0
    for event in events:
        if event.t is not None:
            continue
        if event.utc_epoch is not None and offset is not None:
            event.t = round(event.utc_epoch - offset, 3)
            event.t_source = "utc"
        else:
            missing += 1
    if missing:
        warnings.append("有 %d 条事件缺少可换算的时间，已忽略" % missing)


# ---------------------------------------------------------------------------
# 架次切分
# ---------------------------------------------------------------------------


def build_flights(
    samples: Sequence[Dict[str, Any]],
    events: Sequence[Event],
    meta_vehicle: Optional[str] = None,
    gap_split_s: float = DEFAULT_GAP_SPLIT_S,
) -> List[Flight]:
    """按事件与时间把样本切成架次。

    规则：

    * ``spawn`` / ``vehicle_change`` 严格早于当前样本时，先结束上一架次再开新架次；
    * ``death`` 只做标记，之后的样本（通常是坠机/地面状态）仍归入该架次，
      直到下一个 spawn / vehicle_change；
    * 样本里的 ``vehicle`` 字段发生变化、或相邻样本间隔超过 ``gap_split_s``，
      也会切新架次（兼容没有 event 的旧日志）；
    * 第一条样本之前若有 spawn，则架次 reason 为 ``spawn``；否则为 ``log_start``。
    """
    evs = sorted(
        (e for e in events if e.t is not None), key=lambda e: (e.t, e.kind)
    )
    flights: List[Flight] = []
    cur: Optional[Flight] = None
    cur_vehicle_field: Optional[str] = None
    ei = 0

    def flush(flight: Optional[Flight]) -> bool:
        if flight is None or not flight.samples:
            return False
        flight.end_t = flight.samples[-1]["t"]
        flights.append(flight)
        return True

    for sample in samples:
        while ei < len(evs) and evs[ei].t <= sample["t"] + 1e-9:
            event = evs[ei]
            ei += 1
            if event.kind in ("spawn", "vehicle_change"):
                flush(cur)
                cur = Flight(
                    index=len(flights) + 1,
                    reason=event.kind,
                    vehicle=event.vehicle or meta_vehicle,
                    start_t=sample["t"],
                    end_t=sample["t"],
                )
                cur_vehicle_field = None
            elif event.kind == "death":
                if cur is not None and cur.death_t is None:
                    cur.death_t = event.t
            elif event.kind == "end":
                if cur is not None:
                    cur.ended = True

        if cur is None:
            cur = Flight(
                index=len(flights) + 1,
                reason="log_start",
                vehicle=sample.get("vehicle") or meta_vehicle,
                start_t=sample["t"],
                end_t=sample["t"],
            )
            cur_vehicle_field = None
        elif cur.samples:
            prev = cur.samples[-1]
            if sample["t"] - prev["t"] > gap_split_s:
                flush(cur)
                cur = Flight(
                    index=len(flights) + 1,
                    reason="gap",
                    vehicle=sample.get("vehicle") or meta_vehicle,
                    start_t=sample["t"],
                    end_t=sample["t"],
                )
                cur_vehicle_field = None
            elif (
                cur_vehicle_field
                and sample.get("vehicle")
                and sample["vehicle"] != cur_vehicle_field
            ):
                flush(cur)
                cur = Flight(
                    index=len(flights) + 1,
                    reason="vehicle_field",
                    vehicle=sample["vehicle"],
                    start_t=sample["t"],
                    end_t=sample["t"],
                )
                cur_vehicle_field = None

        if cur_vehicle_field is None and sample.get("vehicle"):
            cur_vehicle_field = sample["vehicle"]
            # 样本里的机型比 meta 更权威（事件里的 vehicle 经常是空串）
            cur.vehicle = sample["vehicle"]

        cur.samples.append(sample)

    flush(cur)

    # 事件归到"时间上不太晚于它"的最后一个架次（死亡事件通常比末样本稍晚）
    for flight in flights:
        flight.events = []
    ordered = sorted(
        (e for e in events if e.t is not None), key=lambda e: (e.t, e.kind)
    )
    for event in ordered:
        target = None
        if event.kind in ("spawn", "vehicle_change"):
            # 这类事件本身标志一个新架次开始，归给 start_t 最接近它的架次
            best: Optional[Tuple[float, Flight]] = None
            for flight in flights:
                distance = abs(flight.start_t - event.t)
                if best is None or distance < best[0]:
                    best = (distance, flight)
            target = best[1] if best is not None else None
        else:
            # 死亡/结束等事件归给时间上最后一个不晚于它的架次
            for flight in flights:
                if flight.start_t - 1e-9 <= event.t:
                    target = flight
                else:
                    break
        if target is None:
            if flights:
                target = flights[0]
            else:
                continue
        target.events.append(event)

    return flights


# ---------------------------------------------------------------------------
# 能量与导数
# ---------------------------------------------------------------------------


def specific_energy(alt_m: Optional[float], tas_kmh: Optional[float]) -> Optional[float]:
    """比能量高度 Es（米）。缺高度返回 None；缺速度退化为位能高度。"""
    if alt_m is None:
        return None
    if tas_kmh is None:
        return float(alt_m)
    speed = tas_kmh / 3.6
    return float(alt_m) + speed * speed / (2.0 * G)


def energy_rate(
    times: Sequence[float],
    values: Sequence[Optional[float]],
    valid: Optional[Sequence[bool]] = None,
    window_s: float = ENERGY_RATE_WINDOW_S,
    min_span_s: float = ENERGY_RATE_MIN_SPAN_S,
    max_dt_s: float = ENERGY_RATE_MAX_DT_S,
    max_step_m: float = ENERGY_RATE_MAX_STEP_M,
) -> List[Optional[float]]:
    """滑动窗口线性回归求 dEs/dt（m/s）。

    用回归而不是差分，是为了压住整数化量化噪声（高度 1 m、速度 1 km/h
    在 900 km/h 时约等于 7 m 的比能量台阶）。

    以下情况该点返回 ``None``：窗内样本不足 / 时间跨度太小 / 有 valid=false
    样本 / 相邻样本间隔过大 / 单步比能量跳变超过 ``max_step_m``（状态重置）。
    """
    n = len(times)
    out: List[Optional[float]] = [None] * n
    if n < ENERGY_RATE_MIN_POINTS:
        return out
    for i in range(n):
        if values[i] is None:
            continue
        if valid is not None and not valid[i]:
            continue
        lo = i
        while lo > 0 and times[i] - times[lo - 1] <= window_s:
            lo -= 1
        hi = i
        while hi < n - 1 and times[hi + 1] - times[i] <= window_s:
            hi += 1
        if hi - lo + 1 < ENERGY_RATE_MIN_POINTS:
            continue
        if times[hi] - times[lo] < min_span_s:
            continue
        ok = True
        for j in range(lo, hi):
            if times[j + 1] - times[j] > max_dt_s:
                ok = False
                break
            if values[j] is None or values[j + 1] is None:
                ok = False
                break
            if abs(values[j + 1] - values[j]) > max_step_m:  # type: ignore[operator]
                ok = False
                break
            if valid is not None and not (valid[j] and valid[j + 1]):
                ok = False
                break
        if not ok:
            continue
        count = hi - lo + 1
        mean_t = sum(times[lo : hi + 1]) / count
        mean_v = sum(values[lo : hi + 1]) / count  # type: ignore[arg-type]
        sxx = 0.0
        sxy = 0.0
        for j in range(lo, hi + 1):
            dx = times[j] - mean_t
            sxx += dx * dx
            sxy += dx * (values[j] - mean_v)  # type: ignore[operator]
        if sxx <= 1e-12:
            continue
        out[i] = sxy / sxx
    return out


# ---------------------------------------------------------------------------
# 序列与统计
# ---------------------------------------------------------------------------


def build_series(flight: Flight) -> Dict[str, List[Optional[float]]]:
    """把架次样本展开成列式序列（供报告与统计使用）。"""
    samples = flight.samples
    times = [s["t"] for s in samples]
    series: Dict[str, List[Optional[float]]] = {"t": times}
    for _src, key in NUMERIC_SAMPLE_FIELDS:
        series[key] = [s.get(key) for s in samples]
    series["valid"] = [0 if s.get("valid") is False else 1 for s in samples]
    series["inflight"] = [1 if s.get("inflight") is True else 0 for s in samples]

    es = [specific_energy(s.get("alt"), s.get("tas")) for s in samples]
    series["es"] = es

    valid_flags = [bool(v) for v in series["valid"]]
    series["des"] = energy_rate(times, es, valid_flags)
    return series


#: 平均速度要统计的通道：(序列键, 原始日志字段名)
_SPEED_CHANNELS = (("ias", "ias_kmh"), ("tas", "tas_kmh"), ("mach", "mach"))


def average_speed(
    samples: Sequence[Dict[str, Any]], t1: Any, t2: Any
) -> Optional[Dict[str, Any]]:
    """统计 ``t`` 落在**闭区间** ``[t1, t2]`` 内样本的平均速度（纯函数）。

    参数
    ----
    samples:
        样本序列（``Flight.samples`` 或任何 dict 列表）。每项至少要有 ``t``；
        速度字段用序列键 ``ias`` / ``tas`` / ``mach``，也兼容原始日志字段名
        ``ias_kmh`` / ``tas_kmh`` / ``mach``。
    t1, t2:
        统计区间的起止秒数（闭区间）。写反会自动交换；不是数字时返回 ``None``。

    返回
    ----
    区间内没有可用样本时返回 ``None``，否则返回::

        {
          "range_start": 30.0, "range_end": 120.0,   # 实际使用的闭区间（已交换成升序）
          "samples": 900, "duration_s": 90.0,        # 样本数 / 区间时长
          "first_t": 30.0, "last_t": 120.0,          # 命中样本的首末时间
          "ias": 887.2, "tas": 984.1, "mach": 0.82,  # 各字段算术平均（无数据为 None）
        }

    三个均值**各自**只统计"该字段有值"的样本：旧日志缺 Mach 时
    ``mach`` 为 ``None``，不影响 IAS/TAS。注意 ``duration_s`` 是用户选区长度
    （``t2 - t1``），不是首末命中样本的间隔。
    """
    start = _num(t1)
    end = _num(t2)
    if start is None or end is None:
        return None
    if start > end:
        start, end = end, start

    count = 0
    first_t: Optional[float] = None
    last_t: Optional[float] = None
    sums: Dict[str, float] = {key: 0.0 for key, _raw in _SPEED_CHANNELS}
    hits: Dict[str, int] = {key: 0 for key, _raw in _SPEED_CHANNELS}

    for sample in samples:
        if not isinstance(sample, dict):
            continue
        t = _num(sample.get("t"))
        if t is None or t < start or t > end:
            continue
        count += 1
        if first_t is None:
            first_t = t
        last_t = t
        for key, raw_key in _SPEED_CHANNELS:
            value = _num(sample.get(key))
            if value is None:
                value = _num(sample.get(raw_key))
            if value is None:
                continue
            sums[key] += value
            hits[key] += 1

    if not count:
        return None

    result: Dict[str, Any] = {
        "range_start": start,
        "range_end": end,
        "samples": count,
        "duration_s": end - start,
        "first_t": first_t,
        "last_t": last_t,
    }
    for key, _raw in _SPEED_CHANNELS:
        result[key] = (sums[key] / hits[key]) if hits[key] else None
    return result


def _minmax(values: Sequence[Optional[float]]) -> Tuple[Optional[float], Optional[float]]:
    present = [v for v in values if v is not None]
    if not present:
        return None, None
    return min(present), max(present)


def _absmax(values: Sequence[Optional[float]]) -> Optional[float]:
    present = [abs(v) for v in values if v is not None]
    return max(present) if present else None


def _integrate(times: Sequence[float], values: Sequence[Optional[float]]) -> float:
    total = 0.0
    for i in range(len(times) - 1):
        a, b = values[i], values[i + 1]
        if a is None or b is None:
            continue
        dt = times[i + 1] - times[i]
        if dt <= 0.0 or dt > 10.0:
            continue
        total += 0.5 * (a + b) * dt
    return total


def _round(value: Optional[float], digits: int = 3) -> Optional[float]:
    return None if value is None else round(float(value), digits)


def compute_flight_stats(flight: Flight) -> Dict[str, Any]:
    """计算单个架次的统计摘要。"""
    series = flight.series or build_series(flight)
    times = series["t"]
    count = len(times)
    span = (times[-1] - times[0]) if count >= 2 else 0.0

    inflight_dt = 0.0
    for i in range(count - 1):
        if series["inflight"][i] and series["inflight"][i + 1]:
            inflight_dt += times[i + 1] - times[i]

    valid_ratio = (
        sum(1 for v in series["valid"] if v) / count if count else 0.0
    )

    es = series["es"]
    des = series["des"]
    es_first = next((v for v in es if v is not None), None)
    es_last = next((v for v in reversed(es) if v is not None), None)
    es_min, es_max = _minmax(es)
    des_min, des_max = _minmax(des)
    # 只统计"空中"样本的比能量首尾，避免坠机后地面 0 速度把变化量带偏
    inflight_es = [
        es[i] for i in range(count) if series["inflight"][i] and es[i] is not None
    ]
    inflight_delta = (
        inflight_es[-1] - inflight_es[0] if len(inflight_es) >= 2 else None
    )

    def _stats_for(key: str) -> Tuple[Optional[float], Optional[float]]:
        return _minmax(series[key])

    ias_min, ias_max = _stats_for("ias")
    tas_min, tas_max = _stats_for("tas")
    _mach_min, mach_max = _stats_for("mach")
    alt_min, alt_max = _stats_for("alt")
    ralt_min, ralt_max = _stats_for("ralt")
    vy_min, vy_max = _stats_for("vy")
    aoa_min, aoa_max = _stats_for("aoa")
    ny_min, ny_max = _stats_for("ny")
    pitch_min, pitch_max = _stats_for("pitch")
    bank_min, bank_max = _stats_for("bank")

    stats: Dict[str, Any] = {
        "index": flight.index,
        "reason": flight.reason,
        "reason_label": _FLIGHT_REASON_LABELS.get(flight.reason, flight.reason),
        "vehicle": flight.vehicle,
        "start_t": _round(times[0], 3) if count else None,
        "end_t": _round(times[-1], 3) if count else None,
        "duration_s": _round(span, 3),
        "inflight_s": _round(inflight_dt, 3),
        "samples": count,
        "rate_hz": _round((count - 1) / span, 2) if span > 0 else None,
        "valid_ratio": _round(valid_ratio, 4),
        "ias": {"min": _round(ias_min), "max": _round(ias_max)},
        "tas": {"min": _round(tas_min), "max": _round(tas_max)},
        "mach": {"max": _round(mach_max)},
        "alt": {"min": _round(alt_min), "max": _round(alt_max)},
        "ralt": {"min": _round(ralt_min), "max": _round(ralt_max)},
        "vy": {"min": _round(vy_min), "max": _round(vy_max)},
        "aoa": {"min": _round(aoa_min), "max": _round(aoa_max)},
        "ny": {"min": _round(ny_min), "max": _round(ny_max), "abs_max": _round(_absmax(series["ny"]))},
        "pitch": {"min": _round(pitch_min), "max": _round(pitch_max)},
        "bank": {"min": _round(bank_min), "max": _round(bank_max), "abs_max": _round(_absmax(series["bank"]))},
        "energy": {
            "start": _round(es_first),
            "end": _round(es_last),
            "min": _round(es_min),
            "max": _round(es_max),
            "delta": _round(None if (es_first is None or es_last is None) else es_last - es_first),
            "inflight_start": _round(inflight_es[0]) if inflight_es else None,
            "inflight_end": _round(inflight_es[-1]) if inflight_es else None,
            "inflight_delta": _round(inflight_delta),
            "gain": _round(_integrate(times, [None if v is None else max(0.0, v) for v in des])),
            "loss": _round(_integrate(times, [None if v is None else min(0.0, v) for v in des])),
            "rate_max": _round(des_max),
            "rate_min": _round(des_min),
        },
        "death_t": _round(flight.death_t, 3),
        "ended": bool(flight.ended),
        "events": [
            {
                "kind": e.kind,
                "label": EVENT_LABELS.get(e.kind, e.kind),
                "t": _round(e.t, 3),
                **({"target": e.target} if e.target else {}),
            }
            for e in flight.events
        ],
    }
    return stats


# ---------------------------------------------------------------------------
# 汇总
# ---------------------------------------------------------------------------


def _iso_now() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def build_summary(model: LogModel) -> Dict[str, Any]:
    """文件级统计摘要（JSON 友好）。"""
    flights = model.flights
    counters = model.counters
    all_times = [s["t"] for f in flights for s in f.samples]
    t_min = min(all_times) if all_times else None
    t_max = max(all_times) if all_times else None

    def _best(path_flight: str) -> Optional[float]:
        values = []
        for flight in flights:
            stats = flight.stats
            node: Any = stats
            for part in path_flight.split("."):
                node = node.get(part) if isinstance(node, dict) else None
            if node is not None:
                values.append(node)
        return max(values) if values else None

    event_counts: Dict[str, int] = {}
    for event in model.events:
        event_counts[event.kind] = event_counts.get(event.kind, 0) + 1

    total_energy_delta = sum(
        f.stats["energy"]["delta"] for f in flights if f.stats["energy"]["delta"] is not None
    )
    has_energy_delta = any(f.stats["energy"]["delta"] is not None for f in flights)
    inflight_deltas = [
        f.stats["energy"]["inflight_delta"]
        for f in flights
        if f.stats["energy"]["inflight_delta"] is not None
    ]

    totals = {
        "duration_s": _round(None if (t_min is None or t_max is None) else t_max - t_min, 3),
        "samples": counters["samples"],
        "flights": len(flights),
        "events": sum(1 for e in model.events if e.kind != "end"),
        "max_ias_kmh": _round(_best("ias.max")),
        "max_tas_kmh": _round(_best("tas.max")),
        "max_mach": _round(_best("mach.max")),
        "max_alt_m": _round(_best("alt.max")),
        "max_ny_g": _round(_best("ny.abs_max")),
        "max_bank_deg": _round(_best("bank.abs_max")),
        "energy_min_m": _round(
            min((f.stats["energy"]["min"] for f in flights if f.stats["energy"]["min"] is not None), default=None)
        ),
        "energy_max_m": _round(
            max((f.stats["energy"]["max"] for f in flights if f.stats["energy"]["max"] is not None), default=None)
        ),
        "energy_delta_m": _round(total_energy_delta, 1) if has_energy_delta else None,
        "energy_delta_inflight_m": _round(sum(inflight_deltas), 1) if inflight_deltas else None,
    }

    summary = {
        "file": os.path.basename(model.path),
        "path": os.path.abspath(model.path),
        "generated_utc": _iso_now(),
        "meta": model.meta,
        "counts": {
            "lines": counters["lines"],
            "samples": counters["samples"],
            "events": counters["events"],
            "bad_json": counters["bad_json"],
            "bad_record": counters["bad_record"],
            "bad_sample": counters["bad_sample"],
            "skipped_event": counters["skipped_event"],
            "unknown_type": counters["unknown_type"],
            "flights": len(flights),
        },
        "time": {
            "t_min": _round(t_min, 3),
            "t_max": _round(t_max, 3),
            "span_s": _round(None if (t_min is None or t_max is None) else t_max - t_min, 3),
        },
        "totals": totals,
        "event_counts": event_counts,
        "events": [e.as_dict() for e in model.events if e.t is not None],
        "flights": [f.stats for f in flights],
        "warnings": list(model.warnings),
    }
    return summary


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------


def analyze(path: str, gap_split_s: float = DEFAULT_GAP_SPLIT_S) -> LogModel:
    """读取并分析一个日志文件，返回完整模型（含 summary）。"""
    raw = load_log(path)
    counters = raw["counters"]
    warnings: List[str] = []

    samples: List[Dict[str, Any]] = raw["samples"]
    events: List[Event] = raw["events"]
    meta = raw["meta"]

    if not samples:
        raise LogError("日志里没有可用的 sample 记录: %s" % path)

    if raw["unsorted"]:
        samples.sort(key=lambda s: s["t"])
        warnings.append("样本时间乱序，已按 t 排序后分析")

    # 只有 utc、没有 t 的事件：用样本的 utc-t 偏移换算成秒
    _resolve_event_times(events, samples, warnings)
    events = [e for e in events if e.t is not None]

    if counters["bad_json"]:
        warnings.append("跳过 %d 行无法解析的 JSON" % counters["bad_json"])
    if counters["bad_sample"]:
        warnings.append("跳过 %d 行缺少有效 t 的样本" % counters["bad_sample"])
    if counters["bad_record"]:
        warnings.append("跳过 %d 行非对象记录" % counters["bad_record"])
    if counters["skipped_event"]:
        warnings.append("跳过 %d 条无法识别的事件" % counters["skipped_event"])
    if counters["unknown_type"]:
        warnings.append("忽略 %d 条未知 type 记录" % counters["unknown_type"])
    if len(raw["metas"]) > 1:
        warnings.append("文件里有 %d 条 meta，只采用第一条" % len(raw["metas"]))
    if not raw["metas"]:
        warnings.append("缺少 meta 记录，机型等信息可能不全")

    structured = [e for e in events if e.kind in ("spawn", "death", "vehicle_change")]
    if not structured:
        warnings.append("没有 spawn/death/vehicle_change 事件，架次仅按时间跳变切分")

    meta_vehicle = meta.get("vehicle")
    if meta_vehicle is None:
        meta_vehicle = next((s["vehicle"] for s in samples if s.get("vehicle")), None)

    flights = build_flights(samples, events, meta_vehicle=meta_vehicle, gap_split_s=gap_split_s)

    missing_vehicle = sum(1 for s in samples if not s.get("vehicle"))
    if missing_vehicle == len(samples):
        warnings.append("样本里没有 vehicle 字段（旧格式），机型取自 meta 或未知")
    missing_ralt = sum(1 for s in samples if s.get("ralt") is None)
    if missing_ralt:
        warnings.append("%d 个样本缺少 ralt_m（雷达高度），对应曲线会断开" % missing_ralt)
    missing_att = sum(1 for s in samples if s.get("pitch") is None and s.get("bank") is None)
    if missing_att:
        warnings.append("%d 个样本缺少姿态字段（pitch/bank）" % missing_att)
    inflight_count = sum(1 for s in samples if s.get("inflight"))
    if inflight_count == 0:
        warnings.append("没有空中样本（inflight 全为 false），图表显示的是地面/大厅数据")

    for flight in flights:
        flight.series = build_series(flight)
        flight.stats = compute_flight_stats(flight)

    model = LogModel(
        path=path,
        meta=meta,
        flights=flights,
        events=events,
        counters=counters,
        warnings=warnings,
    )
    model.summary = build_summary(model)
    return model
