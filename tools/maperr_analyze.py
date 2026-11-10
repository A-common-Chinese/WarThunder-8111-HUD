#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 A-common-Chinese
"""WtHud 编队数据链 · 误差实测分析端（独立脚本，不进产品代码）

吃两份采集 CSV，回答一个问题：**游戏内推演出来的友机位置，到底有多不准？**

定义
----
  基准 truth : 被观察机 A 自己的 map 位置（A.csv 的 self_*，最准的可用基准）
  观测 obs   : 观察机 B 看到的 A（B.csv 的 other_*，含游戏内推演误差）
  误差       = |obs − truth|（米）；这就是数据链能消除掉的那部分

关键点：两台机器的系统时钟不一定同步，而 800 km/h 下 100 ms 就是 22 米，和要测的量同量级。
所以**不用系统时钟**，而是扫描时间偏移、取残差最小的那个（等价于用轨迹互相关对齐）。

用法
----
  python maperr_analyze.py --truth A.csv --obs B.csv
  python maperr_analyze.py --truth A.csv --obs B.csv --out summary.csv --plot err.png
  python maperr_analyze.py --selftest        # 合成数据自检，不需要游戏

列约定见 maperr_capture.py。
"""

import argparse
import bisect
import csv
import json
import math
import random
import sys

DIST_BANDS = [(0, 200), (200, 500), (500, 1000), (1000, 2000), (2000, 4000), (4000, 10 ** 9)]
WX_BANDS = [(0, 2), (2, 10), (10, 30), (30, 10 ** 9)]
NY_BANDS = [(0, 1), (1, 3), (3, 10 ** 9)]
MAX_SEP_M = 6000.0          # 超过这个距离认为身份匹配错了，丢弃
MIN_SAMPLES = 20


