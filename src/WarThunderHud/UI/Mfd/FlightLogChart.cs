// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Drawing;
using WtHud.Hud;

namespace WtHud.UI.Mfd;

/// <summary>曲线画在哪条纵轴。<see cref="Left"/> 恒有刻度；<see cref="Right"/> 只在有曲线时出现。</summary>
internal enum FlightLogAxis
{
    Left,
    Right,
}

/// <summary>
/// 一条曲线：从 <c>series</c> 取哪一列、画在哪根轴、什么颜色、显示时乘什么系数。
/// <para>★ 显示系数只做单位换算（km/h ↔ kt、m ↔ ft），内部值永远是 JSON 里的公制，
/// 与全项目「存储恒公制、只有显示换算」的约定一致。</para>
/// </summary>
internal sealed class FlightLogCurve
{
    public required string LabelKey { get; init; }
    public required Func<FlightLogBridge.FlightLogSeries, double?[]?> Values { get; init; }
    public FlightLogAxis Axis { get; init; } = FlightLogAxis.Left;

    /// <summary>0 = 主曲线（绿）、1 = 次曲线（琥珀）、2 = 第三曲线（灰）。</summary>
    public int Palette { get; init; }

    /// <summary>显示换算：公制值 → (显示数值, 单位文本)。</summary>
    public required Func<HudConfig, (double Factor, string Unit)> Scale { get; init; }
}

/// <summary>右侧统计卡的一行：标签查 L10n，值已含单位。</summary>
internal sealed record FlightLogStatRow(string LabelKey, string Value);

/// <summary>
/// 一个图表主题（= 左侧一页）：曲线集合 + 右侧统计卡。纯数据 + 纯函数，无 Graphics 依赖，
/// 自检可以直接断言「速度页的统计值来自 avg_*、能量页的 dEs/dt 过滤了死亡窗口」。
/// </summary>
internal sealed class FlightLogChartTheme
{
    /// <summary>稳定的英文键（预览视图 <c>flightlog-speed</c> 用它定位）。</summary>
    public required string Key { get; init; }

    public required string TitleKey { get; init; }

    public required Func<FlightLogCurve[]> Curves { get; init; }

    public required Func<HudConfig, FlightLogBridge.FlightLogFlight,
        IReadOnlyList<FlightLogBridge.FlightLogEvent>?, List<FlightLogStatRow>> Stats { get; init; }
}

/// <summary>
/// MFD 飞行记录的图表主题表与统计口径。**纯计算**：取范围、算平均、单位换算都在这里，
/// 页面只负责把结果画出来。
///
/// <para>★ 平均值的口径（用户定稿）：速度/高度的平均直接用 FlightLog 写进 JSON 的 <c>avg_*</c>
/// —— 它已经在工具侧去掉了「死亡前 10 秒 + MAD 3σ 尖峰」。JSON 里没有的（平均 G / 平均迎角 /
/// 平均 dEs/dt）用同一套规则从序列复算（<see cref="FlightLogPlot.MeanFiltered"/>），
/// **不提供手动时间范围**。</para>
/// </summary>
internal static class FlightLogChart
{
    // ------------------------------------------------------------------ 主题表

