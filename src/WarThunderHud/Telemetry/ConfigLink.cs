// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WtHud.Hud;

namespace WtHud.Telemetry;

/// <summary>
/// 收到的一份配置包。
/// <para>只带「谁发的 + 编队配置」，不带任何键位/显示/节点设置 —— 那些是每台机器自己的事。</para>
/// </summary>
public sealed class ConfigOffer
{
    public string Address { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public FormationProfile Profile { get; init; }

    /// <summary>发送方带的机场锚点；空 = 没带，无法校验队标。</summary>
    public bool HasAnchor { get; init; }
    public double AnchorXn { get; init; }
    public double AnchorYn { get; init; }
    public bool AnchorFriendly { get; init; }

    /// <summary>队标与本机一致（true）。false 可能是「没带锚点」或「刚进图还没有机场表」。</summary>
    public bool TeamVerified { get; init; }

    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
    public double AgeS => Math.Max(0, (DateTime.UtcNow - AtUtc).TotalSeconds);

    /// <summary>列表里的称呼：有昵称用昵称，没有就用 IP。</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}

/// <summary>
/// 配置包的构建与解析（纯函数，方便自检）。
/// <para>格式：<c>CFG1|节点|昵称|机型|长机|编号|形状|侧位|横偏|后距|高度|间距|完成%|保持s|掉队%|地图判定|辅助|机型过滤|锚点|时间戳</c></para>
/// <para>★ 走独立端口 48018、独立前缀：状态包是 4 Hz 的热路径，不该为一个「按一下发一次」的配置包
/// 增加字段；前缀还能让将来的版本在不认识它时安静跳过。</para>
/// </summary>
public static class ConfigPacket
{
    public const string Prefix = "CFG1";
    public const int Port = 48018;

    private const int MinFields = 19;

    public static string Build(FormationProfile p, string nodeId, string name, string type,
                               double? anchorXn, double? anchorYn, bool anchorFriendly)
    {
        string anchor = anchorXn is double ax && anchorYn is double ay
            ? N(ax) + "," + N(ay) + "," + (anchorFriendly ? "1" : "0")
            : "";

        var sb = new StringBuilder(192);
        sb.Append(Prefix).Append('|')
          .Append(Clean(nodeId, 12)).Append('|')
          .Append(Clean(name, 24)).Append('|')
          .Append(Clean(type, 16)).Append('|')
          .Append(p.SenderLead ? '1' : '0').Append('|')
          .Append(Clamp(p.Slot, 0, 9)).Append('|')
          .Append(Clean(p.Shape, 12)).Append('|')
          .Append(Clamp(p.Side, -1, 1)).Append('|')
          .Append(N(p.LateralM)).Append('|')
          .Append(N(p.BackM)).Append('|')
          .Append(N(p.UpM)).Append('|')
          .Append(N(p.SpacingM)).Append('|')
          .Append(N(p.DonePct)).Append('|')
          .Append(N(p.HoldS)).Append('|')
          .Append(N(p.DropPct)).Append('|')
          .Append(p.MapOnly ? '1' : '0').Append('|')
          .Append(p.Assist ? '1' : '0').Append('|')
          .Append(Clean(p.LeadIcon, 16)).Append('|')
          .Append(anchor).Append('|')
          .Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>解析一包。失败时不抛异常（网络上什么都可能收到）。</summary>
    public static bool TryParse(string text, out ConfigOffer offer)
    {
        offer = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Trim().Split('|');
        if (parts.Length < MinFields || parts[0] != Prefix) return false;

        if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) return false;
        if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side)) return false;
        if (!D(parts[8], out double lat) || !D(parts[9], out double back) || !D(parts[10], out double up)) return false;
        if (!D(parts[11], out double spacing) || !D(parts[12], out double done)) return false;
        if (!D(parts[13], out double hold) || !D(parts[14], out double drop)) return false;

        string shape = parts[6].Trim();
        if (!FormationGeometry.IsKnownShape(shape)) shape = FormationGeometry.ShapeCustom;

        var profile = new FormationProfile(
            parts[4] == "1", slot, shape, side,
            lat, back, up, spacing, done, hold, drop,
            parts[15] == "1", parts[16] == "1", parts[17]);

        bool hasAnchor = false;
        double axn = 0, ayn = 0;
        bool af = false;
        string[] anchor = parts[18].Split(',');
        if (anchor.Length == 3
            && D(anchor[0], out axn) && D(anchor[1], out ayn))
        {
            hasAnchor = true;
            af = anchor[2] == "1";
        }

        offer = new ConfigOffer
        {
            NodeId = parts[1].Trim(),
            Name = parts[2].Trim(),
            Type = parts[3].Trim(),
            Profile = profile,
            HasAnchor = hasAnchor,
            AnchorXn = axn,
            AnchorYn = ayn,
            AnchorFriendly = af,
        };
        return true;
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool D(string s, out double v)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);

