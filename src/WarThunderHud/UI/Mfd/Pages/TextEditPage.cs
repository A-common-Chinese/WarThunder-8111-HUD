// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 文本输入页（玩家昵称等）：ASCII 直接输入，中文/特殊符号用 Ctrl+V 粘贴。
/// 面板是自绘窗体，没有 IME 输入框；粘贴已覆盖绝大多数中文昵称需求。
/// </summary>
internal sealed class TextEditPage : MfdPageBase
{
    private readonly Func<string> _label;
    private readonly Action<string> _onCommit;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;

    public TextEditPage(Func<string> label, string initial, Action<string> onCommit, Action onCancel,
                        MfdPageLayout layout) : base("textedit")
    {
        _label = label;
        Buffer = initial ?? "";
        _onCommit = onCommit;
        _onCancel = onCancel;
        _layout = layout;
    }

    public override string TitleKey => "tedit.title";
    public override string HintKey => "tedit.hint";

    public string Buffer { get; private set; }

    public void Append(char ch)
    {
        if (ch == '\0' || ch == '\r' || ch == '\n' || ch == '\t') return;
        if (Buffer.Length >= 40) return;
        Buffer += ch;
    }

    public void AppendText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var ch in text)
        {
            if (Buffer.Length >= 40) break;
            if (ch is '\r' or '\n' or '\t') continue;
            Buffer += ch;
        }
    }

    public void Backspace()
    {
        if (Buffer.Length > 0) Buffer = Buffer[..^1];
    }

    public override void Build()
    {
        Buttons.Clear();
        Buttons.Add(new VirtualButton("tedit.commit", VirtualButtonKind.Action)
        {
            Slot = 2, Text = () => L10n.Get(LangScope.Screen, "confirm.ok"), Value = () => Buffer,
        });
        Buttons.Add(new VirtualButton("tedit.cancel", VirtualButtonKind.Action)
        {
            Slot = 3, Text = () => L10n.Get(LangScope.Screen, "confirm.cancel"),
        });
    }

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "tedit.commit":
                _onCommit(Buffer.Trim());
                return true;
            case "tedit.cancel":
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

        using var title = MfdTheme.CjkAt(26f * s);
        using var mono = MfdTheme.MonoBoldAt(48f * s);
        using var hintFont = MfdTheme.CjkAt(17f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cx = glass.Left + glass.Width / 2;
        g.DrawString(_label(), title, muted, cx - g.MeasureString(_label(), title).Width / 2,
                     glass.Top + 60 * s, StringFormat.GenericTypographic);

        var shown = Buffer.Length == 0 ? "_" : Buffer + "_";
        var w = g.MeasureString(shown, mono).Width;
        var boxW = Math.Max(w + 80 * s, 520 * s);
        var boxH = 84 * s;
        var bx = cx - boxW / 2;
        var by = glass.Top + glass.Height * 0.30f;

        using (var boxBg = new SolidBrush(Color.FromArgb(210, 6, 16, 10)))
            g.FillRectangle(boxBg, bx, by, boxW, boxH);
        using (var pen = new Pen(MfdTheme.Green, 2.5f * s))
            g.DrawRectangle(pen, bx, by, boxW, boxH);
        g.DrawString(shown, mono, green, cx - w / 2, by + 10 * s, StringFormat.GenericTypographic);

        var hint = L10n.Get(LangScope.Screen, "tedit.hint");
        g.DrawString(hint, hintFont, muted, cx - g.MeasureString(hint, hintFont).Width / 2,
                     by + boxH + 26 * s, StringFormat.GenericTypographic);

        DrawChoice(g, s, pad, 2, L10n.Get(LangScope.Screen, "confirm.ok"));
        DrawChoice(g, s, pad, 3, L10n.Get(LangScope.Screen, "confirm.cancel"));
    }

    private void DrawChoice(System.Drawing.Graphics g, float s, float pad, int sideKey, string label)
    {
        var row = _layout.ListRowRect(sideKey < 5 ? 0 : 1, sideKey % 5);
        using var font = MfdTheme.CjkAt(20f * s);
        using var bg = new SolidBrush(MfdTheme.GreenDim);
        using var fg = new SolidBrush(MfdTheme.Text);
        g.FillRectangle(bg, row);
        g.DrawString(label, font, fg, row.X + 14 * s, row.Y + 6 * s, StringFormat.GenericTypographic);
    }
}
