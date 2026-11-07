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

    /// <summary>
    /// Native pitch sign: 0 = auto detect from flight data, +1 / -1 = force it. Some aircraft
    /// (F/A-18E, MiG-19) report the horizon pitch inverted.
    /// </summary>
    [JsonPropertyName("pitchSign")] public int PitchSign { get; set; }

    /// <summary>Per-vehicle native pitch sign learned automatically (vehicle id -> +/-1).</summary>
    [JsonPropertyName("pitchSignByVehicle")] public System.Collections.Generic.Dictionary<string, int> PitchSignByVehicle { get; set; } = new();

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

    /// <summary>Settings dialog key.</summary>
    [JsonPropertyName("settingsKey")] public string SettingsKey { get; set; } = "F1";
    /// <summary>Pitch-zero calibration key.</summary>
    [JsonPropertyName("pitchZeroKey")] public string PitchZeroKey { get; set; } = "F7";
    /// <summary>Bank-flip key.</summary>
    [JsonPropertyName("bankFlipKey")] public string BankFlipKey { get; set; } = "F8";
    /// <summary>Body-fixed mode key.</summary>
    [JsonPropertyName("bodyFixedKey")] public string BodyFixedKey { get; set; } = "F9";
    /// <summary>Show / hide HUD key.</summary>
    [JsonPropertyName("showKey")] public string ShowKey { get; set; } = "F10";
    /// <summary>Exit key (press twice within 0.7 s).</summary>
    [JsonPropertyName("exitKey")] public string ExitKey { get; set; } = "F11";
    /// <summary>Save config key.</summary>
    [JsonPropertyName("saveKey")] public string SaveKey { get; set; } = "CTRL+F12";   // plain F12 is taken by Steam screenshots
    /// <summary>Cursor-hole cycle key.</summary>
    [JsonPropertyName("cursorHoleKey")] public string CursorHoleKey { get; set; } = "CTRL+ALT+H";

    /// <summary>Custom key: reset the internal view / map state (default F5).</summary>
    [JsonPropertyName("resetKey")] public string ResetKey { get; set; } = "F5";

    /// <summary>Long-press this key to hide the HUD (default U).</summary>
    [JsonPropertyName("holdHideKey")] public string HoldHideKey { get; set; } = "U";

    /// <summary>Height tape shows radar (above-ground) altitude instead of barometric.</summary>
    [JsonPropertyName("tapeRadarAltitude")] public bool TapeRadarAltitude { get; set; } = true;

    /// <summary>Flip the map Y axis when converting to bearings/grid (depends on the map).</summary>
    [JsonPropertyName("mapYFlip")] public bool MapYFlip { get; set; } = true;   // map image Y grows southwards

    /// <summary>Warn when closer than this fraction of the map size to the map edge.</summary>
    [JsonPropertyName("edgeMarginFraction")] public double EdgeMarginFraction { get; set; } = 0.06;

    /// <summary>Hide the whole HUD while the aircraft sits on the ground.</summary>
    [JsonPropertyName("hideOnGround")] public bool HideOnGround { get; set; } = true;

    /// <summary>Delay (ms) before the HUD hides after the aircraft stops on the ground.</summary>
    [JsonPropertyName("groundHideDelayMs")] public double GroundHideDelayMs { get; set; } = 500;

    /// <summary>Below this IAS (km/h) the aircraft counts as stopped for the ground hide.</summary>
    [JsonPropertyName("groundHideSpeedKmh")] public double GroundHideSpeedKmh { get; set; } = 5;

    /// <summary>Blink the enemy-airfield readout when closer than this many km.</summary>
    [JsonPropertyName("enemyAirfieldAlertKm")] public double EnemyAirfieldAlertKm { get; set; } = 12;

    /// <summary>
    /// Airbrake key. Used by the A-10 family only: their split-aileron (deceleron) brakes make the
    /// reported percentage follow the ailerons, so the box follows the key instead.
    /// </summary>
    [JsonPropertyName("airbrakeKey")] public string AirbrakeKey { get; set; } = "H";

    /// <summary>Stall warning key: records the current AOA as this vehicle''s stall AOA.</summary>
    [JsonPropertyName("stallKey")] public string StallKey { get; set; } = "CTRL+ALT+S";

    /// <summary>Generic stall AOA used before a per-vehicle calibration exists (deg).</summary>
    [JsonPropertyName("stallAoaDeg")] public double StallAoaDeg { get; set; } = 22;

    /// <summary>Per-vehicle calibrated stall AOA, keyed by the API vehicle id.</summary>
    [JsonPropertyName("stallAoaByVehicle")] public System.Collections.Generic.Dictionary<string, double> StallAoaByVehicle { get; set; } = new();

    /// <summary>AOA above this (deg) blinks the AOA warning.</summary>
    [JsonPropertyName("aoaWarnDeg")] public double AoaWarnDeg { get; set; } = 18;

    /// <summary>Endurance below this (seconds) blinks the BINGO fuel warning.</summary>
    [JsonPropertyName("bingoFuelSeconds")] public double BingoFuelSeconds { get; set; } = 180;

    /// <summary>Sign of the lateral flight-path offset: -1 mirrors the AoS-based marker.</summary>
    [JsonPropertyName("fpmAosSign")] public int FpmAosSign { get; set; } = -1;

    /// <summary>Show the pitch ladder.</summary>
    [JsonPropertyName("showPitchLadder")] public bool ShowPitchLadder { get; set; } = true;

    /// <summary>Show the flight path marker (velocity vector).</summary>
    [JsonPropertyName("showFpm")] public bool ShowFpm { get; set; } = true;

    /// <summary>Hold the right mouse button to enter the aiming layout.</summary>
    [JsonPropertyName("aimOnRightMouse")] public bool AimOnRightMouse { get; set; } = true;

    /// <summary>
    /// Multiplies the measured fuel flow before computing endurance. The game shows its own
    /// FM-table estimate, which runs ~1.25-1.3x higher than the measured mass flow at altitude;
    /// set this to match the in-game readout.
    /// </summary>
    [JsonPropertyName("enduranceScale")] public double EnduranceScale { get; set; } = 1.00;

    /// <summary>
    /// Half-size (px) of the transparent hole at screen centre that lets the game draw its cursor.
    /// Smaller = less HUD occlusion near the boresight; 0 disables the hole.
    /// </summary>
    [JsonPropertyName("cursorHoleHalfPx")] public int CursorHoleHalfPx { get; set; } = 16;   // 32 px hole: hides the game cursor, keeps the FPM ring visible

    /// <summary>Use Mach instead of IAS for the manual overspeed warning.</summary>
    [JsonPropertyName("overspeedByMach")] public bool OverspeedByMach { get; set; }

    /// <summary>Mach number above which the speed readout turns red (manual mode).</summary>
    [JsonPropertyName("overspeedMach")] public double OverspeedMach { get; set; } = 1.80;

    /// <summary>IAS above which the speed readout turns red (km/h). No Vne in the API.</summary>
    [JsonPropertyName("overspeedKmh")] public double OverspeedKmh { get; set; } = 1400;

    /// <summary>Hide the HUD while the cockpit view is active.</summary>
    [JsonPropertyName("hideInCockpit")] public bool HideInCockpit { get; set; } = true;

    /// <summary>Hide the HUD in the nose / gunner view (V twice). Off by default.</summary>
    [JsonPropertyName("hideInNose")] public bool HideInNose { get; set; }

    /// <summary>Hide the HUD without quitting (same as F10).</summary>
    [JsonPropertyName("hudVisible")] public bool HudVisible { get; set; } = true;

    /// <summary>Hide the overlay while another window is focused (default true).</summary>
    [JsonPropertyName("hideWhenInactive")] public bool HideWhenInactive { get; set; } = true;

    /// <summary>Show raw gear/flaps/airbrake values and flags for diagnosis.</summary>
    [JsonPropertyName("configDebug")] public bool ConfigDebug { get; set; }

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

    public void Save() => SaveTo(PathFor());

    /// <summary>Writes the config to an explicit path (used by the standalone scratch panel).</summary>
    public void SaveTo(string path)
    {
        try
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public HudConfig Clone() => (HudConfig)MemberwiseClone();

    /// <summary>Copies every writable property from <paramref name="other"/> (used by CANCEL).</summary>
    public void CopyFrom(HudConfig other)
    {
        foreach (var p in typeof(HudConfig).GetProperties())
        {
            if (!p.CanRead || !p.CanWrite) continue;
            p.SetValue(this, p.GetValue(other));
        }
    }

    // ---------------------------------------------------------------- language

    /// <summary>
    /// Language of the three surfaces: panel = silk-screen on the bezel, screen = MFD text,
    /// alerts = texts drawn on the HUD. Defaults: panel/alerts English, screen Chinese.
    /// </summary>
    [JsonPropertyName("language")] public LanguageConfig Language { get; set; } = new();

    /// <summary>MFD panel appearance and input behaviour.</summary>
    [JsonPropertyName("mfd")] public MfdSettings Mfd { get; set; } = new();
}

/// <summary>Persisted language selection (one language per surface).</summary>
public sealed class LanguageConfig
{
    [JsonPropertyName("followSystem")] public bool FollowSystem { get; set; }
    [JsonPropertyName("panel")] public string Panel { get; set; } = "en";
    [JsonPropertyName("screen")] public string Screen { get; set; } = "zh";
    [JsonPropertyName("alerts")] public string Alerts { get; set; } = "en";

}

/// <summary>MFD panel appearance/behaviour settings.</summary>
public sealed class MfdSettings
{
    /// <summary>Normal text color preset: white / green / purple / blue / red.</summary>
    public string InkColor { get; set; } = "white";

    /// <summary>Highlight (accent) color preset: white / green / purple / blue / red.</summary>
    public string AccentColor { get; set; } = "green";

    /// <summary>Realistic mode: screen rows can only be driven by the physical keys.</summary>
    public bool RealisticKeys { get; set; }
}
