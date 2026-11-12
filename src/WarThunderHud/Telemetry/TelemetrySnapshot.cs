// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

namespace WtHud.Telemetry;

/// <summary>
/// Which optional telemetry blocks the current vehicle exposes.
/// Top-tier jets only provide /state (no attitude); props and early jets
/// provide the full instrument panel via /indicators.
/// </summary>
public sealed class Capabilities
{
    public bool HasNativeAttitude { get; init; }
    public bool HasAmmo { get; init; }
    public bool HasFuelIndicator { get; init; }
    public bool HasAltimeterIndicator { get; init; }
    public bool HasVariometer { get; init; }
    public int IndicatorKeyCount { get; init; }

    /// <summary>/state 这一轮是否解得动。<c>false</c> 基本等于「游戏没在跑 / 不在对局」。</summary>
    public bool StateValid { get; init; }

    /// <summary>
    /// /indicators 这一轮是否声称 valid。
    /// <para>★ 它和 <see cref="StateValid"/> 分开记：两者一真一假正是「8111 只起来一半」的特征，
    /// 合起来看才能和「机型不提供仪表」区分开。</para>
    /// </summary>
    public bool IndicatorsValid { get; init; }

    public override string ToString() =>
        $"attitude={HasNativeAttitude} ammo={HasAmmo} fuelInd={HasFuelIndicator} altInd={HasAltimeterIndicator} " +
        $"vario={HasVariometer} keys={IndicatorKeyCount} state={StateValid} ind={IndicatorsValid}";
}

/// <summary>
/// One fused telemetry frame. All values are in canonical units:
/// speed m/s internally? No - keep the game's own units where they are the
/// display unit (km/h, m, degrees, G, kg) to avoid conversion churn.
/// </summary>
public sealed class TelemetrySnapshot
{
    public DateTime TimestampUtc { get; init; }
    public double ElapsedSeconds { get; init; }

    // validity / phase
    public bool Valid { get; init; }
    public bool InFlight { get; init; }
    public string VehicleType { get; init; } = "";

    // primary flight data (always from /state)
    public double IasKmh { get; init; }
    public double TasKmh { get; init; }
    public double Mach { get; init; }
    public double AltM { get; init; }
    public double VyMs { get; init; }
    public double AoaDeg { get; init; }
    public double AosDeg { get; init; }
    public double NyG { get; init; }
    public double RollRateDegS { get; init; }

    /// <summary>True when the aircraft reports a compass (needed for the coordinated-turn bank).</summary>
    public bool HasHeading { get; init; }

    /// <summary>Cockpit AOA indexer value when the aircraft exposes one (normalised, null otherwise).</summary>
    public double? AoaIndexer { get; init; }

    /// <summary>Cockpit Vne (never-exceed speed) in m/s when the aircraft exposes it (null otherwise).</summary>
    public double? VneMs { get; init; }

    /// <summary>
    /// 真雷达高度（离地），**米**。8111 原始字段 <c>radio_altitude</c> 是英尺，
    /// 已在 <see cref="TelemetryParser"/> 里换算成米（2026-11-09 修正）。
    /// </summary>
    public double? RadioAltM { get; init; }

    /// <summary>Airbrake position, 0..100 % (from /state "airbrake, %").</summary>
    public double AirbrakePercent { get; init; }

    /// <summary>Flaps position, 0..100 % (from /state "flaps, %").</summary>
    public double FlapsPercent { get; init; }

    /// <summary>Landing gear position, 0 = retracted, 100 = down (from /state "gear, %").</summary>
    public double GearPercent { get; init; }

    // heading (from /indicators, present on both jet generations)
    public double HeadingDeg { get; init; }

    // fuel
    public double FuelKg { get; init; }
    public double Fuel0Kg { get; init; }

    // native attitude - nullable, depends on vehicle
    public double? PitchDeg { get; init; }
    public double? BankDeg { get; init; }
    public double? TurnRateDegS { get; init; }

    public IReadOnlyList<double> Ammo { get; init; } = Array.Empty<double>();

    public Capabilities Caps { get; init; } = new();

    public double FuelFraction => Fuel0Kg > 1 ? Math.Clamp(FuelKg / Fuel0Kg, 0, 1) : 0;

    /// <summary>Flight path angle (gamma) from vertical speed and true airspeed.</summary>
    public double FlightPathAngleDeg
    {
        get
        {
            if (TasKmh < 5) return 0;
            double tasMs = TasKmh / 3.6;
            double s = Math.Clamp(VyMs / tasMs, -1.0, 1.0);
            return Math.Asin(s) * 180.0 / Math.PI;
        }
    }

    public override string ToString() =>
        $"[{ElapsedSeconds,7:F2}s] {VehicleType,-14} IAS {IasKmh,6:F0} km/h  ALT {AltM,6:F0} m  M {Mach:F2}  " +
        $"Vy {VyMs,6:F1}  AoA {AoaDeg,5:F1}  Ny {NyG,4:F1}  Hdg {HeadingDeg,5:F1}  " +
        $"pitch={(PitchDeg.HasValue ? PitchDeg.Value.ToString("F1") : "n/a"),6}  bank={(BankDeg.HasValue ? BankDeg.Value.ToString("F1") : "n/a"),6}  fuel {FuelKg:F0}/{Fuel0Kg:F0}";
}







