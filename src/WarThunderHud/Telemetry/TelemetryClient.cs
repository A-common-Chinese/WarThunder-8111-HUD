// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Diagnostics;
using System.Net.Http;

namespace WtHud.Telemetry;

/// <summary>
/// Polls the two hot endpoints of the War Thunder local API.
/// The embedded server is single threaded, so requests are strictly serial.
/// </summary>
public sealed class TelemetryClient : IDisposable
{
    private const string StateUrl = "http://127.0.0.1:8111/state";
    private const string IndicatorsUrl = "http://127.0.0.1:8111/indicators";

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _intervalMs;
    private Task? _loop;

    public event Action<TelemetrySnapshot>? SnapshotReceived;

    /// <summary>Diagnostics: smoothed timings in milliseconds.</summary>
    public double AvgCycleMs { get; private set; }
    public double AvgStateRequestMs { get; private set; }
    public double AvgIndRequestMs { get; private set; }

    public TelemetryClient(int intervalMs = 25)
    {
        _intervalMs = Math.Clamp(intervalMs, 0, 500);
        // Small requests over loopback stall ~40 ms with Nagle + delayed ACK enabled,
        // which capped the effective sample rate at ~17 Hz. Disable Nagle explicitly.
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(900) };
    }

    public void Start()
    {
        if (_loop != null) return;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Each request to the embedded server costs ~10 ms, so an extra sleep on top of the
        // round trip halves the achievable rate. /state is polled every cycle (it carries
        // everything except heading/native attitude); /indicators every second cycle.
        string? lastIndicators = null;
        int iter = 0;
        while (!ct.IsCancellationRequested)
        {
            var cycleSw = System.Diagnostics.Stopwatch.StartNew();
            string? state = null;
            var reqSw = System.Diagnostics.Stopwatch.StartNew();
            try { state = await _http.GetStringAsync(StateUrl, ct).ConfigureAwait(false); } catch { }
            AvgStateRequestMs = AvgStateRequestMs * 0.9 + reqSw.Elapsed.TotalMilliseconds * 0.1;

            if ((iter & 1) == 0)
            {
                try
                {
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    var fresh = await _http.GetStringAsync(IndicatorsUrl, ct).ConfigureAwait(false);
                    AvgIndRequestMs = AvgIndRequestMs * 0.9 + sw2.Elapsed.TotalMilliseconds * 0.1;
                    if (fresh != null) lastIndicators = fresh;
                }
                catch { }
            }

            if (state != null || lastIndicators != null)
            {
                TelemetrySnapshot? snap = null;
                try { snap = TelemetryParser.Parse(state, lastIndicators, _clock.Elapsed, DateTime.UtcNow); } catch { }
                if (snap != null) SnapshotReceived?.Invoke(snap);
            }

            AvgCycleMs = AvgCycleMs * 0.9 + cycleSw.Elapsed.TotalMilliseconds * 0.1;
            iter++;
            if (_intervalMs > 0)
            {
                try { await Task.Delay(_intervalMs, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}





