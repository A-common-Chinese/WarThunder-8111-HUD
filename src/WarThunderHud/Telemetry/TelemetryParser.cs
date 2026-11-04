// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;
using System.Text.Json;

namespace WtHud.Telemetry;

/// <summary>
/// Converts the two raw 8111 payloads (/state and /indicators) into a TelemetrySnapshot.
/// Missing fields are tolerated on purpose: field availability varies per vehicle.
/// </summary>
public static class TelemetryParser
{
    public static TelemetrySnapshot Parse(string? stateJson, string? indicatorsJson, TimeSpan elapsed, DateTime utcNow)
    {
        double ias = 0, tas = 0, mach = 0, alt = 0, vy = 0, aoa = 0, aos = 0, ny = 1, wx = 0;
        double mfuel = 0, mfuel0 = 0, gearPct = 0, flapsPct = 0, airbrakePct = 0;
        bool stateValid = false;

        if (!string.IsNullOrWhiteSpace(stateJson))
        {
            using var doc = JsonDocument.Parse(stateJson);
            var r = doc.RootElement;
            stateValid = GetBool(r, "valid");
            ias = GetDouble(r, "IAS, km/h");
            tas = GetDouble(r, "TAS, km/h");
            mach = GetDouble(r, "M");
            alt = GetDouble(r, "H, m");
            vy = GetDouble(r, "Vy, m/s");
            aoa = GetDouble(r, "AoA, deg");
            aos = GetDouble(r, "AoS, deg");
            ny = GetDouble(r, "Ny", 1);
            wx = GetDouble(r, "Wx, deg/s");
            mfuel = GetDouble(r, "Mfuel, kg");
            mfuel0 = GetDouble(r, "Mfuel0, kg");
            gearPct = GetDouble(r, "gear, %", -1);
            flapsPct = GetDouble(r, "flaps, %", -1);
            airbrakePct = GetDouble(r, "airbrake, %", -1);
        }

        string vehicle = "";
        double heading = 0;
        double? pitch = null, bank = null, turn = null;
        double fuelInd = 0;
        bool indValid = false;
        var ammo = new List<double>();
        int indKeyCount = 0;
        bool hasAttitude = false, hasAltInd = false, hasVario = false, hasFuelInd = false;

        if (!string.IsNullOrWhiteSpace(indicatorsJson))
        {
            using var doc = JsonDocument.Parse(indicatorsJson);
            var r = doc.RootElement;
            indValid = GetBool(r, "valid");
            indKeyCount = r.EnumerateObject().Count();
            vehicle = GetString(r, "type");
            heading = GetDouble(r, "compass");
            hasAttitude = r.TryGetProperty("aviahorizon_pitch", out _) || r.TryGetProperty("bank", out _);
            hasAltInd = r.TryGetProperty("altitude_hour", out _);
            hasVario = r.TryGetProperty("vario", out _);
            hasFuelInd = r.TryGetProperty("fuel", out _);

            if (r.TryGetProperty("aviahorizon_pitch", out _)) pitch = GetDouble(r, "aviahorizon_pitch");
            if (r.TryGetProperty("bank", out _)) bank = GetDouble(r, "bank");
            else if (r.TryGetProperty("aviahorizon_roll", out _)) bank = -GetDouble(r, "aviahorizon_roll");
            if (r.TryGetProperty("turn", out _)) turn = GetDouble(r, "turn");
            if (hasFuelInd) fuelInd = GetDouble(r, "fuel");
            for (int i = 1; i <= 4; i++)
            {
                if (r.TryGetProperty($"ammo_counter{i}", out _)) ammo.Add(GetDouble(r, $"ammo_counter{i}"));
            }
        }

        // Fallbacks when /state is unavailable or a value is absent.
        if (mfuel <= 0 && fuelInd > 0) mfuel = fuelInd;

        bool valid = stateValid || indValid;
        // On the apron AoA/AoS contain garbage (observed -28.9 / -36.4 deg),
        // so attitude-facing symbology must be suppressed until actually flying.
        bool inFlight = valid && ias > 30.0;

        var caps = new Capabilities
        {
            HasNativeAttitude = hasAttitude,
            HasAmmo = ammo.Count > 0,
            HasFuelIndicator = hasFuelInd,
            HasAltimeterIndicator = hasAltInd,
            HasVariometer = hasVario,
            IndicatorKeyCount = indKeyCount,
        };

        return new TelemetrySnapshot
        {
            TimestampUtc = utcNow,
            ElapsedSeconds = elapsed.TotalSeconds,
            Valid = valid,
            InFlight = inFlight,
            VehicleType = vehicle,
            IasKmh = ias,
            TasKmh = tas,
            Mach = mach,
            AltM = alt,
            VyMs = vy,
            AoaDeg = inFlight ? aoa : 0,
            AosDeg = inFlight ? aos : 0,
            NyG = ny,
            RollRateDegS = wx,
            GearPercent = gearPct,
            FlapsPercent = flapsPct,
            AirbrakePercent = airbrakePct,
            HeadingDeg = heading,
            FuelKg = mfuel,
            Fuel0Kg = mfuel0,
            PitchDeg = hasAttitude ? pitch : null,
            BankDeg = hasAttitude ? bank : null,
            TurnRateDegS = turn,
            Ammo = ammo,
            Caps = caps,
        };
    }

    private static double GetDouble(JsonElement r, string name, double fallback = 0)
    {
        if (!r.TryGetProperty(name, out var e)) return fallback;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback,
            _ => fallback,
        };
    }

    private static bool GetBool(JsonElement r, string name)
    {
        if (!r.TryGetProperty(name, out var e)) return false;
        return e.ValueKind == JsonValueKind.True;
    }

    private static string GetString(JsonElement r, string name)
    {
        if (!r.TryGetProperty(name, out var e)) return "";
        return e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";
    }
}