def _f(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def load(path):
    """读 CSV → {'t':[...], 'self':[(x,y)], 'other':[(x,y)或None], 'wx':[...], 'ny':[...]}，坐标已换算成米。"""
    rows = []
    with open(path, newline="", encoding="utf-8") as fh:
        for r in csv.DictReader(fh):
            t = _f(r.get("utc_ms"))
            sx, sy = _f(r.get("self_x")), _f(r.get("self_y"))
            gw, gh = _f(r.get("grid_w")), _f(r.get("grid_h"))
            if t is None or sx is None or sy is None or not gw or not gh:
                continue
            ox, oy = _f(r.get("other_x")), _f(r.get("other_y"))
            rows.append({
                "t": t / 1000.0,
                "grid": (gw, gh),
                "self": (sx * gw, sy * gh),
                "other": (ox * gw, oy * gh) if (ox is not None and oy is not None) else None,
                "wx": _f(r.get("wx")) or 0.0,
                "ny": _f(r.get("ny")) or 1.0,
            })
    if not rows:
        raise SystemExit(f"{path}: 没有可用样本")
    rows.sort(key=lambda r: r["t"])
    return rows


class Series:
    """按时间可插值的一维轨迹（米）。"""

    def __init__(self, points):
        points = sorted(points, key=lambda p: p[0])
        self.t = [p[0] for p in points]
        self.p = [(p[1], p[2]) for p in points]

    def at(self, t):
        if not self.t or t < self.t[0] or t > self.t[-1]:
            return None
        i = bisect.bisect_left(self.t, t)
        if i <= 0:
            return self.p[0]
        if i >= len(self.t):
            return self.p[-1]
        span = self.t[i] - self.t[i - 1]
        if span <= 0:
            return self.p[i]
        k = (t - self.t[i - 1]) / span
        (x0, y0), (x1, y1) = self.p[i - 1], self.p[i]
        return (x0 + (x1 - x0) * k, y0 + (y1 - y0) * k)


def build(truth_rows, obs_rows):
    truth_self = Series([(r["t"], *r["self"]) for r in truth_rows])
    obs_self = Series([(r["t"], *r["self"]) for r in obs_rows])
    obs_other_pts = [(r["t"], *r["other"]) for r in obs_rows if r["other"]]
    obs_other = Series(obs_other_pts)
    return truth_self, obs_self, obs_other


def sample(truth_rows, truth_self, obs_self, obs_other, offset_ms):
    """按给定时间偏移采样，返回 [(error_m, separation_m, wx, ny)]。offset = B 的时钟 − A 的时钟。"""
    shift = offset_ms / 1000.0
    out = []
    for r in truth_rows:
        tb = r["t"] + shift
        obs = obs_other.at(tb)
        if obs is None:
            continue
        bs = obs_self.at(tb)
        if bs is None:
            continue
        sep = math.hypot(r["self"][0] - bs[0], r["self"][1] - bs[1])
        if sep > MAX_SEP_M:
            continue
        err = math.hypot(obs[0] - r["self"][0], obs[1] - r["self"][1])
        out.append((err, sep, r["wx"], r["ny"]))
    return out


def rms(values):
    return math.sqrt(sum(v * v for v in values) / len(values)) if values else float("nan")


def pct(values, q):
    if not values:
        return float("nan")
    s = sorted(values)
    idx = min(len(s) - 1, max(0, int(round(q * (len(s) - 1)))))
    return s[idx]


def stats_table(title, buckets, key_index):
    lines = [f"\n{title}"]
    lines.append(f"{'区间':<16}{'样本':>6}{'中位':>9}{'90分位':>9}{'最大':>9}{'RMS':>9}")
    for label, errs in buckets:
        if not errs:
            lines.append(f"{label:<16}{0:>6}{'--':>9}{'--':>9}{'--':>9}{'--':>9}")
            continue
        lines.append(f"{label:<16}{len(errs):>6}{pct(errs, 0.5):>9.0f}{pct(errs, 0.9):>9.0f}"
                     f"{max(errs):>9.0f}{rms(errs):>9.0f}")
    return "\n".join(lines), [(l, len(e), pct(e, 0.9) if e else None) for l, e in buckets]


def main():
    ap = argparse.ArgumentParser(description="分析 WtHud 编队误差实测数据")
    ap.add_argument("--truth", help="被观察机 A 的 CSV")
    ap.add_argument("--obs", help="观察机 B 的 CSV")
    ap.add_argument("--search-ms", type=int, default=600, help="时间偏移搜索范围 ±ms（默认 600）")
    ap.add_argument("--step-ms", type=int, default=10, help="搜索步长 ms（默认 10）")
    ap.add_argument("--out", help="把分档结果写成 CSV")
    ap.add_argument("--plot", help="出图（需要 matplotlib），给个 png 路径")
    ap.add_argument("--selftest", action="store_true", help="用合成数据自检，不需要游戏")
    args = ap.parse_args()

    if args.selftest:
        return selftest()
    if not args.truth or not args.obs:
        ap.error("要么给 --truth/--obs，要么用 --selftest")

    truth_rows = load(args.truth)
    obs_rows = load(args.obs)
    # 时间基准各自归一化。★ 必须先取出基准值再遍历——边遍历边读 obs_rows[0]["t"] 会把它自己抹成 0，
    # 后面的行就保持绝对时间戳，Series 的二分查找直接失效（这个坑踩过一次）。
    base_a = truth_rows[0]["t"]
    base_b = obs_rows[0]["t"]
    for r in truth_rows:
        r["t"] -= base_a
    for r in obs_rows:
        r["t"] -= base_b

    gw_a, gh_a = truth_rows[0]["grid"]
    gw_b, gh_b = obs_rows[0]["grid"]
    if abs(gw_a - gw_b) > 1 or abs(gh_a - gh_b) > 1:
        print(f"★ 两份数据的地图尺寸不一致（{gw_a:.0f}x{gh_a:.0f} vs {gw_b:.0f}x{gh_b:.0f}）——"
              "不是同一张图/同一局，结论不可用。")
        return 2

    truth_self, obs_self, obs_other = build(truth_rows, obs_rows)
    n_obs = sum(1 for r in obs_rows if r["other"])
    print(f"truth {len(truth_rows)} 行（{truth_rows[-1]['t']:.1f} s），obs {len(obs_rows)} 行，"
          f"其中看到目标 {n_obs} 行（{100.0 * n_obs / len(obs_rows):.0f}%）")

    best = None
    for off in range(-args.search_ms, args.search_ms + 1, args.step_ms):
        s = sample(truth_rows, truth_self, obs_self, obs_other, off)
        if len(s) < MIN_SAMPLES:
            continue
        score = rms([e[0] for e in s])
        if best is None or score < best[1]:
            best = (off, score, len(s))

    if best is None:
        print("没有足够的重叠样本。检查：两机是否同局、是否同队、对方是否在地图显示范围内。")
        return 3

    off, score, n = best
    print(f"\n时间对齐：偏移 {off:+d} ms（即两台机器时钟差），重叠 {n} 个样本，RMS {score:.0f} m")
    print("  注：这个偏移是扫出来的，不依赖系统对时；它同时也是数据链需要的时间同步精度参考。")

    s = sample(truth_rows, truth_self, obs_self, obs_other, off)
    errs = [e[0] for e in s]
    print(f"\n总体：中位 {pct(errs, 0.5):.0f} m，90 分位 {pct(errs, 0.9):.0f} m，"
          f"最大 {max(errs):.0f} m，RMS {rms(errs):.0f} m（样本 {len(errs)}）")

    def bucket_by(bands, pick):
        return [(f"{lo}–{hi if hi < 10 ** 8 else '∞'}", [e[0] for e in s if lo <= pick(e) < hi])
                for lo, hi in bands]

    dist_b = bucket_by(DIST_BANDS, lambda e: e[1])
    wx_b = bucket_by(WX_BANDS, lambda e: abs(e[2]))
    ny_b = bucket_by(NY_BANDS, lambda e: abs(e[3] - 1.0))

    t1, d1 = stats_table("按真实间距分档（米，误差单位 m）", dist_b, 1)
    t2, _ = stats_table("按被观察机滚转角速度 |Wx| 分档（deg/s）", wx_b, 0)
    t3, _ = stats_table("按被观察机 |Ny−1| 分档（机动强度）", ny_b, 0)
    print(t1)
    print(t2)
    print(t3)

    near = [e[0] for e in s if e[1] < 500]
    far = [e[0] for e in s if 1000 <= e[1] < 2000]
    print("\n结论参考（阈值你定）：")
    if near:
        print(f"  500 m 内 90 分位 {pct(near, 0.9):.0f} m —— 编队贴翼阶段"
              f"{'可以用游戏内数据' if pct(near, 0.9) < 50 else '已经不够用'}")
    if far:
        print(f"  1–2 km 90 分位 {pct(far, 0.9):.0f} m —— "
              f"{'数据链有明确价值' if pct(far, 0.9) > 100 else '数据链收益有限'}")

    if args.out:
        with open(args.out, "w", newline="", encoding="utf-8") as fh:
            wr = csv.writer(fh)
            wr.writerow(["band_type", "band", "samples", "median_m", "p90_m", "max_m", "rms_m"])
            for kind, buckets, idx in (("distance", dist_b, 1), ("abs_wx", wx_b, 0), ("abs_ny_dev", ny_b, 0)):
                for label, errs in buckets:
                    if not errs:
                        wr.writerow([kind, label, 0, "", "", "", ""])
                    else:
                        wr.writerow([kind, label, len(errs), f"{pct(errs, 0.5):.0f}",
                                     f"{pct(errs, 0.9):.0f}", f"{max(errs):.0f}", f"{rms(errs):.0f}"])
            wr.writerow(["offset_ms", off, n, "", "", "", f"{rms([e[0] for e in s]):.0f}"])
        print(f"分档结果已写入 {args.out}")

    if args.plot:
        try:
            import matplotlib
            matplotlib.use("Agg")
            import matplotlib.pyplot as plt
        except ImportError:
            print("没装 matplotlib，跳过出图（pip install matplotlib）")
        else:
            fig, ax = plt.subplots(1, 2, figsize=(11, 4))
            ax[0].scatter([e[1] for e in s], [e[0] for e in s], s=8, alpha=0.5)
            ax[0].set_xlabel("true separation (m)")
            ax[0].set_ylabel("position error (m)")
            ax[0].set_title("error vs separation")
            ax[0].grid(alpha=0.3)
            ax[1].scatter([abs(e[2]) for e in s], [e[0] for e in s], s=8, alpha=0.5, color="tab:orange")
            ax[1].set_xlabel("|Wx| (deg/s)")
            ax[1].set_ylabel("position error (m)")
            ax[1].set_title("error vs roll rate")
            ax[1].grid(alpha=0.3)
            fig.tight_layout()
            fig.savefig(args.plot, dpi=120)
            print(f"图已保存 {args.plot}")
    return 0


def selftest():
    """合成一条匀速盘旋轨迹：观测端有 80 ms 延迟 + 15 m 噪声。

    观测端的每条记录用**自己的时钟**打时间戳，但里面装的是 80 ms 之前的位置 ——
    这正是真实场景（游戏内推演会滞后）。检查两点：
      1. 能不能把这个 80 ms 扫出来（期望 +80 ms 左右）
      2. 对齐之后剩下的误差应≈噪声水平（单轴 15 m → 二维 RMS ≈ 21 m）
    """
    random.seed(7)
    radius, omega, dt, latency = 800.0, math.radians(14.0), 0.25, 0.08
    truth = []
    t = 0.0
    while t < 60.0:
        truth.append((t, radius * math.cos(omega * t), radius * math.sin(omega * t)))
        t += dt

    truth_rows = [{"t": t, "grid": (1.0, 1.0), "self": (x, y), "other": None, "wx": 14.0, "ny": 1.0}
                  for (t, x, y) in truth]
    obs_rows = []
    for (t, _x, _y) in truth:
        stale = t - latency                       # 看到的是这一刻的位置
        if stale < truth[0][0]:
            continue
        ox = radius * math.cos(omega * stale) + random.gauss(0, 15)
        oy = radius * math.sin(omega * stale) + random.gauss(0, 15)
        obs_rows.append({"t": t, "grid": (1.0, 1.0), "self": (-3000.0, 0.0),
                         "other": (ox, oy), "wx": 0.0, "ny": 1.0})

    truth_self, obs_self, obs_other = build(truth_rows, obs_rows)
    best = None
    for off in range(-400, 401, 10):
        s = sample(truth_rows, truth_self, obs_self, obs_other, off)
        if len(s) < MIN_SAMPLES:
            continue
        score = rms([e[0] for e in s])
        if best is None or score < best[1]:
            best = (off, score)
    if best is None:
        print("selftest FAIL: 没有重叠样本")
        return 1

    off, score = best
    ok_off = 60 <= off <= 100          # 期望 ≈ +80 ms（lag 被扫出来）
    ok_err = 15.0 <= score <= 30.0     # 期望 ≈ 21 m（噪声）
    print(f"selftest: 扫出的延迟 {off:+d} ms（真值 +80），对齐后 RMS {score:.1f} m（期望 ≈ 21）")
    print("  PASS" if (ok_off and ok_err) else f"  FAIL（偏移判定 {ok_off}，误差判定 {ok_err}）")
    return 0 if (ok_off and ok_err) else 1


if __name__ == "__main__":
    sys.exit(main())
