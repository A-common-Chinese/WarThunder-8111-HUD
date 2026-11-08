// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI;

namespace WtHud.Hud;

/// <summary>
/// Which dataset the settings panel is currently editing: the global config, or one airframe's
/// overrides. Detection alone never switches the scope — only an explicit pick does.
/// </summary>
public static class EditScope
{
    /// <summary>null = global config; otherwise the airframe whose overrides are edited.</summary>
    public static string? Target { get; set; }

    /// <summary>Airframe the HUD detected in the current match (informational only).</summary>
    public static string? DetectedVehicle => AirframeDb.CurrentVehicle;

    public static bool IsGlobal => string.IsNullOrEmpty(Target);

    public static void UseGlobal() => Target = null;

    public static void UseVehicle(string vehicle) =>
        Target = string.IsNullOrWhiteSpace(vehicle) ? null : vehicle;

    public static string HeaderText => IsGlobal
        ? L10n.Get(LangScope.Screen, "scope.global")
        : L10n.Get(LangScope.Screen, "scope.vehicle") + Target;
}
