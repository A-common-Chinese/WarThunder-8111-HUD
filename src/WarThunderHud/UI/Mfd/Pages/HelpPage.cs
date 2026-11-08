// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.Hud;
using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 系统帮助/导航手册：左侧按键说明 + 结构树，右侧选中条目说明；
/// PAGE 翻页、UP/DOWN 选择、可点击选中。
/// 内容由 <c>pages()</c> / <c>cfg()</c> 实时读取，页面不持有配置或模块列表的副本。
/// </summary>
internal sealed class HelpPage : MfdPageBase
{
    private const float HelpLineHeight = 30f;

    private readonly Func<IReadOnlyList<MfdPage>> _pages;
    private readonly Func<HudConfig> _cfg;
    private readonly Action _onClose;
    private readonly MfdPageLayout _layout;

    /// <summary>当前页的扁平行文本（物理按键说明 + 结构树，跨模块连续排版）。</summary>
    private List<string> _lines = new();

    /// <summary>结构树条目：行号 + 说明 key，选中/说明面板都按这张表定位。</summary>
    private List<(int Line, string Key)> _entries = new();

    public HelpPage(Func<IReadOnlyList<MfdPage>> pages,
                    Func<HudConfig> cfg,
                    Action onClose,
                    MfdPageLayout layout) : base("help")
    {
        _pages = pages;
        _cfg = cfg;
        _onClose = onClose;
        _layout = layout;
    }

    public override string TitleKey => "help.title";
    public override string HintKey => "help.pagerHint";

    /// <summary>当前页号（0 起）。</summary>
    public int PageIndex { get; private set; }

    /// <summary>选中项在结构树条目表里的下标（不是屏幕行号）。</summary>
    public int Selected { get; private set; }

    /// <summary>UP/DOWN：选中上/下一条；跨页时页码跟随选中条目（原 MoveSelection 的 help 分支）。</summary>
    public void Move(int delta)
    {
        var per = HelpLinesPerPage(_layout.Glass, _layout.Scale);
        BuildHelpLines();
        MoveHelpSelection(delta);
        if (_entries.Count > 0)
            PageIndex = _entries[Selected].Line / per;
    }

    /// <summary>PAGE：翻页并选中目标页第一个条目；没有下一页时保持不动（原 CyclePage 的 help 分支）。</summary>
    public void CyclePage(int delta)
    {
        var per = HelpLinesPerPage(_layout.Glass, _layout.Scale);
        var total = Math.Max(1, (BuildHelpLines().Count + per - 1) / per);
        var next = Math.Clamp(PageIndex + delta, 0, total - 1);
        if (next != PageIndex) SelectFirstEntryOnPage(next, per);
    }

    /// <summary>跳到指定页并选中该页首个条目（预览用）。</summary>
    public void ShowPage(int page)
    {
        var per = HelpLinesPerPage(_layout.Glass, _layout.Scale);
        var total = Math.Max(1, (BuildHelpLines().Count + per - 1) / per);
        SelectFirstEntryOnPage(Math.Clamp(page, 0, total - 1), per);
    }

