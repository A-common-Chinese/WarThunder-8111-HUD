// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WtHud.Hud;

/// <summary>Per-aircraft capabilities that differ between airframes.</summary>
public sealed class AirframeProfile
{
    /// <summary>Radar altitude available (enables PULL UP instead of SINK RATE).</summary>
    [JsonPropertyName("radarAltitude")] public bool RadarAltitude { get; set; }

    /// <summary>Attitude source: "game" (native AHRS) or "est" (synthesised).</summary>
    [JsonPropertyName("attitude")] public string Attitude { get; set; } = "est";

    /// <summary>Airbrake source: "telemetry" or "key" (deceleron aircraft such as the A-10).</summary>
    [JsonPropertyName("airbrake")] public string Airbrake { get; set; } = "telemetry";

    /// <summary>Calibrated stall angle of attack for this airframe (0 = use default).</summary>
    [JsonPropertyName("stallAoa")] public double StallAoa { get; set; }

    /// <summary>Terrain alert upgrades to PULL UP (needs radar altitude).</summary>
    [JsonPropertyName("pullUp")] public bool PullUp { get; set; }

    /// <summary>Per-aircraft AoA warning angle (0 = use the global default).</summary>
    [JsonPropertyName("aoaWarn")] public double AoaWarn { get; set; }

    /// <summary>Per-aircraft overspeed IAS limit in km/h (0 = global).</summary>
    [JsonPropertyName("overspeedIas")] public double OverspeedIas { get; set; }

    /// <summary>Per-aircraft overspeed Mach limit (0 = global).</summary>
    [JsonPropertyName("overspeedMach")] public double OverspeedMach { get; set; }

    /// <summary>Per-aircraft sink-rate alert threshold in m/s (0 = global).</summary>
    [JsonPropertyName("sinkRate")] public double SinkRate { get; set; }

    /// <summary>Per-aircraft PULL UP lead time in seconds, TTI below it triggers (0 = global).</summary>
    [JsonPropertyName("pullUpTti")] public double PullUpTti { get; set; }

    /// <summary>Free-form note shown in the panel.</summary>
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

/// <summary>
/// External per-aircraft database (<c>airframes.json</c> next to the exe). New airframes are
/// created automatically the first time the player flies them.
/// </summary>
public static class AirframeDb
{
    private static Dictionary<string, AirframeProfile> _map = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Airframe currently flown (set by the overlay loop), empty when unknown.</summary>
    public static string CurrentVehicle { get; set; } = "";

    /// <summary>Airframe picked by hand in the panel when nothing was detected yet.</summary>
    public static string ManualVehicle { get; set; } = "";

    /// <summary>Vehicle the panel should write to: detected first, then the manual pick.</summary>
    public static string EffectiveVehicle =>
        !string.IsNullOrWhiteSpace(CurrentVehicle) ? CurrentVehicle : ManualVehicle;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "airframes.json");

    public static IReadOnlyDictionary<string, AirframeProfile> All
    {
        get { EnsureLoaded(); return _map; }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, AirframeProfile>>(
                    File.ReadAllText(FilePath), Opts);
                if (data != null) _map = new Dictionary<string, AirframeProfile>(data, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Seed();
                Save();
            }
        }
        catch
        {
            _map = new Dictionary<string, AirframeProfile>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Known airframes that ship with the tool (regenerated only when the file is missing).</summary>
    private static void Seed()
    {
        void Add(string id, bool ra, string att, string ab, double stall, string note) =>
            _map[id] = new AirframeProfile
            {
                RadarAltitude = ra, Attitude = att, Airbrake = ab, StallAoa = stall, Note = note,
            };

        Add("fa_18e_block_2", true, "est", "telemetry", 0, "雷达高度可用；无原生姿态");
        Add("a_10a", true, "game", "key", 0, "减速板为分裂式副翼，按按键读取");
        Add("a_10c", true, "game", "key", 0, "减速板为分裂式副翼，按按键读取");
        Add("j_7e", false, "est", "telemetry", 6.1, "记录临界迎角 6.1°");
        Add("j_16", false, "est", "telemetry", 0, "俯仰符号记录为 -1");
        Add("su_33", true, "est", "telemetry", 6.9, "记录临界迎角 6.9°");
        Add("rafale_c_f3", false, "est", "telemetry", 22.5, "无航向数据，坡度用滚转率积分");
        Add("mirage_2000c_s5", false, "est", "telemetry", 0, "俯仰符号记录为 -1");
        Add("kfir_c10_colombia", false, "est", "telemetry", 0, "俯仰符号记录为 -1");
        Add("mig_23ml", false, "est", "telemetry", 0, "俯仰符号记录为 -1");
        Add("mig_29m_9_15", false, "est", "telemetry", 0, "俯仰符号记录为 -1");
    }

    /// <summary>Creates a new entry for an unknown airframe and persists the file.</summary>
    public static AirframeProfile Ensure(string vehicleType, bool radarAltitude, string attitude, string airbrake)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(vehicleType)) return new AirframeProfile();
        if (_map.TryGetValue(vehicleType, out var existing)) return existing;

