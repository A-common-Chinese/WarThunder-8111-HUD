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
/// 位置不共享 —— 单机拿到的友机位置与对方自报值一致到 1–2 m（实测 27 m–31 km 全段），
/// 而高度在 <c>map_obj</c> 里根本没有 z，单机无论如何拿不到。高度又恰好是慢变量
/// （Vy 通常 0–5 m/s），100 ms 延迟只差半米，所以它对时间同步几乎没有要求。
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
/// <c>WTDL2|&lt;节点ID&gt;|&lt;气压高度 m&gt;|&lt;升降速度 0.1 m/s&gt;|&lt;航向 度&gt;|&lt;转向率 0.1 °/s&gt;</c>
/// 后两段是后加的：老版本只发 4 段、也只读 4 段，双方仍能互通（只是没有转向率可用）。
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
        public long TickAt;
    }

    private readonly Dictionary<int, Remote> _nodes = new();
    private readonly object _lock = new();

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
    /// 解析一行报文。<paramref name="headingDeg"/> / <paramref name="turnRateDeg"/> 为 null
    /// 表示对方是老版本（只发 4 段）或该段无效，上层会退化为本地近似值。
    /// </summary>
    internal static bool TryParsePayload(string text, out int selfId, out int altM, out double vyMs,
                                         out double? turnRateDeg, out double? headingDeg)
    {
        selfId = 0;
        altM = 0;
        vyMs = 0;
        turnRateDeg = null;
        headingDeg = null;

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

        return true;
    }

    /// <summary>由渲染循环每帧调用，内部按 4 Hz 限频真正发包。</summary>
    /// <param name="headingDeg">自己的航向（0..359）；无罗盘时上层用航迹角兜底。</param>
    /// <param name="turnRateDeg">自己当前的转向率（°/s，正 = 右转）。僚机拿它算转弯提前量。</param>
    public void Send(double nowS, int altM, double vyMs, double headingDeg, double turnRateDeg)
    {
        if (!_running || _socket == null) return;
        if (_lastSendS >= 0 && nowS - _lastSendS < SendIntervalS) return;
        _lastSendS = nowS;

        try
        {
            var payload = BuildPayload(_selfId, altM, vyMs, headingDeg, turnRateDeg);
            var bytes = Encoding.ASCII.GetBytes(payload);
            _socket.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port));
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
                Parse(data);
            }
            catch
            {
                if (!_running) return;
                Thread.Sleep(50);      // 别在异常上打转
            }
        }
    }

    private void Parse(byte[] data)
    {
        string text;
        try { text = Encoding.ASCII.GetString(data); }
        catch { return; }

        if (!TryParsePayload(text, out int id, out int alt, out double vyMs,
                             out double? turnRate, out double? heading)) return;

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
            node.TickAt = Environment.TickCount64;
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _socket?.Close(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
    }
}
