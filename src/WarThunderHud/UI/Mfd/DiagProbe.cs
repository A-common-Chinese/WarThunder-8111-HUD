// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace WtHud.UI.Mfd;

/// <summary>
/// Background probe for the DIAG page: measures latency and validity of the local 8111 endpoints.
/// Read-only, only runs while the DIAG page is visible.
/// </summary>
internal static class DiagProbe
{
    private sealed record Snapshot(
        string State, string Indicators, string Map, string MapInfo, string Error, DateTime At);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private static Snapshot _snap = new("—", "—", "—", "—", "", DateTime.MinValue);
    private static CancellationTokenSource? _cts;

    public static bool Running => _cts != null;

    public static void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                var err = "";
                string state = "—", ind = "—", map = "—", info = "—";
                try
                {
                    var body = await Http.GetStringAsync("http://127.0.0.1:8111/state", ct);
                    sw.Stop();
                    state = body.Contains("\"valid\": true") || body.Contains("\"valid\":true")
                        ? $"OK {sw.ElapsedMilliseconds}ms"
                        : $"INVALID {sw.ElapsedMilliseconds}ms";
                }
                catch (Exception ex)
                {
                    err = ex.GetType().Name;
                    state = "NO LINK";
                }

                try
                {
                    var sw2 = Stopwatch.StartNew();
                    var body = await Http.GetStringAsync("http://127.0.0.1:8111/indicators", ct);
                    sw2.Stop();
                    ind = body.Contains("\"valid\": true") || body.Contains("\"valid\":true")
                        ? $"VALID {sw2.ElapsedMilliseconds}ms"
                        : "EMPTY";
                }
                catch { ind = "NO LINK"; }

                try
                {
                    var sw3 = Stopwatch.StartNew();
                    var body = await Http.GetStringAsync("http://127.0.0.1:8111/map_obj.json", ct);
                    sw3.Stop();
                    var airfields = 0;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        foreach (var e in doc.RootElement.EnumerateArray())
                            if (e.TryGetProperty("type", out var t) && t.GetString() == "airfield") airfields++;
                        map = $"{body.Length}B · {airfields} AF";
                    }
                    else map = "0B · 0 AF";
                }
                catch { map = "NO LINK"; }

                try
                {
                    var body = await Http.GetStringAsync("http://127.0.0.1:8111/map_info.json", ct);
                    info = body.Contains("\"valid\" : true") || body.Contains("\"valid\":true") ? "OK" : "—";
                }
                catch { info = "NO LINK"; }

                _snap = new Snapshot(state, ind, map, info, err, DateTime.UtcNow);
                try { await Task.Delay(1000, ct); } catch { return; }
            }
        }, ct);
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private static string Ago()
    {
        if (_snap.At == DateTime.MinValue) return "";
        var s = (int)(DateTime.UtcNow - _snap.At).TotalSeconds;
        return s <= 1 ? "  ●" : $"  {s}s";
    }

    public static string StateText => _snap.State + Ago();
    public static string IndicatorsText => _snap.Indicators + Ago();
    public static string MapText => _snap.Map + Ago();
    public static string MapInfoText => _snap.MapInfo;
}