        var profile = new AirframeProfile
        {
            RadarAltitude = radarAltitude,
            Attitude = attitude,
            Airbrake = airbrake,
            Note = "自动创建：可在 airframes.json 中补充",
        };
        _map[vehicleType] = profile;
        Save();
        return profile;
    }


    /// <summary>
    /// Refreshes the auto-detected capabilities of one airframe (called when a match starts, so
    /// game updates that change the exposed indicators are picked up). User-edited fields
    /// (stall angle, note) are preserved.
    /// </summary>
    public static AirframeProfile Refresh(string vehicleType, bool radarAltitude, bool hasNativeAttitude,
        string airbrake, double stallAoaFromDetection = 0)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(vehicleType)) return new AirframeProfile();

        var exists = _map.TryGetValue(vehicleType, out var profile);
        if (!exists || profile == null)
        {
            profile = new AirframeProfile { Note = "自动创建：可在 airframes.json 中补充" };
            _map[vehicleType] = profile;
        }

        var changed = !exists;
        var att = hasNativeAttitude ? "game" : "est";
        if (profile.Attitude != att) { profile.Attitude = att; changed = true; }
        if (profile.RadarAltitude != radarAltitude) { profile.RadarAltitude = radarAltitude; changed = true; }
        if (!string.Equals(profile.Airbrake, airbrake, StringComparison.OrdinalIgnoreCase))
        {
            profile.Airbrake = airbrake;
            changed = true;
        }
        profile.PullUp = radarAltitude;
        if (stallAoaFromDetection > 0 && profile.StallAoa <= 0)
        {
            profile.StallAoa = Math.Round(stallAoaFromDetection, 1);
            changed = true;
        }

        if (changed) Save();
        return profile;
    }

    public static AirframeProfile Get(string vehicleType)
    {
        EnsureLoaded();
        if (vehicleType != null && _map.TryGetValue(vehicleType, out var p)) return p;
        return new AirframeProfile();
    }

    /// <summary>
    /// 已废弃：旧版本把面板里改的全局告警阈值写进当前机型档案，形成与 perVehicleOverrides
    /// 并存的第二套「按机型」数据（也是「j16 覆盖 f4j」显示混乱的来源之一）。保留此说明，
    /// 逻辑已删除；按机型的值只走 HudConfig.PerVehicleOverrides，全局值不再改机型档案。
    /// </summary>

    /// <summary>Clears the per-aircraft overrides so the global defaults apply again.</summary>
    public static void ResetOverrides(string vehicleType)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(vehicleType)) return;
        if (!_map.TryGetValue(vehicleType, out var p) || p == null) return;
        p.StallAoa = 0;
        p.AoaWarn = 0;
        p.OverspeedIas = 0;
        p.OverspeedMach = 0;
        p.SinkRate = 0;
        p.PullUpTti = 0;
        Save();
    }

    public static void Save()
    {
        try
        {
            var ordered = _map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(k => k.Key, v => v.Value);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(ordered, Opts));
        }
        catch { }
    }
}