    /// <summary>7 个主题，左右翻页顺序即此顺序。</summary>
    public static readonly IReadOnlyList<FlightLogChartTheme> Themes = new List<FlightLogChartTheme>
    {
        new()
        {
            Key = "speed",
            TitleKey = "flylog.theme.speed",
            Curves = () => new[]
            {
                SpeedCurve("flylog.leg.ias", s => s.IasKmh, 0),
                SpeedCurve("flylog.leg.tas", s => s.TasKmh, 1),
            },
            Stats = (cfg, flight, _) =>
            {
                var st = flight.Stats;
                return new List<FlightLogStatRow>
                {
                    new("flylog.st.maxIas", FmtSpeed(st?.MaxIasKmh, cfg)),
                    new("flylog.st.avgIas", FmtSpeed(st?.AvgIasKmh, cfg)),
                    new("flylog.st.maxTas", FmtSpeed(st?.MaxTasKmh, cfg)),
                    new("flylog.st.avgTas", FmtSpeed(st?.AvgTasKmh, cfg)),
                    new("flylog.st.maxMach", FmtMach(st?.MaxMach)),
                    new("flylog.st.maxIasFiltered", FmtSpeed(st?.MaxIasFilteredKmh, cfg)),
                };
            },
        },
        new()
        {
            Key = "altitude",
            TitleKey = "flylog.theme.altitude",
            Curves = () => new[]
            {
                AltitudeCurve("flylog.leg.alt", s => s.AltM, 0),
                AltitudeCurve("flylog.leg.ralt", s => s.RaltM, 1),
            },
            Stats = (cfg, flight, _) =>
            {
                var st = flight.Stats;
                var series = flight.Series;
                var rows = new List<FlightLogStatRow>
                {
                    new("flylog.st.maxAlt", FmtLength(st?.MaxAltM, cfg)),
                    new("flylog.st.avgAlt", FmtLength(st?.AvgAltM, cfg)),
                };
                if (series is not null && FlightLogPlot.Range(series.AltM) is { } altRange)
                    rows.Add(new("flylog.st.altRange", FmtLength(altRange.Min, cfg) + " – "
                                                       + FmtLength(altRange.Max, cfg)));
                return rows;
            },
        },
        new()
        {
            Key = "energy",
            TitleKey = "flylog.theme.energy",
            Curves = () => new[]
            {
                EnergyCurve("flylog.leg.es", s => s.EsM, 0),
            },
            Stats = (cfg, flight, events) =>
            {
                var st = flight.Stats;
                var rows = new List<FlightLogStatRow>
                {
                    new("flylog.st.esRange", FmtEnergyRange(st?.EsRangeM, cfg)),
                    new("flylog.st.esDelta", FmtSignedLength(st?.EsDeltaM, cfg)),
                    new("flylog.st.avgDes", FmtEnergyRate(AvgOf(flight, events, s => s.DesDtMs), cfg)),
                };
                if (flight.Series is { } s && FlightLogPlot.MaxOf(s.DesDtMs) is { } maxDes)
                    rows.Add(new("flylog.st.maxDes", FmtEnergyRate(maxDes, cfg)));
                if (flight.Series is { } s2 && FlightLogPlot.MinOf(s2.DesDtMs) is { } minDes)
                    rows.Add(new("flylog.st.minDes", FmtEnergyRate(minDes, cfg)));
                return rows;
            },
        },
        new()
        {
            Key = "energyRate",
            TitleKey = "flylog.theme.energyRate",
            Curves = () => new[]
            {
                EnergyRateCurve("flylog.leg.des", s => s.DesDtMs, 0),
            },
            Stats = (cfg, flight, events) =>
                new List<FlightLogStatRow>
                {
                    new("flylog.st.avgDes", FmtEnergyRate(AvgOf(flight, events, s => s.DesDtMs), cfg)),
                    new("flylog.st.maxDes", FmtEnergyRate(MaxOf(flight, s => s.DesDtMs), cfg)),
                    new("flylog.st.minDes", FmtEnergyRate(MinOf(flight, s => s.DesDtMs), cfg)),
                    new("flylog.st.esDelta", FmtSignedLength(flight.Stats?.EsDeltaM, cfg)),
                },
        },
        new()
        {
            Key = "vy",
            TitleKey = "flylog.theme.vy",
            Curves = () => new[]
            {
                VyCurve("flylog.leg.vy", s => s.VyMs, 0),
            },
            Stats = (cfg, flight, events) =>
                new List<FlightLogStatRow>
                {
                    new("flylog.st.maxVy", FmtVy(MaxOf(flight, s => s.VyMs), cfg)),
                    new("flylog.st.minVy", FmtVy(MinOf(flight, s => s.VyMs), cfg)),
                    new("flylog.st.avgVy", FmtVy(AvgOf(flight, events, s => s.VyMs), cfg)),
                },
        },
        new()
        {
            Key = "aoa",
            TitleKey = "flylog.theme.aoa",
            Curves = () => new[]
            {
                AngleCurve("flylog.leg.aoa", s => s.AoaDeg, 0),
                GCurve("flylog.leg.ny", s => s.NyG, 1),
            },
            Stats = (cfg, flight, events) =>
                new List<FlightLogStatRow>
                {
                    new("flylog.st.maxG", FmtG(flight.Stats?.MaxG)),
                    new("flylog.st.avgG", FmtG(AvgOf(flight, events, s => s.NyG))),
                    new("flylog.st.maxAoa", FmtDeg(MaxOf(flight, s => s.AoaDeg))),
                    new("flylog.st.avgAoa", FmtDeg(AvgOf(flight, events, s => s.AoaDeg))),
                },
        },
        new()
        {
            Key = "attitude",
            TitleKey = "flylog.theme.attitude",
            Curves = () => new[]
            {
                AngleCurve("flylog.leg.pitch", s => s.PitchDeg, 0),
                // 坡度与俯仰同是角度但量级差很多（俯仰 ±40°、坡度 ±180°）：各占一根轴，
                // 否则共用一根轴时俯仰会被压成一条贴着中线的直线。
                BankCurve("flylog.leg.bank", s => s.BankDeg, 1),
            },
            Stats = (cfg, flight, events) =>
            {
                var rows = new List<FlightLogStatRow>
                {
                    new("flylog.st.maxG", FmtG(flight.Stats?.MaxG)),
                    new("flylog.st.avgG", FmtG(AvgOf(flight, events, s => s.NyG))),
                    new("flylog.st.maxAoa", FmtDeg(MaxOf(flight, s => s.AoaDeg))),
                };
                if (flight.Series is { } s)
                {
                    if (FlightLogPlot.Range(s.PitchDeg) is { } pr)
                        rows.Add(new("flylog.st.pitchRange",
                            FmtDeg(pr.Min) + " – " + FmtDeg(pr.Max)));
                    if (MaxAbs(s.BankDeg) is { } bank)
                        rows.Add(new("flylog.st.maxBank", FmtDeg(bank)));
                }
                return rows;
            },
        },
    };

