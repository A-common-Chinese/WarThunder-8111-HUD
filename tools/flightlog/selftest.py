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
9. 生成的 HTML 自包含（无外链脚本/样式/图片）、含事件与图表；
10. CLI ``--summary`` / ``--out`` 可用。
"""

from __future__ import annotations

import contextlib
import datetime as _dt
import io
import json
import math
import os
import sys
import tempfile
from pathlib import Path

_TOOL_DIR = Path(__file__).resolve().parent
_PARENT = _TOOL_DIR.parent
if str(_PARENT) not in sys.path:
    sys.path.insert(0, str(_PARENT))

from flightlog import parse as P  # noqa: E402
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
    check('<div class="ptitle">姿态</div>' not in html, "没有姿态数据时不画姿态面板")
    check("background:#2f9e75" not in html, "没有 ralt 数据时不画雷达高度图例")
    check("全局总览" in html, "两个架次（时间跳变）会生成全局总览")


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
    print("\n[4] HTML 报告：自包含 / 事件 / 图表 / 转义")
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
    check("<script src" not in html, "没有外链脚本")
    check("<link" not in html, "没有外链样式")
    check("<img" not in html, "没有外部图片")
    check("@import" not in html, "没有 CSS @import")
    check(html.count("<svg ") >= 8, "内联 SVG 图数量 >= 8（%d）" % html.count("<svg "))
    check("死亡" in html and "dEs/dt" in html, "包含事件标签与能量变化率")
    check("svg-f1-speed" in html and "svg-f3-speed" in html, "三个架次都有速度图")
    check(html.count('id="flight-') == 3, "三个架次小节都渲染")
    check("<script>window.__FLIGHTLOG__=" in html, "数据脚本内联在页面里")

    out = tmp / "report.html"
    from flightlog.report import write_report

    written = write_report(model, str(out))
    check(os.path.isfile(written) and os.path.getsize(written) > 20000,
          "write_report 写出非空文件（%d 字节）" % os.path.getsize(written))
    raw = Path(written).read_text(encoding="utf-8")
    check("\ufffd" not in raw, "输出没有替换字符（编码正常）")


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
        test_cli(tmp)

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
