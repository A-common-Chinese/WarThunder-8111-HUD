// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.Hud;
using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>机型详情页：展示所选机型的能力汇总（雷达高度/迎角/极速/俯仰符号/失速迎角…）。</summary>
internal sealed class DetailPage : MfdPageBase
{
    private readonly Func<HudConfig> _cfg;
    private readonly Action _onBack;
    private readonly Action _onReset;

    // layout 目前用不到：Draw 直接取 MfdRenderContext 的 Glass/Pad（与迁移前一致）；
    // 参数保留是为了和其它二级页保持统一的构造形状，Form 侧接线时不用特判。
    public DetailPage(string vehicleId,
                      Func<HudConfig> cfg,
                      Action onBack,
                      Action onReset,
                      MfdPageLayout layout) : base("detail")
    {
        VehicleId = vehicleId;
        _cfg = cfg;
        _onBack = onBack;
        _onReset = onReset;
    }

    /// <summary>要展示的机型 id（空串 = 未能识别的机型）。</summary>
    public string VehicleId { get; }

    public override string TitleKey => "page.airframe";
    public override string HintKey => "hint.item";

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "reset":
                _onReset();      // 二次确认由 Form 弹 ConfirmPage，页面只负责请求
                return true;
            case "nav.back":
                _onBack();
                return true;
        }
        return false;
    }

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        var glass = ctx.Glass;
        var pad = ctx.Pad;

        var zh = L10n.Screen == Lang.Zh;
        var id = VehicleId;
        var cfg = _cfg();
        var prof = WtHud.Hud.AirframeDb.Get(id);

        using var head = MfdTheme.CjkAt(24f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var cjk = MfdTheme.CjkAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var left = glass.Left + pad;
        var y = glass.Top + 16 * s;
        g.DrawString(id, head, accent, left, y, StringFormat.GenericTypographic);

        y += 38 * s;
        void Row(string label, string value)
        {
            g.DrawString(label, cjk, muted, left, y, StringFormat.GenericTypographic);
            g.DrawString(value, body, text, left + 200 * s, y, StringFormat.GenericTypographic);
            y += 28 * s;
        }

        Row(L10n.Get(LangScope.Screen, "airframe.ra"), prof.RadarAltitude ? (zh ? "支持" : "YES") : (zh ? "不支持" : "NO"));
        Row("PULL UP", prof.PullUp ? (zh ? "支持" : "YES") : (zh ? "不支持" : "NO"));

        // 这里必须按 *本页机型* 取值（不能用 Scoped.Get：它跟着面板当前编辑的机型走）。
        // 显示「生效值 + 来源」：机型覆盖 > 机型档案 > 全局；否则全局值会显示成「默认」，
        // 看起来像按机型改了却没生效。
        void ScopedRow(string label, string key, double profileValue, double globalValue,
                       string format, string unit, string fallback)
        {
            var (value, source) = WtHud.Hud.EffectiveSettings.Resolve(cfg, id, key, profileValue, globalValue);
            if (source == WtHud.Hud.ValueSource.None)
            {
                Row(label, fallback);
                return;
            }
            var tag = source switch
            {
                WtHud.Hud.ValueSource.Override => zh ? "机型" : "AIRFRAME",
                WtHud.Hud.ValueSource.Profile => zh ? "档案" : "PROFILE",
                _ => zh ? "全局" : "GLOBAL",
            };
            Row(label, $"{value.ToString(format)}{unit}（{tag}）");
        }

        ScopedRow(L10n.Get(LangScope.Screen, "airframe.aoa"), "row.aoaWarn",
            prof.AoaWarn, cfg.AoaWarnDeg, "F0", "°", zh ? "默认" : "DEFAULT");
        ScopedRow("IAS 极速", "row.overspeedIas",
            prof.OverspeedIas, cfg.OverspeedKmh, "F0", " km/h", zh ? "默认" : "DEFAULT");
        ScopedRow("MACH", "row.overspeedMach",
            prof.OverspeedMach, cfg.OverspeedMach, "F2", "", zh ? "默认" : "DEFAULT");
        Row(L10n.Get(LangScope.Screen, "airframe.sink"), prof.SinkRate > 0 ? $"{prof.SinkRate:F0} m/s" : (zh ? "默认" : "DEFAULT"));
        Row(L10n.Get(LangScope.Screen, "airframe.att"), prof.Attitude == "game" ? "AHRS GAME" : "AHRS EST");
        Row(L10n.Get(LangScope.Screen, "airframe.ab"), prof.Airbrake == "key" ? (zh ? "按键读取" : "KEY") : (zh ? "遥测读取" : "TELEMETRY"));
        var shownSign = WtHud.Hud.SettingsRouter.GetFor(cfg, id, "row.pitchSign", out var storedSign) ? storedSign : cfg.PitchSign;
        Row(L10n.Get(LangScope.Screen, "airframe.pitchSign"),
            shownSign == 0 ? (zh ? "自动" : "AUTO") : (shownSign > 0 ? "+1" : "-1"));
        ScopedRow(L10n.Get(LangScope.Screen, "airframe.stall"), "row.stallAoa",
            prof.StallAoa, cfg.StallAoaDeg, "F1", "°", zh ? "未校准" : "DEFAULT");

        y += 8 * s;
        foreach (var line in PageText.Wrap(g, cjk, L10n.Get(LangScope.Screen, "airframe.note") + ": " + prof.Note,
                     glass.Width - pad * 2))
        {
            g.DrawString(line, cjk, muted, left, y, StringFormat.GenericTypographic);
            y += 26 * s;
        }
    }
}
