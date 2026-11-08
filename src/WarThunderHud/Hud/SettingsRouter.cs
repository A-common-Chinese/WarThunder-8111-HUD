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
    /// <summary>Vehicle whose overrides are visible: the edited one, else the detected one.</summary>
    private static string? ResolveVehicle() => EditScope.Target ?? EditScope.DetectedVehicle;

    public static double Get(HudConfig cfg, string name, double globalValue)
    {
        var vehicle = ResolveVehicle();
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
        // 自动状态：生效值来自机型覆盖就继续写机型，否则写全局
        var vehicle = ResolveVehicle();
        if (string.IsNullOrEmpty(vehicle)) return;   // caller writes the global field itself
        if (!cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map))
        {
            map = new Dictionary<string, double>();
            cfg.PerVehicleOverrides[vehicle] = map;
        }
        map[name] = value;
    }

    // ---- explicit-vehicle variants (pitch sign detection and other per-airframe writers) ----

    public static bool GetFor(HudConfig cfg, string vehicle, string name, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(vehicle)) return false;
        if (!cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map)) return false;
        if (!map.TryGetValue(name, out var raw)) return false;
        value = (int)Math.Round(raw);
        return true;
    }

    public static void SetFor(HudConfig cfg, string vehicle, string name, double value)
    {
        if (string.IsNullOrEmpty(vehicle)) return;
        if (!cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map))
        {
            map = new Dictionary<string, double>();
            cfg.PerVehicleOverrides[vehicle] = map;
        }
        map[name] = value;
    }

    public static bool HasFor(HudConfig cfg, string vehicle, string name) =>
        !string.IsNullOrEmpty(vehicle)
        && cfg.PerVehicleOverrides.TryGetValue(vehicle, out var map)
        && map.ContainsKey(name);

    /// <summary>True when the edited airframe overrides this setting (drives the [V] badge).</summary>
    public static bool HasOverride(HudConfig cfg, string name)
    {
        var vehicle = ResolveVehicle();
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
