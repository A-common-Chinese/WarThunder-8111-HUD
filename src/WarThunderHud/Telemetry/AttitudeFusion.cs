// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Collections.Generic;

namespace WtHud.Telemetry;

public enum AttitudeSource
{
    /// <summary>Native artificial horizon from /indicators (props, early jets).</summary>
    Game = 0,
    /// <summary>Synthesised from /state (required for top-tier jets).</summary>
    Estimated = 1,
    /// <summary>No usable attitude (on ground, low confidence, or data gap).</summary>
    Unavailable = 2,
}

public readonly struct AttitudeSolution
{
    public double PitchDeg { get; init; }
    public double BankDeg { get; init; }
    public AttitudeSource Source { get; init; }
    /// <summary>0..1, fades symbology in/out.</summary>
    public double Confidence { get; init; }
    public bool Usable => Source != AttitudeSource.Unavailable && Confidence > 0.2;

    public override string ToString() => $"{Source,-9} pitch={PitchDeg,6:F1} bank={BankDeg,6:F1} conf={Confidence:F2}";
}

/// <summary>
/// Produces a usable attitude for both vehicle generations.
///
/// NATIVE (props / early jets): /indicators exposes aviahorizon_pitch and bank.
/// aviahorizon_pitch carries a per-vehicle constant bias (measured -4.3 deg while a
/// parked MiG-19 reads level), so the bias is learned online by low-passing
/// (native - synthetic) while airborne; the native reading then stays authoritative.
///
/// SYNTHETIC (top-tier jets, no attitude in the API):
///   pitch = flight path angle + AoA            (both from /state)
///   bank  = atan(V * dPsi/dt / g)              (coordinated turn, V from TAS)
///
/// Bank must NOT be derived from acos(1/Ny): that identity only holds for a steady
/// level turn. During a pull-up (measured Ny 7-10 in a loop) it reports 80+ degrees
/// of bank while the aircraft is wings-level - observed as a wildly spinning ladder.
///
/// Compass differentiation also must not be done per frame: at 25 Hz the
/// quantisation noise dominates (measured 14.6 deg average error). A ~1 s window
/// linear trend is used instead.
/// </summary>
public sealed class AttitudeFusion
{
    private const double WindowSeconds = 1.6;
    private const int MaxSamples = 64;

    private readonly double _alpha;
    private readonly Queue<(double T, double Hdg)> _hdg = new();

    private double _pitchBias;
    private bool _biasInit;
    private double _pitchSmooth;
    private double _bankSmooth;
    private double _turnRateDegS;   // low-passed
    private double _confidence = 1;
    private double _bankIntegrated; // from Wx roll rate
    private double _lastTime = double.NaN;
    private double _nativeDot;       // correlation between native pitch and synthesis
    private int _nativeSamples;
    private int _nativeSign = 1;

    /// <summary>0 = auto-detect the native pitch sign, +1 / -1 = forced by the user.</summary>
    public int ForcedPitchSign { get; set; }

    /// <summary>-1 when the aircraft reports its horizon pitch inverted.</summary>
    public int DetectedPitchSign { get; private set; } = 1;

    private string _lastVehicle = "";
    private double _pitchDot;
    private int _pitchVotes;
    private double _nativeTrust = 1.0;
    private double _nativeDissentSec;

    public AttitudeFusion(double smoothing = 0.45)
    {
        _alpha = Math.Clamp(smoothing, 0.01, 1.0);
    }

