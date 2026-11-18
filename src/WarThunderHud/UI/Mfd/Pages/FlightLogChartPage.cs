// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using WtHud.Hud;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 飞行记录图表页：**左侧约 62% 折线图 + 右侧约 38% 统计卡**，每个主题一页。
///
/// <list type="bullet">
/// <item>UP/DOWN：切换架次（<c>flights[i]</c>，一对多）；</item>
/// <item>PAGE / 左右：切换主题（速度 / 高度 / 比能量 / 能量变化率 / Vy / AoA+Ny / 姿态）；</item>
/// <item>BACK：回记录列表（由面板弹栈）。</item>
/// </list>
///
/// <para>页面只画，不跑进程、不算过滤规则：数据来自 <see cref="FlightLogBridge.FlightLogResult"/>，
/// 取值范围/刻度/像素映射来自 <see cref="FlightLogPlot"/>，主题与统计口径来自
/// <see cref="FlightLogChart"/>。这样自检能在无窗口条件下断言这三层。</para>
/// </summary>
internal sealed class FlightLogChartPage : MfdPageBase
{
    private readonly FlightLogBridge.FlightLogResult _result;
    private readonly Func<HudConfig> _cfg;
    private int _flightIndex;
    private int _themeIndex;

    public FlightLogChartPage(FlightLogBridge.FlightLogResult result, Func<HudConfig> cfg)
        : base("flightlog:chart")
    {
        _result = result;
        _cfg = cfg;
    }

    public override string TitleKey => "flylog.title";
    public override string HintKey => "flylog.hint.chart";

    public int ThemeIndex => _themeIndex;
    public int ThemeCount => FlightLogChart.ThemeCount;

    public int FlightIndex => _flightIndex;
    public int FlightCount => _result.Flights?.Count ?? 0;

    public string RecordingName =>
        string.IsNullOrWhiteSpace(_result.Source) ? (L10n.Get(LangScope.Screen, "flylog.recording")) : _result.Source!;

    /// <summary>UP/DOWN：换架次（夹紧，不循环 —— 架次顺序就是时间顺序）。</summary>
    public void Move(int delta)
    {
        if (FlightCount <= 1) return;
        _flightIndex = Math.Clamp(_flightIndex + delta, 0, FlightCount - 1);
    }

    /// <summary>PAGE / 左右：换主题；返回 false 表示只有一个主题（调用方提示）。</summary>
    public bool CyclePage(int delta)
    {
        if (ThemeCount <= 1) return false;
        _themeIndex = ((_themeIndex + delta) % ThemeCount + ThemeCount) % ThemeCount;
        return true;
    }

    /// <summary>预览用：直接定位主题（按稳定键）；未知键回到第 0 页。</summary>
    public void SetTheme(string? key) => _themeIndex = FlightLogChart.IndexOfKey(key);

