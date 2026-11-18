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
    /// 平均值（忽略 null），复刻 FlightLog <c>avg_filter</c> 的口径：
    /// <list type="number">
    /// <item>先排除每个 <c>death</c> 事件前 <paramref name="deathWindowS"/> 秒（闭区间）内的样本；</item>
    /// <item>**只在死亡前 <paramref name="spikeWindowS"/> 秒窗口内**做 MAD 尖峰剔除
    /// （<c>|x - median| &gt; sigma × 1.4826 × MAD</c>）；窗口外一律不动 —— 起飞/巡航的
    /// 机动峰值不是坏点；</item>
    /// <item>没有 death 事件时完全不做 MAD 过滤（原始均值）；</item>
    /// <item>窗口内过滤后剩余样本少于 <see cref="MinFilterSamples"/> 时退回「只排除死亡窗口」。</item>
    /// </list>
    ///
    /// <para>FlightLog 只在 <c>stats</c> 里给速度/高度的 <c>avg_*</c>；本页另外要显示平均 G /
    /// 平均迎角 / 平均 dEs/dt，只能从序列自己算。这里复刻同一套规则 —— 没
    /// 有「手动范围选择」，但不做这个过滤的话，坠机瞬间的 13 G 会把整页平均值拉偏。</para>
    /// </summary>
    internal static double? MeanFiltered(double?[]? values, double[]? t,
        IReadOnlyList<FlightLogBridge.FlightLogEvent>? events,
        double deathWindowS, double sigma, double spikeWindowS = DefaultSpikeWindowS)
    {
        if (values is null) return null;

        // 没有时间轴：死亡窗口与尖峰范围都无从定位，只能给算术平均。
        if (t is null) return PlainMean(values);

        // 1) 候选：非空 + 不在任何 death 前的 deathWindowS 秒内。
        var candidates = new List<int>(values.Length);
        for (var i = 0; i < values.Length && i < t.Length; i++)
        {
            if (values[i] is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            if (NearDeath(t[i], events, deathWindowS)) continue;
            candidates.Add(i);
        }
        if (candidates.Count == 0) return null;

        // 2) MAD 尖峰：只在死亡前窗口 ∩ 候选里判，命中者从平均里剔除。
        var excluded = new HashSet<int>();
        if (sigma > 0 && spikeWindowS > 0 && HasDeath(events))
        {
            var domain = new List<(int Index, double Value)>(candidates.Count);
            foreach (var i in candidates)
                if (InDeathWindow(t[i], events, spikeWindowS)) domain.Add((i, values[i]!.Value));

            if (domain.Count >= 3)
            {
                var median = Median(domain.Select(p => p.Value).ToList());
                var mad = Median(domain.Select(p => Math.Abs(p.Value - median)).ToList());
                var threshold = sigma * MadToSigma * mad;
                foreach (var (index, value) in domain)
                    if (Math.Abs(value - median) > threshold) excluded.Add(index);

                // 窗口内剩太少 → 该通道退回「只排除死亡窗口」（与 FlightLog 的 fallback 一致）
                if (domain.Count - excluded.Count < MinFilterSamples) excluded.Clear();
            }
        }

        double sum = 0;
        var count = 0;
        foreach (var i in candidates)
        {
            if (excluded.Contains(i)) continue;
            sum += values[i]!.Value;
            count++;
        }
        return count > 0 ? sum / count : null;
    }

    private static double? PlainMean(double?[] values)
    {
        double sum = 0;
        var count = 0;
        foreach (var v in values)
        {
            if (v is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            sum += d;
            count++;
        }
        return count > 0 ? sum / count : null;
    }

    /// <summary>是否存在任何带时间的 death 事件（没有就等于不做 MAD 过滤）。</summary>
    private static bool HasDeath(IReadOnlyList<FlightLogBridge.FlightLogEvent>? events)
    {
        if (events is null) return false;
        foreach (var e in events)
        {
            if (e.T is null) continue;
            if (string.Equals(e.Kind, "death", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>t 是否落在某个 death 前 <paramref name="window"/> 秒的闭区间内。</summary>
    private static bool InDeathWindow(double t, IReadOnlyList<FlightLogBridge.FlightLogEvent>? events, double window)
    {
        if (events is null || !(window > 0)) return false;
        foreach (var e in events)
        {
            if (e.T is not double et) continue;
            if (!string.Equals(e.Kind, "death", StringComparison.OrdinalIgnoreCase)) continue;
            if (t >= et - window && t <= et) return true;
        }
        return false;
    }

    /// <summary>
    /// 「死亡前 N 秒」：只剔除死亡之前的那一段（死亡之后已经换架次/复飞了）。
    /// 与 <see cref="InDeathWindow"/> 相同的判定，单独留一个名字让调用处语义清楚。
    /// </summary>
    private static bool NearDeath(double t, IReadOnlyList<FlightLogBridge.FlightLogEvent>? events, double window)
        => InDeathWindow(t, events, window);

    /// <summary>中位数（偶数个取中间两个的平均，与 Python statistics.median 一致）。</summary>
    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    /// <summary>死亡窗口/σ/尖峰窗口的默认值：与 FlightLog 的 avg_filter 默认一致，缺 stats 时兜底。</summary>
    internal const double DefaultDeathWindowS = 10.0;
    internal const double DefaultSpikeSigma = 3.0;
    internal const double DefaultSpikeWindowS = 30.0;

    /// <summary>MAD → 标准差的换算系数（正态分布下）。与 FlightLog 的 1.4826 一致。</summary>
    internal const double MadToSigma = 1.4826;

    /// <summary>窗口内过滤后至少要保留的样本数；不足则退回「只排除死亡窗口」。</summary>
    internal const int MinFilterSamples = 30;
}
