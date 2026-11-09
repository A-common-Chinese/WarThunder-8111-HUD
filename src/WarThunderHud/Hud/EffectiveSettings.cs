// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

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
}