    /// <summary>昵称里带分隔符会把整包拆错，先清洗；长度也要限，UDP 包不是日记本。</summary>
    private static string Clean(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (char ch in s)
        {
            if (sb.Length >= max) break;
            if (ch is '|' or '\r' or '\n' or '\t') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}

/// <summary>
/// 配置互传通道（UDP 48018）。与状态链完全独立：
/// <list type="bullet">
/// <item>状态链没开也能收配置 —— 这正是「新装机上线就能飞」要走的路。</item>
/// <item>旧版本根本没有监听这个端口，所以不会把配置包当成坏包。</item>
/// </list>
/// </summary>
public sealed class ConfigLink : IDisposable
{
    private const int MaxOffers = 4;
    private const double OfferTtlS = 30.0;

    private readonly object _lock = new();
    private readonly List<ConfigOffer> _offers = new();
    private UdpClient? _socket;
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>最近创建的那个实例（面板从这里取；没有链路时是 null）。</summary>
    public static ConfigLink? Current { get; private set; }

    /// <summary>队标校验（复用状态链那套机场锚点比较）。返回 Other 的包直接丢。</summary>
    internal Func<double, double, bool, DataLink.TeamVerdict>? TeamCheck { get; set; }

    public bool Listening => _socket != null;
    public string LastError { get; private set; } = "";

    public ConfigLink() => Current = this;

    public void Start()
    {
        try
        {
            _socket = new UdpClient(new IPEndPoint(IPAddress.Any, ConfigPacket.Port)) { EnableBroadcast = true };
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "WtHud-ConfigLink" };
            _thread.Start();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _socket = null;
        }
    }

    private void ReceiveLoop()
    {
        while (!_stop)
        {
            try
            {
                if (_socket is null) return;
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _socket.Receive(ref from);
                if (!ConfigPacket.TryParse(Encoding.UTF8.GetString(data), out var parsed)) continue;

                // 队标不符直接丢；对不上任何机场（刚进图）先收下，列表里标「未校验」
                bool verified = false;
                if (parsed.HasAnchor && TeamCheck is { } check)
                {
                    var verdict = check(parsed.AnchorXn, parsed.AnchorYn, parsed.AnchorFriendly);
                    if (verdict == DataLink.TeamVerdict.Other) continue;
                    verified = verdict == DataLink.TeamVerdict.Same;
                }

                var stored = new ConfigOffer
                {
                    Address = from.Address.ToString(),
                    NodeId = parsed.NodeId,
                    Name = parsed.Name,
                    Type = parsed.Type,
                    Profile = parsed.Profile,
                    HasAnchor = parsed.HasAnchor,
                    AnchorXn = parsed.AnchorXn,
                    AnchorYn = parsed.AnchorYn,
                    AnchorFriendly = parsed.AnchorFriendly,
                    TeamVerified = verified,
                    AtUtc = DateTime.UtcNow,
                };

                lock (_lock)
                {
                    // 同一来源只留最新一包：连发 3 包不该在列表里出现 3 行
                    _offers.RemoveAll(o => o.Address == stored.Address && o.NodeId == stored.NodeId);
                    _offers.Insert(0, stored);
                    if (_offers.Count > MaxOffers) _offers.RemoveRange(MaxOffers, _offers.Count - MaxOffers);
                }
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { Thread.Sleep(50); }
            catch { Thread.Sleep(50); }
        }
    }

    /// <summary>当前可用的配置包（已过期的不返回）。</summary>
    public ConfigOffer[] Offers
    {
        get
        {
            lock (_lock)
            {
                _offers.RemoveAll(o => o.AgeS > OfferTtlS);
                return _offers.ToArray();
            }
        }
    }

    /// <summary>
    /// 广播（或按对端地址单播）本机配置，默认连发 3 包。
    /// <para>★ 对端地址只说明「是哪台机器」，端口一律用 <see cref="ConfigPacket.Port"/>：
    /// 把配置包发到 48017 会被对方的状态链当成坏包。</para>
    /// </summary>
    /// <returns>实际目标数（0 = 没在监听）。</returns>
    public int Send(FormationProfile profile, string nodeId, string name, string type,
                    double? anchorXn, double? anchorYn, bool anchorFriendly,
                    IReadOnlyList<string> peers, int copies = 3, int gapMs = 200)
    {
        var socket = _socket;
        if (socket is null) return 0;

        string payload = ConfigPacket.Build(profile, nodeId, name, type, anchorXn, anchorYn, anchorFriendly);
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        var targets = ResolveTargets(peers);

        var sender = new Thread(() =>
        {
            for (int i = 0; i < copies && !_stop; i++)
            {
                foreach (var ep in targets)
                {
                    try { socket.Send(bytes, bytes.Length, ep); } catch { }
                }
                if (i + 1 < copies) Thread.Sleep(gapMs);
            }
        })
        { IsBackground = true, Name = "WtHud-ConfigSend" };
        sender.Start();
        return targets.Length;
    }

    internal static IPEndPoint[] ResolveTargets(IReadOnlyList<string> peers)
    {
        var list = new List<IPEndPoint>();
        foreach (string spec in peers)
            if (DataLink.TryParsePeer(spec, out var ep))
                list.Add(new IPEndPoint(ep.Address, ConfigPacket.Port));
        if (list.Count == 0) list.Add(new IPEndPoint(IPAddress.Broadcast, ConfigPacket.Port));
        return list.ToArray();
    }

    public void Dispose()
    {
        _stop = true;
        try { _socket?.Dispose(); } catch { }
        _socket = null;
        if (Current == this) Current = null;
    }
}
