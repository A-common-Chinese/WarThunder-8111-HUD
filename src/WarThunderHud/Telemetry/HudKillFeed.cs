// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Text.RegularExpressions;

namespace WtHud.Telemetry;

/// <summary>
/// 解析 8111 <c>/hudmsg</c> 的 damage 文本，识别「击落了」（空战击落）。
/// 只认空战：地面/AI 目标用的是「击毁了」，这里一律忽略（用户要求）。
/// 文本里的 <c>^DFCN^</c> 之类是联队/颜色标记（因人而异，不能当身份），统一剥掉。
/// </summary>
public static class HudKillFeed
{
    // 成对出现的颜色/联队标记：^DFCN^ ^ZJWB^ ^LostC^ ...
    private static readonly Regex ColorTag = new(@"\^[A-Za-z0-9]{2,10}\^", RegexOptions.Compiled);
    // U+200B..U+200D / BOM / word-joiner：WT 会在中文词之间插这些
    private static readonly Regex ZeroWidth = new(@"[\u200B-\u200D\uFEFF\u2060]", RegexOptions.Compiled);
    // A (机型) 击落了 B (机型)  —— 只命中「击落了」
    private static readonly Regex KillLine = new(
        @"^(?<actor>.+?)\s*\((?<type>[^)]+)\)\s*击落了\s*(?<target>.+)$", RegexOptions.Compiled);

    /// <summary>清洗一条 hudmsg 文本：去颜色标记、零宽字符、前导装饰符，首尾空白。</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = ColorTag.Replace(raw, "");
        s = ZeroWidth.Replace(s, "");
        s = s.Trim();
        s = s.TrimStart('\u22c6', '\u25c4', '\u2584', '\u2605', '\u2606', ' ');   // ⋇ ◄ ▄ ★ ☆
        return s.Trim();
    }

    /// <summary>解析「actor (机型) 击落了 target」；不是击落消息返回 false。</summary>
    public static bool TryParse(string? raw, out string actor, out string actorType, out string target)
    {
        actor = actorType = target = "";
        var m = KillLine.Match(Clean(raw));
        if (!m.Success) return false;
        actor = m.Groups["actor"].Value.Trim();
        actorType = m.Groups["type"].Value.Trim();
        target = m.Groups["target"].Value.Trim();
        return actor.Length > 0 && target.Length > 0;
    }

    /// <summary>这条消息是不是「自己击落了别人」；是则给出目标名（已清洗）。</summary>
    public static bool IsOwnKill(string? raw, string? playerName, out string target)
    {
        target = "";
        if (string.IsNullOrWhiteSpace(playerName)) return false;
        if (!TryParse(raw, out var actor, out _, out var tgt)) return false;
        if (!string.Equals(actor, playerName.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        target = tgt;
        return true;
    }
}
