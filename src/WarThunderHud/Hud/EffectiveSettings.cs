// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>某个数值当前来自哪一层（详情页/诊断显示用）。</summary>
public enum ValueSource
{
    /// <summary>三层都没有非零值。</summary>
    None,
    /// <summary>机型覆盖（perVehicleOverrides），只影响这一台。</summary>
    Override,
    /// <summary>机型档案（airframes.json）。</summary>
    Profile,
    /// <summary>全局配置，影响所有没覆盖的机型。</summary>
    Global,
}

/// <summary>
/// 运行时生效值：机型覆盖（perVehicleOverrides）→ 机型档案（airframes.json）→ 全局配置。
///
/// 面板里「按机型」改的值必须在这里生效，否则 HUD 实际用的是旧口径，
/// 而面板只是显示了一组没接线的数字（v0.3.1 的 bug：7 项参数只在面板里存在）。
/// </summary>
public static class EffectiveSettings
{
    public static double PitchZeroDeg(HudConfig cfg, string? vehicle) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.pitchzero", cfg.PitchZeroDeg);

    public static double BankTrimDeg(HudConfig cfg, string? vehicle) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.bankTrim", cfg.BankTrimDeg);

    public static double FpmTrimDeg(HudConfig cfg, string? vehicle) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.fpmTrim", cfg.FpmTrimDeg);

    public static double AoaWarnDeg(HudConfig cfg, string? vehicle, AirframeProfile profile) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.aoaWarn",
            profile.AoaWarn > 0 ? profile.AoaWarn : cfg.AoaWarnDeg);

    public static double OverspeedKmh(HudConfig cfg, string? vehicle, AirframeProfile profile) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.overspeedIas",
            profile.OverspeedIas > 0 ? profile.OverspeedIas : cfg.OverspeedKmh);

    public static double OverspeedMach(HudConfig cfg, string? vehicle, AirframeProfile profile) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.overspeedMach",
            profile.OverspeedMach > 0 ? profile.OverspeedMach : cfg.OverspeedMach);

    public static double StallAoaDeg(HudConfig cfg, string? vehicle, AirframeProfile profile) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.stallAoa",
            profile.StallAoa > 0 ? profile.StallAoa : cfg.StallAoaDeg);

    /// <summary>下降率告警阈值（m/s，正值）：机型覆盖 > 机型档案 > 全局（0 时由调用方回退 40）。</summary>
    public static double SinkRateMs(HudConfig cfg, string? vehicle, AirframeProfile profile) =>
        SettingsRouter.GetForVehicle(cfg, vehicle, "row.sinkRate",
            profile.SinkRate > 0 ? profile.SinkRate : cfg.SinkRateMs);

    /// <summary>
    /// 解析「某一台机型」的生效值和来源：覆盖 &gt; 档案 &gt; 全局。
    /// 详情页必须按这个顺序显示，否则全局值被显示成「默认」，看起来像没生效。
    /// </summary>
    public static (double Value, ValueSource Source) Resolve(
        HudConfig cfg, string? vehicle, string key, double profileValue, double globalValue)
    {
        if (SettingsRouter.TryGetForVehicle(cfg, vehicle, key, out var overridden))
            return (overridden, ValueSource.Override);
        if (profileValue > 0)
            return (profileValue, ValueSource.Profile);
        if (globalValue > 0)
            return (globalValue, ValueSource.Global);
        return (0, ValueSource.None);
    }
}
