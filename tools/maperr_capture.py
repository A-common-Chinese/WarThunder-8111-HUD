#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
"""WtHud 编队数据链 · 误差实测采集端（独立脚本，不进产品代码）

每台机器各跑一份，轮询本机 8111，把自己看到的东西写进 CSV：

  * self_*  : /map_obj.json 里 icon=Player 的那条 —— 本机自己的位置（最准的"基准"）
  * other_* : 同一份 map_obj 里另一架飞机的位置 —— 本机对"对方"的观测（含游戏内推演误差）
  * 以及 /state 的飞行量，供事后按机动强度分档

用法（两台机器各跑一次，文件名带机号）：

  python maperr_capture.py --label A --out A.csv
  python maperr_capture.py --label B --out B.csv --seconds 300

约定：自定义战斗/双人房，场上只放两架飞机，否则 other_* 的身份会有歧义
（脚本会在 n_aircraft > 1 时给出警告，但仍取离自己最近的那架）。
"""

import argparse
import csv
import json
import math
import os
import sys
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8111"
FIELDS = [
    "utc_ms", "grid_w", "grid_h",
    "self_x", "self_y", "self_dx", "self_dy",
    "other_x", "other_y", "other_dx", "other_dy", "other_icon", "other_type",
    "n_aircraft", "dist_m",
    "ias", "tas", "alt", "vy", "ny", "wx", "aoa",
]


def get_json(path, timeout=0.7):
    with urllib.request.urlopen(BASE + path, timeout=timeout) as r:
        body = r.read().decode("utf-8", "replace")
    if not body.strip():
        return None
    return json.loads(body)