    /// <summary>预览用：直接定位架次。</summary>
    public void SetFlight(int index) => _flightIndex = Math.Clamp(index, 0, Math.Max(0, FlightCount - 1));

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "confirm.ok":
                CyclePage(1);          // 图表页没有「执行」：CONFIRM 等价于下一页
                return true;
        }
        return false;
    }

    private FlightLogBridge.FlightLogFlight? CurrentFlight =>
        _result.Flights is { Count: > 0 } flights ? flights[Math.Clamp(_flightIndex, 0, flights.Count - 1)] : null;

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        var cfg = _cfg();
        var theme = FlightLogChart.ThemeAt(_themeIndex);
        var content = FlightLogChart.ContentRect(ctx.Glass, ctx.Pad, s);

        if (CurrentFlight is not { } flight || flight.Series is not { } series
            || series.T is not { Length: > 0 } times)
        {
            DrawCentered(g, L10n.Get(LangScope.Screen, "flylog.noData"), content, 20f * s);
            return;
        }

        // ---- 页内头部两行（玻璃区顶部 46*s 由窗体占用，这里从它下面开始） ----
        using var headFont = MfdTheme.CjkAt(17f * s);
        using var subFont = MfdTheme.MonoAt(15f * s);
        using var accentBrush = new SolidBrush(MfdTheme.Green);
        using var inkBrush = new SolidBrush(MfdTheme.Text);
        using var mutedBrush = new SolidBrush(MfdTheme.Muted);

        var headerH = 44 * s;
        var canvas = new RectangleF(content.Left, content.Top + headerH, content.Width, content.Height - headerH);

        // 第一行：记录文件名（截断）+ 架次/机型
        var name = Truncate(g, headFont, RecordingName, content.Width * 0.58f);
        g.DrawString(name, headFont, inkBrush, content.Left, content.Top, StringFormat.GenericTypographic);
        var flightText = string.Format(CultureInfo.InvariantCulture,
            L10n.Get(LangScope.Screen, "flylog.flight"),
            Math.Clamp(_flightIndex, 0, Math.Max(0, FlightCount - 1)) + 1, Math.Max(1, FlightCount));
        if (!string.IsNullOrWhiteSpace(flight.Vehicle))
            flightText += " · " + flight.Vehicle;
        var fw = g.MeasureString(flightText, subFont).Width;
        g.DrawString(flightText, subFont, mutedBrush, content.Right - fw, content.Top + 3 * s,
                     StringFormat.GenericTypographic);

        // 第二行：主题名（高亮）+ 时长/采样（说明这一页在看什么）
        var themeName = L10n.Get(LangScope.Screen, theme.TitleKey);
        g.DrawString(themeName, headFont, accentBrush, content.Left, content.Top + 21 * s,
                     StringFormat.GenericTypographic);
        var duration = times[^1] - times[0];
        var info = string.Format(CultureInfo.InvariantCulture,
            L10n.Get(LangScope.Screen, "flylog.chartInfo"), duration, times.Length);
        var iw = g.MeasureString(info, subFont).Width;
        g.DrawString(info, subFont, mutedBrush, content.Right - iw, content.Top + 24 * s,
                     StringFormat.GenericTypographic);

        var (chart, stats) = FlightLogChart.SplitColumns(canvas, s);
        DrawChart(g, s, cfg, theme, flight, series, times, chart);
        DrawStats(g, s, cfg, theme, flight, series, stats);
    }

    // ------------------------------------------------------------------ 折线图

    private void DrawChart(System.Drawing.Graphics g, float s, HudConfig cfg, FlightLogChartTheme theme,
        FlightLogBridge.FlightLogFlight flight, FlightLogBridge.FlightLogSeries series,
        double[] times, RectangleF chart)
    {
        var curves = theme.Curves();
        var prepared = new List<(FlightLogCurve Curve, double?[] Values, double Factor)>();

        var hasRight = false;
        foreach (var curve in curves)
        {
            var raw = curve.Values(series);
            if (raw is null || raw.Length == 0) continue;
            var (factor, _) = curve.Scale(cfg);
            var display = new double?[raw.Length];
            for (var i = 0; i < raw.Length; i++)
                display[i] = raw[i] is double v && !double.IsNaN(v) && !double.IsInfinity(v) ? v * factor : null;
            prepared.Add((curve, display, factor));
            if (curve.Axis == FlightLogAxis.Right) hasRight = true;
        }

        if (prepared.Count == 0)
        {
            DrawCentered(g, L10n.Get(LangScope.Screen, "flylog.noData"), chart, 18f * s);
            return;
        }

        var plot = FlightLogChart.PlotRect(chart, s, hasRight);
        using var grid = new Pen(MfdTheme.Grid);
        using var muted = new SolidBrush(MfdTheme.Muted);
        using var tickFont = MfdTheme.MonoAt(14f * s);
        using var unitFont = MfdTheme.MonoAt(13f * s);
        using var legendFont = MfdTheme.MonoAt(13f * s);

        var t0 = times[0];
        var t1 = times[^1];

        // 左/右轴各自的取值范围（同轴多条线合并）
        var leftVals = prepared.Where(p => p.Curve.Axis == FlightLogAxis.Left).Select(p => (double?[]?)p.Values);
        var rightVals = prepared.Where(p => p.Curve.Axis == FlightLogAxis.Right).Select(p => (double?[]?)p.Values);
        var leftRange = FlightLogPlot.PaddedRangeOf(leftVals);
        var rightRange = hasRight ? FlightLogPlot.PaddedRangeOf(rightVals) : (0d, 1d);

        // 横向网格 + 左轴刻度
        var leftTicks = FlightLogPlot.NiceTicks(leftRange.Item1, leftRange.Item2, 4, out var leftStep);
        foreach (var tick in leftTicks)
        {
            var y = FlightLogPlot.MapY(tick, leftRange.Item1, leftRange.Item2, plot);
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            var text = FlightLogPlot.TickText(tick, leftStep);
            var tw = g.MeasureString(text, tickFont).Width;
            g.DrawString(text, tickFont, muted, plot.Left - tw - 5 * s, y - 7 * s,
                         StringFormat.GenericTypographic);
        }

        if (hasRight)
        {
            var rightTicks = FlightLogPlot.NiceTicks(rightRange.Item1, rightRange.Item2, 4, out var rightStep);
            foreach (var tick in rightTicks)
            {
                var y = FlightLogPlot.MapY(tick, rightRange.Item1, rightRange.Item2, plot);
                var text = FlightLogPlot.TickText(tick, rightStep);
                g.DrawString(text, tickFont, muted, plot.Right + 5 * s, y - 7 * s,
                             StringFormat.GenericTypographic);
            }
        }

        // 时间刻度（自架次起点起算的 mm:ss）
        var elapsed = t1 - t0;
        var timeTicks = FlightLogPlot.NiceTicks(0, elapsed > 0 ? elapsed : 1, 4, out _);
        foreach (var tick in timeTicks)
        {
            var x = FlightLogPlot.MapX(t0 + tick, t0, t1 > t0 ? t1 : t0 + 1, plot);
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            var text = ClockText(tick);
            var tw = g.MeasureString(text, tickFont).Width;
            g.DrawString(text, tickFont, muted, x - tw / 2, plot.Bottom + 4 * s,
                         StringFormat.GenericTypographic);
        }

        // 轴单位：左上 / 右上（图例在同一行的右侧）
        var leftUnit = prepared.First(p => p.Curve.Axis == FlightLogAxis.Left).Curve.Scale(cfg).Unit;
        g.DrawString(leftUnit, unitFont, muted, chart.Left, chart.Top + 2 * s, StringFormat.GenericTypographic);
        if (hasRight)
        {
            var rightUnit = prepared.First(p => p.Curve.Axis == FlightLogAxis.Right).Curve.Scale(cfg).Unit;
            var uw = g.MeasureString(rightUnit, unitFont).Width;
            g.DrawString(rightUnit, unitFont, muted, chart.Right - uw, chart.Top + 2 * s,
                         StringFormat.GenericTypographic);
        }

        // 图例（只画实际有数据的曲线）
        var legendX = chart.Left + 44 * s;
        foreach (var (curve, _, _) in prepared)
        {
            var colour = PaletteColor(curve.Palette);
            using var swatch = new SolidBrush(colour);
            g.FillRectangle(swatch, legendX, chart.Top + 6 * s, 9 * s, 9 * s);
            legendX += 12 * s;
            var label = L10n.Get(LangScope.Screen, curve.LabelKey);
            g.DrawString(label, legendFont, muted, legendX, chart.Top + 3 * s, StringFormat.GenericTypographic);
            legendX += g.MeasureString(label, legendFont).Width + 14 * s;
            if (legendX > chart.Right - 60 * s) break;
        }

        // 事件竖线（death / spawn）——画在曲线之前，避免盖住数据
        DrawEvents(g, s, flight, times, plot, t0, t1, tickFont);

        // 曲线
        foreach (var (curve, values, factor) in prepared)
        {
            var range = curve.Axis == FlightLogAxis.Left ? leftRange : rightRange;
            using var pen = new Pen(PaletteColor(curve.Palette), 1.8f * s)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            PointF? previous = null;
            for (var i = 0; i < values.Length && i < times.Length; i++)
            {
                if (values[i] is not double v)
                {
                    previous = null;          // 缺失值断线，不插值（JSON 用 null 表示没有数据）
                    continue;
                }
                var point = new PointF(
                    FlightLogPlot.MapX(times[i], t0, t1 > t0 ? t1 : t0 + 1, plot),
                    FlightLogPlot.MapY(v, range.Item1, range.Item2, plot));
                if (previous is { } prev) g.DrawLine(pen, prev, point);
                previous = point;
            }
        }
    }

    /// <summary>死亡/复活竖线：死亡琥珀色虚线、复活绿点线；在底部留一个小标签。</summary>
    private void DrawEvents(System.Drawing.Graphics g, float s, FlightLogBridge.FlightLogFlight flight,
        double[] times, RectangleF plot, double t0, double t1, Font font)
    {
        if (_result.Events is not { Count: > 0 } events) return;
        using var deathPen = new Pen(MfdTheme.Amber, 1.6f * s) { DashStyle = DashStyle.Dash };
        using var spawnPen = new Pen(MfdTheme.GreenDim, 1.2f * s) { DashStyle = DashStyle.Dot };
        using var deathText = new SolidBrush(MfdTheme.Amber);
        using var spawnText = new SolidBrush(MfdTheme.GreenDim);

        var marks = new List<(double T, bool Death)>();
        foreach (var ev in events)
        {
            if (ev.T is not double t) continue;
            if (t < t0 - 0.5 || t > t1 + 0.5) continue;
            var death = string.Equals(ev.Kind, "death", StringComparison.OrdinalIgnoreCase);
            var spawn = string.Equals(ev.Kind, "spawn", StringComparison.OrdinalIgnoreCase);
            if (!death && !spawn) continue;
            marks.Add((t, death));
        }
        marks.Sort((a, b) => a.T.CompareTo(b.T));

        foreach (var (t, death) in marks)
        {
            // 旧版记录器把载入时 valid 的短暂闪断也写成 death+spawn（0.1–0.5 s），
            // 两条线会叠在一起；相距 <2 s 的死亡直接省略，只留复活线。
            if (death && marks.Any(m => !m.Death && Math.Abs(m.T - t) <= 2.0)) continue;

            var x = FlightLogPlot.MapX(t, t0, t1 > t0 ? t1 : t0 + 1, plot);
            g.DrawLine(death ? deathPen : spawnPen, x, plot.Top, x, plot.Bottom);

            var label = L10n.Get(LangScope.Screen, death ? "flylog.event.death" : "flylog.event.spawn");
            var tw = g.MeasureString(label, font).Width;
            // 标签夹在绘图区内：事件落在最右端时文字不会溢到统计卡上
            var labelX = Math.Clamp(x - tw / 2, plot.Left, Math.Max(plot.Left, plot.Right - tw));
            g.DrawString(label, font, death ? deathText : spawnText,
                labelX, plot.Bottom - 16 * s, StringFormat.GenericTypographic);
        }
    }

    // ------------------------------------------------------------------ 统计卡

    private void DrawStats(System.Drawing.Graphics g, float s, HudConfig cfg, FlightLogChartTheme theme,
        FlightLogBridge.FlightLogFlight flight, FlightLogBridge.FlightLogSeries series, RectangleF card)
    {
        using var cardBg = new SolidBrush(Color.FromArgb(70, 8, 26, 14));
        using var border = new Pen(MfdTheme.Divider, 1f * s);
        g.FillRectangle(cardBg, card);
        g.DrawRectangle(border, card.X, card.Y, card.Width, card.Height);

        var rows = theme.Stats(cfg, flight, _result.Events);
        using var title = MfdTheme.CjkBoldAt(19f * s);
        using var label = MfdTheme.CjkAt(16f * s);
        using var value = MfdTheme.MonoBoldAt(17f * s);
        using var accent = new SolidBrush(MfdTheme.Green);
        using var ink = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var left = card.Left + 12 * s;
        var innerW = card.Width - 24 * s;
        g.DrawString(L10n.Get(LangScope.Screen, theme.TitleKey), title, accent, left, card.Top + 10 * s,
                     StringFormat.GenericTypographic);
        using (var sep = new Pen(MfdTheme.Divider, 1f * s))
            g.DrawLine(sep, left, card.Top + 36 * s, card.Right - 12 * s, card.Top + 36 * s);

        // 行距自适应：统计行多（最多 6 行）时压缩，保证不越过卡片底部
        var top = card.Top + 48 * s;
        var available = card.Height - 48 * s - 46 * s;       // 底部留架次说明
        var step = rows.Count > 0
            ? Math.Min(38 * s, available / rows.Count)
            : 38 * s;

        var y = top;
        foreach (var row in rows)
        {
            if (y + step > card.Bottom - 40 * s) break;
            g.DrawString(L10n.Get(LangScope.Screen, row.LabelKey), label, muted, left, y,
                         StringFormat.GenericTypographic);
            var vw = g.MeasureString(row.Value, value).Width;
            g.DrawString(row.Value, value, ink, card.Right - 12 * s - vw, y - 1 * s,
                         StringFormat.GenericTypographic);
            y += step;
        }

        // 卡片底部：架次 / 机型 / 样本数（回答「这页是哪一段」）
        var footer = string.Format(CultureInfo.InvariantCulture,
            L10n.Get(LangScope.Screen, "flylog.flightInfo"),
            Math.Clamp(_flightIndex, 0, Math.Max(0, FlightCount - 1)) + 1,
            Math.Max(1, FlightCount), series.T?.Length ?? 0);
        using var footerFont = MfdTheme.MonoAt(14f * s);
        g.DrawString(footer, footerFont, muted, left, card.Bottom - 30 * s, StringFormat.GenericTypographic);
        if (!string.IsNullOrWhiteSpace(flight.Vehicle))
        {
            var vw = g.MeasureString(flight.Vehicle, footerFont).Width;
            g.DrawString(flight.Vehicle, footerFont, muted, card.Right - 12 * s - vw, card.Bottom - 30 * s,
                         StringFormat.GenericTypographic);
        }
    }

    // ------------------------------------------------------------------ 小工具

    private static Color PaletteColor(int palette) => palette switch
    {
        1 => MfdTheme.Amber,
        2 => MfdTheme.Muted,
        _ => MfdTheme.Green,
    };

    /// <summary>时间刻度文本 mm:ss（自架次起点起算）。</summary>
    private static string ClockText(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var total = (int)Math.Round(seconds);
        return (total / 60).ToString(CultureInfo.InvariantCulture) + ":"
               + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string Truncate(System.Drawing.Graphics g, Font font, string text, float maxWidth)
    {
        if (g.MeasureString(text, font).Width <= maxWidth) return text;
        var cut = text;
        while (cut.Length > 4 && g.MeasureString(cut + "…", font).Width > maxWidth)
            cut = cut[..^1];
        return cut + "…";
    }

    private static void DrawCentered(System.Drawing.Graphics g, string text, RectangleF rect, float fontSize)
    {
        using var font = MfdTheme.CjkAt(fontSize);
        using var brush = new SolidBrush(MfdTheme.Muted);
        using var centre = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, brush, rect, centre);
    }
}
