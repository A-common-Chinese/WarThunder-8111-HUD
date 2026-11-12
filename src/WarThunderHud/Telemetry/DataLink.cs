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
/// <c>WTDL2|&lt;节点ID&gt;|&lt;气压高度 m&gt;|&lt;升降速度 0.1 m/s&gt;|&lt;航向 度&gt;|&lt;转向率 0.1 °/s&gt;[|&lt;x 1e-5&gt;|&lt;y 1e-5&gt;[|&lt;坡度 0.1°&gt;[|&lt;角色&gt;]]]</c>
/// 后几段都是后加的：老版本只发 4 段、也只读 4 段，双方仍能互通（只是没有转向率可用）。
/// 坐标两段同样是追加的，老读端会忽略，不影响高度；角色（1 长机 / 2 僚机）是第 10 段。
/// <para>
/// ★ 坐标拿不到时第 7、8 段留**空**（<c>||||</c>），段位不挪：角色/站位照发，
/// 长机才知道这架是僚机、只是没有位置，从而显示「数据错误」而不是当它不存在。
/// 第 11 段是站位 <c>槽位,状态,误差m</c>，状态 <c>-1</c> = 在编队里但本帧无判定。
/// </para>
/// <para>
/// 套接字是**双栈**的（IPv6Any + DualMode）：同一个端口同时收 IPv4 和 IPv6。
/// 两边宽带都给 IPv6 时可以直接填对方的 v6 地址，不需要任何组网软件；
/// 没有 v6 的环境里 v4 走 v4-mapped 地址，行为与以前完全一致。
/// </para>
/// </para>
/// </summary>
internal sealed class DataLink : IDisposable
{
    public const int Port = 48017;
    private const int StaleMs = 3000;          // 3 秒收不到就当对方掉线
    private const double SendIntervalS = 0.25; // 4 Hz，高度是慢变量，够用
    private const int MaxPayloadBytes = 256;   // 一行文本撑死 60 字节，超了必是畸形包

    /// <summary>本机在编队里的角色。写到报文的第 10 段，僚机端靠它认出谁是长机。</summary>
    public enum LinkRole { Unknown = 0, Lead = 1, Wing = 2 }

    /// <summary>
    /// 僚机自报的站位状态（报文第 11 段）。
    /// <para>★ 由僚机自己判定：它知道自己的槽位和误差，长机不知道。长机只负责把结果列出来，
    /// 不参与判定 —— 否则两边阈值不一致时，僚机会看到「我在编队里」而长机显示「掉队」。</para>
    /// </summary>
    public enum WingState
    {
        /// <summary>老版本，或这台没开编队辅助 —— 没有可用的站位信息。</summary>
        Unknown = -1,
        /// <summary>在槽位上。</summary>
        InPosition = 0,
        /// <summary>比槽位更远（掉队）。</summary>
        Behind = 1,
        /// <summary>比槽位更近（冲到前面）。</summary>
        Ahead = 2,
        /// <summary>距离差不多但方位偏了，没站在槽位方向上。</summary>
        OffStation = 3,
    }

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

        /// <summary>对方自报的编队角色。老版本不发第 10 段 → Unknown，选择逻辑退回「最近活跃」。</summary>
        public LinkRole Role = LinkRole.Unknown;

        /// <summary>对方自报的站位状态（第 11 段）。老版本/未开编队 → Unknown。</summary>
        public WingState Wing = WingState.Unknown;

        /// <summary>对方自报的槽位序号（第 11 段第 1 子段），-1 = 未知。</summary>
        public int Slot = -1;

        /// <summary>对方自报的站位距离误差（米，正 = 比槽位远）。</summary>
        public int ErrM;

        /// <summary>
        /// 这一包是从哪个地址来的。<see cref="Send"/> 用它回包 —— 长机不必把每个僚机的
        /// IP 都手填一遍，谁发过来就回给谁。
        /// </summary>
        public IPEndPoint? LastFrom;

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

