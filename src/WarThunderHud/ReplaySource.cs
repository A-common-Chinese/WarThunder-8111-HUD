// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;
using System.Text.Json;
using WtHud.Telemetry;

namespace WtHud;

/// <summary>
/// Replays a captured .jsonl recording (the sampler format: one JSON object per line
/// with optional "state" and "ind" sub objects) at its original timing.
/// </summary>
public static class ReplaySource
{
    public static IEnumerable<(double t, string? stateJson, string? indJson)> Read(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("t", out var tEl)) continue;
                double t = tEl.GetDouble();
                string? state = root.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object ? s.GetRawText() : null;
                string? ind = root.TryGetProperty("ind", out var i) && i.ValueKind == JsonValueKind.Object ? i.GetRawText() : null;

                // Legacy recordings (first sampler version) stored a flattened subset instead of raw payloads.
                if (state == null && ind == null && root.TryGetProperty("ias", out _))
                {
                    state = BuildLegacyState(root);
                    ind = BuildLegacyIndicators(root);
                }

                if (state == null && ind == null) continue;
                yield return (t, state, ind);
            }
        }
    }

    private static string BuildLegacyState(JsonElement r)
    {
        var d = new Dictionary<string, object?>();
        d["valid"] = r.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        AddIf(d, r, "ias", "IAS, km/h");
        AddIf(d, r, "tas", "TAS, km/h");
        AddIf(d, r, "alt", "H, m");
        AddIf(d, r, "vy", "Vy, m/s");
        AddIf(d, r, "aoa", "AoA, deg");
        AddIf(d, r, "aos", "AoS, deg");
        AddIf(d, r, "ny", "Ny");
        AddIf(d, r, "m", "M");
        return JsonSerializer.Serialize(d);
    }

    private static string BuildLegacyIndicators(JsonElement r)
    {
        var d = new Dictionary<string, object?>();
        d["valid"] = true;
        if (r.TryGetProperty("itype", out var it) && it.ValueKind == JsonValueKind.String) d["type"] = it.GetString();
        AddIf(d, r, "cmp", "compass");
        AddIf(d, r, "pitch", "aviahorizon_pitch");
        AddIf(d, r, "bank", "bank");
        AddIf(d, r, "turn", "turn");
        AddIf(d, r, "gm", "g_meter");
        return JsonSerializer.Serialize(d);
    }

    private static void AddIf(Dictionary<string, object?> d, JsonElement r, string src, string dst)
    {
        if (!r.TryGetProperty(src, out var e)) return;
        if (e.ValueKind == JsonValueKind.Number) d[dst] = e.GetDouble();
    }

    public static void Play(string path, double speed = 1.0, bool verbose = true)
    {
        var records = Read(path).ToList();
        if (records.Count == 0)
        {
            Console.WriteLine($"no usable frames in {path}");
            return;
        }

        Console.WriteLine($"replaying {records.Count} frames from {Path.GetFileName(path)} at {speed:0.##}x");
        double t0 = records[0].t;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double lastWall = -1;

        foreach (var rec in records)
        {
            double target = (rec.t - t0) / Math.Max(0.01, speed);
            double wait = target - (lastWall < 0 ? 0 : clock.Elapsed.TotalSeconds);
            if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
            lastWall = clock.Elapsed.TotalSeconds;

            var snap = TelemetryParser.Parse(rec.stateJson, rec.indJson, TimeSpan.FromSeconds(rec.t - t0), DateTime.UtcNow);
            if (verbose) Console.WriteLine(snap.ToString() + "  caps: " + snap.Caps);
        }
    }
}
