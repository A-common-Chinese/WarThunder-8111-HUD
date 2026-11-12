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
/// The reading is used directly; what has to be decided per vehicle is only its sign
/// (some aircraft report the horizon pitch inverted), voted online against the
/// physical estimate and stored per airframe.
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

    private double _pitchSmooth;
    private double _bankSmooth;
    private double _turnRateDegS;   // low-passed
    private double _confidence = 1;
    private double _bankIntegrated; // from Wx roll rate
    private double _bankSign = 1.0; // 最近一次滚转的方向，稳定压坡度后 Wx 归零时靠它定符号
    private double _lastTime = double.NaN;

    /// <summary>0 = auto-detect the native pitch sign, +1 / -1 = forced by the user.</summary>
    public int ForcedPitchSign { get; set; }

    /// <summary>-1 when the aircraft reports its horizon pitch inverted.</summary>
    public int DetectedPitchSign { get; private set; } = 1;

    private string _lastVehicle = "";
    private double _pitchDot;
    private int _pitchVotes;

    /// <summary>True once the automatic sign vote produced a result for the current vehicle.</summary>
    public bool SignDecided { get; private set; }

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
                SignDecided = false;
                DetectedPitchSign = 1;
            }

            if (ForcedPitchSign != 0)
            {
                DetectedPitchSign = ForcedPitchSign;
            }
            else if (!SignDecided && _pitchVotes < 60 && Math.Abs(pitchSynth) > 3.0 && Math.Abs(s.PitchDeg.Value) > 1.0)
            {
                _pitchDot += s.PitchDeg.Value * pitchSynth;
                _pitchVotes++;
                if (_pitchVotes >= 60)
                {
                    SignDecided = true;
                    DetectedPitchSign = _pitchDot >= 0 ? 1 : -1;
                    Console.WriteLine($"native pitch sign for {_lastVehicle}: {DetectedPitchSign} " +
                                      "(auto-detected; persisted per vehicle)");
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

        if (!s.HasHeading)
        {
            // No compass at all (e.g. Rafale C): the turn-rate estimate is always zero, so the
            // bank pointer would never move. Integrate the roll rate instead.
            _bankIntegrated += s.RollRateDegS * stepDt;
            Normalize(ref _bankIntegrated);

            // 稳定压住坡度后 Wx 归零，符号只能靠「最近一次滚转」记住
            if (Math.Abs(s.RollRateDegS) > 15.0) _bankSign = Math.Sign(s.RollRateDegS);

            // ★ 2026-11-12 实测修正：只靠积分 + 回平衰减，会把持续坡度吃掉。
            // 阵风实飞两段稳盘（Ny 2.07 / 2.52，真值 55.2° / 66.0°）读数是 −7.1° / −1.4° ——
            // 因为稳盘时 Wx≈0，正好触发下面那条 4 秒衰减。
            // Ny 是现成的绝对参考：协调平飞转弯里 bank = acos(1/Ny)，只要高度不变就成立。
            // 于是「压坡度瞬间」靠积分跟手，「保持坡度」靠 Ny 锚住，两者互补且不需要地图数据。
            bool steadyTurn = Math.Abs(s.VyMs) < 3.0
                              && s.NyG > 1.08 && s.NyG < 2.6     // 排除推杆/大 G 拉升
                              && Math.Abs(s.RollRateDegS) < 5.0; // 只在不滚的时候锚
            if (steadyTurn)
            {
                double target = Math.Acos(Math.Clamp(1.0 / s.NyG, -1.0, 1.0)) * 180.0 / Math.PI * _bankSign;
                double k = 1.0 - Math.Exp(-stepDt / 2.0);        // τ=2 s：比原来的衰减快，又不会被噪声带着跳
                _bankIntegrated += (target - _bankIntegrated) * k;
            }
            else if (Math.Abs(s.RollRateDegS) < 2.0)
            {
                double decay = 40.0 * stepDt;             // deg per step back towards level (~4 s to settle)
                _bankIntegrated += Math.Clamp(-_bankIntegrated, -decay, decay);
            }
            _bankSmooth = _bankIntegrated;
        }
        else
        {
            double bankTarget = Math.Abs(bankSynth) > 6.0 ? bankSynth : 0.0;
            double slew = 140.0 * stepDt;   // deg per step
            _bankSmooth += Math.Clamp(bankTarget - _bankSmooth, -slew, slew);
            _bankIntegrated = _bankSmooth;
        }

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













