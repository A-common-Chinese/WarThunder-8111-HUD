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
    // 成对出现的颜色/联队标记：^DFCN^ ^ZJWB^ ^LostC^，也可能是中文联队名 ^雄鹰^
    // 字符集放宽到「任意非 ^」，只保留成对与长度约束 —— 原来只认 [A-Za-z0-9]，
    // 中文战队标签会原地留下，清洗后 actor 变成 "^雄鹰^张三"，跟设置里的昵称对不上。
    private static readonly Regex ColorTag = new(@"\^[^^]{1,16}\^", RegexOptions.Compiled);
    // U+200B..U+200D / BOM / word-joiner：WT 会在中文词之间插这些
    private static readonly Regex ZeroWidth = new(@"[\u200B-\u200D\uFEFF\u2060]", RegexOptions.Compiled);

    /// <summary>
    /// 空战击落的动词。中文客户端「击落了」，英文客户端 "shot down"。
    /// <para>「击毁了 / destroyed」是打地面目标，不算击落，故意不收。</para>
    /// </summary>
    private static readonly string[] KillVerbs = { "击落了", "shot down" };

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
    /// <remarks>
    /// 不能再用一条从头匹配的正则：`(?&lt;actor&gt;.+?)` 是非贪婪的，遇到第一个 `(` 就停，
    /// 昵称里带括号（张三(老阳)）会被截成「张三」。改成先按动词切两半、再从**最后**一个
    /// `" ("` 处切机型 —— 机型括号永远跟在名字后面，这个位置是确定的。
    /// </remarks>
    public static bool TryParse(string? raw, out string actor, out string actorType, out string target)
    {
        actor = actorType = target = "";
        string s = Clean(raw);
        if (s.Length == 0) return false;

        // 取最靠后的一次动词出现：昵称里万一含有同样的词，宁可让 actor 长一点也别切错
        int vi = -1, vl = 0;
        foreach (var v in KillVerbs)
        {
            int i = s.LastIndexOf(v, StringComparison.OrdinalIgnoreCase);
            if (i > vi) { vi = i; vl = v.Length; }
        }
        if (vi <= 0) return false;                 // 没有动词，或者动词在开头（没有 actor）

        string left = s[..vi].Trim();
        string right = s[(vi + vl)..].Trim();
        if (left.Length == 0 || right.Length == 0) return false;

        // 机型括号前一定有一个空格（"张三 (J-7)"），昵称里的括号通常紧跟（"张三(老阳)"）。
        // 用 " (" 而不是 '(' 就是为了区分这两种，否则无机型的消息会把昵称括号当机型。
        int sp = left.LastIndexOf(" (", StringComparison.Ordinal);
        if (sp > 0 && left.EndsWith(')'))
        {
            actor = left[..sp].Trim();
            actorType = left[(sp + 2)..^1].Trim();
        }
        else
        {
            actor = left;                          // 没有机型括号的消息也接受
            actorType = "";
        }

        target = right;                            // 目标保持原样（含机型），播报里直接显示
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