def num(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def read_grid(retries=20):
    """map_info.grid_size = 地图尺寸（米）。归一化坐标 × grid_size = 米。"""
    for _ in range(retries):
        try:
            info = get_json("/map_info.json")
            gs = (info or {}).get("grid_size")
            if gs and len(gs) >= 2:
                w, h = num(gs[0]), num(gs[1])
                if w > 1 and h > 1:
                    return w, h
        except (urllib.error.URLError, OSError, ValueError, json.JSONDecodeError):
            pass
        time.sleep(0.5)
    return 0.0, 0.0


def pick(objs, grid_w, grid_h):
    """返回 (self, other, n_aircraft)。other = 离自己最近的非 Player 飞机。"""
    self_pos = None
    others = []
    n_aircraft = 0
    for e in objs:
        if not isinstance(e, dict):
            continue
        etype = e.get("type", "")
        icon = e.get("icon", "")
        if etype != "aircraft":
            continue
        if icon == "Player":
            self_pos = (num(e.get("x")), num(e.get("y")),
                        num(e.get("dx")), num(e.get("dy")))
            continue
        n_aircraft += 1
        others.append((num(e.get("x")), num(e.get("y")),
                       num(e.get("dx")), num(e.get("dy")), icon, etype))

    other = None
    if self_pos and others:
        if grid_w > 0 and grid_h > 0:
            def sep(o):
                return math.hypot((o[0] - self_pos[0]) * grid_w, (o[1] - self_pos[1]) * grid_h)
            other = min(others, key=sep)
        else:
            other = others[0]
    return self_pos, other, n_aircraft


def state_values():
    """8111 的键名自带单位、含空格逗号；一律取不到就给 0。

    ★ 端点是 `/state`（没有 .json 后缀），写成 /state.json 会 404，静默变成全 0 —— 踩过。
    """
    try:
        st = get_json("/state") or {}
    except (urllib.error.URLError, OSError, ValueError, json.JSONDecodeError):
        return (0.0,) * 7
    return (
        num(st.get("IAS, km/h")), num(st.get("TAS, km/h")), num(st.get("H, m")),
        num(st.get("Vy, m/s")), num(st.get("Ny"), 1.0), num(st.get("Wx, deg/s")),
        num(st.get("AoA, deg")),
    )


def main():
    ap = argparse.ArgumentParser(description="采集 WtHud 编队误差实测数据")
    ap.add_argument("--out", default=None, help="输出 CSV（默认桌面 maperr-<label>.csv）")
    ap.add_argument("--label", default="A", help="机号，仅用于日志显示")
    ap.add_argument("--hz", type=float, default=4.0, help="采样频率（默认 4 Hz）")
    ap.add_argument("--seconds", type=float, default=0, help="采集时长，0 = 一直采到 Ctrl+C")
    ap.add_argument("--probe", type=float, default=15.0, help="开跑前等待 8111 的秒数")
    args = ap.parse_args()

    out_path = args.out or os.path.join(os.path.expanduser("~"), "Desktop", f"maperr-{args.label}.csv")
    period = 1.0 / max(0.5, args.hz)
    print(f"[{args.label}] 等待 8111 接口（最多 {args.probe:.0f} s）…")
    grid_w, grid_h = read_grid(retries=max(2, int(args.probe * 2)))
    if grid_w <= 0:
        print("拿不到 /map_info.json 的 grid_size：游戏没开、或不在对局里。退出。")
        return 2
    print(f"[{args.label}] 地图尺寸 {grid_w:.0f} x {grid_h:.0f} m，开始采集 → {out_path}")

    t0 = time.time()
    rows = 0
    last_report = 0.0
    ambiguous = 0
    empty_other = 0

    with open(out_path, "w", newline="", encoding="utf-8") as fh:
        wr = csv.writer(fh)
        wr.writerow(FIELDS)
        try:
            while True:
                if args.seconds > 0 and time.time() - t0 >= args.seconds:
                    break
                tick = time.time()
                utc_ms = int(tick * 1000)

                try:
                    objs = get_json("/map_obj.json")
                except (urllib.error.URLError, OSError, ValueError, json.JSONDecodeError):
                    objs = None

                if not isinstance(objs, list):
                    time.sleep(period)
                    continue

                self_pos, other, n_aircraft = pick(objs, grid_w, grid_h)
                if not self_pos:
                    time.sleep(period)
                    continue

                if n_aircraft > 1:
                    ambiguous += 1
                if other is None:
                    empty_other += 1

                sx, sy, sdx, sdy = self_pos
                if other:
                    ox, oy, odx, ody, oicon, otype = other
                    dist = math.hypot((ox - sx) * grid_w, (oy - sy) * grid_h)
                else:
                    ox = oy = odx = ody = ""
                    oicon = otype = ""
                    dist = ""

                ias, tas, alt, vy, ny, wx, aoa = state_values()
                wr.writerow([utc_ms, f"{grid_w:.1f}", f"{grid_h:.1f}",
                             f"{sx:.6f}", f"{sy:.6f}", f"{sdx:.4f}", f"{sdy:.4f}",
                             (f"{ox:.6f}" if ox != "" else ""), (f"{oy:.6f}" if oy != "" else ""),
                             (f"{odx:.4f}" if odx != "" else ""), (f"{ody:.4f}" if ody != "" else ""),
                             oicon, otype, n_aircraft, (f"{dist:.0f}" if dist != "" else ""),
                             f"{ias:.1f}", f"{tas:.1f}", f"{alt:.1f}", f"{vy:.2f}",
                             f"{ny:.2f}", f"{wx:.2f}", f"{aoa:.2f}"])
                rows += 1

                if tick - last_report >= 1.0:
                    last_report = tick
                    elapsed = tick - t0
                    seen = f"{dist:6.0f} m" if dist != "" else "   --   "
                    print(f"[{args.label}] t={elapsed:6.1f}s  rows={rows:5d}  目标 {seen}  "
                          f"其他飞机={n_aircraft}  ny={ny:.2f} wx={wx:+.1f}")
                    fh.flush()

                sleep = period - (time.time() - tick)
                if sleep > 0:
                    time.sleep(sleep)
        except KeyboardInterrupt:
            print("\n手动停止。")

    print(f"[{args.label}] 完成：{rows} 行 → {out_path}")
    if ambiguous:
        print(f"[{args.label}] ★ 有 {ambiguous} 次采样里场上不止一架其它飞机，other_* 取的是最近的那架——"
              "建议下一轮改成只放两架飞机的房间。")
    if empty_other:
        print(f"[{args.label}] ★ 有 {empty_other} 次采样没看到对方（超出地图显示范围/不同队/已阵亡）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
