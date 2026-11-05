// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WtHud.Telemetry;

/// <summary>One navigation-relevant point taken from /map_obj.json.</summary>
public readonly record struct MapPoint(double X, double Y, bool Friendly, double RunwayHeadingDeg);

/// <summary>
/// Minimal map reader for navigation only: the player's own icon, the airfields and the grid
/// geometry. Enemy/ground entries are deliberately skipped - this project does not do ESP.
/// </summary>
public sealed class MapData : IDisposable
{
    private const string ObjUrl = "http://127.0.0.1:8111/map_obj.json";
    private const string InfoUrl = "http://127.0.0.1:8111/map_info.json";

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private int _infoTick;

    /// <summary>Player position in normalised map space (0..1) or null when unknown.</summary>
    public (double X, double Y)? Self { get; private set; }

    /// <summary>Airfields on this map (friendly + enemy), static positions.</summary>
    public List<MapPoint> Airfields { get; private set; } = new();

    /// <summary>Playable map size in metres (x, y).</summary>
    public (double X, double Y) MapSizeM { get; private set; }

    /// <summary>Grid geometry from /map_info.json: origin and cell size in metres.</summary>
    public (double X, double Y) GridZeroM { get; private set; }
    public (double X, double Y) GridStepM { get; private set; } = (5500, 5500);

    public MapData()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollObjectsAsync(ct).ConfigureAwait(false);
                // Grid geometry changes only between maps: refresh it every 4th cycle.
                if (++_infoTick >= 4) { _infoTick = 0; await PollInfoAsync(ct).ConfigureAwait(false); }
            }
            catch
            {
                // The API disappears when the game closes or changes scene; keep polling quietly.
            }

            try { await Task.Delay(250, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollObjectsAsync(CancellationToken ct)
    {
        string json = await _http.GetStringAsync(ObjUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        (double, double)? self = null;
        var fields = new List<MapPoint>();

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string type = e.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
            string icon = e.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "";

            if (type == "aircraft" && icon == "Player")
            {
                self = (GetDouble(e, "x"), GetDouble(e, "y"));
                continue;
            }

            if (type != "airfield") continue;   // everything else (enemy units!) is ignored

            // Runways come as a segment sx,sy -> ex,ey: use the midpoint and its orientation.
            double sx = GetDouble(e, "sx"), sy = GetDouble(e, "sy");
            double ex = GetDouble(e, "ex"), ey = GetDouble(e, "ey");
            double mx = (sx + ex) / 2, my = (sy + ey) / 2;
            double heading = Math.Atan2(ex - sx, -(ey - sy)) * 180.0 / Math.PI;
            string color = e.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "";
            // Friendly airfields are drawn blue, enemy ones red: compare the channels instead of
            // matching one exact hex string.
            bool friendly = false;
            if (color.Length >= 7 && color[0] == '#')
            {
                if (int.TryParse(color.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rr) &&
                    int.TryParse(color.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bb))
                    friendly = bb > rr;
            }
            fields.Add(new MapPoint(mx, my, friendly, heading));
        }

        Self = self;
        Airfields = fields;
    }

    private async Task PollInfoAsync(CancellationToken ct)
    {
        string json = await _http.GetStringAsync(InfoUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("grid_size", out var gs) || gs.ValueKind != JsonValueKind.Array) return;
        if (gs.GetArrayLength() < 2) return;
        MapSizeM = (gs[0].GetDouble(), gs[1].GetDouble());

        if (r.TryGetProperty("grid_zero", out var gz) && gz.ValueKind == JsonValueKind.Array && gz.GetArrayLength() >= 2)
            GridZeroM = (gz[0].GetDouble(), gz[1].GetDouble());
        if (r.TryGetProperty("grid_steps", out var st) && st.ValueKind == JsonValueKind.Array && st.GetArrayLength() >= 2)
        {
            double sx = st[0].GetDouble(), sy = st[1].GetDouble();
            if (sx > 1 && sy > 1) GridStepM = (sx, sy);
        }
    }

    private static double GetDouble(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}