    public static int ThemeCount => Themes.Count;

    /// <summary>按稳定键找页号；找不到返回 0（预览视图写错时不至于白屏）。</summary>
    public static int IndexOfKey(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            for (var i = 0; i < Themes.Count; i++)
                if (string.Equals(Themes[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return 0;
    }

    public static FlightLogChartTheme ThemeAt(int index) =>
        Themes[Math.Clamp(index, 0, Themes.Count - 1)];

    // ------------------------------------------------------------------ 曲线工厂

    private static FlightLogCurve SpeedCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = cfg => (1.0 / Units.KmhPerSpeedUnit(cfg.SpeedUnits), Units.SpeedLabel(cfg.SpeedUnits)),
    };

    private static FlightLogCurve AltitudeCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = cfg => (1.0 / Units.MetresPerLengthUnit(cfg.LengthUnits), Units.LengthLabel(cfg.LengthUnits)),
    };

    private static FlightLogCurve EnergyCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = cfg => (1.0 / Units.MetresPerLengthUnit(cfg.LengthUnits), Units.LengthLabel(cfg.LengthUnits)),
    };

    /// <summary>能量变化率：公制 m/s，英制按垂直速度换算成 ft/min（与 Vy 同一口径）。</summary>
    private static FlightLogCurve EnergyRateCurve(string label,
        Func<FlightLogBridge.FlightLogSeries, double?[]?> get, int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = cfg => cfg.LengthUnits == LengthUnit.Imperial
            ? (Units.FpmPerMps, "ft/min")
            : (1.0, "m/s"),
    };

