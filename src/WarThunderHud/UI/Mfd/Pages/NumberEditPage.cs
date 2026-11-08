// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// Manual numeric entry: owns the text buffer and the three decision rows (confirm / cancel /
/// pick airframe). The form supplies what to do with the parsed value.
/// </summary>
internal sealed class NumberEditPage : MfdPageBase
{
    private readonly Func<(string Label, string Unit)> _texts;
    private readonly Action<string> _onCommit;
    private readonly Action _onCancel;
    private readonly Action _onPick;
    private readonly MfdPageLayout _layout;

    public NumberEditPage(Func<(string Label, string Unit)> texts, string initial,
                          Action<string> onCommit, Action onCancel, Action onPick,
                          MfdPageLayout layout) : base("editnumber")
    {
        _texts = texts;
        Buffer = initial;
        _onCommit = onCommit;
        _onCancel = onCancel;
        _onPick = onPick;
        _layout = layout;
    }

    public override string TitleKey => "edit.title";
    public override string HintKey => "edit.prompt";

    public string Buffer { get; private set; }

    /// <summary>0 = confirm, 1 = cancel, 2 = pick airframe.</summary>
    public int Choice { get; private set; }

    public override void Build()
    {
        Buttons.Clear();
        Buttons.Add(new VirtualButton("edit.commit", VirtualButtonKind.Action)
        {
            Slot = 2,
            Text = () => L10n.Get(LangScope.Screen, "confirm.ok"),
            Value = () => Buffer,
        });
        Buttons.Add(new VirtualButton("edit.cancel", VirtualButtonKind.Action)
        {
            Slot = 3,
            Text = () => L10n.Get(LangScope.Screen, "confirm.cancel"),
        });
        Buttons.Add(new VirtualButton("edit.pick", VirtualButtonKind.Action)
        {
            Slot = 7,
            Text = () => L10n.Get(LangScope.Screen, "edit.pick"),
        });
    }

    public void AppendChar(char ch)
    {
        if (ch == '\0' || Buffer.Length >= 10) return;
        Buffer += ch;
    }

    public void Backspace()
    {
        if (Buffer.Length > 0) Buffer = Buffer[..^1];
    }

    public void Select(int index) => Choice = index < 0 ? Choice : Math.Clamp(index, 0, 2);

    public void MoveChoice(int delta) => Choice = (Choice + delta + 3) % 3;

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "edit.commit":
                _onCommit(Buffer);
                return true;
            case "edit.cancel":
            case "nav.back":
                _onCancel();
                return true;
            case "edit.pick":
                _onPick();
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

        using var title = MfdTheme.CjkAt(26f * s);
        using var mono = MfdTheme.MonoBoldAt(56f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var (label, unit) = _texts();
        var cx = glass.Left + glass.Width / 2;

        var zh = L10n.Screen == Lang.Zh;
        var vehicle = WtHud.Hud.AirframeDb.EffectiveVehicle;
        using var vFont = MfdTheme.CjkAt(17f * s);
        var vText = string.IsNullOrWhiteSpace(vehicle)
            ? (zh ? "当前机型：未读取到机型数据" : "AIRFRAME: NOT DETECTED")
            : (zh ? "当前机型：" + vehicle : "AIRFRAME: " + vehicle);
        g.DrawString(vText, vFont, string.IsNullOrWhiteSpace(vehicle) ? muted : text,
                     cx - g.MeasureString(vText, vFont).Width / 2, glass.Top + 26 * s,
                     StringFormat.GenericTypographic);

        g.DrawString(label, title, muted, cx - g.MeasureString(label, title).Width / 2,
                     glass.Top + 62 * s, StringFormat.GenericTypographic);

        var shown = Buffer.Length == 0 ? "_" : Buffer + "_";
        var w = g.MeasureString(shown, mono).Width;
        var boxW = Math.Max(w + 80 * s, 320 * s);
        var boxH = 90 * s;
        var bx = cx - boxW / 2;
        var by = glass.Top + glass.Height * 0.28f;

        using (var boxBg = new SolidBrush(Color.FromArgb(210, 6, 16, 10)))
            g.FillRectangle(boxBg, bx, by, boxW, boxH);
        using (var pen = new Pen(MfdTheme.Green, 2.5f * s))
            g.DrawRectangle(pen, bx, by, boxW, boxH);
        g.DrawString(shown, mono, green, cx - w / 2, by + 12 * s, StringFormat.GenericTypographic);

        if (!string.IsNullOrEmpty(unit))
            g.DrawString(unit, title, muted, bx + boxW + 10 * s, by + boxH / 2 - 14 * s,
                         StringFormat.GenericTypographic);

        // Three decision rows on side keys 2 / 3 / 7.
        DrawChoiceRow(g, s, glass, pad, 2, 0, L10n.Get(LangScope.Screen, "confirm.ok"));
        DrawChoiceRow(g, s, glass, pad, 3, 1, L10n.Get(LangScope.Screen, "confirm.cancel"));
        DrawChoiceRow(g, s, glass, pad, 7, 2, L10n.Get(LangScope.Screen, "edit.pick"));
    }

    private void DrawChoiceRow(System.Drawing.Graphics g, float s, System.Drawing.RectangleF glass,
                               float pad, int sideKey, int choice, string label)
    {
        var row = _layout.ListRowRect(sideKey < 5 ? 0 : 1, sideKey % 5);
        var selected = choice == Choice;
        if (selected)
        {
            using var hi = new SolidBrush(choice == 0 ? MfdTheme.Green : MfdTheme.GreenDim);
            g.FillRectangle(hi, row.X, row.Y, row.Width, row.Height);
        }
        using var font = MfdTheme.CjkAt(20f * s);
        using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
        g.DrawString(label, font, fg, row.X + 14 * s, row.Y + 6 * s, StringFormat.GenericTypographic);
    }
}
