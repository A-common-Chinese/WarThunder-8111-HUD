// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 文本输入页（玩家昵称 / 呼号等）：ASCII 直接输入，中文/特殊符号用 Ctrl+V 粘贴。
/// 交互与数值编辑页保持一致：确认/取消是**选项**（侧键/上下选择、CONFIRM 执行、双击鼠标执行），
/// 不是按下即执行的按钮。
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

    /// <summary>0 = 确认, 1 = 取消（与数值编辑页同一套选择语义）。</summary>
    public int Choice { get; private set; }

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
        AddChoice("tedit.commit", sideKey: 2, choice: 0, "confirm.ok", () => Buffer);
        AddChoice("tedit.cancel", sideKey: 3, choice: 1, "confirm.cancel", () => "");
    }

    private void AddChoice(string id, int sideKey, int choice, string labelKey, Func<string> value)
    {
        Buttons.Add(new VirtualButton(id, VirtualButtonKind.Action)
        {
            Slot = sideKey % 5,
            Column = sideKey / 5,
            Text = () => L10n.Get(LangScope.Screen, labelKey),
            Value = value,
            Target = MfdCommand.Of("tedit.select", choice.ToString()),
            ActivateBy = new[] { "SIDE" + MfdPageLayout.CellFromSideKey(sideKey) },
        });
    }

    public int HitOption(PointF p)
    {
        var s = _layout.Scale;
        foreach (var (sideKey, choice) in OptionRows)
        {
            var row = _layout.ListRowRect(sideKey < 5 ? 0 : 1, sideKey % 5);
            row.Inflate(6 * s, 0);
            if (row.Contains(p)) return choice;
        }
        return -1;
    }

    private static readonly (int SideKey, int Choice)[] OptionRows = { (2, 0), (3, 1) };

    public void Select(int index) => Choice = index < 0 ? Choice : Math.Clamp(index, 0, 1);

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "tedit.select":
                if (int.TryParse(cmd.Arg, out var i)) Select(i);
                return true;
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
        using var green = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cx = glass.Left + glass.Width / 2;
        g.DrawString(_label(), title, muted, cx - g.MeasureString(_label(), title).Width / 2,
                     glass.Top + 60 * s, StringFormat.GenericTypographic);

        // 框宽固定，超宽只保留尾部（光标所在端），正在输入的那一段永远看得见。
        var boxW = Math.Min(760 * s, glass.Width - pad * 2 - 40 * s);
        var boxH = 84 * s;
        var bx = cx - boxW / 2;
        var by = glass.Top + glass.Height * 0.30f;

        var shown = Buffer.Length == 0 ? "_" : Buffer + "_";
        var maxTextW = boxW - 48 * s;
        if (g.MeasureString(shown, mono).Width > maxTextW)
        {
            var keep = shown.Length;
            while (keep > 1 && g.MeasureString("…" + shown[^keep..], mono).Width > maxTextW) keep--;
            shown = "…" + shown[^keep..];
        }

        using (var boxBg = new SolidBrush(Color.FromArgb(210, 6, 16, 10)))
            g.FillRectangle(boxBg, bx, by, boxW, boxH);
        using (var pen = new Pen(MfdTheme.Green, 2.5f * s))
            g.DrawRectangle(pen, bx, by, boxW, boxH);
        g.DrawString(shown, mono, green, cx - g.MeasureString(shown, mono).Width / 2, by + 10 * s,
                     StringFormat.GenericTypographic);

        DrawChoiceRow(g, s, 2, 0, L10n.Get(LangScope.Screen, "confirm.ok"));
        DrawChoiceRow(g, s, 3, 1, L10n.Get(LangScope.Screen, "confirm.cancel"));
    }

    private void DrawChoiceRow(System.Drawing.Graphics g, float s, int sideKey, int choice, string label)
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
