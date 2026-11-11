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
        // 这两行是动作而不是选项（按下即提交/取消），所以 Target 直接是执行命令，
        // 与原来的侧键行为一致。
        AddAction("tedit.commit", sideKey: 2, "confirm.ok", () => Buffer);
        AddAction("tedit.cancel", sideKey: 3, "confirm.cancel", () => "");
    }

    private void AddAction(string id, int sideKey, string labelKey, Func<string> value)
    {
        Buttons.Add(new VirtualButton(id, VirtualButtonKind.Action)
        {
            Slot = sideKey % 5,
            Column = sideKey / 5,
            Text = () => L10n.Get(LangScope.Screen, labelKey),
            Value = value,
            Target = MfdCommand.Of(id),
            ActivateBy = new[] { "SIDE" + MfdPageLayout.CellFromSideKey(sideKey) },
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
        using var green = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cx = glass.Left + glass.Width / 2;
        g.DrawString(_label(), title, muted, cx - g.MeasureString(_label(), title).Width / 2,
                     glass.Top + 60 * s, StringFormat.GenericTypographic);

        // 框宽固定，不再随文本长度增长：40 字符的长昵称曾把框撑到玻璃外面（压住侧键和边框）。
        // 超宽时只保留尾部（光标所在端），前面补省略号 —— 正在输入的那一段永远看得见。
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

        // 这里不再自绘提示行：页脚已经按 HintKey 统一画了一遍（同一句 tedit.hint），
        // 页面自己再画一次会正好落在「确认」行上，把那一行的左半截盖掉。
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