    private static FlightLogCurve VyCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = cfg => cfg.LengthUnits == LengthUnit.Imperial
            ? (Units.FpmPerMps, "ft/min")
            : (1.0, "m/s"),
    };

    private static FlightLogCurve AngleCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Left, Palette = palette,
        Scale = _ => (1.0, "°"),
    };

    private static FlightLogCurve GCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Right, Palette = palette,
        Scale = _ => (1.0, "G"),
    };

    /// <summary>坡度：与俯仰同为角度，但画在右轴（量级差一个数量级，共用一根轴会把俯仰压平）。</summary>
    private static FlightLogCurve BankCurve(string label, Func<FlightLogBridge.FlightLogSeries, double?[]?> get,
        int palette) => new()
    {
        LabelKey = label, Values = get, Axis = FlightLogAxis.Right, Palette = palette,
        Scale = _ => (1.0, "°"),
    };

    // ------------------------------------------------------------------ 序列统计

    /// <summary>按 FlightLog 的自动过滤口径求平均（死亡前 10 s + 死亡前 30 s 窗口内的 MAD 尖峰）。</summary>
    private static double? AvgOf(FlightLogBridge.FlightLogFlight flight,
        IReadOnlyList<FlightLogBridge.FlightLogEvent>? events,
        Func<FlightLogBridge.FlightLogSeries, double?[]?> pick)
    {
        if (flight.Series is not { } series) return null;
        var (window, sigma, spikeWindow) = FilterParams(flight);
        return FlightLogPlot.MeanFiltered(pick(series), series.T, events, window, sigma, spikeWindow);
    }

    private static double? MaxOf(FlightLogBridge.FlightLogFlight flight,
        Func<FlightLogBridge.FlightLogSeries, double?[]?> pick) =>
        flight.Series is { } s ? FlightLogPlot.MaxOf(pick(s)) : null;

    private static double? MinOf(FlightLogBridge.FlightLogFlight flight,
        Func<FlightLogBridge.FlightLogSeries, double?[]?> pick) =>
        flight.Series is { } s ? FlightLogPlot.MinOf(pick(s)) : null;

    /// <summary>
    /// 过滤参数：优先用 JSON 里的 <c>avg_filter</c>（含 <c>spike_window_s</c>），缺省回到
    /// FlightLog 默认值。MAD 尖峰只在死亡前窗口内生效 —— 窗口外不做离群过滤。
    /// </summary>
    internal static (double Window, double Sigma, double SpikeWindow) FilterParams(
        FlightLogBridge.FlightLogFlight flight)
    {
        var f = flight.Stats?.AvgFilter;
        return (
            f?.DeathWindowS is double w && w >= 0 ? w : FlightLogPlot.DefaultDeathWindowS,
            f?.SpikeSigma is double s && s > 0 ? s : FlightLogPlot.DefaultSpikeSigma,
            f?.SpikeWindowS is double sw && sw > 0 ? sw : FlightLogPlot.DefaultSpikeWindowS);
    }

    /// <summary>坡度这类对称量：取绝对值最大的点（左右坡度都算「最大坡度」）。</summary>
    private static double? MaxAbs(double?[]? values)
    {
        double? best = null;
        if (values is null) return null;
        foreach (var v in values)
        {
            if (v is not double d || double.IsNaN(d) || double.IsInfinity(d)) continue;
            var a = Math.Abs(d);
            if (best is null || a > best) best = a;
        }
        return best;
    }

    // ------------------------------------------------------------------ 文本格式化（显示单位）

    internal static string FmtSpeed(double? kmh, HudConfig cfg)
    {
        if (kmh is not double v) return "—";
        return Units.SpeedValue(v, cfg.SpeedUnits).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
               + " " + Units.SpeedLabel(cfg.SpeedUnits);
    }

    internal static string FmtMach(double? mach) =>
        mach is double v ? v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "—";

    internal static string FmtLength(double? metres, HudConfig cfg)
    {
        if (metres is not double v) return "—";
        return Units.LengthValue(v, cfg.LengthUnits).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
               + " " + Units.LengthLabel(cfg.LengthUnits);
    }

    /// <summary>带正负号的高度类数值（能量变化可正可负）。</summary>
    internal static string FmtSignedLength(double? metres, HudConfig cfg)
    {
        if (metres is not double v) return "—";
        var text = Units.LengthValue(v, cfg.LengthUnits);
        var sign = text >= 0 ? "+" : "";
        return sign + text.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
               + " " + Units.LengthLabel(cfg.LengthUnits);
    }

    /// <summary>Es 范围：两端的单位写在后面，中间用 – 连接。</summary>
    internal static string FmtEnergyRange(double?[]? range, HudConfig cfg)
    {
        if (range is not { Length: >= 2 } || range[0] is not double lo || range[1] is not double hi) return "—";
        if (lo > hi) (lo, hi) = (hi, lo);
        return FmtLength(lo, cfg) + " – " + FmtLength(hi, cfg);
    }

    internal static string FmtEnergyRate(double? mps, HudConfig cfg)
    {
        if (mps is not double v) return "—";
        if (cfg.LengthUnits == LengthUnit.Imperial)
            return (v * Units.FpmPerMps).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ft/min";
        return v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " m/s";
    }

    internal static string FmtVy(double? mps, HudConfig cfg) => FmtEnergyRate(mps, cfg);

    internal static string FmtG(double? g) =>
        g is double v ? v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " G" : "—";

    internal static string FmtDeg(double? deg) =>
        deg is double v ? v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "°" : "—";

    // ------------------------------------------------------------------ 绘图布局

    /// <summary>图表区占内容宽度 62%，统计卡占 38%（用户定稿比例）。</summary>
    internal const float ChartFraction = 0.62f;

    /// <summary>
    /// 内容区：玻璃区去掉页眉与页脚提示。页眉由窗体画（品牌/标题/版本），页脚是操作提示，
    /// 页面内容不能盖住它们。
    /// </summary>
    internal static RectangleF ContentRect(RectangleF glass, float pad, float scale) =>
        new(glass.Left + pad,
            glass.Top + 46 * scale,
            Math.Max(10, glass.Width - pad * 2),
            Math.Max(10, glass.Height - 46 * scale - 30 * scale));

    /// <summary>左右两栏矩形（中间留 10*s 间隙）。</summary>
    internal static (RectangleF Chart, RectangleF Stats) SplitColumns(RectangleF content, float scale)
    {
        var gap = 10 * scale;
        var chartW = (content.Width - gap) * ChartFraction;
        var statsW = content.Width - gap - chartW;
        return (
            new RectangleF(content.Left, content.Top, chartW, content.Height),
            new RectangleF(content.Left + chartW + gap, content.Top, statsW, content.Height));
    }

    /// <summary>图表内的绘图矩形：左留刻度、右留有右侧轴时留刻度、上留图例、下留时间刻度。</summary>
    internal static RectangleF PlotRect(RectangleF chart, float scale, bool hasRightAxis)
    {
        var left = 48 * scale;
        var right = (hasRightAxis ? 48 : 12) * scale;
        var top = 30 * scale;
        var bottom = 26 * scale;
        return new RectangleF(
            chart.Left + left, chart.Top + top,
            Math.Max(10, chart.Width - left - right),
            Math.Max(10, chart.Height - top - bottom));
    }
}
