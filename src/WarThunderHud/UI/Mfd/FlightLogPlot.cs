// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Drawing;
using System.Globalization;
using WtHud.Hud;
using WtHud.UI.Mfd;

namespace WtHud.UI.Mfd;

/// <summary>
/// 飞行记录图表的**纯计算**部分：取值范围、nice numbers 刻度、数据 → 像素映射、以及
/// 序列统计（最大值 / 过滤后的平均）。全部无窗口依赖，自检可直接断言。
///
/// <para>这里刻意不碰 Graphics：绘制只在页面里发生，坐标必须来自同一套函数，
/// 否则「线画在这里、刻度标在那里」很容易差半格。</para>
/// </summary>
internal static class FlightLogPlot
{
    // ------------------------------------------------------------------ 取值范围

    /// <summary>序列的最小/最大值（忽略 null）。全空时返回 null。</summary>
    internal static (double Min, double Max)? Range(double?[]? values)
    {
        if (values is null) return null;
        double min = double.MaxValue, max = double.MinValue;
        var any = false;
        foreach (var v in values)
        {
            if (v is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (d < min) min = d;
            if (d > max) max = d;
            any = true;
        }
        return any ? (min, max) : null;
    }

    /// <summary>
    /// 绘图用的取值范围：在数据范围外留 <paramref name="padFraction"/> 的余量；
    /// 所有值相同时人为撑开，避免除零（画出来是一条贴着中线/边界的直线）。
    /// </summary>
    internal static (double Min, double Max) PaddedRange(double?[]? values, double padFraction = 0.06)
    {
        var r = Range(values);
        if (r is null) return (0, 1);
        var (min, max) = r.Value;
        var span = max - min;
        if (!(span > 0))
        {
            var pad = Math.Max(Math.Abs(max) * 0.1, 1.0);
            return (min - pad, max + pad);
        }
        var margin = span * Math.Max(0, padFraction);
        return (min - margin, max + margin);
    }

    /// <summary>多序列合并后的取值范围（左右双轴各算各的，所以这里只服务同轴多条线）。</summary>
    internal static (double Min, double Max) PaddedRangeOf(IEnumerable<double?[]?> series, double padFraction = 0.06)
    {
        double min = double.MaxValue, max = double.MinValue;
        var any = false;
        foreach (var s in series)
        {
            var r = Range(s);
            if (r is null) continue;
            if (r.Value.Min < min) min = r.Value.Min;
            if (r.Value.Max > max) max = r.Value.Max;
            any = true;
        }
        if (!any) return (0, 1);
        var span = max - min;
        if (!(span > 0))
        {
            var pad = Math.Max(Math.Abs(max) * 0.1, 1.0);
            return (min - pad, max + pad);
        }
        var margin = span * Math.Max(0, padFraction);
        return (min - margin, max + margin);
    }

    // ------------------------------------------------------------------ 刻度

    /// <summary>
    /// 自适应刻度：按目标格数算出 1/2/5×10ⁿ 的步长，再取落在 [min,max] 内的整步长刻度。
    /// 至少给出 2 个刻度（退化区间时用中点两侧兜底），最多不会超过目标格数太多。
    /// </summary>
    internal static List<double> NiceTicks(double min, double max, int targetTicks, out double step)
    {
        targetTicks = Math.Clamp(targetTicks, 2, 8);
        var span = max - min;
        if (!(span > 0) || double.IsNaN(span) || double.IsInfinity(span))
        {
            step = 1;
            return new List<double> { min };
        }

        step = Units.NiceStep(span / targetTicks);
        if (!(step > 0) || double.IsNaN(step) || double.IsInfinity(step)) step = span / targetTicks;

        var ticks = new List<double>();
        var first = Math.Ceiling(min / step) * step;
        // 索引循环而不是 v += step 累加：浮点累加 20 次之后刻度文本会出现 99.99999。
        var count = (int)Math.Floor((max - first) / step) + 1;
        count = Math.Clamp(count, 0, 12);
        for (var i = 0; i < count; i++) ticks.Add(first + step * i);

        if (ticks.Count == 0) ticks.Add((min + max) / 2);
        return ticks;
    }

    /// <summary>刻度/数值文本：按步长决定小数位，避免「0.30000000000000004」。</summary>
    internal static string TickText(double value, double step)
    {
        var decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : 3;
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>数值文本：按量级保留合适的位数（卡片用）。</summary>
    internal static string ValueText(double value, int decimals = 0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "—";
        return value.ToString("F" + Math.Clamp(decimals, 0, 3), CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ 数据 → 像素

    /// <summary>时间 → 横向像素。t1 = t0（单点）时落在绘图区中间。</summary>
    internal static float MapX(double t, double t0, double t1, RectangleF plot)
    {
        if (!(t1 > t0)) return plot.Left + plot.Width / 2f;
        var k = (float)((t - t0) / (t1 - t0));
        return plot.Left + k * plot.Width;
    }

    /// <summary>数值 → 纵向像素（屏幕坐标，值越大越靠上）。vmax = vmin 时落在中间。</summary>
    internal static float MapY(double v, double vmin, double vmax, RectangleF plot)
    {
        if (!(vmax > vmin)) return plot.Top + plot.Height / 2f;
        var k = (float)((v - vmin) / (vmax - vmin));
        return plot.Bottom - k * plot.Height;
    }

    // ------------------------------------------------------------------ 统计

    /// <summary>序列最大值（忽略 null）。空序列返回 null。</summary>
    internal static double? MaxOf(double?[]? values)
    {
        double? best = null;
        if (values is null) return null;
        foreach (var v in values)
        {
            if (v is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (best is null || d > best) best = d;
        }
        return best;
    }

    /// <summary>序列最小值（忽略 null）。空序列返回 null。</summary>
    internal static double? MinOf(double?[]? values)
    {
        double? best = null;
        if (values is null) return null;
        foreach (var v in values)
        {
            if (v is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (best is null || d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// 平均值（忽略 null），可选按 FlightLog 的口径过滤：死亡前 <paramref name="deathWindowS"/> 秒的
    /// 样本 + 超过 <paramref name="sigma"/> 倍标准差的尖峰。
    ///
    /// <para>FlightLog 只在 <c>stats</c> 里给速度/高度的 <c>avg_*</c>；本页另外要显示平均 G / 平均迎角 /
    /// 平均 dEs/dt，只能从序列自己算。这里复刻同一套过滤规则 —— 不是「手动范围选择」，
    /// 不做这个过滤的话，坠机瞬间的 13 G 会把整页平均值拉偏。</para>
    /// </summary>
    internal static double? MeanFiltered(double?[]? values, double[]? t,
        IReadOnlyList<FlightLogBridge.FlightLogEvent>? events,
        double deathWindowS, double sigma)
    {
        if (values is null) return null;

        // 1) 收集候选（跳过 null / NaN），并剔除死亡前的样本。
        var candidates = new List<double>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (t is not null && i < t.Length && NearDeath(t[i], events, deathWindowS)) continue;
            candidates.Add(d);
        }
        if (candidates.Count == 0) return null;

        // 2) 3σ 尖峰剔除（一趟足够：这一步只是别让单个坏点主导均值）。
        var mean = Average(candidates);
        if (sigma > 0 && candidates.Count >= 4)
        {
            double sq = 0;
            foreach (var v in candidates) sq += (v - mean) * (v - mean);
            var sd = Math.Sqrt(sq / candidates.Count);
            if (sd > 1e-9)
            {
                var limit = sigma * sd;
                var kept = new List<double>(candidates.Count);
                foreach (var v in candidates)
                    if (Math.Abs(v - mean) <= limit) kept.Add(v);
                if (kept.Count > 0) mean = Average(kept);
            }
        }
        return mean;
    }

    private static bool NearDeath(double t, IReadOnlyList<FlightLogBridge.FlightLogEvent>? events, double window)
    {
        if (events is null || !(window > 0)) return false;
        foreach (var e in events)
        {
            if (e.T is not double et) continue;
            if (!string.Equals(e.Kind, "death", StringComparison.OrdinalIgnoreCase)) continue;
            // 「死亡前 N 秒」：只剔除死亡之前的那一段（死亡之后已经换架次/复飞了）。
            if (t >= et - window && t <= et) return true;
        }
        return false;
    }

    private static double Average(List<double> values)
    {
        double sum = 0;
        foreach (var v in values) sum += v;
        return sum / values.Count;
    }

    /// <summary>死亡窗口/σ 的默认值：与 FlightLog 的 avg_filter 默认一致，缺 stats 时兜底。</summary>
    internal const double DefaultDeathWindowS = 10.0;
    internal const double DefaultSpikeSigma = 3.0;
}
