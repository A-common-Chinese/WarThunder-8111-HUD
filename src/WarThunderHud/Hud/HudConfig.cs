// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WtHud.Hud;

/// <summary>
/// User-tunable HUD calibration, persisted next to the executable.
/// Everything here is meant to be dialed in live with the hotkeys and then saved (F12),
/// because the game exposes no camera FOV and its attitude fields do not share our datum.
/// </summary>
public sealed class HudConfig
{
    [JsonPropertyName("verticalFovDeg")] public double VerticalFovDeg { get; set; } = 45.0;
    [JsonPropertyName("layoutOffsetYDeg")] public double LayoutOffsetYDeg { get; set; } = 1.8;

    /// <summary>Added to the displayed pitch; F7 sets it so the ladder reads 0 while level.</summary>
    [JsonPropertyName("pitchZeroDeg")] public double PitchZeroDeg { get; set; }

    /// <summary>+1 or -1; flips the bank direction if the ladder rotates the wrong way.</summary>
    [JsonPropertyName("bankSign")] public int BankSign { get; set; } = 1;

    /// <summary>Small bank trim in degrees.</summary>
    [JsonPropertyName("bankTrimDeg")] public double BankTrimDeg { get; set; }

    /// <summary>
    /// Extrapolate speed/altitude between samples to hide the 17-25 Hz update cap of the
    /// local API. Attitude is never extrapolated - prediction overshoot on the ladder was
    /// worse than the raw stepping.
    /// </summary>
    [JsonPropertyName("extrapolate")] public bool Extrapolate { get; set; }

    // ---- key bindings (passthrough polling, the game still gets every key) ----
    [JsonPropertyName("viewKey")] public string ViewKey { get; set; } = "V";
    [JsonPropertyName("zoomKey")] public string ZoomKey { get; set; } = "Z";
    [JsonPropertyName("mapKey")] public string MapKey { get; set; } = "M";
    [JsonPropertyName("scoreboardKey")] public string ScoreboardKey { get; set; } = "TAB";

    /// <summary>
    /// Multiplies the measured fuel flow before computing endurance. The game shows its own
    /// FM-table estimate, which runs ~1.25-1.3x higher than the measured mass flow at altitude;
    /// set this to match the in-game readout.
    /// </summary>
    [JsonPropertyName("enduranceScale")] public double EnduranceScale { get; set; } = 1.30;

    /// <summary>
    /// Half-size (px) of the transparent hole at screen centre that lets the game draw its cursor.
    /// Smaller = less HUD occlusion near the boresight; 0 disables the hole.
    /// </summary>
    [JsonPropertyName("cursorHoleHalfPx")] public int CursorHoleHalfPx { get; set; } = 16;   // 32 px hole: hides the game cursor, keeps the FPM ring visible

    /// <summary>IAS above which the speed readout turns red (km/h). No Vne in the API.</summary>
    [JsonPropertyName("overspeedKmh")] public double OverspeedKmh { get; set; } = 1400;

    /// <summary>Hide the HUD while the cockpit view is active.</summary>
    [JsonPropertyName("hideInCockpit")] public bool HideInCockpit { get; set; } = true;

    /// <summary>Hide the HUD in the nose / gunner view (V twice). Off by default.</summary>
    [JsonPropertyName("hideInNose")] public bool HideInNose { get; set; }

    /// <summary>Hide the HUD without quitting (same as F10).</summary>
    [JsonPropertyName("hudVisible")] public bool HudVisible { get; set; } = true;

    /// <summary>How much of the aircraft roll the camera performs (0 = horizon-stabilised chase cam).</summary>
    [JsonPropertyName("rollCoupling")] public double RollCoupling { get; set; }

    /// <summary>Extra vertical trim for the flight path marker, degrees.</summary>
    [JsonPropertyName("fpmTrimDeg")] public double FpmTrimDeg { get; set; }

    private static string PathFor() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "hud-config.json");

    public static HudConfig Load()
    {
        try
        {
            var path = PathFor();
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path));
                if (cfg != null) return cfg;
            }
        }
        catch { }
        return new HudConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(PathFor(),
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public HudConfig Clone() => (HudConfig)MemberwiseClone();
}









