// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI;

namespace WtHud.Hud;

/// <summary>
/// Which layer the settings panel is editing.
/// <list type="bullet">
/// <item><b>Auto</b> — nothing picked yet: show/write the effective layer (detected airframe override, else global).</item>
/// <item><b>Explicit global</b> — the user picked「全局」: always the global config, never the detected airframe.</item>
/// <item><b>Explicit airframe</b> — the user picked one airframe: that airframe's overrides only.</item>
/// </list>
/// </summary>
public static class EditScope
{
    /// <summary>null = no explicit airframe picked (auto, or explicit global).</summary>
    public static string? Target { get; private set; }

    /// <summary>True after the user picked「全局」explicitly.</summary>
    public static bool ExplicitGlobal { get; private set; }

    public static string? DetectedVehicle => AirframeDb.CurrentVehicle;

    public static bool IsAuto => Target is null && !ExplicitGlobal;

    /// <summary>Vehicle whose overrides are visible: explicit pick, else detected (auto only).</summary>
    public static string? ResolvedVehicle => Target ?? (ExplicitGlobal ? null : DetectedVehicle);

    /// <summary>True when writes go to the global config.</summary>
    public static bool IsGlobal => Target is null;

    public static void UseAuto()
    {
        Target = null;
        ExplicitGlobal = false;
    }

    public static void UseGlobal()
    {
        Target = null;
        ExplicitGlobal = true;
    }

    public static void UseVehicle(string vehicle)
    {
        Target = string.IsNullOrWhiteSpace(vehicle) ? null : vehicle;
        ExplicitGlobal = Target is null;
    }

    public static string HeaderText
    {
        get
        {
            if (Target is { } explicitVehicle)
                return L10n.Get(LangScope.Screen, "scope.vehicle") + explicitVehicle;
            if (ExplicitGlobal)
                return L10n.Get(LangScope.Screen, "scope.global");
            var detected = DetectedVehicle;
            return string.IsNullOrEmpty(detected)
                ? L10n.Get(LangScope.Screen, "scope.global")
                : L10n.Get(LangScope.Screen, "scope.vehicle") + detected;
        }
    }
}
