// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>Where a setting lives.</summary>
public enum SettingScope
{
    /// <summary>Only the global config; switches always use this.</summary>
    Global,

    /// <summary>Global value plus an optional per-airframe override.</summary>
    Airframe,
}

/// <summary>
/// Single source of truth for "may this parameter differ per airframe?".
/// Anything not registered here is Global, so a new row can never silently become per-airframe.
/// </summary>
public static class SettingScopes
{
    private static readonly Dictionary<string, (SettingScope Scope, string Why)> Map = new()
    {
        ["row.pitchzero"] = (SettingScope.Airframe, "座舱/装配基准，随机型"),
        ["row.bankTrim"] = (SettingScope.Airframe, "校准值"),
        ["row.fpmTrim"] = (SettingScope.Airframe, "校准值"),
        ["row.stallAoa"] = (SettingScope.Airframe, "气动特性"),
        ["row.aoaWarn"] = (SettingScope.Airframe, "气动特性"),
        ["row.overspeedIas"] = (SettingScope.Airframe, "结构限制"),
        ["row.overspeedMach"] = (SettingScope.Airframe, "结构限制"),
        ["row.sinkRate"] = (SettingScope.Airframe, "进近/结构限制"),
        ["row.pitchSign"] = (SettingScope.Airframe, "个别机型反向"),
    };

    public static SettingScope Of(string key) =>
        Map.TryGetValue(key, out var entry) ? entry.Scope : SettingScope.Global;

    public static bool IsAirframe(string key) => Of(key) == SettingScope.Airframe;

    /// <summary>For documentation generation / self-checks.</summary>
    public static IEnumerable<(string Key, SettingScope Scope, string Why)> All() =>
        Map.Select(kv => (kv.Key, kv.Value.Scope, kv.Value.Why));

    /// <summary>Every registered key (used by the startup self-check against the real rows).</summary>
    public static IReadOnlyCollection<string> Keys => Map.Keys;
}

/// <summary>
/// Row-level helpers: one call decides routing, so a settings row no longer contains the
/// "global or override" branch itself.
/// </summary>
public static class Scoped
{
    /// <summary>Value shown in the panel (effective value for airframe-scoped rows).</summary>
    public static double Get(HudConfig cfg, string key, double globalValue) =>
        SettingScopes.IsAirframe(key) ? SettingsRouter.Get(cfg, key, globalValue) : globalValue;

    public static int GetInt(HudConfig cfg, string key, int globalValue) =>
        (int)Math.Round(Get(cfg, key, globalValue));

    /// <summary>Routes a write; <paramref name="writeGlobal"/> performs the plain global assignment.</summary>
    public static void Set(HudConfig cfg, string key, double value, Action writeGlobal)
    {
        if (!SettingScopes.IsAirframe(key) || EditScope.ExplicitGlobal)
        {
            writeGlobal();
            return;
        }

        // 自动模式：有识别到的机型就写该机型的覆盖（不是全局！），否则才退回全局。
        // 之前用 EditScope.IsGlobal（Target==null）判断，导致「自动」永远写全局，
        // 必须手动选一次机型才会写覆盖。
        if (!string.IsNullOrEmpty(EditScope.ResolvedVehicle))
        {
            SettingsRouter.Set(cfg, key, value);
            return;
        }

        writeGlobal();
    }

    public static bool HasOverride(HudConfig cfg, string key) =>
        SettingScopes.IsAirframe(key) && SettingsRouter.HasOverride(cfg, key);
}