    /// <summary>点击屏幕行选中结构树条目；点到非结构树行时选中本页最近的条目。</summary>
    public bool TryClick(System.Drawing.PointF p)
    {
        if (!_layout.Glass.Contains(p)) return false;

        var s = _layout.Scale;
        var glass = _layout.Glass;
        var textTop = glass.Top + 72 * s;                 // 页眉 + 虚线之后
        var lineH = HelpLineHeight * s;

        var per = HelpLinesPerPage(glass, s);
        var lines = BuildHelpLines();
        var total = Math.Max(1, (lines.Count + per - 1) / per);
        if (PageIndex >= total) PageIndex = total - 1;

        var rowIndex = (int)((p.Y - textTop) / lineH);
        if (rowIndex < 0) return false;

        var lineIndex = PageIndex * per + rowIndex;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Line != lineIndex) continue;
            Selected = i;
            return true;
        }

        // 点到非结构树行：选中本页最近的条目，保持左右一致
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Line / per == PageIndex) { Selected = i; return true; }
        }
        return false;
    }

    /// <summary>只处理返回；HELP 键再按一次关闭的行为留在 Form（Form 在 Mode.Help 时先调 LeaveHelp）。</summary>
    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "nav.back":
            case "help.close":
                _onClose();
                return true;
        }
        return false;
    }

    public override void Draw(MfdRenderContext ctx) =>
        DrawHelpStructure(ctx.G, ctx.Scale, ctx.Glass, ctx.Pad);

    private int HelpLinesPerPage(RectangleF glass, float s) =>
        Math.Max(8, (int)((glass.Height - 78 * s) / (HelpLineHeight * s)));

    /// <summary>Flat help content: key reference followed by every module and every item.</summary>
    private List<string> BuildHelpLines()
    {
        var zh = L10n.Screen == Lang.Zh;
        var pages = _pages();
        var cfg = _cfg();
        var lines = new List<string>();
        _entries = new List<(int Line, string Key)>();
        void Add(string t) => lines.Add(t);
        void AddEntry(string t, string key)
        {
            _entries.Add((lines.Count, key));
            lines.Add(t);
        }

        // ---- 物理按键：每一项都带方框 ----
        Add(zh ? "[ 物理按键功能说明 ]" : "[ PHYSICAL KEY FUNCTIONS ]");
        Add("");
        Add(zh ? " \u25a1 UP       : 向上移动高亮选项" : " \u25a1 UP       : move highlight up");
        Add(zh ? " \u25a1 DOWN     : 向下移动高亮选项" : " \u25a1 DOWN     : move highlight down");
        Add(zh ? " \u25a1 CONFIRM  : 确认 / 进入所选模块" : " \u25a1 CONFIRM  : apply / enter module");
        Add(zh ? " \u25a1 BACK     : 返回上一级" : " \u25a1 BACK     : leave one level");
        Add(zh ? " \u25a1 HELP     : 打开本手册" : " \u25a1 HELP     : this manual");
        Add(zh ? " \u25a1 RESET    : 本页恢复默认" : " \u25a1 RESET    : reset current page");
        Add(zh ? " \u25a1 PAGE-/PAGE+: 翻页" : " \u25a1 PAGE+/-  : flip pages");
        Add(zh ? " \u25a1 CANCEL   : 放弃改动并关闭" : " \u25a1 CANCEL   : discard and close");
        Add(zh ? " \u25a1 SAVE     : 保存并关闭" : " \u25a1 SAVE     : save and close");
        Add(zh ? " \u25a1 侧键 x10 : 选择对应行" : " \u25a1 SIDE KEYS: select row");
        Add("");

        // ---- 结构树：全部展开 ----
        AddEntry(zh ? "\u251c\u2500 L0  首页" : "\u251c\u2500 L0  MAIN MENU", "L0");
        for (var m = 0; m < pages.Count; m++)
        {
            var p = pages[m];
            var isLast = m == pages.Count - 1;
            var branch = isLast ? "\u2514\u2500" : "\u251c\u2500";
            var name = L10n.Get(LangScope.Screen, p.MenuKey).ToUpperInvariant();
            AddEntry($"     {branch} \u25b8 {name}", p.MenuKey);

            for (var i = 0; i < p.Items.Count; i++)
            {
                var it = p.Items[i];
                var itemLast = i == p.Items.Count - 1;
                var tail = itemLast ? "\u2514\u2500" : "\u251c\u2500";
                var side = i % 2 == 0 ? (zh ? "左" : "L") : (zh ? "右" : "R");
                var slot = $"{side}{i / 2 + 1}";
                var label = L10n.Get(LangScope.Screen, it.LabelKey);
                var value = it.ValueText(cfg);
                AddEntry($"      |        {tail} {slot,-4} {label,-20} [{value}]", it.LabelKey);
            }
        }

        _lines = lines;
        return _lines;
    }

    /// <summary>Selects the first tree entry that lives on the given page.</summary>
    private void SelectFirstEntryOnPage(int page, int per)
    {
        PageIndex = page;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Line / per == page) { Selected = i; return; }
        }
        Selected = 0;
    }

    private void MoveHelpSelection(int delta)
    {
        if (_entries.Count == 0) return;
        Selected = Math.Clamp(Selected + delta, 0, _entries.Count - 1);
    }

    /// <summary>Draws the description of the selected entry in a right-hand column.</summary>
    private void DrawHelpDescription(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        if (_entries.Count == 0) return;
        Selected = Math.Clamp(Selected, 0, _entries.Count - 1);
        var key = _entries[Selected].Key;
        var text = HelpDescriptions.Get(key, L10n.Screen == Lang.Zh);
        if (string.IsNullOrWhiteSpace(text)) return;

        using var title = MfdTheme.CjkAt(16f * s);
        using var body = MfdTheme.CjkAt(16f * s);
        using var accent = new SolidBrush(MfdTheme.Green);
        using var ink = new SolidBrush(MfdTheme.Text);

        var colX = glass.Right - glass.Width * 0.34f;
        var colW = glass.Width * 0.34f - pad;
        using (var sep = new Pen(MfdTheme.Muted, 1f))
        {
            sep.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            g.DrawLine(sep, colX - 8 * s, glass.Top + 40 * s, colX - 8 * s, glass.Bottom - 34 * s);
        }

        g.DrawString("INFO", title, accent, colX, glass.Top + 42 * s, StringFormat.GenericTypographic);

        var y = glass.Top + 70 * s;
        foreach (var raw in text.Split('\n'))
        {
            foreach (var line in PageText.Wrap(g, body, raw, colW))
            {
                if (y > glass.Bottom - 40 * s) return;
                g.DrawString(line, body, ink, colX, y, StringFormat.GenericTypographic);
                y += 24 * s;
            }
        }
    }

    private void DrawHelpStructure(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        using var head = MfdTheme.CjkAt(22f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var cjk = MfdTheme.CjkAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var zh = L10n.Screen == Lang.Zh;
        var left = glass.Left + pad;
        var y = glass.Top + 34 * s;

        var lines = BuildHelpLines();
        var per = HelpLinesPerPage(glass, s);
        var total = Math.Max(1, (lines.Count + per - 1) / per);
        PageIndex = Math.Clamp(PageIndex, 0, total - 1);

        var title = zh ? "SYSTEM HELP / 导航手册" : "SYSTEM HELP / NAVIGATION";
        g.DrawString(title, head, accent, left, y, StringFormat.GenericTypographic);
        var pager = $"{PageIndex + 1}/{total}";
        g.DrawString(pager, head, text, glass.Right - pad - g.MeasureString(pager, head).Width, y,
                     StringFormat.GenericTypographic);

        y += 30 * s;
        using (var dash = new Pen(MfdTheme.Muted, 1f))
        {
            dash.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            dash.DashPattern = new[] { 4f, 3f };
            g.DrawLine(dash, left, y, glass.Right - pad, y);
        }
        y += 8 * s;

        // 逐行填满整页（跨模块连续排版）
        // 切页后自动选中本页第一个结构树项
        if (_entries.Count > 0)
        {
            var selLine = _entries[Math.Clamp(Selected, 0, _entries.Count - 1)].Line;
            if (selLine / per != PageIndex) SelectFirstEntryOnPage(PageIndex, per);
        }

        var lineH = HelpLineHeight * s;
        var start = PageIndex * per;
        var selLineIndex = _entries.Count > 0
            ? _entries[Math.Clamp(Selected, 0, _entries.Count - 1)].Line
            : -1;

        for (var i = start; i < Math.Min(lines.Count, start + per); i++)
        {
            if (y + lineH > glass.Bottom - 30 * s) break;
            var line = lines[i];
            var f = line.Any(c => c > 0x7F) ? cjk : body;

            if (i == selLineIndex)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, left - 4 * s, y - 2 * s,
                                glass.Width * 0.62f, lineH - 4 * s);
                using var dark = new SolidBrush(MfdTheme.Dark);
                g.DrawString(line, f, dark, left, y, StringFormat.GenericTypographic);
            }
            else
            {
                g.DrawString(line, f, line.StartsWith("  [") ? muted : text, left, y,
                             StringFormat.GenericTypographic);
            }
            y += lineH;
        }

        DrawHelpDescription(g, s, glass, pad);
    }
}