    private int _lastTargetCount;

    /// <summary>
    /// 上一包的**实际**发送目标数 = 手填的地址 + 在线节点的来源地址。
    /// <para>面板上的「发送目标」读这个：三人编队时它应该等于 2，而不是手填的 1。</para>
    /// </summary>
    public int TargetCount => _lastTargetCount;

    /// <summary>收到的一个数据链节点。位置可能为 null（对方版本不带坐标）。</summary>
    public readonly record struct LinkNode(int Id, int AltM, double VyMs,
                                           double? HeadingDeg, double? TurnRateDeg,
                                           double? Xn, double? Yn, int AgeMs,
                                           double? TargetBankDeg = null,
                                           LinkRole Role = LinkRole.Unknown,
                                           int Packets = 0,
                                           WingState Wing = WingState.Unknown,
                                           int Slot = -1,
                                           int ErrM = 0);

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
                                      n.Xn, n.Yn, (int)(now - n.TickAt), n.TargetBankDeg,
                                      n.Role, n.Packets, n.Wing, n.Slot, n.ErrM));
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

    /// <summary>
    /// 解析 <c>"host[:port]"</c>；端口缺省用 <see cref="Port"/>。
    /// <para>支持三种写法：<c>192.168.1.7:48017</c>、<c>[2001:db8::1]:48017</c>、
    /// <c>2001:db8::1</c>（裸 IPv6，端口用默认）。</para>
    /// <para>★ 裸 IPv6 有多个冒号，不能按「最后一个冒号切端口」——那会把 <c>::1</c> 切成
    /// <c>:</c> 和 <c>1</c>。所以先看方括号，再看冒号个数。</para>
    /// </summary>
    internal static bool TryParsePeer(string? spec, out IPEndPoint ep)
    {
        ep = null!;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        string text = spec.Trim();
        int port = Port;
        string host;

        if (text.StartsWith("["))
        {
            // [v6] 或 [v6]:port
            int close = text.IndexOf(']');
            if (close < 0) return false;
            host = text[1..close];
            string rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':' || !int.TryParse(rest.AsSpan(1), out port)) return false;
                if (port <= 0 || port > 65535) return false;
            }
        }
        else
        {
            int first = text.IndexOf(':');
            int last = text.LastIndexOf(':');
            if (first >= 0 && first == last)
            {
                // 恰好一个冒号 = host:port
                if (!int.TryParse(text.AsSpan(last + 1), out port)) return false;
                if (port <= 0 || port > 65535) return false;
                host = text[..last];
            }
            else
            {
                // 零个或多个冒号：整串当地址，端口用默认（多冒号必然是裸 IPv6）
                host = text;
            }
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

    /// <summary>长机是怎么认出来的。面板要显示这个，认不出来时用户才知道该手填 ID。</summary>
    public enum LeadSource
    {
        /// <summary>没有任何可用节点。</summary>
        None,
        /// <summary>配置里手填的长机 ID（dataLinkLeadId）。</summary>
        ByConfig,
        /// <summary>对方自报 role=1，且只有一个节点这么报。</summary>
        ByRole,
        /// <summary>场上只有一个节点，天然就是它（老版本没有 role 段时的主路径）。</summary>
        SingleNode,
        /// <summary>多个候选，退回「最近活跃」——有歧义，调用方要谨慎。</summary>
        Newest,
    }

    private int _leadId;      // 0 = 自动

    /// <summary>手填的长机节点 ID（0 = 自动）。由上层在配置变更时写入。</summary>
    public int LeadId
    {
        get => _leadId;
        set => _leadId = value;
    }

    /// <summary>
    /// 选出「编队长机」这一个节点。
    /// <para>规则按优先级：手填 ID → 唯一 role=1 → 场上只有一个节点 → 最近活跃（有歧义）。</para>
    /// <para>★ 三条理由：一是 <c>map_obj</c> 里没有呼号，节点 ID 是随机数，没有 role 时根本分不清
    /// 谁是谁；二是三方编队时「最近活跃」会随开火/机动来回跳，高度值跟着跳；三是手填 ID 是
    /// 自动识别失败时的唯一强制手段，所以它排第一。</para>
    /// </summary>
    /// <param name="ambiguous">true = 候选不止一个、退回了最近活跃，上层应给出提示。</param>
    public bool TryGetLeadNode(out LinkNode lead, out LeadSource source, out bool ambiguous)
    {
        lead = default;
        source = LeadSource.None;
        ambiguous = false;

        long now = Environment.TickCount64;
        lock (_lock)
        {
            var entries = new List<(int Id, int Role, long TickAt)>(_nodes.Count);
            var remotes = new List<Remote>(_nodes.Count);
            foreach (var kv in _nodes)
            {
                entries.Add((kv.Key, (int)kv.Value.Role, kv.Value.TickAt));
                remotes.Add(kv.Value);
            }

            var (idx, src, amb) = SelectLead(entries, _leadId, now);
            if (idx < 0) return false;

            var r = remotes[idx];
            source = src;
            ambiguous = amb;
            lead = new LinkNode(entries[idx].Id, r.AltM, r.VyMs, r.HeadingDeg, r.TurnRateDeg,
                                r.Xn, r.Yn, (int)(now - r.TickAt), r.TargetBankDeg,
                                r.Role, r.Packets, r.Wing, r.Slot, r.ErrM);
            return true;
        }
    }

    /// <summary>
    /// 「谁是长机」的纯函数版本：只吃节点清单，不碰锁、不碰套接字，方便在 selftest 里断言。
    /// <para>返回 -1 表示没有可用节点（或手填的 ID 不在线）。</para>
    /// <para>★ 多条规则里唯一会「返回一个不确定答案」的是 <see cref="LeadSource.Newest"/>：
    /// 候选不止一个时只能挑最近活跃的，调用方必须看 <c>ambiguous</c> 决定要不要信它。</para>
    /// </summary>
    internal static (int Index, LeadSource Source, bool Ambiguous) SelectLead(
        IReadOnlyList<(int Id, int Role, long TickAt)> nodes, int leadId, long now)
    {
        var fresh = new List<int>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
            if (now - nodes[i].TickAt < StaleMs) fresh.Add(i);
        if (fresh.Count == 0) return (-1, LeadSource.None, false);

        if (leadId != 0)
        {
            foreach (int i in fresh)
                if (nodes[i].Id == leadId) return (i, LeadSource.ByConfig, false);
            // 手填了就必须认这个 ID：认不到就说认不到，绝不拿别人的高度顶替
            return (-1, LeadSource.None, false);
        }

        int leadIdx = -1, leadCount = 0;
        foreach (int i in fresh)
            if (nodes[i].Role == (int)LinkRole.Lead) { leadIdx = i; leadCount++; }
        if (leadCount == 1) return (leadIdx, LeadSource.ByRole, false);
        if (fresh.Count == 1) return (fresh[0], LeadSource.SingleNode, false);

        // 多个 role=1（两台都开了长机模式），或者全都没报角色：只能取最近活跃
        int best = fresh[0];
        foreach (int i in fresh)
            if (nodes[i].TickAt > nodes[best].TickAt) best = i;
        return (best, LeadSource.Newest, true);
    }

    /// <summary>取长机节点（不需要知道来源时用这个）。</summary>
    public bool TryGetLeadNode(out LinkNode lead) => TryGetLeadNode(out lead, out _, out _);

    /// <summary>取长机的目标坡度（长机转向辅助广播）。认不出长机或对方没发 → false。</summary>
    public bool TryGetPartnerTargetBank(out double bankDeg)
    {
        bankDeg = 0;
        if (!TryGetLeadNode(out var lead)) return false;
        if (lead.TargetBankDeg is not double b) return false;
        bankDeg = b;
        return true;
    }

    public bool TryGetPartnerAltitude(out int altM, out double vyMs)
        => TryGetPartnerAltitude(out altM, out vyMs, out _);

    /// <summary>取长机高度，并给出这个包的年龄（秒）。上层用它判断「新鲜」还是「保留值」。</summary>
    public bool TryGetPartnerAltitude(out int altM, out double vyMs, out double ageS)
    {
        altM = 0;
        vyMs = 0;
        ageS = 0;
        if (!TryGetLeadNode(out var lead)) return false;
        altM = lead.AltM;
        vyMs = lead.VyMs;
        ageS = lead.AgeMs / 1000.0;
        return true;
    }

    /// <summary>
    /// 取长机的转向率。返回 false 表示没有可用数据（链路没开、对方没发、老版本只发 4 段）
    /// **或者长机认不准**（多个候选退回最近活跃）。
    /// <para>★ 认不准时必须返回 false：这个值直接进转弯前馈，认错人会让僚机往反方向补油门。</para>
    /// <para>转向率每 250 ms 才更新一次，调用方不要拿它做逐帧积分 —— 它是给前馈用的稳态量。</para>
    /// </summary>
    public bool TryGetPartnerTurnRate(out double degPerSec)
    {
        degPerSec = 0;
        if (!TryGetLeadNode(out var lead, out var source, out bool ambiguous)) return false;
        if (ambiguous && source == LeadSource.Newest) return false;
        if (lead.TurnRateDeg is not double rate) return false;
        degPerSec = rate;
        return true;
    }

    /// <summary>
    /// 取长机航向（度）。没有链路时上层用本机航向近似 —— 编队两机航向近似平行，实测差别不大。
    /// </summary>
    public bool TryGetPartnerHeading(out double headingDeg)
    {
        headingDeg = 0;
        if (!TryGetLeadNode(out var lead)) return false;
        if (lead.HeadingDeg is not double hdg) return false;
        headingDeg = hdg;
        return true;
    }

    /// <summary>在线节点列表，按包龄从新到旧。面板用它显示「谁在线、谁的包在掉」。</summary>
    public LinkNode[] OnlineNodes()
        => Snapshot().OrderBy(n => n.AgeMs).ToArray();

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
            // 双栈：一个套接字同时收 IPv4 和 IPv6。IPv6 直连（两边宽带都有 v6）时不需要任何
            // 组网软件；没有 v6 的环境里，v4 走 v4-mapped 地址，行为和以前完全一样。
            _socket = new UdpClient(AddressFamily.InterNetworkV6);
            _socket.Client.DualMode = true;
            _socket.Client.EnableBroadcast = true;
            // 收到自己发的广播时不要回环进队列（同一台机器多实例的保险）
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
        }
        catch
        {
            // 系统禁用了 IPv6（或组策略关掉双栈）时退回纯 v4 —— 老机器不该因为这条起不来
            try
            {
                _socket?.Dispose();
                _socket = new UdpClient(new IPEndPoint(IPAddress.Any, Port)) { EnableBroadcast = true };
            }
            catch
            {
                // 端口被占或被防火墙挡：静默降级，功能等同于没开数据链
                _socket?.Dispose();
                _socket = null;
                _running = false;
                return;
            }
        }

        _running = true;
        _rx = new Thread(ReceiveLoop) { IsBackground = true, Name = "WtHud-DataLink" };
        _rx.Start();
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
    /// 完整报文：基础 6 段 + 坐标 2 段 + 目标坡度 1 段（度 ×10，带符号）+ 角色 + 站位。
    /// <para>★ 目标坡度是**追加**段：老读端按前 8 段解析，不会因为多一段而失败；
    /// 新读端遇到 8 段报文就把目标坡度当未知。</para>
    /// <para>★ 坐标未知（自己的 map_obj 还没给出自机图标）时**不能提前截断**：留两个空段占位，
    /// 角色和站位照发。否则长机收到的只是一包「有高度、没身份」的数据，连这架是不是僚机都认不出，
    /// 只能当它不存在 —— 「数据错误」也就无从显示。</para>
    /// </summary>
    internal static string BuildPayload(int selfId, int altM, double vyMs, double headingDeg, double turnRateDeg,
                                        double? xn, double? yn, double? targetBankDeg,
                                        LinkRole role = LinkRole.Unknown,
                                        int slot = -1, WingState wing = WingState.Unknown, int errM = 0)
    {
        string head = BuildPayload(selfId, altM, vyMs, headingDeg, turnRateDeg);
        bool hasPos = xn is double && yn is double;

        // 既没坐标又没身份：保持与旧版完全一致的 6 段（纯高度包）
        if (!hasPos && role == LinkRole.Unknown) return head;

        double x = xn ?? 0.0, y = yn ?? 0.0;
        string tail;
        if (hasPos)
        {
            int ix = (int)Math.Round(Math.Clamp(x, 0.0, 1.0) * CoordinateScale);
            int iy = (int)Math.Round(Math.Clamp(y, 0.0, 1.0) * CoordinateScale);
            tail = $"{head}|{ix}|{iy}";
        }
        else
        {
            // 空段占位：段位不能挪，role 必须永远落在第 10 段
            tail = $"{head}||";
        }

        if (targetBankDeg is double bank)
            tail += "|" + (int)Math.Round(Math.Clamp(bank, -90.0, 90.0) * 10.0);
        // ★ 第 10 段（角色）：只要身份已知就发，与坐标是否存在无关。
        if (role != LinkRole.Unknown)
        {
            // 坡度段缺失时用空段占位，保证 role 永远落在第 10 段 —— 解析端按位置取值
            if (targetBankDeg is not double) tail += "|";
            tail += "|" + (int)role;

            // 第 11 段：僚机自报站位 `槽位,状态,误差m`。
            // ★ 三个数挤在一段里用逗号分开，而不是再占三段：段位越往后越容易和别处的
            //   解析约定打架，聚成一段之后老读端忽略整段就行，报文也还看得懂。
            // ★ 槽位已知就发，哪怕状态是 Unknown（=-1）：那表示「我在编队里，但这一帧算不出
            //   自己在哪」—— 长机靠这一点显示「数据错误」。不发等于让长机以为这架没参加编队。
            if (slot >= 0)
                tail += $"|{slot},{(int)wing},{Math.Clamp(errM, -9999, 9999)}";
        }
        return tail;
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
        => TryParsePayload(text, out selfId, out altM, out vyMs, out turnRateDeg, out headingDeg,
                           out xn, out yn, out targetBankDeg, out _);

    /// <summary>解析一行报文（含角色段）。<paramref name="role"/> 为 Unknown 表示对方是老版本。</summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg,
                                         out double? xn, out double? yn, out double? targetBankDeg,
                                         out LinkRole role)
        => TryParsePayload(text, out selfId, out altM, out vyMs, out turnRateDeg, out headingDeg,
                           out xn, out yn, out targetBankDeg, out role,
                           out _, out _, out _);

    /// <summary>
    /// 解析一行报文（含角色段与僚机站位段）。
    /// <para><paramref name="wing"/> 为 Unknown 表示对方没发第 11 段（老版本，或它没开编队辅助）；
    /// 此时 <paramref name="slot"/> = -1、<paramref name="errM"/> = 0。</para>
    /// </summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg,
                                         out double? xn, out double? yn, out double? targetBankDeg,
                                         out LinkRole role, out int slot, out WingState wing, out int errM)
    {
        targetBankDeg = null;
        role = LinkRole.Unknown;
        slot = -1;
        wing = WingState.Unknown;
        errM = 0;
        selfId = 0;
        altM = 0;
        vyMs = 0;
        turnRateDeg = null;
        headingDeg = null;
        xn = null;
        yn = null;

        // 首尾的空白 / BOM 不该让整包作废：手工用 echo 或脚本造包时很容易带进来
        var parts = text.Trim().Trim('\uFEFF').Split('|');
        if (parts.Length < 4 || parts[0] != "WTDL2") return false;
        if (!int.TryParse(parts[1], out selfId)) return false;
        // ID 是 6 位随机数，越界的一律当坏包：它会被当成节点键，脏值会污染整个节点表
        if (selfId < 1 || selfId > 999_999) return false;
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

        // 角色（第 10 段）：0/1/2。老版本只有 9 段 → Unknown，选择逻辑退回「最近活跃」。
        if (parts.Length >= 10 && int.TryParse(parts[9], out int roleVal)
            && roleVal >= 0 && roleVal <= 2)
            role = (LinkRole)roleVal;

        // 站位（第 11 段）：`槽位,状态,误差m`。任何一段不合法就整段当未知 ——
        // 但**不因此丢掉整包**：高度和角色仍然有效，长机不该因为一个脏子段就看不见这架飞机。
        // ★ 状态允许 -1（Unknown）：那是「我在编队里，但这一帧算不出位置」，长机据此显示
        //   「数据错误」。和「整段缺失」（= 这架没开编队）必须区分开，靠的就是槽位有没有来。
        if (parts.Length >= 11)
        {
            var st = parts[10].Split(',');
            if (st.Length == 3
                && int.TryParse(st[0], out int slotVal) && slotVal >= 0 && slotVal <= 8
                && int.TryParse(st[1], out int wingVal) && wingVal >= -1 && wingVal <= 3
                && int.TryParse(st[2], out int errVal) && errVal >= -9999 && errVal <= 9999)
            {
                slot = slotVal;
                wing = (WingState)wingVal;
                errM = errVal;
            }
        }

        return true;
    }

    /// <summary>由渲染循环每帧调用，内部按 4 Hz 限频真正发包。</summary>
    /// <param name="headingDeg">自己的航向（0..359）；无罗盘时上层用航迹角兜底。</param>
    /// <param name="turnRateDeg">自己当前的转向率（°/s，正 = 右转）。僚机拿它算转弯提前量。</param>
    /// <param name="xn">自己的归一化位置（0..1）。null = 这一帧地图还没有自己的图标，不发坐标。</param>
    /// <param name="yn">同上，y 轴。</param>
    public void Send(double nowS, int altM, double vyMs, double headingDeg, double turnRateDeg,
                     double? xn = null, double? yn = null, double? targetBankDeg = null,
                     LinkRole role = LinkRole.Unknown,
                     int slot = -1, WingState wing = WingState.Unknown, int errM = 0)
    {
        if (!_running || _socket == null) return;
        if (_lastSendS >= 0 && nowS - _lastSendS < SendIntervalS) return;
        _lastSendS = nowS;

        IPEndPoint[] targets;
        lock (_lock)
        {
            long now = Environment.TickCount64;
            var sources = new List<IPEndPoint?>(_nodes.Count);
            foreach (var kv in _nodes)
                sources.Add(now - kv.Value.TickAt < StaleMs ? kv.Value.LastFrom : null);

            targets = MergeTargets(_peers, sources);
            _lastTargetCount = targets.Length;
        }

        try
        {
            var payload = BuildPayload(_selfId, altM, vyMs, headingDeg, turnRateDeg, xn, yn, targetBankDeg,
                                       role, slot, wing, errM);
            var bytes = Encoding.ASCII.GetBytes(payload);
            foreach (var ep in targets)
            {
                try
                {
                    _socket.Send(bytes, bytes.Length, ForSocket(ep, _socket.Client));
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

    /// <summary>
    /// 合并本包要发给谁：手填的对端地址 + 最近 3 秒内给我们发过包的节点来源。
    /// <para>★ 回包是三人编队的关键：长机只填一个僚机地址时，另一个僚机本来永远收不到；
    /// 现在只要它给长机发过一包，长机就会把它加进目标表。</para>
    /// <para>抽成纯函数是为了能在 selftest 里断言去重和广播回退，不必真开两台机器。</para>
    /// </summary>
    internal static IPEndPoint[] MergeTargets(IReadOnlyList<IPEndPoint> peers,
                                              IReadOnlyList<IPEndPoint?> nodeSources)
    {
        var list = new List<IPEndPoint>(peers.Count + nodeSources.Count + 1);
        list.AddRange(peers);

        foreach (var ep in nodeSources)
        {
            if (ep is null) continue;
            bool dup = false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Equals(ep)) { dup = true; break; }
            if (!dup) list.Add(ep);
        }

        // 一个目标都没有时退回广播（同一局域网、没填地址的默认行为）
        if (list.Count == 0) list.Add(new IPEndPoint(IPAddress.Broadcast, Port));
        return list.ToArray();
    }

    /// <summary>v4 端点要映射成 v4-mapped v6，双栈套接字才肯发；v6 端点原样。</summary>
    private static IPEndPoint ForSocket(IPEndPoint ep, Socket socket)
        => socket.AddressFamily == AddressFamily.InterNetworkV6 && ep.AddressFamily == AddressFamily.InterNetwork
            ? new IPEndPoint(ep.Address.MapToIPv6(), ep.Port)
            : ep;

    /// <summary>显示与回调里把 v4-mapped 还原成 v4，日志里才不会出现 ::ffff:192.168.1.7。</summary>
    private static IPEndPoint Unmap(IPEndPoint ep)
        => ep.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(ep.Address.MapToIPv4(), ep.Port) : ep;

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
        LastPeer = Unmap(from).ToString();
        LastRxTick = Environment.TickCount64;

        // 正常报文 50 字节上下，超过 256 只可能是畸形包或别的协议串进来了 ——
        // 先挡掉再解码，免得一个超长包让 Split 分配一堆无用的字符串。
        if (data.Length > MaxPayloadBytes)
        {
            Interlocked.Increment(ref _rxBad);
            return;
        }

        string text;
        try { text = Encoding.ASCII.GetString(data); }
        catch { return; }

        if (!TryParsePayload(text, out int id, out int alt, out double vyMs,
                             out double? turnRate, out double? heading,
                             out double? xn, out double? yn, out double? targetBank,
                             out LinkRole role, out int slot, out WingState wing, out int errM))
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
            node.Role = role;
            node.Wing = wing;
            node.Slot = slot;
            node.ErrM = errM;
            // 记住来源，回包时能直接发回去（长机靠这个自动覆盖所有僚机）
            node.LastFrom = from;
            node.TickAt = Environment.TickCount64;
            node.Packets++;
        }

        if (PacketCallback is { } cb)
        {
            try { cb(new LinkNode(id, alt, vyMs, heading, turnRate, xn, yn, 0, targetBank, role,
                                 0, wing, slot, errM), Unmap(from).ToString()); }
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
        var idx = MatchNodeIndex(friendlies, nodes, toleranceNorm);
        var alt = new int?[idx.Length];
        for (int i = 0; i < idx.Length; i++)
            if (idx[i] is int k) alt[i] = nodes[k].AltM;
        return alt;
    }

    /// <summary>
    /// 同 <see cref="MatchAltitudes"/>，但返回的是**节点下标**而不是高度。
    /// <para>引导层要的是「地图上哪一架是数据链里的长机」，不是「它有多高」——
    /// 三机以上时这一步决定了圈和转向指向谁。</para>
    /// <para>贪心一对一：一架友机只配一个节点，先配近的。编队间距几百米，
    /// 而容差是几十米，不会出现互相抢。</para>
    /// </summary>
    internal static int?[] MatchNodeIndex(IReadOnlyList<(double X, double Y)> friendlies,
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
                result[i] = best;
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
