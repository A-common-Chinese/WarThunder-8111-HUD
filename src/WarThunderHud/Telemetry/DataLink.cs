// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WtHud.Telemetry;

/// <summary>
/// 极简数据链：**只广播自己的气压高度**。
/// <para>
/// 位置不需要共享 —— 单机拿到的友机位置与对方自报值一致到 1–2 m（实测 27 m–31 km 全段），
/// 而高度在 <c>map_obj</c> 里根本没有 z，单机无论如何拿不到。高度又恰好是慢变量
/// （Vy 通常 0–5 m/s），100 ms 延迟只差半米，所以它对时间同步几乎没有要求，
/// 是整个数据链里性价比最高的一项。
/// </para>
/// <para>
/// **配对方式是全自动的**：每台机器首次启用时生成一个 6 位节点 ID 存进配置，此后一直沿用；
/// 收到别人 ID 的包就当成编队伙伴，不需要人工填任何东西。两台机器撞同一个随机 ID 的概率极低，
/// 真撞了会因为收到"自己"的包而被检测到并记录日志。
/// </para>
/// <para>
/// 协议刻意做成一行文本，方便用任何工具手工调试：
/// <c>WTDL2|&lt;节点ID&gt;|&lt;气压高度 m&gt;|&lt;升降速度 0.1 m/s&gt;</c>
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

    /// <summary>由渲染循环每帧调用，内部按 4 Hz 限频真正发包。</summary>
    public void Send(double nowS, int altM, double vyMs)
    {
        if (!_running || _socket == null) return;
        if (_lastSendS >= 0 && nowS - _lastSendS < SendIntervalS) return;
        _lastSendS = nowS;

        try
        {
            var payload = $"WTDL2|{_selfId}|{altM}|{(int)Math.Round(vyMs * 10.0)}";
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

        var parts = text.Split('|');
        if (parts.Length < 4 || parts[0] != "WTDL2") return;
        if (!int.TryParse(parts[1], out int id)) return;
        if (!int.TryParse(parts[2], out int alt)) return;
        if (!int.TryParse(parts[3], out int vyTenths)) return;

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
            node.VyMs = vyTenths / 10.0;
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
