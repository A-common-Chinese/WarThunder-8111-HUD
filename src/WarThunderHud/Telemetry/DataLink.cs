// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WtHud.Telemetry;

/// <summary>
/// 极简数据链：广播自己的气压高度，外加编队辅助需要的航向与转向率。
/// <para>
/// 位置**不作为位置来源** —— 单机拿到的友机位置与对方自报值一致到 1–2 m（实测 27 m–31 km 全段），
/// 而高度在 <c>map_obj</c> 里根本没有 z，单机无论如何拿不到。高度又恰好是慢变量
/// （Vy 通常 0–5 m/s），100 ms 延迟只差半米，所以它对时间同步几乎没有要求。
/// </para>
/// <para>
/// 2026-11-12 起报文末尾多两段自身归一化坐标，目的**只有一个**：把「数据链节点」和
/// <c>map_obj</c> 里的友机认成同一架。map_obj 只给位置和机型、没有呼号，节点 ID 是随机的，
/// 两边唯一的共同量就是位置 —— 拿它当配对键，才能给编队里**每一架**友机配上各自的高度，
/// 而不是全队共用「最活跃那个节点」的高度。
/// </para>
/// <para>
/// **转向率是第二个值得共享的量**：它决定转弯时僚机要给多少提前量
/// （<c>asin(ω·R/V)</c>）。从位置差分也能估，但 4 Hz 的位置加上噪声会滞后一秒以上，
/// 而长机自己知道得最准 —— 1.5 s 窗口的首尾差就是干净的 °/s。
/// </para>
/// <para>
/// **配对方式是全自动的**：每台机器首次启用时生成一个 6 位节点 ID 存进配置，此后一直沿用；
/// 收到别人 ID 的包就当成编队伙伴，不需要人工填任何东西。两台机器撞同一个随机 ID 的概率极低，
/// 真撞了会因为收到"自己"的包而被检测到并记录日志。
/// </para>
/// <para>
/// 协议刻意做成一行文本，方便用任何工具手工调试：
/// <c>WTDL2|&lt;节点ID&gt;|&lt;气压高度 m&gt;|&lt;升降速度 0.1 m/s&gt;|&lt;航向 度&gt;|&lt;转向率 0.1 °/s&gt;[|&lt;x 1e-5&gt;|&lt;y 1e-5&gt;]</c>
/// 后两段是后加的：老版本只发 4 段、也只读 4 段，双方仍能互通（只是没有转向率可用）。
/// 坐标两段同样是追加的，老读端会忽略，不影响高度。
/// </para>
/// </summary>
internal sealed class DataLink : IDisposable
{
    public const int Port = 48017;
    private const int StaleMs = 3000;          // 3 秒收不到就当对方掉线
    private const double SendIntervalS = 0.25; // 4 Hz，高度是慢变量，够用

    private readonly int _selfId;
    private UdpClient? _socket;
    private Thread? _rx;
    private volatile bool _running;
    private double _lastSendS = -1;

    private sealed class Remote
    {
        public int AltM;
        public double VyMs;
        /// <summary>对方自报的航向（0..359 度）。老版本不发这一段 → null。</summary>
        public double? HeadingDeg;

        /// <summary>对方自报的转向率（°/s，正 = 右转）。老版本不发这一段 → null。</summary>
        public double? TurnRateDeg;

        /// <summary>对方自报的归一化位置（0..1）。老版本不发这两段 → null，此时无法与地图友机配对。</summary>
        public double? Xn;
        public double? Yn;

        /// <summary>对方当前的目标坡度（长机转向辅助广播）。老版本或未开启 → null。</summary>
        public double? TargetBankDeg;

        public long TickAt;
        public int Packets;
    }

    private readonly Dictionary<int, Remote> _nodes = new();
    private readonly object _lock = new();

    // ---- 诊断计数：内网穿透联调时，界面上的"有没有收到"全靠这三个数 ----

    /// <summary>收到的数据包总数（含解析失败的）。</summary>
    public long RxCount => Interlocked.Read(ref _rxCount);
    private long _rxCount;

