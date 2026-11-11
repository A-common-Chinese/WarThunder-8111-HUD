// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// Generic "are you sure" sheet. It only knows two options and two callbacks, so every destructive
/// action reuses it instead of carrying its own switch branches.
/// </summary>
internal sealed class ConfirmPage : MfdPageBase
{
    private readonly Func<(string Label, string Sub)> _texts;
    private readonly Action _onOk;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;

    public ConfirmPage(Func<(string Label, string Sub)> texts, Action onOk, Action onCancel,
                       MfdPageLayout layout) : base("confirm")
    {
        _texts = texts;
        _onOk = onOk;
        _onCancel = onCancel;
        _layout = layout;
    }

    public override string TitleKey => "confirm.title";
    public override string HintKey => "hint.confirm";

    /// <summary>0 = confirm, 1 = cancel.</summary>
    public int Selected { get; private set; }

    public override void Build()
    {
        Buttons.Clear();
        // 侧键 2 / 3 是这两个选项在面板上的真实槽位。按钮自己声明位置和按键，
        // 表单不再靠 index==2/3 的硬编码去猜（那样一改版面就错）。
        // Target 是「执行」而不是「选中」：这与原来的侧键行为一致（按下即确认/取消）。
        AddOption("confirm.ok", sideKey: 2, index: 0);
        AddOption("confirm.cancel", sideKey: 3, index: 1);
    }

    private void AddOption(string id, int sideKey, int index)
    {
        Buttons.Add(new VirtualButton(id, VirtualButtonKind.Action)
        {
            Slot = sideKey % 5,
            Column = sideKey / 5,
            Text = () => L10n.Get(LangScope.Screen, id),
            Target = MfdCommand.Of(id, index.ToString()),
            ActivateBy = new[] { "SIDE" + MfdPageLayout.CellFromSideKey(sideKey) },
        });
    }

    public void Select(int index) => Selected = Math.Clamp(index, 0, 1);

    public void Move(int delta) => Selected = (Selected + delta + 2) % 2;

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "confirm.ok":
                _onOk();
                return true;
            case "confirm.cancel":
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
        var glass = ctx.Glass;
        var pad = ctx.Pad;
        var left = glass.Left + pad;

        var (label, sub) = _texts();

        using var big = MfdTheme.CjkAt(28f * s);
        using var small = MfdTheme.MonoAt(15f * s);
        using var white = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cy = glass.Top + glass.Height * 0.30f;
        var w = g.MeasureString(label, big).Width;
        g.DrawString(label, big, white, glass.Left + glass.Width / 2 - w / 2, cy - 40 * s,
                     StringFormat.GenericTypographic);

        var sw = g.MeasureString(sub, small).Width;
        g.DrawString(sub, small, muted, glass.Left + glass.Width / 2 - sw / 2, cy + 4 * s,
                     StringFormat.GenericTypographic);

        var rowH = 44 * s;
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        for (var i = 0; i < 2; i++)
        {
            var row = _layout.ListRowRect(0, 2 + i);   // 侧键 2/3 竖排
            var btn = new System.Drawing.RectangleF(row.X, row.Y, row.Width, row.Height);
            var yy = btn.Y;
            var selected = i == Selected;
            if (selected)
            {
                using var hi = new SolidBrush(i == 0 ? MfdTheme.Green : MfdTheme.GreenDim);
                g.FillRectangle(hi, left - 6 * s, yy, colW + 12 * s, rowH);
            }
            using var fgBrush = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
            var text = L10n.Get(LangScope.Screen, i == 0 ? "confirm.ok" : "confirm.cancel");
            g.DrawString(text, big, fgBrush, left + 20 * s, yy + 2 * s, StringFormat.GenericTypographic);
        }
    }
}