    public AttitudeSolution Update(TelemetrySnapshot s)
    {
        if (!s.Valid || !s.InFlight)
        {
            _hdg.Clear();
            _confidence = 0;
            _bankSmooth *= 0.8;
            return new AttitudeSolution { Source = AttitudeSource.Unavailable, Confidence = 0 };
        }

        // ---- heading trend over ~1 s ----
        _hdg.Enqueue((s.ElapsedSeconds, s.HeadingDeg));
        while (_hdg.Count > 2 && s.ElapsedSeconds - _hdg.Peek().T > WindowSeconds) _hdg.Dequeue();
        while (_hdg.Count > MaxSamples) _hdg.Dequeue();

        double turnRate = 0;
        if (_hdg.Count >= 2)
        {
            var arr = _hdg.ToArray();
            double dt = arr[^1].T - arr[0].T;
            if (dt > 0.15)
            {
                // unwrap the heading difference across the 0/360 seam
                double d = arr[^1].Hdg - arr[0].Hdg;
                if (d > 180) d -= 360;
                else if (d < -180) d += 360;
                turnRate = d / dt;      // deg/s
            }
        }
        _turnRateDegS = _turnRateDegS * 0.85 + turnRate * 0.15;

        // ---- pitch ----
        double pitchSynth = s.FlightPathAngleDeg + s.AoaDeg;

        // ---- bank from coordinated turn ----
        double tasMs = Math.Max(1.0, s.TasKmh / 3.6);
        double omega = _turnRateDegS * Math.PI / 180.0;
        double bankSynth = Math.Atan2(tasMs * omega, 9.80665) * 180.0 / Math.PI;
        bankSynth = Math.Clamp(bankSynth, -80, 80);

        // ---- native path ----
        // The game value is used directly, except for its sign: some aircraft (F/A-18E, MiG-19)
        // report the horizon pitch inverted. Vote on the sign against the physical estimate
        // instead of trusting one convention; the user can force it with the pitchSign config.
        if (s.PitchDeg.HasValue && s.BankDeg.HasValue)
        {
            if (s.VehicleType != _lastVehicle)
            {
                _lastVehicle = s.VehicleType;
                _pitchDot = 0;
                _pitchVotes = 0;
                DetectedPitchSign = 1;
            }

            if (ForcedPitchSign != 0)
            {
                DetectedPitchSign = ForcedPitchSign;
            }
            else if (_pitchVotes < 40 && Math.Abs(pitchSynth) > 3.0 && Math.Abs(s.PitchDeg.Value) > 1.0)
            {
                _pitchDot += s.PitchDeg.Value * pitchSynth;
                _pitchVotes++;
                if (_pitchVotes == 40)
                {
                    DetectedPitchSign = _pitchDot >= 0 ? 1 : -1;
                    Console.WriteLine($"native pitch sign for {_lastVehicle}: {DetectedPitchSign} " +
                                      "(auto-detected; override via pitchSign in hud-config.json)");
                }
            }

            _pitchSmooth = DetectedPitchSign * s.PitchDeg.Value;
            _bankSmooth = s.BankDeg.Value;
            _confidence = 1.0;
            return new AttitudeSolution
            {
                PitchDeg = _pitchSmooth,
                BankDeg = _bankSmooth,
                Source = AttitudeSource.Game,
                Confidence = 1.0,
            };
        }

        // ---- synthetic path ----
        _pitchSmooth += (pitchSynth - _pitchSmooth) * _alpha;

        // Bank: integrate the roll rate for short-term fidelity (this is what makes roll
        // manoeuvres look right), then slowly pull toward the coordinated-turn estimate
        // whenever the aircraft is not rolling hard. Pure turn-rate bank lags badly.
        // Roll-rate integration had no absolute reference and drifted without bound
        // (measured -170 deg on a wings-level aircraft). The coordinated-turn estimate is
        // bounded, so it drives the displayed bank; when the aircraft is not turning the
        // value decays to level instead of accumulating error.
        double stepDt = double.IsNaN(_lastTime) ? 0.04 : Math.Clamp(s.ElapsedSeconds - _lastTime, 0.0, 0.2);
        _lastTime = s.ElapsedSeconds;
        double bankTarget = Math.Abs(bankSynth) > 6.0 ? bankSynth : 0.0;
        double slew = 140.0 * stepDt;   // deg per step
        _bankSmooth += Math.Clamp(bankTarget - _bankSmooth, -slew, slew);
        _bankIntegrated = _bankSmooth;

        // Confidence drops when the aircraft is manoeuvring hard or the data looks noisy.
        double noise = Math.Abs(s.NyG - 1.0) > 4 ? 0.75 : 1.0;
        _confidence = Math.Min(1.0, _confidence + 0.1) * 0.98 + 0.02 * noise;

        return new AttitudeSolution
        {
            PitchDeg = _pitchSmooth,
            BankDeg = _bankSmooth,
            Source = AttitudeSource.Estimated,
            Confidence = Math.Clamp(_confidence, 0, 1),
        };
    }

    private static void Normalize(ref double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
    }

    /// <summary>Current low-passed turn rate, deg/s (exposed for testing/logging).</summary>
    public double TurnRateDegS => _turnRateDegS;
}









