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
            var applied = new List<string>();
            foreach (var (action, field, label) in WtControlsFile.Supported)
            {
                if (!map.TryGetValue(action, out var codes)) continue;
                string? binding = WtControlsFile.BuildBinding(codes);
                if (binding == null) continue;
                if (field == "AirbrakeKey" && cfg.AirbrakeKey != binding)
                {
                    cfg.AirbrakeKey = binding;
                    applied.Add(label + "=" + binding);
                }
                else if (field == "ScoreboardKey" && cfg.ScoreboardKey != binding)
                {
                    cfg.ScoreboardKey = binding;
                    applied.Add(label + "=" + binding);
                }
            }

            LastSummary = applied.Count > 0
                ? (L10n.Screen == Lang.Zh
                    ? "已导入：" + string.Join("，", applied) + "（记得 SAVE）"
                    : "IMPORTED: " + string.Join(", ", applied) + " (SAVE to keep)")
                : (L10n.Screen == Lang.Zh
                    ? "文件里没有减速板/计分板键位"
                    : "NO AIRBRAKE/SCOREBOARD BINDING IN FILE");
        }
        catch (Exception ex)
        {
            LastSummary = (L10n.Screen == Lang.Zh ? "导入失败：" : "IMPORT FAILED: ") + ex.Message;
        }
    }
}
