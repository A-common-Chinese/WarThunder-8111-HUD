// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.Hud;
using WtHud.UI;

namespace WtHud.Input;

/// <summary>
/// 从战雷导出的 controls .blk 里导入 HUD 关心的按键绑定
/// （减速板 / 计分板 / 导引头 / 吊舱 / 第三人称 / 人在回路）。
/// 只读文件、只改 HUD 自己的配置；用户仍需 SAVE 才落盘（CANCEL 可回退）。
/// </summary>
public static class WtControlsImport
{
    /// <summary>上一次导入的提示文本（给 MFD 面板 toast 用）；取消/未选文件时为 null。</summary>
    public static string? LastSummary { get; private set; }

    /// <summary>
    /// 一个逻辑动作可以对应「主键 + 备用键」两套：减速板和人在回路在游戏里飞机/直升机是
    /// 两个独立动作，各写一个字段；导引头/吊舱/第三人称只有一个字段，允许带
    /// <c>_HELICOPTER</c> 后缀的变体兜底（按 Actions 顺序取第一个有键盘键的）。
    /// </summary>
    private static readonly (string Field, string Zh, string En, string[] Actions)[] Targets =
    {
        ("AirbrakeKey", "减速板·主键", "AIRBRAKE", new[] { "ID_AIR_BRAKE" }),
        ("AirbrakeKeyHeli", "减速板·直升机键", "AIRBRAKE (HELI)", new[] { "ID_AIR_BRAKE_HELICOPTER" }),
        ("ScoreboardKey", "计分板", "SCOREBOARD", new[] { "ID_MPSTATSCREEN" }),
        ("SeekerViewKey", "导引头视角", "SEEKER VIEW",
            new[] { "ID_CAMERA_SEEKER", "ID_CAMERA_SEEKER_HELICOPTER" }),
        ("PodViewKey", "吊舱视角", "POD VIEW",
            new[] { "ID_CAMERA_OPTICS", "ID_CAMERA_OPTICS_HELICOPTER" }),
        ("TpsViewKey", "第三人称视角", "THIRD-PERSON VIEW",
            new[] { "ID_CAMERA_TPS", "ID_CAMERA_TPS_HELICOPTER" }),
        ("ManLoopKey", "人在回路·主键", "MAN LOOP", new[] { "ID_CAMERA_SHELL_FPV" }),
        ("ManLoopKeyHeli", "人在回路·直升机键", "MAN LOOP (HELI)", new[] { "ID_CAMERA_SHELL_FPV_HELICOPTER" }),
    };

