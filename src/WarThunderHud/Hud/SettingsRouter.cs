// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// Read/write router for settings that support a per-airframe override.
///
/// Priority: airframe override (only while <see cref="EditScope.Target"/> is that airframe)
///           → global config value.
/// Writes go to whichever scope the panel is currently editing, so "change once for all" and
/// "change only this jet" use the same row.
/// </summary>
public static class SettingsRouter
{
    /// <summary>Effective value for display: override of the edited/detected airframe, else global.</summary>
    public static double Get(HudConfig cfg, string name, double globalValue)
    {
        var vehicle = EditScope.Target;
        if (string.IsNullOrEmpty(vehicle)) return globalValue;
        return cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map) && map.TryGetValue(name, out var v)
            ? v
            : globalValue;
    }

    public static int GetInt(HudConfig cfg, string name, int globalValue) =>
        (int)Math.Round(Get(cfg, name, globalValue));

    /// <summary>Routes a write to the global config or to the edited airframe's override map.</summary>
    public static void Set(HudConfig cfg, string name, double value)
    {
        var vehicle = EditScope.Target;
        if (string.IsNullOrEmpty(vehicle)) return;   // caller writes the global field itself
        if (!cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map))
        {
            map = new Dictionary<string, double>();
            cfg.PerVehicleOverrides[vehicle] = map;
        }
        map[name] = value;
    }

    /// <summary>True when the edited airframe overrides this setting (drives the [V] badge).</summary>
    public static bool HasOverride(HudConfig cfg, string name)
    {
        var vehicle = EditScope.Target;
        return !string.IsNullOrEmpty(vehicle)
               && cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map)
               && map.ContainsKey(name);
    }

    /// <summary>RESET in airframe scope: drop every override of the edited airframe.</summary>
    public static void ClearVehicleOverrides(HudConfig cfg)
    {
        var vehicle = EditScope.Target;
        if (!string.IsNullOrEmpty(vehicle)) cfg.PerVehicleOverrides.Remove(vehicle);
    }
}
