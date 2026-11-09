// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Net.Http;
using System.Text.Json;

namespace WtHud.Telemetry;

/// <summary>
/// 4 Hz 轮询 8111 <c>/hudmsg</c>，发现「自己击落了谁」就触发 <see cref="OwnKill"/>（SPLASH 播报）。
///
/// 注意：
/// - 只认「击落了」，AI/地面的「击毁了」不触发（用户要求）；
/// - 8111 会重复推送同一 id（time 递增），所以只处理 id &gt; 游标 的条目；
/// - 新对局 id 重新计数：<see cref="Reset"/> 后第一轮只推进游标、不播报，避免回放旧消息；
/// - 玩家昵称来自配置（8111 没有可靠的自我标识，`^DFCN^` 是联队标记、因人而异）；
/// - 任何网络/解析失败都静默返回，不影响 HUD 主循环。
/// </summary>
public sealed class HudMsgWatcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private int _lastDamageId;
    private bool _resync = true;
    private DateTime _lastPollUtc = DateTime.MinValue;

    /// <summary>发现自己的新击落；参数 = 被击落目标（已清洗）。</summary>
    public event Action<string>? OwnKill;

    /// <summary>新对局 / 离开对局时调用：清空游标，下一轮只记录不播报。</summary>
    public void Reset()
    {
        _lastDamageId = 0;
        _resync = true;
    }

    /// <summary>在渲染循环里调用；内部限频 250 ms。</summary>
    public void Poll(string? playerName, bool inMatch)
    {
        if (!inMatch || string.IsNullOrWhiteSpace(playerName)) return;
        var now = DateTime.UtcNow;
        if ((now - _lastPollUtc).TotalMilliseconds < 250) return;
        _lastPollUtc = now;

        string json;
        try
        {
            json = Http.GetStringAsync(
                $"http://127.0.0.1:8111/hudmsg?lastEvt=0&lastDmg={_lastDamageId}").GetAwaiter().GetResult();
        }
        catch
        {
            return;
        }

        try { Handle(json, playerName); }
        catch { /* 静默：解析失败不能刷日志 */ }
    }

    private void Handle(string json, string playerName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("damage", out var damage) ||
            damage.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = new List<(int Id, string Msg)>();
        foreach (var d in damage.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object) continue;
            int id = d.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int parsed) ? parsed : 0;
            string msg = d.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "" : "";
            if (id > 0) items.Add((id, msg));
        }
        if (items.Count == 0) return;
        items.Sort((a, b) => a.Id.CompareTo(b.Id));

        // Reset 后第一轮：只把游标推到当前最大值（不播报上一局/本局开场前的旧消息）
        if (_resync)
        {
            _lastDamageId = items[^1].Id;
            _resync = false;
            return;
        }

        // 新对局 id 从 1 重新计数：同样只推进游标，不播报
        if (items[0].Id <= _lastDamageId && _lastDamageId - items[0].Id > 20)
        {
            _lastDamageId = items[^1].Id;
            return;
        }

        foreach (var (id, msg) in items)
        {
            if (id <= _lastDamageId) continue;
            _lastDamageId = id;
            if (HudKillFeed.IsOwnKill(msg, playerName, out var target))
                OwnKill?.Invoke(target);
        }
    }
}
