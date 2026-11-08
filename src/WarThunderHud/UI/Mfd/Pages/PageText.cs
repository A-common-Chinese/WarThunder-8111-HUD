// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Pages;

/// <summary>页内文本换行：拉丁词保持完整，CJK 字符逐字断行。</summary>
internal static class PageText
{
    public static List<string> Wrap(System.Drawing.Graphics g, System.Drawing.Font font, string text, float maxWidth)
    {
        var result = new List<string>();
        var tokens = new List<string>();
        var word = "";
        foreach (var ch in text)
        {
            var isAsciiWord = ch < 0x80 && ch != ' ';
            if (isAsciiWord) { word += ch; continue; }
            if (word.Length > 0) { tokens.Add(word); word = ""; }
            tokens.Add(ch.ToString());
        }
        if (word.Length > 0) tokens.Add(word);

        var line = "";
        foreach (var t in tokens)
        {
            if (t == " ") { line += " "; continue; }
            var candidate = line + t;
            if (g.MeasureString(candidate, font).Width > maxWidth && line.Trim().Length > 0)
            {
                result.Add(line.TrimEnd());
                line = t;
            }
            else line = candidate;
        }
        if (line.Trim().Length > 0) result.Add(line.TrimEnd());
        return result;
    }
}