    /// <summary>成对字段：主键与备用键（同键时提示已去重）。</summary>
    private static readonly (string Main, string Alt)[] Pairs =
    {
        ("AirbrakeKey", "AirbrakeKeyHeli"),
        ("ManLoopKey", "ManLoopKeyHeli"),
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
    /// <para>★ 主键/备用键必须分别写清（「主键=…」「直升机键=…」）：两套动作在游戏里本来
    /// 就分开，混成一句话用户不知道自己改的是哪个。</para>
    /// </summary>
    internal static string Apply(HudConfig cfg, IReadOnlyDictionary<string, int[]> map, Lang lang)
    {
        bool zh = lang == Lang.Zh;
        var applied = new List<string>();
        var unchanged = new List<string>();
        var noKey = new List<string>();
        var heliVariant = new List<string>();

        foreach (var (field, zhLabel, enLabel, actions) in Targets)
        {
            string label = zh ? zhLabel : enLabel;
            string? binding = null;
            bool found = false;
            bool fromHeliVariant = false;

            for (int i = 0; i < actions.Length; i++)
            {
                if (!map.TryGetValue(actions[i], out var codes)) continue;   // 文件里没有这条动作
                found = true;
                if (codes.Length == 0) continue;                              // 这条只绑了鼠标/手柄
                string? b = WtControlsFile.BuildBinding(codes);
                if (!string.IsNullOrEmpty(b))
                {
                    binding = b;
                    fromHeliVariant = i > 0;    // 主动作没有、取了 _HELICOPTER 变体
                    break;
                }
            }

            if (!found) continue;
            if (binding is null) { noKey.Add(label); continue; }              // 有动作但没键盘键

            string current = GetField(cfg, field);
            if (current == binding)
            {
                unchanged.Add(label + "=" + binding);
            }
            else
            {
                SetField(cfg, field, binding);
                applied.Add(label + "=" + binding);
                if (fromHeliVariant) heliVariant.Add(label);
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
                ? "文件里没有可导入的按键（解析到 " + map.Count + " 个动作）"
                : "NO IMPORTABLE BINDING (" + map.Count + " ACTIONS PARSED)";
        }

        // 2) 命中但值没变：这是重复导入，不是「文件里没有」
        if (applied.Count == 0)
            return zh
                ? "无需修改：" + string.Join("、", unchanged) + " 已是当前键位"
                : "NO CHANGE NEEDED: " + string.Join(", ", unchanged) + " ALREADY SET";

        // 3) 真导入了。附带说明：哪些已经是原值、哪些只绑了鼠标/手柄、哪些是备用键/同键
        var notes = new List<string>();
        if (unchanged.Count > 0)
            notes.Add(string.Join(zh ? "、" : ", ", unchanged) + (zh ? " 未变" : " unchanged"));
        if (noKey.Count > 0)
            notes.Add(string.Join(zh ? "、" : ", ", noKey) + (zh ? " 仅鼠标/手柄" : " mouse/HOTAS only"));
        if (heliVariant.Count > 0)
            notes.Add(string.Join(zh ? "、" : ", ", heliVariant) + (zh ? " 取自直升机变体" : " FROM HELI VARIANT"));
        foreach (var (main, alt) in Pairs)
        {
            if (string.IsNullOrWhiteSpace(GetField(cfg, main))) continue;
            if (GetField(cfg, main) != GetField(cfg, alt)) continue;
            string pairLabel = zh ? "主键与直升机键相同（已去重）" : "MAIN AND HELI KEYS MATCH (DEDUPED)";
            if (!notes.Contains(pairLabel)) notes.Add(pairLabel);
        }
        if (!string.IsNullOrWhiteSpace(cfg.PodViewKey) && cfg.PodViewKey == cfg.TpsViewKey)
            notes.Add(zh ? "吊舱与第三人称同键（组合键模式）" : "POD AND TPS SHARE ONE KEY (COMBO MODE)");

        string joined = string.Join(zh ? "、" : ", ", applied);
        string rest = notes.Count > 0 ? "（" + string.Join(zh ? "；" : "; ", notes) + "）" : "";
        return zh
            ? "已导入：" + joined + rest + " · SAVE 保存"
            : "IMPORTED: " + joined + rest + " (SAVE to keep)";
    }

    private static string GetField(HudConfig c, string field) => field switch
    {
        "AirbrakeKey" => c.AirbrakeKey,
        "AirbrakeKeyHeli" => c.AirbrakeKeyHeli,
        "ScoreboardKey" => c.ScoreboardKey,
        "SeekerViewKey" => c.SeekerViewKey,
        "PodViewKey" => c.PodViewKey,
        "TpsViewKey" => c.TpsViewKey,
        "ManLoopKey" => c.ManLoopKey,
        "ManLoopKeyHeli" => c.ManLoopKeyHeli,
        _ => "",
    };

    private static void SetField(HudConfig c, string field, string value)
    {
        switch (field)
        {
            case "AirbrakeKey": c.AirbrakeKey = value; break;
            case "AirbrakeKeyHeli": c.AirbrakeKeyHeli = value; break;
            case "ScoreboardKey": c.ScoreboardKey = value; break;
            case "SeekerViewKey": c.SeekerViewKey = value; break;
            case "PodViewKey": c.PodViewKey = value; break;
            case "TpsViewKey": c.TpsViewKey = value; break;
            case "ManLoopKey": c.ManLoopKey = value; break;
            case "ManLoopKeyHeli": c.ManLoopKeyHeli = value; break;
        }
    }
}
