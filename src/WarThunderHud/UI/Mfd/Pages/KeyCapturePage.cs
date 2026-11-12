// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using System.Windows.Forms;
using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 键位捕获页：按下什么就显示什么，全部松开后冻结，CONFIRM 提交 / BACK 取消。
/// <para>它曾经是唯一一个「有 Mode 但没有页面类」的页面 —— 状态、绘制、键盘处理散在
/// <c>MfdFrameForm</c> 的六个地方（字段、三个 switch、两个键盘分支、一个格式化函数）。
/// 收进来之后，那三个 switch 分支都只剩一句转调。</para>
/// <para>页面不消费命令：确认要写配置、取消要退回模块页，那些只有表单知道。</para>
/// </summary>
internal sealed class KeyCapturePage : MfdPageBase
{
    /// <summary>修饰键的固定显示顺序 —— 不能靠 HashSet 的枚举顺序，否则 CTRL+Q 会时而变成 Q+CTRL。</summary>
    private static readonly Keys[] ModifierOrder = { Keys.ControlKey, Keys.ShiftKey, Keys.Menu };

    private readonly Func<string> _label;
    private readonly MfdPageLayout _layout;
    private readonly HashSet<Keys> _held = new();

    public KeyCapturePage(Func<string> label, MfdPageLayout layout) : base("keycapture")
    {
        _label = label;
        _layout = layout;
    }

    public override string TitleKey => "capture.title";
    public override string HintKey => "capture.hint";

    /// <summary>边按边显示的组合文本。空 = 还没按。</summary>
    public string Buffer { get; private set; } = "";

    /// <summary>可提交的组合；空或只有修饰键时返回 null，让调用方保持原值。</summary>
    public string? Result => string.IsNullOrWhiteSpace(Buffer) ? null : Buffer;

    /// <summary>清空：重新进入或取消时调用。</summary>
    public void Reset()
    {
        Buffer = "";
        _held.Clear();
    }

    /// <summary>按下：新一轮组合从空开始，避免上一轮的残渣接在前面。</summary>
    public void KeyDown(Keys key)
    {
        if (_held.Count == 0) Buffer = "";
        _held.Add(key);
        Buffer = Format();
    }

    /// <summary>松开：只移出集合，文本保持在最后一次完整组合上（冻结）。</summary>
    public void KeyUp(Keys key) => _held.Remove(key);

    /// <summary>预览用：直接塞一个成品文本（mfdpreview 的 capture 样张）。</summary>
    public void SetPreview(string text) => Buffer = text;

    public override void Build()
    {
        // 没有屏幕按钮：只用 CONFIRM / BACK 两个固定键
    }

    public override bool Handle(MfdCommand cmd) => false;

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        var glass = ctx.Glass;

        using var title = MfdTheme.CjkAt(26f * s);
        using var mono = MfdTheme.MonoBoldAt(44f * s);
        using var hint = MfdTheme.CjkAt(18f * s);
        using var big = MfdTheme.CjkAt(28f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var name = _label();
        var cx = glass.Left + glass.Width / 2;
        g.DrawString(name, title, muted, cx - g.MeasureString(name, title).Width / 2,
                     glass.Top + 60 * s, StringFormat.GenericTypographic);

        var prompt = L10n.Get(LangScope.Screen, "capture.prompt");
        g.DrawString(prompt, hint, text, cx - g.MeasureString(prompt, hint).Width / 2,
                     glass.Top + glass.Height * 0.26f, StringFormat.GenericTypographic);

        var shown = Buffer.Length == 0 ? "—" : Buffer;
        var w = g.MeasureString(shown, mono).Width;
        g.DrawString(shown, mono, green, cx - w / 2, glass.Top + glass.Height * 0.36f,
                     StringFormat.GenericTypographic);

        // 确认 / 取消两行，槽位与 ConfirmPage 完全一致（侧键 2 / 3）
        for (var i = 0; i < 2; i++)
        {
            var row = _layout.ListRowRect(0, 2 + i);
            if (i == 0)
                g.FillRectangle(green, row.X - 6 * s, row.Y, row.Width + 12 * s, row.Height);
            using var fgBrush = new SolidBrush(i == 0 ? MfdTheme.Dark : MfdTheme.Text);
            var label = L10n.Get(LangScope.Screen, i == 0 ? "confirm.ok" : "confirm.cancel");
            g.DrawString(label, big, fgBrush, row.X + 20 * s, row.Y + 2 * s,
                         StringFormat.GenericTypographic);
        }
    }

    /// <summary>修饰键固定在前，其余按虚拟键码排序 —— 结果与按键先后无关。</summary>
    private string Format()
    {
        var parts = new List<string>();
        foreach (var mod in ModifierOrder)
            if (_held.Contains(mod)) parts.Add(ModName(mod));

        foreach (var key in _held.OrderBy(k => (int)k))
        {
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) continue;
            parts.Add(KeyName(key));
        }
        return parts.Count == 0 ? "" : string.Join("+", parts);
    }

    private static string ModName(Keys k) => k switch
    {
        Keys.ControlKey => "CTRL",
        Keys.ShiftKey => "SHIFT",
        Keys.Menu => "ALT",
        _ => k.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// 键名列。★ OEM 键必须映射成符号：`Keys.Oemtilde.ToString()` 是 <c>Oemtilde</c>，
    /// 存进配置后 <see cref="WtHud.Input.KeyBinding.NameToVk"/> 认不出来，会静默退回默认键 ——
    /// 用户看到的是「按 ` 能录进去，但游戏里不生效」，改别的键就好了。
    /// </summary>
    private static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "NUM" + (k - Keys.NumPad0),
        >= Keys.A and <= Keys.Z => ((char)('A' + (k - Keys.A))).ToString(),
        Keys.Oemtilde => "~",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        _ => k.ToString().ToUpperInvariant(),
    };
}
