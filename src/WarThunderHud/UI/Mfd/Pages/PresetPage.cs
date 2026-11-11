// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>Colour preset picker: six cells, one row per column pair, CONFIRM applies.</summary>
internal sealed class PresetPage : MfdPageBase
{
    private readonly Func<string> _currentKey;
    private readonly Action<string> _onApply;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;

    public PresetPage(Func<string> currentKey, Action<string> onApply, Action onCancel,
                      MfdPageLayout layout) : base("preset")
    {
        _currentKey = currentKey;
        _onApply = onApply;
        _onCancel = onCancel;
        _layout = layout;
        Selected = Math.Max(0, Array.FindIndex(MfdTheme.Presets, p => p.Key == currentKey()));
    }

    public override string TitleKey => "preset.title";
    public override string HintKey => "selectPreset";

    public int Selected { get; private set; }

    public override void Build()
    {
        Buttons.Clear();
        // 每个预设格 = 一个按钮：位置与 Draw 的 ListRowRect(i % 2, i / 2) 一致，
        // 侧键只负责选中，应用仍走 CONFIRM。
        for (var i = 0; i < MfdTheme.Presets.Length; i++)
        {
            var cell = i;
            Buttons.Add(new VirtualButton("preset" + cell, VirtualButtonKind.Action)
            {
                Slot = cell / 2,
                Column = cell % 2,
                Text = () => L10n.Get(LangScope.Screen, "color." + MfdTheme.Presets[cell].Key),
                Target = MfdCommand.Of("preset.select", cell.ToString()),
                ActivateBy = new[] { "SIDE" + cell },
            });
        }
    }

    public void Select(int index) => Selected = Math.Clamp(index, 0, MfdTheme.Presets.Length - 1);

    public void Move(int delta) =>
        Selected = (Selected + delta + MfdTheme.Presets.Length) % MfdTheme.Presets.Length;

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "preset.select":
                if (int.TryParse(cmd.Arg, out var i)) Select(i);
                return true;
            case "confirm.ok":
                _onApply(MfdTheme.Presets[Selected].Key);
                return true;
            case "nav.back":
                _onCancel();
                return true;
        }
        return false;
    }

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        using var title = MfdTheme.CjkAt(24f * s);
        using var body = MfdTheme.CjkAt(20f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var dark = new SolidBrush(MfdTheme.Dark);

        for (var i = 0; i < MfdTheme.Presets.Length; i++)
        {
            var cell = _layout.ListRowRect(i % 2, i / 2);
            var preset = MfdTheme.Presets[i];
            using var brush = new SolidBrush(preset.Accent);
            g.FillRectangle(brush, cell);
            if (i == Selected)
            {
                using var pen = new Pen(MfdTheme.Green, 3f * s);
                g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
            }
            var label = L10n.Get(LangScope.Screen, "color." + preset.Key);
            g.DrawString(label, body, dark, cell.X + 12 * s, cell.Y + 8 * s,
                         StringFormat.GenericTypographic);
        }
    }
}
