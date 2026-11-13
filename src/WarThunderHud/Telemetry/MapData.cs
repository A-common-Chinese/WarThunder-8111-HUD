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
    private static readonly DateTime _epoch = DateTime.UtcNow;

    /// <summary>Player position in normalised map space (0..1) or null when unknown.</summary>
    public (double X, double Y)? Self { get; private set; }

    /// <summary>
    /// 上一次成功解析 map_obj 的时刻（<see cref="Environment.TickCount64"/> 毫秒）。
    /// <para>★ 本机位置只能从这里拿，所以它天然比"现在"旧 0~250 ms。
    /// 编队引导要把链路位置外推到同一时刻，就得知道这个年龄 —— 否则两边年龄之差会直接变成距离误差。</para>
    /// </summary>
    public long UpdatedAtMs { get; private set; }

    /// <summary>Course over ground (deg, 0 = north) derived from position changes; null when slow.</summary>
    public double? CourseDeg { get; private set; }

    private readonly Queue<(double T, double X, double Y)> _track = new();
    private DateTime _lastGoodMap = DateTime.MinValue;   // 上次拿到非空 map_obj 的时间
    private DateTime _lastSample = DateTime.MinValue;

    /// <summary>Airfields on this map (friendly + enemy), static positions.</summary>
    public List<MapPoint> Airfields { get; private set; } = new();

    /// <summary>Spotted ground units: position, icon class (SPAA/IFV/tank) and friend flag.</summary>
    public List<(double X, double Y, string Icon, bool Friendly)> GroundUnits { get; private set; } = new();

    /// <summary>
    /// 友军飞机（已按 color 过滤，**不含敌人**）。归一化坐标 + 单位方向向量（航向）。
    /// map_obj 里敌人也会出现，所以这里必须白名单：非蓝系一律不进这个表。
    /// </summary>
    public List<MapAircraft> Friendlies { get; private set; } = new();

    /// <summary>
    /// 编队功能开关：关掉时连友机表都不解析（"不进行编队数据的运算"）。
    /// 由宿主按 hud-config 的编队开关每帧设置。
    /// </summary>
    public bool CollectFriendlies { get; set; }

    /// <summary>
    /// 长机图标过滤（map_obj 的 <c>icon</c>，如 Fighter / Bomber / Assault）。
    /// <para>僚机靠「离我最近的友军」认长机时，战场上多一架友军就会认错。
    /// 空字符串 = 不过滤（沿用最近友军）；非空则只保留图标匹配的友机。</para>
    /// <para>★ 这是**类别**不是具体机型：同为 Fighter 的两架飞机无法区分。</para>
    /// </summary>
    public string LeadIconFilter { get; set; } = "";

    /// <summary>
    /// 按机型过滤**之前**这一帧识别到的友军数量。
    /// <para>用来区分「附近根本没有友军」和「有友军但机型都不匹配」——
    /// 两种情况在界面上都是"什么都没画"，但对飞行员是两件事：一个该等，一个该改设置。</para>
    /// </summary>
    public int FriendlyTotal { get; private set; }

    /// <summary>地图上的一架友机：位置（0..1 归一化）、航向单位向量、图标类别。</summary>
    public readonly record struct MapAircraft(double X, double Y, double Dx, double Dy, string Icon);

    /// <summary>Playable map size in metres (x, y).</summary>
    public (double X, double Y) MapSizeM { get; private set; }

    /// <summary>Size of the last /map_obj.json payload, for diagnostics.</summary>
    /// <summary>Number of aircraft markers on the map (other than the player).</summary>
    public int AircraftMarkers { get; private set; }

    public int RawBytes { get; private set; }

    /// <summary>Last polling exception text, for diagnostics.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Grid geometry from /map_info.json: origin and cell size in metres.</summary>

    /// <summary>Number of parsed airfields in the last non-empty payload.</summary>
    public int AirfieldCount => Airfields.Count;

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
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                // The API disappears when the game closes or changes scene; keep polling quietly.
            }

            try { await Task.Delay(250, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollObjectsAsync(CancellationToken ct)
    {
        string json = await _http.GetStringAsync(ObjUrl, ct).ConfigureAwait(false);
        RawBytes = json.Length;
        UpdatedAtMs = Environment.TickCount64;

        // The game legitimately answers 200 with an empty body when it has no map data for the
        // current battle (some modes never expose it). Parsing that would throw and take the
        // grid poll down with it, so treat "no tokens" as "no data this cycle".
        if (string.IsNullOrWhiteSpace(json))
        {
            // ★ 空 body 多半是 8111 还没就绪、或者正在换局，不是"这局没有地图"。
            //   旧实现在这里直接 return，Self / 友军 / 机场会**冻结在上一局的值**：
            //   换图后长机把上一局的坐标发给僚机，僚机拿它去配这一局的友机，必然配错。
            //   宁可让上层看到"没有位置"，也不要让它用错的位置 —— 前者只是没有圈，
            //   后者是圈指向错误的方向，更难发现。
            Self = null;
            Friendlies.Clear();
            FriendlyTotal = 0;
            Airfields.Clear();
            return;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        (double, double)? self = null;
        int aircraftCount = 0;
        FriendlyTotal = 0;
        var units = new List<(double X, double Y, string Icon, bool Friendly)>();
        var friendlies = new List<MapAircraft>();
        var fields = new List<MapPoint>();

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string type = e.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
            string icon = e.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "";

            if (type == "aircraft")
            {
                if (icon == "Player")
                {
                    self = (GetDouble(e, "x"), GetDouble(e, "y"));
                    continue;
                }

                aircraftCount++;

                // ★ 只保留友军。map_obj 里敌人也会出现（color 红系 #fa0C00），
                // 把敌人位置画到 HUD 上就等于 ESP —— 本项目明确不做的红线。
                // 颜色判据与地面单位/机场一致：蓝通道 > 红通道 = 友军。
                // CollectFriendlies 由编队开关控制：关掉时连这一步解析都不做。
                if (CollectFriendlies && IsFriendlyColor(e))
                {
                    FriendlyTotal++;
                    // 指定了长机图标就只收匹配的：多一架友军也不会把引导带偏
                    if (LeadIconFilter.Length == 0
                        || icon.Equals(LeadIconFilter, StringComparison.OrdinalIgnoreCase))
                        friendlies.Add(new MapAircraft(GetDouble(e, "x"), GetDouble(e, "y"),
                                                       GetDouble(e, "dx"), GetDouble(e, "dy"), icon));
                }
                continue;
            }

            // 友方地面单位：只记录会出现在地图上的已侦察单位（用于全真导航）
            if (type == "ground_model")
            {
                if (IsFriendlyColor(e))
                {
                    // 地面单位用 x/y（机场才是 sx/sy 线段）
                    units.Add((GetDouble(e, "x"), GetDouble(e, "y"), icon, true));
                }
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

        // The API occasionally returns an empty body for a frame or two (scene load, map redraw).
        // Keep the previous airfield table briefly so the navigation readout does not flicker out.
        if (fields.Count == 0 && Airfields.Count > 0 && (DateTime.UtcNow - _lastGoodMap).TotalSeconds < 5.0)
            return;

        if (fields.Count > 0) _lastGoodMap = DateTime.UtcNow;

        Self = self;
        Friendlies = friendlies;
        GroundUnits = units;
        AircraftMarkers = aircraftCount;
        Airfields = fields;

        // Course over ground: some aircraft expose no compass, but the map position still moves.
        // Compare against the oldest sample in a ~2.5 s window so the bearing is stable.
        if (self is { } pos)
        {
            double now = (DateTime.UtcNow - _epoch).TotalSeconds;
            _track.Enqueue((now, pos.Item1, pos.Item2));
            while (_track.Count > 2 && now - _track.Peek().T > 2.5) _track.Dequeue();
            while (_track.Count > 200) _track.Dequeue();

            if (_track.Count >= 3)
            {
                var first = _track.Peek();
                double dx = (pos.Item1 - first.X) * MapSizeM.X;
                double dy = (pos.Item2 - first.Y) * MapSizeM.Y;
                double flown = Math.Sqrt(dx * dx + dy * dy);
                double dtTrack = now - first.T;
                if (flown > 250 && dtTrack > 0.8)
                {
                    // Map Y grows southwards: north is -y.
                    double deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
                    if (deg < 0) deg += 360;
                    CourseDeg = deg;
                }
                else
                {
                    CourseDeg = null;
                }
            }
        }
        else
        {
            CourseDeg = null;
            _track.Clear();
        }
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

    /// <summary>
    /// /map_obj 的 color 是敌我标识：蓝系（如 #174DFF）= 友军，红系（如 #fa0C00）= 敌人。
    /// 用通道比较而不是精确匹配十六进制，避免联队配色/高亮导致漏判。
    /// </summary>
    /// <summary>
    /// 友军颜色判据：**蓝系（普通友军）或绿系（同小队队友）**都算友军，红系（敌机）不算。
    /// <para>★ 2026-11-11 实测 map_obj 实际取值：友军 <c>#134AFF</c>（蓝）、
    /// 同小队队友 <c>#39D921</c>（绿）、敌机 <c>#fa0C00</c> 系（红）、自己 <c>#faC81E</c>（黄）。</para>
    /// <para>原先只判「蓝通道 > 红通道」，会把**绿色队友误当敌机丢弃** ——
    /// 两人组队时僚机根本看不见长机，编队功能直接失效。这里改成蓝、绿双白名单。</para>
    /// </summary>
    private static bool IsFriendlyColor(JsonElement e)
    {
        string color = e.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "";
        if (color.Length < 7 || color[0] != '#') return false;
        if (!int.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)) return false;
        if (!int.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)) return false;
        if (!int.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b)) return false;
        bool blue = b > r + 40;                   // 普通友军
        bool green = g > r + 40 && g > b;         // 同小队队友：绿通道显著高于红、且盖过蓝
        return blue || green;
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






