// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.Hud;
using WtHud.UI;

namespace WtHud.Input;

/// <summary>
/// 从战雷导出的 controls .blk 里导入 HUD 关心的按键绑定（减速板 / 计分板）。
/// 只读文件、只改 HUD 自己的配置；用户仍需 SAVE 才落盘（CANCEL 可回退）。
/// </summary>
public static class WtControlsImport
{
    /// <summary>上一次导入的提示文本（给 MFD 面板 toast 用）；取消/未选文件时为 null。</summary>
    public static string? LastSummary { get; private set; }

    /// <summary>
    /// 要导入的目标。一个字段可能对应多个动作（飞机减速板和直升机减速板是两条），
    /// **按顺序取第一个有键盘键的** —— 以前是后者覆盖前者，直升机那条会盖掉飞机的。
    /// </summary>
    private static readonly (string Field, string Zh, string En, string[] Actions)[] Targets =
    {
        ("AirbrakeKey", "减速板", "AIRBRAKE", new[] { "ID_AIR_BRAKE", "ID_AIR_BRAKE_HELICOPTER" }),
        ("ScoreboardKey", "计分板", "SCOREBOARD", new[] { "ID_MPSTATSCREEN" }),
    };

    public static void PickAndApply(HudConfig cfg)
    {
        LastSummary = null;
        using var dlg = new OpenFileDialog
        {
            Title = L10n.Screen == Lang.Zh
                ? "选择战雷按键设置文件（controls .blk 导出）"
                : "Pick the War Thunder controls .blk export",
            Filter = "War Thunder controls (*.blk)|*.blk|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(Form.ActiveForm) != DialogResult.OK) return;

        try
        {
            var map = WtControlsFile.Parse(File.ReadAllText(dlg.FileName));
            LastSummary = Apply(cfg, map, L10n.Screen);
        }
        catch (Exception ex)
        {
            LastSummary = (L10n.Screen == Lang.Zh ? "导入失败：" : "IMPORT FAILED: ") + ex.Message;
        }
    }

    /// <summary>
    /// 把解析结果写进配置，返回一句人话小结。
    /// <para>★ 必须区分「文件里没有这个动作」和「有，但已经是这个键位」：以前两者共用
    /// 「文件里没有减速板/计分板键位」这一句，重复导入同一份文件就会被告知「文件里没有」，
    /// 而文件明明有 —— 这正是被反馈回来的「导入后显示无键位」。</para>
    /// </summary>
    internal static string Apply(HudConfig cfg, IReadOnlyDictionary<string, int[]> map, Lang lang)
    {
        bool zh = lang == Lang.Zh;
        var applied = new List<string>();
        var unchanged = new List<string>();
        var noKey = new List<string>();

        foreach (var (field, zhLabel, enLabel, actions) in Targets)
        {
            string label = zh ? zhLabel : enLabel;
            string? binding = null;
            bool found = false;
            foreach (string action in actions)
            {
                if (!map.TryGetValue(action, out var codes)) continue;   // 文件里没有这条动作
                found = true;
                if (codes.Length == 0) continue;                          // 这条只绑了鼠标/手柄
                string? b = WtControlsFile.BuildBinding(codes);
                if (!string.IsNullOrEmpty(b)) { binding = b; break; }     // 取第一个有键盘键的
            }

            if (!found) continue;
            if (binding is null) { noKey.Add(label); continue; }          // 有动作但没键盘键

            string current = field == "AirbrakeKey" ? cfg.AirbrakeKey : cfg.ScoreboardKey;
            if (current == binding)
            {
                unchanged.Add(label + "=" + binding);
            }
            else
            {
                if (field == "AirbrakeKey") cfg.AirbrakeKey = binding;
                else cfg.ScoreboardKey = binding;
                applied.Add(label + "=" + binding);
            }
        }

        // 1) 一个都没动、也没有「已经是这个值」—— 那才算真的没有
        if (applied.Count == 0 && unchanged.Count == 0)
        {
            if (noKey.Count > 0)
                return zh
                    ? string.Join("、", noKey) + " 只绑了鼠标/手柄，没有键盘键"
                    : string.Join(", ", noKey) + ": MOUSE/HOTAS ONLY, NO KEYBOARD KEY";
            if (map.Count == 0)
                return zh
                    ? "解析到 0 个动作 · 确认选的是 controls .blk"
                    : "0 ACTIONS PARSED - IS THIS A CONTROLS .BLK?";
            return zh
                ? "文件里没有减速板/计分板（解析到 " + map.Count + " 个动作）"
                : "NO AIRBRAKE/SCOREBOARD BINDING (" + map.Count + " ACTIONS PARSED)";
        }

        // 2) 命中但值没变：这是重复导入，不是「文件里没有」
        if (applied.Count == 0)
            return zh
                ? "无需修改：" + string.Join("、", unchanged) + " 已是当前键位"
                : "NO CHANGE NEEDED: " + string.Join(", ", unchanged) + " ALREADY SET";

        // 3) 真导入了。附带说明：哪些已经是原值、哪些只绑了鼠标/手柄（后者没导入，得自己设）
        string joined = string.Join(zh ? "、" : ", ", applied);
        var notes = new List<string>();
        if (unchanged.Count > 0)
            notes.Add(string.Join(zh ? "、" : ", ", unchanged) + (zh ? " 未变" : " unchanged"));
        if (noKey.Count > 0)
            notes.Add(string.Join(zh ? "、" : ", ", noKey) + (zh ? " 仅鼠标/手柄" : " mouse/HOTAS only"));
        string rest = notes.Count > 0 ? "（" + string.Join(zh ? "；" : "; ", notes) + "）" : "";
        return zh
            ? "已导入：" + joined + rest + " · SAVE 保存"
            : "IMPORTED: " + joined + rest + " (SAVE to keep)";
    }
}
