// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// One-shot helper that decides the pitch sign for the current airframe by correlating the
/// attitude the game reports with our own flight-path estimate (gamma from vario / TAS).
/// Correlation is immune to lag and zero offset, so a single 5 s climb/hold is enough.
/// </summary>
public static class PitchDetect
{
    private static readonly List<(double Est, double Game)> Samples = new();

    public static event Action? BeginRequested;

    public static bool Active { get; private set; }
    public static DateTime StartedUtc { get; private set; }
    public static double Seconds => Active ? (DateTime.UtcNow - StartedUtc).TotalSeconds : 0;

    /// <summary>Live number of usable samples (updated while capturing).</summary>
    public static int SampleCount => Samples.Count;

    public static int Sign { get; private set; }
    public static double Confidence { get; private set; }
    public static int UsedSamples { get; private set; }
    public static bool HasResult { get; private set; }

    public static void Request() => BeginRequested?.Invoke();

    /// <summary>Writes the sampled sign into the config for this airframe.</summary>
    public static void SaveFor(string vehicle) => PendingSave = (vehicle, Sign);

    /// <summary>Consumed by the panel when it saves the config.</summary>
    public static (string Vehicle, int Sign)? PendingSave { get; private set; }

    public static void ClearPendingSave() => PendingSave = null;

    public static void Begin()
    {
        Samples.Clear();
        Sign = 0;
        Confidence = 0;
        UsedSamples = 0;
        HasResult = false;
        Active = true;
        StartedUtc = DateTime.UtcNow;
    }

    public static void Abort()
    {
        Active = false;
        Samples.Clear();
    }

    /// <summary>Fed from the overlay loop: game pitch (deg) and flight-path estimate (deg).</summary>
    public static void Feed(double? gamePitch, double? gammaDeg)
    {
        if (!Active || gamePitch is null || gammaDeg is null) return;
        if (Math.Abs(gamePitch.Value) < 5.0) return;          // level flight carries no sign information
        Samples.Add((gammaDeg.Value, gamePitch.Value));
        if (Samples.Count > 2000) Samples.RemoveAt(0);
    }

    /// <summary>Computes the sign; returns true when the result is trustworthy.</summary>
    public static bool Finish()
    {
        Active = false;
        HasResult = true;
        UsedSamples = Samples.Count;

        if (Samples.Count < 20) { Sign = 0; Confidence = 0; return false; }

        double me = 0, mg = 0;
        foreach (var s in Samples) { me += s.Est; mg += s.Game; }
        me /= Samples.Count; mg /= Samples.Count;

        double cov = 0, ve = 0, vg = 0;
        foreach (var s in Samples)
        {
            var de = s.Est - me;
            var dg = s.Game - mg;
            cov += de * dg; ve += de * de; vg += dg * dg;
        }

        if (ve < 1e-6 || vg < 1e-6) { Sign = 0; Confidence = 0; return false; }

        Confidence = Math.Abs(cov) / Math.Sqrt(ve * vg);
        Sign = cov > 0 ? 1 : -1;
        return Confidence >= 0.6;
    }
}