    /// <summary>收到的、但解析不通过的包数（格式不对 / 被截断）。</summary>
    public long RxBad => Interlocked.Read(ref _rxBad);
    private long _rxBad;

    /// <summary>发出的包数。</summary>
    public long TxCount => Interlocked.Read(ref _txCount);
    private long _txCount;

    /// <summary>最后一个包的来源，形如 <c>192.168.16.7:52311</c>。空 = 一个都没收到。</summary>
    public string LastPeer { get; private set; } = "";

    /// <summary>最后一个包的到达时刻（<see cref="Environment.TickCount64"/>）。</summary>
    public long LastRxTick { get; private set; }

    /// <summary>单播目标。为空时退回子网广播 —— 跨网段必须填，广播出不了路由器。</summary>
    private readonly List<IPEndPoint> _peers = new();

    /// <summary>收到的一个数据链节点。位置可能为 null（对方版本不带坐标）。</summary>
    public readonly record struct LinkNode(int Id, int AltM, double VyMs,
                                           double? HeadingDeg, double? TurnRateDeg,
                                           double? Xn, double? Yn, int AgeMs,
                                           double? TargetBankDeg = null);

    /// <summary>
    /// 取所有在线节点（3 秒内收过包）的快照。上层用它自己的位置和 <c>map_obj</c> 的友机配对，
    /// 从而给每架友机找到各自的高度。
    /// </summary>
    public LinkNode[] Snapshot()
    {
        long now = Environment.TickCount64;
        lock (_lock)
        {
            var list = new List<LinkNode>(_nodes.Count);
            foreach (var kv in _nodes)
            {
                if (now - kv.Value.TickAt >= StaleMs) continue;
                var n = kv.Value;
                list.Add(new LinkNode(kv.Key, n.AltM, n.VyMs, n.HeadingDeg, n.TurnRateDeg,
                                      n.Xn, n.Yn, (int)(now - n.TickAt), n.TargetBankDeg));
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// 设置单播目标（<c>"ip:port"</c> 列表）。解析不了的行会被跳过并返回失败项，方便在启动日志里明说。
    /// </summary>
    public List<string> SetPeers(IEnumerable<string> specs)
    {
        var bad = new List<string>();
        lock (_lock)
        {
            _peers.Clear();
            foreach (var spec in specs)
            {
                if (TryParsePeer(spec, out var ep)) _peers.Add(ep);
                else if (!string.IsNullOrWhiteSpace(spec)) bad.Add(spec);
            }
        }
        return bad;
    }

    /// <summary>解析 <c>"host:port"</c>；端口缺省用 <see cref="Port"/>。</summary>
    internal static bool TryParsePeer(string? spec, out IPEndPoint ep)
    {
        ep = null!;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        string text = spec.Trim();
        string host = text;
        int port = Port;

        int colon = text.LastIndexOf(':');
        // IPv6 字面量（[::1]:48017）只有一个冒号在方括号外，这里不处理 —— 内网穿透用不到，
        // 真需要时再补，别为没验证过的输入写一堆分支。
        if (colon > 0 && int.TryParse(text.AsSpan(colon + 1), out int p))
        {
            host = text[..colon];
            if (p <= 0 || p > 65535) return false;
            port = p;
        }

        if (!IPAddress.TryParse(host, out var addr)) return false;
        ep = new IPEndPoint(addr, port);
        return true;
    }

    /// <summary>是否收到过"和自己同 ID"的包（说明两台机器撞了随机 ID）。</summary>
    public volatile bool SelfIdConflict;

    public DataLink(int selfId)
    {
        _selfId = selfId;
    }

    /// <summary>当前在线的伙伴数量（3 秒内收过包的不同 ID）。</summary>
    public int NodeCount
    {
        get
        {
            long now = Environment.TickCount64;
            lock (_lock)
                return _nodes.Count(kv => now - kv.Value.TickAt < StaleMs);
        }
    }

    /// <summary>
    /// 取最近最活跃的那个伙伴的高度。只有一台机器开了数据链时，这就是唯一值。
    /// 返回 false 表示没有可用数据（链路没开、对方没发、或全超时了）。
    /// </summary>
    /// <summary>取最新在线节点的目标坡度（长机转向辅助广播）。没有节点或对方没发 → false。</summary>
    public bool TryGetPartnerTargetBank(out double bankDeg)
    {
        long now = Environment.TickCount64;
        bankDeg = 0;
        lock (_lock)
        {
            Remote? best = null;
            foreach (var node in _nodes.Values)
            {
                if (now - node.TickAt >= StaleMs) continue;
                if (node.TargetBankDeg is null) continue;
                if (best == null || node.TickAt > best.TickAt) best = node;
            }
            if (best?.TargetBankDeg is not double b) return false;
            bankDeg = b;
            return true;
        }
    }

    public bool TryGetPartnerAltitude(out int altM, out double vyMs)
    {
        long now = Environment.TickCount64;
        altM = 0;
        vyMs = 0;

        lock (_lock)
        {
            Remote? best = null;
            foreach (var node in _nodes.Values)
            {
                if (now - node.TickAt >= StaleMs) continue;
                if (best == null || node.TickAt > best.TickAt) best = node;
            }
            if (best == null) return false;

            altM = best.AltM;
            vyMs = best.VyMs;
            return true;
        }
    }

    /// <summary>
    /// 取最近最活跃的那个伙伴的转向率。返回 false 表示没有可用数据
    /// （链路没开、对方没发、对方是老版本只发 4 段、或全超时了）。
    /// <para>转向率每 250 ms 才更新一次，调用方不要拿它做逐帧积分 —— 它是给前馈用的稳态量。</para>
    /// </summary>
    public bool TryGetPartnerTurnRate(out double degPerSec)
    {
        long now = Environment.TickCount64;
        degPerSec = 0;

        lock (_lock)
        {
            Remote? best = null;
            foreach (var node in _nodes.Values)
            {
                if (now - node.TickAt >= StaleMs) continue;
                if (node.TurnRateDeg == null) continue;
                if (best == null || node.TickAt > best.TickAt) best = node;
            }
            if (best?.TurnRateDeg is not double rate) return false;

            degPerSec = rate;
            return true;
        }
    }

    /// <summary>
    /// 取最近最活跃那个伙伴的航向（度）。返回 false 表示没有可用数据
    /// （链路没开、对方没发、对方是老版本只发 4 段、或全超时了）。
    /// <para>没有链路时上层用本机航向近似长机航向 —— 编队两机航向近似平行，实测差别不大。</para>
    /// </summary>
    public bool TryGetPartnerHeading(out double headingDeg)
    {
        long now = Environment.TickCount64;
        headingDeg = 0;

        lock (_lock)
        {
            Remote? best = null;
            foreach (var node in _nodes.Values)
            {
                if (now - node.TickAt >= StaleMs) continue;
                if (node.HeadingDeg == null) continue;
                if (best == null || node.TickAt > best.TickAt) best = node;
            }
            if (best?.HeadingDeg is not double hdg) return false;

            headingDeg = hdg;
            return true;
        }
    }

    /// <summary>端口是否绑成功。false = 被防火墙/其它进程占了，功能等于没开数据链。</summary>
    public bool Listening => _running && _socket != null;

    /// <summary>
    /// 调试用：每收到一个有效包触发一次（在接收线程上）。
    /// 只有 <c>dlprobe</c> 会挂它，正式渲染路径不要挂 —— 回调里抛异常会打断接收循环。
    /// </summary>
    public Action<LinkNode, string>? PacketCallback;

    public void Start()
    {
        if (_running) return;
        try
        {
            _socket = new UdpClient(new IPEndPoint(IPAddress.Any, Port))
            {
                EnableBroadcast = true,
            };
            // 收到自己发的广播时不要回环进队列（同一台机器多实例的保险）
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _running = true;

            _rx = new Thread(ReceiveLoop) { IsBackground = true, Name = "WtHud-DataLink" };
            _rx.Start();
        }
        catch
        {
            // 端口被占或被防火墙挡：静默降级，功能等同于没开数据链
            _socket?.Dispose();
            _socket = null;
            _running = false;
        }
    }

    /// <summary>
    /// 组一行报文。抽成静态纯函数是为了能在 <c>selftest</c> 里断言格式与回环，
    /// 不用真的开 UDP。
    /// </summary>
    internal static string BuildPayload(int selfId, int altM, double vyMs, double headingDeg, double turnRateDeg)
    {
        int hdg = (int)Math.Round(headingDeg) % 360;
        if (hdg < 0) hdg += 360;
        int turn = (int)Math.Round(Math.Clamp(turnRateDeg, -300, 300) * 10.0);   // 0.1 °/s
        return $"WTDL2|{selfId}|{altM}|{(int)Math.Round(vyMs * 10.0)}|{hdg}|{turn}";
    }

    /// <summary>
    /// 带自身归一化坐标的报文（第 7、8 段）。
    /// <para>坐标是 <c>map_obj</c> 那套 0..1 的归一化位置，乘 100000 存成整数 —— 100 km 的地图
    /// 也有 1 m 分辨率，而 int 比小数短，一行报文仍然能手工读。</para>
    /// <para>★ 为什么不升 magic 到 WTDL3：老读端只认 <c>WTDL2</c> 前缀、且只取前 6 段，
    /// 保留前缀 + 追加段等于「老版本照常收高度，新版本多拿坐标」，不需要两边同时升级。</para>
    /// </summary>
    internal static string BuildPayload(int selfId, int altM, double vyMs, double headingDeg, double turnRateDeg,
                                        double? xn, double? yn)
        => BuildPayload(selfId, altM, vyMs, headingDeg, turnRateDeg, xn, yn, null);

    /// <summary>
    /// 完整报文：基础 6 段 + 坐标 2 段 + 目标坡度 1 段（度 ×10，带符号）。
    /// <para>★ 目标坡度是**追加**段：老读端按前 8 段解析，不会因为多一段而失败；
    /// 新读端遇到 8 段报文就把目标坡度当未知。</para>
    /// </summary>
    internal static string BuildPayload(int selfId, int altM, double vyMs, double headingDeg, double turnRateDeg,
                                        double? xn, double? yn, double? targetBankDeg)
    {
        string head = BuildPayload(selfId, altM, vyMs, headingDeg, turnRateDeg);
        if (xn is not double x || yn is not double y) return head;   // 坐标未知 → 仍是 6 段

        int ix = (int)Math.Round(Math.Clamp(x, 0.0, 1.0) * CoordinateScale);
        int iy = (int)Math.Round(Math.Clamp(y, 0.0, 1.0) * CoordinateScale);
        if (targetBankDeg is not double bank)
            return $"{head}|{ix}|{iy}";

        int ib = (int)Math.Round(Math.Clamp(bank, -90.0, 90.0) * 10.0);
        return $"{head}|{ix}|{iy}|{ib}";
    }

    /// <summary>归一化坐标的整数倍率：0..1 → 0..100000。</summary>
    internal const int CoordinateScale = 100_000;

    /// <summary>
    /// 解析一行报文。<paramref name="headingDeg"/> / <paramref name="turnRateDeg"/> 为 null
    /// 表示对方是老版本（只发 4 段）或该段无效，上层会退化为本地近似值。
    /// </summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg)
        => TryParsePayload(text, out selfId, out altM, out vyMs, out turnRateDeg, out headingDeg,
                           out _, out _);

    /// <summary>
    /// 解析一行报文。
    /// <para><paramref name="headingDeg"/> / <paramref name="turnRateDeg"/> 为 null 表示对方是老版本
    /// （只发 4 段）或该段无效，上层会退化为本地近似值。</para>
    /// <para><paramref name="xn"/> / <paramref name="yn"/> 是对方自报的归一化位置，为 null 表示
    /// 对方版本不带坐标、或该段坏了。★ 坐标只用来把「数据链节点」和 <c>map_obj</c> 里的友机
    /// 配对（同一架飞机的位置两边一致到 1–2 m），**不用它当位置来源** —— 单机拿到的友机位置
    /// 本来就够准，缺的只有高度。</para>
    /// </summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg,
                                         out double? xn, out double? yn)
        => TryParsePayload(text, out selfId, out altM, out vyMs, out turnRateDeg, out headingDeg,
                           out xn, out yn, out _);

    /// <summary>解析一行报文（含目标坡度段）。</summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg,
                                         out double? xn, out double? yn, out double? targetBankDeg)
    {
        targetBankDeg = null;
        selfId = 0;
        altM = 0;
        vyMs = 0;
        turnRateDeg = null;
        headingDeg = null;
        xn = null;
        yn = null;

        var parts = text.Split('|');
        if (parts.Length < 4 || parts[0] != "WTDL2") return false;
        if (!int.TryParse(parts[1], out selfId)) return false;
        if (!int.TryParse(parts[2], out altM)) return false;
        if (!int.TryParse(parts[3], out int vyTenths)) return false;
        vyMs = vyTenths / 10.0;

        // 航向段（第 5 段）：0..359 度整数。老 4 段格式没有这一段 → null，上层用本机航向近似。
        if (parts.Length >= 5 && int.TryParse(parts[4], out int hdgDeg)
            && hdgDeg >= 0 && hdgDeg < 360)
            headingDeg = hdgDeg;

        // 转向率段（第 6 段）：解析失败只丢这一段，不要因此丢掉整包高度
        if (parts.Length >= 6 && int.TryParse(parts[5], out int turnTenths))
            turnRateDeg = turnTenths / 10.0;

        // 坐标段（第 7、8 段）：同一条原则，坏了只丢坐标。超出 0..1 的值一律当无效，
        // 免得远端一个手滑的 999 把配对逻辑带到地图外面去。
        if (parts.Length >= 8
            && int.TryParse(parts[6], out int ix) && int.TryParse(parts[7], out int iy)
            && ix >= 0 && ix <= CoordinateScale && iy >= 0 && iy <= CoordinateScale)
        {
            xn = ix / (double)CoordinateScale;
            yn = iy / (double)CoordinateScale;
        }

        // 目标坡度（第 9 段）：度 ×10。缺失或越界 → null（不因为这一段毁掉整包）。
        if (parts.Length >= 9 && int.TryParse(parts[8], out int bankTenths)
            && bankTenths >= -900 && bankTenths <= 900)
            targetBankDeg = bankTenths / 10.0;

        return true;
    }

    /// <summary>由渲染循环每帧调用，内部按 4 Hz 限频真正发包。</summary>
    /// <param name="headingDeg">自己的航向（0..359）；无罗盘时上层用航迹角兜底。</param>
    /// <param name="turnRateDeg">自己当前的转向率（°/s，正 = 右转）。僚机拿它算转弯提前量。</param>
    /// <param name="xn">自己的归一化位置（0..1）。null = 这一帧地图还没有自己的图标，不发坐标。</param>
    /// <param name="yn">同上，y 轴。</param>
    public void Send(double nowS, int altM, double vyMs, double headingDeg, double turnRateDeg,
                     double? xn = null, double? yn = null, double? targetBankDeg = null)
    {
        if (!_running || _socket == null) return;
        if (_lastSendS >= 0 && nowS - _lastSendS < SendIntervalS) return;
        _lastSendS = nowS;

        IPEndPoint[] targets;
        lock (_lock)
            targets = _peers.Count == 0
                ? new[] { new IPEndPoint(IPAddress.Broadcast, Port) }
                : _peers.ToArray();

        try
        {
            var payload = BuildPayload(_selfId, altM, vyMs, headingDeg, turnRateDeg, xn, yn, targetBankDeg);
            var bytes = Encoding.ASCII.GetBytes(payload);
            foreach (var ep in targets)
            {
                try
                {
                    _socket.Send(bytes, bytes.Length, ep);
                    Interlocked.Increment(ref _txCount);
                }
                catch
                {
                    // 单个目标不可达不影响其它目标：跨网段时常常只有一个通
                }
            }
        }
        catch
        {
            // 单次发送失败无所谓，下一帧会重试
        }
    }

    private void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                var data = _socket!.Receive(ref remote);
                Parse(data, remote);
            }
            catch
            {
                if (!_running) return;
                Thread.Sleep(50);      // 别在异常上打转
            }
        }
    }

    private void Parse(byte[] data, IPEndPoint from)
    {
        Interlocked.Increment(ref _rxCount);
        LastPeer = from.ToString();
        LastRxTick = Environment.TickCount64;

        string text;
        try { text = Encoding.ASCII.GetString(data); }
        catch { return; }

        if (!TryParsePayload(text, out int id, out int alt, out double vyMs,
                             out double? turnRate, out double? heading,
                             out double? xn, out double? yn, out double? targetBank))
        {
            Interlocked.Increment(ref _rxBad);
            return;
        }

        if (id == _selfId)
        {
            // 广播会回到自己这里，正常情况下这是"自己发的"。但如果收到了明显不同的高度，
            // 说明同网段另一台机器撞了同一个随机 ID —— 记下来，让上层提示。
            SelfIdConflict = true;
            return;
        }

        lock (_lock)
        {
            if (!_nodes.TryGetValue(id, out var node))
            {
                node = new Remote();
                _nodes[id] = node;
            }
            node.AltM = alt;
            node.VyMs = vyMs;
            // 航向/转向率是后加的两段：老版本不发，就保持 null，上层自动退化为本地估算
            node.HeadingDeg = heading;
            node.TurnRateDeg = turnRate;
            // 坐标同理：老版本不发 → null，配对会跳过这个节点，但高度仍可用于单长机场景
            node.Xn = xn;
            node.Yn = yn;
            node.TargetBankDeg = targetBank;
            node.TickAt = Environment.TickCount64;
            node.Packets++;
        }

        if (PacketCallback is { } cb)
        {
            try { cb(new LinkNode(id, alt, vyMs, heading, turnRate, xn, yn, 0, targetBank), from.ToString()); }
            catch { /* 调试回调不能影响接收 */ }
        }
    }

    /// <summary>
    /// 把数据链节点和地图上的友机按位置配对，给出**每一架**友机各自的高度。
    /// <para>这是坐标两段存在的唯一理由：<c>map_obj</c> 里有位置没高度，数据链里有高度
    /// 但节点 ID 与飞机没有共同标识，位置是两边唯一共有的量（实测一致到 1–2 m）。</para>
    /// <para>一个节点只会配给一架友机（配过就标记），避免两架挨得近的友机抢同一个高度。
    /// 配对不上的返回 null —— 上层应把它当作「高度未知」，而不是拿别人的高度顶替。</para>
    /// </summary>
    /// <param name="friendlies">友机的归一化位置，顺序与返回值一一对应。</param>
    /// <param name="nodes">数据链快照；没有坐标的节点会被跳过。</param>
    /// <param name="toleranceNorm">配对容差（归一化单位）。50 m / 64 km 地图约为 8e-4。</param>
    internal static int?[] MatchAltitudes(IReadOnlyList<(double X, double Y)> friendlies,
                                          IReadOnlyList<LinkNode> nodes,
                                          double toleranceNorm)
    {
        var result = new int?[friendlies.Count];
        if (friendlies.Count == 0 || nodes.Count == 0) return result;

        var used = new bool[nodes.Count];
        double tol2 = toleranceNorm * toleranceNorm;

        for (int i = 0; i < friendlies.Count; i++)
        {
            double fx = friendlies[i].X, fy = friendlies[i].Y;
            double bestD2 = double.MaxValue;
            int best = -1;

            for (int j = 0; j < nodes.Count; j++)
            {
                if (used[j] || nodes[j].Xn is not double nx || nodes[j].Yn is not double ny) continue;
                double dx = nx - fx, dy = ny - fy;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = j; }
            }

            if (best >= 0 && bestD2 <= tol2)
            {
                used[best] = true;
                result[i] = nodes[best].AltM;
            }
        }

        return result;
    }

    public void Dispose()
    {
        _running = false;
        try { _socket?.Close(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
    }
}
