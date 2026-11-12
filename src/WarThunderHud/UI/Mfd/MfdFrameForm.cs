// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using WtHud.Hud;

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd;

/// <summary>
/// MFD-styled settings panel: module menu → module page (2 x 5 cells aligned with the side
/// buttons) → confirmation sheet for destructive/one-shot actions.
/// </summary>
public sealed class MfdFrameForm : Form
{
    private static readonly string[] TopKeys = { "confirm", "back", "up", "down", "help" };
    private static readonly string[] BottomKeys = { "reset", "pagePrev", "pageNext", "cancel", "save" };

    private enum ButtonKind { None, Top, Bottom, Side }
    private enum Mode { Menu, Items, Confirm, EditNumber, CaptureKey, TextEdit, Preset, Help, Detail, PickVehicle, Snapshots }

    private readonly MfdGeometry _geo;
    private readonly MfdLayout _layout;
    private readonly Bitmap _bezel;
    private readonly HudConfig _cfg;
    private readonly HudConfig _work;
    private readonly List<MfdPage> _pages;
    // 每个一级模块一个页面对象：绘制/命中/选中/翻页都在页面里，表单只留下「确认之后做什么」。
    // 与 _pages 同索引、同顺序。
    private readonly List<Pages.MfdItemsPage> _itemPages = new();

    private Mode _mode = Mode.Menu;
    private readonly Pages.MfdNavigator _nav = new();
    private Pages.ConfirmPage? _confirmPage;
    private Pages.NumberEditPage? _editPage;
    private Pages.TextEditPage? _textPage;
    private Pages.PresetPage? _presetPage;
    private Pages.PickVehiclePage? _pickPageUI;
    private Pages.SnapshotsPage? _snapPageUI;
    private int _page;
    // 三级页面：只在 _mode == Mode.Items 时有意义。_subPage 是定义，_subPageUI 是渲染实例
    // （按 Key 缓存 → 退出再进时选中位置还在）。
    private MfdPage? _subPage;
    private Pages.MfdItemsPage? _subPageUI;
    private readonly Dictionary<string, Pages.MfdItemsPage> _subCache = new();
    private MfdPageLayout? _itemLayout;      // 与一级模块同一套布局，造子页实例时要用
    private int _menuSel;
    private int _menuPageIdx;              // 主菜单页号（每页 10 个模块，与模块内页号独立）
    private MfdItem? _pendingAction;
    private bool _dirty;
    private bool _closeConfirm;
    private bool _forceClose;
    private (ButtonKind Kind, int Index) _pressed = (ButtonKind.None, -1);
    private string? _toast;
    private Pages.KeyCapturePage? _keyPage;   // 键位捕获页：有 Mode 也有页面类，但不进 _nav（它无按钮）
    private Mode _helpPrevMode = Mode.Menu;
    private int _helpPrevPage;
    private int _helpPrevItemSel;
    private Pages.HelpPage? _helpPageUI;
    private Pages.DetailPage? _detailPageUI;
    private bool _resetConfirm;
    private MfdScreenPage? _menuPage;
    private MfdInputRouter? _menuRouter;
    // 二级页面自己的 router：页面在 Build() 里声明按钮（位置 + 侧键 + 命令），
    // 这里只负责换绑，不再由表单硬编码 index→选项。
    private MfdInputRouter? _pageRouter;
    private List<RealButton>? _realButtons;
    // 双击判定：区域名 + 索引一起比。以前所有点击共用一个 _lastHitCell，
    // 「确认页第 3 项」和「模块页第 3 行」索引相同就会互相误判成双击。
    private (string Zone, int Index) _lastHitKey = ("", int.MinValue);
    private DateTime _lastHitUtc = DateTime.MinValue;

    public bool Saved { get; private set; }

    private readonly string? _savePath;

    /// <param name="savePath">When set, SAVE writes here instead of hud-config.json (scratch mode).</param>
    public MfdFrameForm(HudConfig cfg, string? savePath = null)
    {
        _savePath = savePath;
        _cfg = cfg;
        _work = cfg.Clone();
        WtHud.Hud.ConfigSnapshots.MarkCancelPoint(cfg);   // CANCEL 回退点（每次 F1 覆盖）
        WtHud.Hud.ConfigSnapshots.BeginSession();          // 本次未保存操作的快照目录
        L10n.LoadFromConfig(_work.Language);
        MfdTheme.ApplyColors(_work.Mfd.InkColor, _work.Mfd.AccentColor);

        _pages = MfdPages.Build();
        _geo = MfdGeometry.Load();
        _bezel = LoadBezel();

        // 自适应：按工作区大小缩放，最多 100%，最小 55%（1080p 屏可完整显示）
        // 面板跟随游戏所在显示器：多屏时不会开到主屏上
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        if (WtHud.Overlay.GameWindowTracker.TryGetClientRect(out var gx, out var gy, out var gw, out var gh, out _))
        {
            var screen = Screen.FromRectangle(new Rectangle(gx, gy, gw, gh));
            if (screen != null) work = screen.WorkingArea;
        }
        var fit = Math.Min(1.0f, Math.Min(work.Width * 0.92f / _bezel.Width, work.Height * 0.92f / _bezel.Height));
        var envFit = Environment.GetEnvironmentVariable("WT_MFD_FIT");
        if (float.TryParse(envFit, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var forced) && forced > 0.1f)
            fit = forced;
        fit = Math.Clamp(fit, 0.55f, 1.0f);
        var panelW = (int)(_bezel.Width * fit);
        var panelH = (int)(_bezel.Height * fit);

        ClientSize = new Size(panelW, panelH);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        KeyPreview = true;
        DoubleBuffered = true;
        TopMost = true;
        _layout = _geo.ScaleTo(panelW, panelH);
        ApplySilhouetteRegion(panelW, panelH);

        // 一级模块页对象：几何与配置都用委托/共享实例，页面自己不会去碰别的东西。
        var pageLayout = new MfdPageLayout(_layout, _layout.Width / 1330f);
        _itemLayout = pageLayout;
        foreach (var data in _pages)
        {
            var itemsPage = new Pages.MfdItemsPage(data, () => _work, pageLayout, Activate);
            itemsPage.Build();          // 先造一遍按钮，首次按键就能路由
            _itemPages.Add(itemsPage);
        }
    }

    private static Bitmap LoadBezel()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("mfd-frame.png", StringComparison.OrdinalIgnoreCase));
        if (name == null) return new Bitmap(1330, 1064);
        using var stream = asm.GetManifestResourceStream(name)!;
        return new Bitmap(stream);
    }

    /// <summary>
    /// 当前页定义：三级页优先，否则是当前模块。
    /// <para>★ 所有读 CurrentPage 的地方（激活分派、命中、特判）都自动支持三级页，
    /// 不需要各自判断「现在在第几层」。</para>
    /// </summary>
    private MfdPage? CurrentPage => _subPage ?? (_page >= 0 && _page < _pages.Count ? _pages[_page] : null);

    /// <summary>当前模块的页面对象 —— 绘制、命中、选中、翻页都问它。</summary>
    private Pages.MfdItemsPage? ItemsPage => _subPage != null
        ? _subPageUI
        : _page >= 0 && _page < _itemPages.Count ? _itemPages[_page] : null;

    /// <summary>进入下一级页面。实例按 Key 缓存，退出再进来选中位置不变。</summary>
    private void EnterSubPage(MfdPage page)
    {
        if (!_subCache.TryGetValue(page.CacheKey, out var ui))
        {
            ui = new Pages.MfdItemsPage(page, () => _work, _itemLayout!, Activate);
            ui.Build();
            _subCache[page.CacheKey] = ui;
        }
        _subPage = page;
        _subPageUI = ui;
        _mode = Mode.Items;
        _toast = null;
        Invalidate();
    }

    /// <summary>从三级页退回到进入它的那一页（模块页或上一级）。</summary>
    private void LeaveSubPage()
    {
        _subPage = null;
        _subPageUI = null;
        _toast = null;
        SyncDiagProbe();
        Invalidate();
    }

    /// <summary>当前占着面板的二级页面（没有就是 null）。</summary>
    private Pages.MfdPageBase? ActivePage => _mode switch
    {
        Mode.Items => ItemsPage,
        Mode.Confirm => _confirmPage,
        Mode.EditNumber => _editPage,
        Mode.TextEdit => _textPage,
        Mode.Preset => _presetPage,
        Mode.Help => _helpPageUI,
        Mode.Detail => _detailPageUI,
        Mode.PickVehicle => _pickPageUI,
        Mode.Snapshots => _snapPageUI,
        _ => null,
    };

    /// <summary>
    /// 把侧键交给当前页面的按钮：页面自己声明了槽位和按键，表单不再猜 index 的含义。
    /// 返回 true 表示页面消费了这次按键。
    /// </summary>
    private bool RouteSideKeyToPage(int sideKey)
    {
        var page = ActivePage;
        if (page == null || page.Buttons.Count == 0) return false;

        _pageRouter ??= new MfdInputRouter(
            new MfdPageLayout(_layout, _layout.Width / 1330f), _realButtons);
        // 每次按键都重绑：页面重复 Push 会重新 Build（按钮对象全换），
        // 缓存绑定会留上一轮的对象。最多 10 个，成本可忽略。
        _pageRouter.SetButtons(page.Buttons);

        var cmd = _pageRouter.PressSlot("SIDE" + MfdPageLayout.CellFromSideKey(sideKey),
                                        InputSource.RealButton);
        return cmd != null && page.Handle(cmd);
    }

    /// <summary>选中项由当前模块页面持有（表单不再存一份副本）。</summary>
    private MfdItem? SelectedItem => ItemsPage?.SelectedItem;

    /// <summary>
    /// 双击判定。区域名把不同类型的点击隔开：共用一个字段时，「确认页第 3 项」和
    /// 「模块页第 3 行」在 400 ms 内会互相触发，等于点一下就执行。
    /// </summary>
    /// <summary>双击判定的纯逻辑：区域不同、索引不同、或已超时，都不算重复。</summary>
    internal static bool IsRepeat((string Zone, int Index) previous, string zone, int index,
                                  double elapsedMs, double windowMs)
        => previous.Zone == zone && previous.Index == index && elapsedMs < windowMs;

    private bool IsRepeatClick(string zone, int index, double windowMs)
    {
        var now = DateTime.UtcNow;
        bool again = IsRepeat(_lastHitKey, zone, index,
                              (now - _lastHitUtc).TotalMilliseconds, windowMs);
        _lastHitKey = (zone, index);
        _lastHitUtc = now;
        return again;
    }

    /// <summary>点了空白格：清掉上次点击，避免下次点击被当成连击。</summary>
    private void ClearRepeatClick() => _lastHitKey = ("", int.MinValue);

    private static RectangleF InsetRect(RectangleF r, float d) =>
        new(r.X + d, r.Y + d, Math.Max(2, r.Width - d * 2), Math.Max(2, r.Height - d * 2));

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e) => RenderTo(e.Graphics);

    public void RenderTo(System.Drawing.Graphics g)
    {
        MfdTheme.Smooth(g);
        g.Clear(Color.Black);
        g.DrawImage(_bezel, 0, 0, _layout.Width, _layout.Height);
        DrawButtonStrips(g);
        DrawPanelLabels(g);
        DrawScreen(g);
    }

    private void DrawButtonStrips(System.Drawing.Graphics g)
    {
        var s = _layout.Width / 1330f;

        void Strip(RectangleF r, ButtonKind kind, int index)
        {
            var inner = InsetRect(r, 11f * s);
            var pressed = _pressed.Kind == kind && _pressed.Index == index;
            using var halo = new Pen(Color.FromArgb(pressed ? 26 : 70, MfdTheme.Green), 4f * s);
            using var strip = new Pen(pressed ? MfdTheme.GreenDim : MfdTheme.Green, (pressed ? 1.1f : 1.8f) * s);
            var path = RoundRect(inner, 2f * s);
            g.DrawPath(halo, path);
            g.DrawPath(strip, path);
            if (pressed)
            {
                using var shade = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
                g.FillPath(shade, path);
            }
        }

        for (var i = 0; i < _layout.TopButtons.Length; i++) Strip(_layout.TopButtons[i], ButtonKind.Top, i);
        for (var i = 0; i < _layout.BottomButtons.Length; i++) Strip(_layout.BottomButtons[i], ButtonKind.Bottom, i);
        for (var i = 0; i < _layout.SideButtons.Length; i++) Strip(_layout.SideButtons[i], ButtonKind.Side, i);
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Max(1f, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void DrawPanelLabels(System.Drawing.Graphics g)
    {
        var s = _layout.Width / 1330f;
        using var f = MfdTheme.MonoAt(13f * s);
        using var fb = MfdTheme.MonoBoldAt(13f * s);

        for (var i = 0; i < _layout.TopButtons.Length && i < TopKeys.Length; i++)
        {
            var r = _layout.TopButtons[i];
            DrawCentered(g, L10n.Get(LangScope.Panel, TopKeys[i]), fb, MfdTheme.Ink,
                r.X + r.Width / 2, _layout.TopLabelY - 8 * s);
        }
        for (var i = 0; i < _layout.BottomButtons.Length && i < BottomKeys.Length; i++)
        {
            var r = _layout.BottomButtons[i];
            DrawCentered(g, L10n.Get(LangScope.Panel, BottomKeys[i]), f,
                i == 4 ? MfdTheme.Green : MfdTheme.Ink, r.X + r.Width / 2, _layout.BottomLabelY + 6 * s);
        }
    }

    private void DrawCentered(System.Drawing.Graphics g, string text, Font font, Color colour, float cx, float cy)
    {
        var size = g.MeasureString(text, font);
        using var brush = new SolidBrush(colour);
        g.DrawString(text, font, brush, cx - size.Width / 2, cy - 2, StringFormat.GenericTypographic);
    }

    private void DrawScreen(System.Drawing.Graphics g)
    {
        var s = _layout.Width / 1330f;
        var glass = _layout.Glass;

        using (var bg = new SolidBrush(MfdTheme.ScreenBg)) g.FillRectangle(bg, glass);
        using (var grid = new Pen(MfdTheme.Grid))
        {
            for (var x = glass.Left; x < glass.Right; x += 26 * s) g.DrawLine(grid, x, glass.Top, x, glass.Bottom);
            for (var y = glass.Top; y < glass.Bottom; y += 26 * s) g.DrawLine(grid, glass.Left, y, glass.Right, y);
        }

        var pad = 18 * s;
        var left = glass.Left + pad;

        using var brand = MfdTheme.MonoBoldAt(16f * s);
        using var meta = MfdTheme.MonoAt(14f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        g.DrawString(L10n.Get(LangScope.Panel, "brand"), brand, green, left, glass.Top + 6 * s, StringFormat.GenericTypographic);
        // 标题由页面自己给（MfdPageBase.TitleKey）；表单只知道没有页面类的那几种。
        var titleKey = _mode switch
        {
            Mode.Menu => "menu",
            Mode.CaptureKey => _keyPage?.TitleKey ?? "capture.title",
            _ => ActivePage?.TitleKey ?? CurrentPage?.TitleKey ?? "menu",
        };
        var title = L10n.Get(LangScope.Screen, titleKey);
        // 三级页标题显示「父 › 子」：只有一层箭头，但足够回答「我在哪」。
        if (_mode == Mode.Items && _subPage?.ParentTitleKey is { } parentKey)
            title = L10n.Get(LangScope.Screen, parentKey) + " › " + title;
        if (_mode is Mode.EditNumber)      // 只有编辑页需要说明正在改哪一层
        {
            var scopeText = WtHud.Hud.EditScope.HeaderText;
            using var scopeFont = MfdTheme.CjkAt(15f * s);
            using var scopeBrush = new SolidBrush(MfdTheme.Green);

            // 左侧显示；机型名过长就截断，避免越过边框
            var maxW = glass.Width * 0.45f;
            while (scopeText.Length > 4 && g.MeasureString(scopeText, scopeFont).Width > maxW)
                scopeText = scopeText[..^2] + "…";
            g.DrawString(scopeText, scopeFont, scopeBrush, glass.Left + pad, glass.Top + 30 * s,
                         StringFormat.GenericTypographic);
        }
        g.DrawString(title, meta, muted, glass.Left + glass.Width / 2 - g.MeasureString(title, meta).Width / 2,
                     glass.Top + 8 * s);
        // 页眉右上角：菜单显示菜单页号，模块内显示条目页号，其余页面不显示
        var (pageNo, pages) = _mode switch
        {
            Mode.Menu => (_menuPageIdx + 1, (_pages.Count + 9) / 10),
            Mode.Items when ItemsPage != null => (ItemsPage.PageIndex + 1, ItemsPage.PageCount),
            _ => (1, 1),
        };
        var ver = (pages > 1 ? pageNo + "/" + pages + "  " : "") + "v" + Program.ShortVersion;
        g.DrawString(ver, meta, muted, glass.Right - pad - g.MeasureString(ver, meta).Width, glass.Top + 8 * s);

        switch (_mode)
        {
            case Mode.Menu: DrawMenu(g, s); break;
            case Mode.Confirm: _confirmPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.EditNumber: _editPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.TextEdit: _textPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.CaptureKey:
                _keyPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad });
                break;
            case Mode.Preset: _presetPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Help: _helpPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Detail: _detailPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.PickVehicle: _pickPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Snapshots: _snapPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            default: ItemsPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
        }

        using var hint = MfdTheme.MonoAt(14f * s);
        // 页脚提示同理：二级页面自带 HintKey，表单只处理菜单/模块/捕获这三种没有页面的情况。
        var hintKey = _mode switch
        {
            Mode.Menu => "hint.menu",
            Mode.Items => "hint.item",
            Mode.CaptureKey => _keyPage?.HintKey ?? "capture.hint",
            _ => ActivePage?.HintKey ?? "hint.item",
        };
        var hintText = _toast ?? L10n.Get(LangScope.Screen, hintKey);
        g.DrawString(hintText, hint, muted,
            glass.Left + glass.Width / 2 - g.MeasureString(hintText, hint).Width / 2, glass.Bottom - 20 * s);
    }

    private void DrawMenu(System.Drawing.Graphics g, float s)
    {
        var pageLayout = new MfdPageLayout(_layout, s);
        using var name = MfdTheme.CjkAt(24f * s);
        using var hintF = MfdTheme.MonoAt(13f * s);

        // 主菜单也分页：每页 10 个模块（两列五行，正好用满 10 个侧键）。
        // 页号与模块内页号（ItemsPage.PageIndex）互相独立，这样模块再多也不会被静默截断。
        var first = _menuPageIdx * 10;
        for (var cell = 0; cell < 10; cell++)
        {
            var i = first + cell;
            if (i >= _pages.Count) break;

            var row = pageLayout.ListRowRect(cell % 2, cell / 2);
            var x = row.X;
            var y = row.Y;
            var rowH = row.Height;
            var colW = row.Width;
            var selected = i == _menuSel;

            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);
            if (cell % 2 == 0)
            {
                using var mark = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Green);
                g.FillRectangle(mark, x, y + rowH / 2 - 5 * s, 9 * s, 9 * s);
            }
            var page = _pages[i];
            g.DrawString(L10n.Get(LangScope.Screen, page.MenuKey), name, fgBrush, x + 16 * s, y + 5 * s,
                         StringFormat.GenericTypographic);
            var countText = CountItems(page)
                            + (L10n.Screen == Lang.Zh ? " 项" : " ITEMS");
            var hw = g.MeasureString(countText, hintF).Width;
            g.DrawString(countText, hintF, fgBrush, x + colW - hw, y + 12 * s, StringFormat.GenericTypographic);
        }
    }


    /// <summary>
    /// 模块的实际条目数：三级页的子项算进去，入口行本身不算 ——
    /// 否则加了子菜单后「编队 34 项」会变成 35，数字和内容对不上。
    /// </summary>
    private static int CountItems(MfdPage page)
    {
        if (page.LiveCount is { } live) return live();
        var total = 0;
        foreach (var item in page.Items)
            total += item.SubPage is { } build ? CountItems(build()) : 1;
        return total;
    }

    /// <summary>HELP 键：进入帮助页（再按一次退出）。页面的内容/翻页/选中都在 HelpPage 里。</summary>
    private void EnterHelp()
    {
        // HELP 再按一次 = 关闭帮助（避免把 Help 存成“返回目标”导致退不出来）
        if (_mode == Mode.Help) { LeaveHelp(); return; }

        _helpPrevMode = _mode;
        _helpPrevPage = _page;
        _helpPrevItemSel = ItemsPage?.Selected ?? 0;
        _helpPageUI = new Pages.HelpPage(
            // 三级页 HELP 只列出这一页：整棵模块树里没有它，混进去会显示成同级模块。
            pages: () => _subPage != null ? new List<MfdPage> { _subPage } : _pages,
            cfg: () => _cfg,
            onClose: LeaveHelp,
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_helpPageUI);
        _toast = null;
        _mode = Mode.Help;
    }

    private void LeaveHelp()
    {
        _nav.Clear();
        _mode = _helpPrevMode;      // EnterHelp 在已是 Help 时提前返回，所以这里不会是 Help
        _page = _helpPrevPage;
        ItemsPage?.SelectRaw(_helpPrevItemSel);
        _toast = null;
        SyncDiagProbe();
    }



    /// <summary>
    /// 键位捕获页底部的 确认 / 取消 两行（对应左侧第 3、4 个键）。捕获页没有「选中项」，
    /// 第一行恒为主动作，所以这里不再依赖任何遗留选中字段。
    /// </summary>
    // ---------------------------------------------------------------- input

    private ButtonKind HitTest(PointF p, out int index)
    {
        for (var i = 0; i < _layout.SideButtons.Length; i++)
            if (_layout.SideButtons[i].Contains(p)) { index = i; return ButtonKind.Side; }
        for (var i = 0; i < _layout.TopButtons.Length; i++)
            if (_layout.TopButtons[i].Contains(p)) { index = i; return ButtonKind.Top; }
        for (var i = 0; i < _layout.BottomButtons.Length; i++)
            if (_layout.BottomButtons[i].Contains(p)) { index = i; return ButtonKind.Bottom; }
        index = -1;
        return ButtonKind.None;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // 编辑页：命中由页面给出（三个选项行的矩形与它自己的绘制同源）
        if (!_work.Mfd.RealisticKeys && _mode == Mode.EditNumber
            && _editPage is { } editPage
            && editPage.HitOption(new PointF(e.X, e.Y)) is int choiceIdx && choiceIdx >= 0)
        {
            var againC = IsRepeatClick("number", choiceIdx, 500);
            editPage.Select(choiceIdx);
            if (againC || e.Clicks >= 2) Confirm();
            Invalidate();
            return;
        }

        // 选机型页：命中由页面给出（与它自己的绘制同一个 ListRowRect）
        if (!_work.Mfd.RealisticKeys && _mode == Mode.PickVehicle
            && _pickPageUI is { } pickPage
            && pickPage.HitRow(new PointF(e.X, e.Y)) is int pickCell && pickCell >= 0)
        {
            var item = pickPage.PageIndex * 10 + pickCell;
            var again = IsRepeatClick("pick", item, 400);
            pickPage.SelectCell(pickCell);
            if (again || e.Clicks >= 2) pickPage.Handle(MfdCommand.Of("confirm.ok"));
            Invalidate();
            return;
        }

        // 快照列表：单击选中，同一行 400ms 内再点 = 进入恢复确认（与选机型页一致）
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Snapshots
            && _snapPageUI is { } snapPage
            && snapPage.HitRow(new PointF(e.X, e.Y)) is int snapCell && snapCell >= 0)
        {
            var againS = IsRepeatClick("snapshot", snapCell, 400);
            snapPage.SelectCell(snapCell);
            if (againS || e.Clicks >= 2) snapPage.Handle(MfdCommand.Of("confirm.ok"));
            Invalidate();
            return;
        }

        // 二级确认页：单击「确认/取消」行选中，双击（或同一行 400ms 内再点）执行该选项
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Confirm &&
            TryClickConfirmRow(new PointF(e.X, e.Y), e.Clicks))
        {
            Invalidate();
            return;
        }

        // 首页菜单：只有真正点在模块行上才交给 router，其它位置继续走按键逻辑
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Menu)
        {
            EnsureMenuPage();
            var pt = new PointF(e.X, e.Y);

            var hitIndex = -1;
            var pageLayout = new MfdPageLayout(_layout, _layout.Width / 1330f);
            for (var i = 0; i < _menuPage!.Buttons.Count; i++)
            {
                // 只认当前菜单页的格子：不同页的按钮矩形是重叠的（同一批侧键）
                if (_menuPage.Buttons[i].PageIndex != _menuPageIdx) continue;
                if (pageLayout.RectFor(_menuPage.Buttons[i]).Contains(pt))
                {
                    hitIndex = i;
                    break;
                }
            }

            if (hitIndex >= 0)
            {
                if (_menuRouter!.MouseDown(pt, e.Clicks >= 2, _menuPageIdx) is { Id: "menu.open" } cmdM
                    && int.TryParse(cmdM.Arg, out var mIdx))
                {
                    _menuSel = mIdx;
                    EnterPage();
                }
                else
                {
                    _menuSel = hitIndex;      // 单击只选中
                }
                Invalidate();
                return;
            }
        }

        // 帮助页：非拟真模式下可直接点击某一行来选中
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Help &&
            _helpPageUI?.TryClick(new PointF(e.X, e.Y)) == true)
        {
            Invalidate();
            return;
        }

        // 一级模块页：十个格子的命中由页面自己算 —— 和它自己的绘制同一个 ListRowRect，
        // 所以「画在哪里就能点在哪里」对一级页也成立。
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Items &&
            ItemsPage is { } itemsPage && itemsPage.HitCell(new PointF(e.X, e.Y)) is int hitCell && hitCell >= 0)
        {
            // 复刻历史判断：拿「格号」和模块项数比较（不是绝对索引），所以最后一页不满时
            // 点空格仍会高亮一行 "—"、按确认没反应。要改这条得单独提，别混在重构里。
            if (hitCell >= (CurrentPage?.Items.Count ?? 0)) { ClearRepeatClick(); return; }

            var repeatedI = IsRepeatClick("items", hitCell, 400);

            itemsPage.SelectRaw(itemsPage.PageIndex * 10 + hitCell);
            if (repeatedI || e.Clicks >= 2) Confirm();
            Invalidate();
            return;
        }

        // 色板页：命中由页面给出，只认到 Presets.Length，下面的空格不算
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Preset
            && _presetPage is { } presetPage
            && presetPage.HitRow(new PointF(e.X, e.Y)) is int presetCell && presetCell >= 0)
        {
            var againP = IsRepeatClick("preset", presetCell, 400);
            presetPage.Select(presetCell);
            if (againP || e.Clicks >= 2) presetPage.Handle(MfdCommand.Of("confirm.ok"));
            Invalidate();
            return;
        }

        // 主菜单行：命中同样走布局，这里只剩「格号 → 模块索引」这一步
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Menu
            && RowHitTest(new PointF(e.X, e.Y), out var rowCell))
        {
            if (rowCell >= _pages.Count) { ClearRepeatClick(); return; }     // 空单元格：不选也不确认
            var repeated = IsRepeatClick("menu", rowCell, 400);
            _menuSel = rowCell;
            if (repeated || e.Clicks >= 2) EnterPage();
            Invalidate();
            return;
        }

        var kind = HitTest(new PointF(e.X, e.Y), out var index);
        if (kind == ButtonKind.None) return;
        _pressed = (kind, index);
        Invalidate();

        switch (kind)
        {
            case ButtonKind.Side:
                SidePressed(index);
                break;
            case ButtonKind.Top:
                PressTop(index);
                break;
            case ButtonKind.Bottom:
                PressBottom(index);
                break;
        }
        Invalidate();
    }

    /// <summary>主菜单行的命中（一页最多 10 个模块）。矩形由布局统一给出。</summary>
    private bool RowHitTest(PointF p, out int cell) =>
        new MfdPageLayout(_layout, _layout.Width / 1330f).TryHitListCell(p, 10, out cell);

    /// <summary>确认页行的点击处理（OnMouseDown 与预览钩子共用）：单击选中，双击执行。</summary>
    private bool TryClickConfirmRow(PointF point, int clicks)
    {
        if (_confirmPage is not { } page) return false;
        int option = page.HitRow(point);         // 矩形由页面自己算，与 Draw 同源
        if (option < 0) return false;

        var repeated = IsRepeatClick("confirm", option, 400);
        page.Select(option);
        if (repeated || clicks >= 2)
            page.Handle(MfdCommand.Of(option == 0 ? "confirm.ok" : "confirm.cancel"));
        return true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_menuRouter != null) _menuRouter.MouseUp();
        _pressed = (ButtonKind.None, -1);
        Invalidate();
    }

    private void SidePressed(int index)
    {
        var cell = MfdPageLayout.CellFromSideKey(index);

        // 页面（一级模块页 / 二级页）把按钮交给各自的 router：页面在 Build() 里声明
        // 「哪个槽位、哪个侧键、发什么命令」，这里只转发，不再由表单硬编码 index 的含义。
        if (RouteSideKeyToPage(index))
        {
            _toast = null;
            Invalidate();
            return;
        }

        if (_mode == Mode.CaptureKey)
        {
            if (index == 2) Confirm();
            else if (index == 3) CancelSecondLevel();
            return;
        }

        if (_mode == Mode.Menu)
        {
            EnsureMenuPage();
            var cmd = _menuRouter!.PressSlot($"SIDE{cell}", InputSource.RealButton, _menuPageIdx);
            if (cmd is { Id: "menu.open" } && int.TryParse(cmd.Arg, out var idx))
            {
                _menuSel = idx;
                EnterPage();
            }
            return;
        }

        // 其余情况（一级模块页的空格子 / 编辑 / 捕获 / 确认）：侧键不改变任何选中
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (_mode != Mode.TextEdit) return;
        if (char.IsControl(e.KeyChar)) return;
        _textPage?.Append(e.KeyChar);
        e.Handled = true;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // 文本输入（玩家昵称）：ASCII 直接打字，中文/符号用 Ctrl+V
        if (_mode == Mode.TextEdit)
        {
            e.Handled = true;
            if (e.KeyCode == Keys.Escape) { _textPage?.Handle(MfdCommand.Of("nav.back")); Invalidate(); return; }
            if (e.KeyCode == Keys.Enter) { _textPage?.Handle(MfdCommand.Of("tedit.commit")); Invalidate(); return; }
            if (e.KeyCode == Keys.Back) { _textPage?.Backspace(); Invalidate(); return; }
            if (e.Control && e.KeyCode == Keys.V)
            {
                string clip = "";
                try { clip = Clipboard.GetText() ?? ""; } catch { }
                _textPage?.AppendText(clip);
                Invalidate();
            }
            return;
        }
        // 数值手输
        if (_mode == Mode.EditNumber)
        {
            e.Handled = true;
            if (e.KeyCode == Keys.Escape) { _editPage?.Handle(MfdCommand.Of("nav.back")); Invalidate(); return; }
            if (e.KeyCode == Keys.Enter) { Confirm(); Invalidate(); return; }
            if (e.KeyCode == Keys.Back)
            {
                _editPage?.Backspace();
                Invalidate();
                return;
            }
            var ch = KeyToDigit(e.KeyCode);
            if (ch != '\0' && (_editPage?.Buffer.Length ?? 0) < 10)
            {
                _editPage?.AppendChar(ch);
                Invalidate();
            }
            return;
        }

        // 键位捕获：边按边显示（Ctrl 按下即显示 CTRL，再按 Q 变 CTRL+Q，全松开后冻结）
        if (_mode == Mode.CaptureKey)
        {
            e.Handled = true;
            if (e.KeyCode == Keys.Escape) { _mode = Mode.Items; _keyPage?.Reset(); Invalidate(); return; }
            _keyPage?.KeyDown(e.KeyCode);
            Invalidate();
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Up: MoveSelection(-1); break;
            case Keys.Down: MoveSelection(1); break;
            case Keys.Left: Step(-1); break;
            case Keys.Right: Step(1); break;
            case Keys.Enter: Confirm(); break;
            case Keys.Escape: Back(); break;
            case Keys.PageUp: CyclePage(-1); break;
            case Keys.PageDown: CyclePage(1); break;
            case Keys.F12: SaveAndClose(); break;
            default: return;
        }
        e.Handled = true;
        Invalidate();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_mode != Mode.CaptureKey) return;
        _keyPage?.KeyUp(e.KeyCode);
        // 全部松开后保持最后显示的文字（冻结），下次按下重新开始
        e.Handled = true;
        Invalidate();
    }

    private static char KeyToDigit(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
        >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
        Keys.OemPeriod or Keys.Decimal => '.',
        Keys.OemMinus or Keys.Subtract => '-',
        _ => '\0',
    };

    /// <summary>Leaves a second-level page without applying (数值编辑 / 键位捕获 / 预置色板)。</summary>
    private void CancelSecondLevel()
    {
        _nav.Clear();
        _mode = Mode.Items;
        _keyPage?.Reset();
        _toast = null;
    }

    /// <summary>Moves the selection inside the current page (UP/DOWN).</summary>
    private void MoveSelection(int delta)
    {
        if (_mode == Mode.Snapshots)
        {
            _snapPageUI?.Move(delta);
            _toast = null;
            return;
        }

        if (_mode == Mode.PickVehicle)
        {
            _pickPageUI?.Move(delta);       // 由页面维护选中项
            return;
        }

        if (_mode == Mode.Help)
        {
            _helpPageUI?.Move(delta);
            _toast = null;
            return;
        }

        if (_mode == Mode.PickVehicle)
        {
            _pickPageUI?.Move(delta);
            return;
        }
        if (_mode == Mode.Preset)
        {
            _presetPage?.Move(delta);
            _toast = null;
            return;
        }
        if (_mode == Mode.Confirm)
        {
            _confirmPage?.Move(delta);
            return;
        }
        if (_mode == Mode.Menu)
        {
            _menuSel = (_menuSel + delta + _pages.Count) % _pages.Count;
            _menuPageIdx = _menuSel / 10;   // 选中项跨页时，菜单跟着翻过去
            EnsureMenuPage();               // 路由器和菜单页是懒建的，必须先建好再同步
            _menuRouter?.Select(_menuSel);  // 按全局索引同步，避免 Move 在边界处与选中项脱节
        }
        else
        {
            ItemsPage?.Move(delta);
        }
        _toast = null;
    }

    private void Step(int direction)
    {
        if (_mode == Mode.Items) ItemsPage?.Step(direction);
    }

    private void Confirm()
    {
        switch (_mode)
        {
            case Mode.Menu:
                EnsureMenuPage();
                if (_menuRouter!.ActivateSelected() is { Id: "menu.open" } cmd2
                    && int.TryParse(cmd2.Arg, out var sel))
                {
                    _menuSel = sel;
                    EnterPage();
                }
                break;
            case Mode.Confirm:
                // 跟随页面上高亮的选项：UP/DOWN 移到「取消」后按 CONFIRM 应是取消
                _confirmPage?.Handle(MfdCommand.Of(
                    _confirmPage?.Selected == 1 ? "confirm.cancel" : "confirm.ok"));
                break;
            case Mode.EditNumber:
                _editPage?.Handle(MfdCommand.Of(
                    _editPage.Choice == 1 ? "edit.cancel" :
                    _editPage.Choice == 2 ? "edit.pick" : "edit.commit"));
                break;
            case Mode.TextEdit:
                _textPage?.Handle(MfdCommand.Of("tedit.commit"));
                break;
            case Mode.CaptureKey:
                CommitCapture();
                break;
            case Mode.Preset:
                _presetPage?.Handle(MfdCommand.Of("confirm.ok"));   // 页面回调里已返回模块页
                break;
            case Mode.PickVehicle:
                _pickPageUI?.Handle(MfdCommand.Of("confirm.ok"));
                break;
            case Mode.Snapshots:
                _snapPageUI?.Handle(MfdCommand.Of("confirm.ok"));
                break;
            default:
                ItemsPage?.ActivateSelected();
                break;
        }
    }

    /// <summary>解析手输文本并写入当前数值行（NumberEditPage 的 onCommit 回调）。</summary>
    private void CommitNumber(string buffer)
    {
        if (SelectedItem is not { } item || item.Kind != ItemKind.Number)
        {
            _mode = Mode.Items;
            return;
        }

        var text = (buffer ?? "").Replace(",", ".").Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            v = Math.Clamp(v, item.Min, item.Max);
            item.SetNumber?.Invoke(_work, v);
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        }
        _mode = Mode.Items;
        _toast = null;
    }

    /// <summary>Applies the captured key combination to the selected binding row.</summary>
    private void CommitCapture()
    {
        if (SelectedItem is { } item && item.Kind == ItemKind.KeyBinding && _keyPage?.Result is { } captured)
        {
            item.SetText2?.Invoke(_work, captured);
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        }
        _mode = Mode.Items;
        _keyPage = null;
        _toast = null;
    }

    private void Activate(MfdItem item)
    {
        // 联动项不可用：在这里统一拦住所有 Kind（含会开子页的那些）。
        // MfdItemsPage 的按钮 Enabled 只覆盖鼠标/侧键，键盘确认走的是这条路径。
        if (!item.IsEnabled(_work))
        {
            _toast = item.DisabledText?.Invoke(_work) ?? L10n.Get(LangScope.Screen, "hint.unavailable");
            return;
        }

        // 打开击落播报但没填昵称 → 提示先填，避免「开了却没反应」
        if (item.LabelKey == "row.splashEnabled"
            && item.GetBool?.Invoke(_work) == false
            && string.IsNullOrWhiteSpace(_work.PlayerName))
        {
            _toast = L10n.Get(LangScope.Screen, "splash.needName");
        }

        switch (item.Kind)
        {
            case ItemKind.Action:
                // Direct = 动作自带页面/对话框，不需要再弹「确认执行」二次确认
                if (item.Direct)
                {
                    item.Run?.Invoke(_work);
                    var toast = item.ToastAfterRun?.Invoke(_work);
                    _dirty = true;
                    WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
                    _toast = string.IsNullOrEmpty(toast) ? null : toast;
                    Invalidate();
                    return;
                }
                _pendingAction = item;
                EnterConfirm();
                return;
            case ItemKind.Number:
                EnterEditNumber(item);
                return;
            case ItemKind.KeyBinding:
                EnterCaptureKey();
                return;
            case ItemKind.Text:
                EnterTextEdit(item);
                return;
            case ItemKind.Submenu:
                if (item.SubPage is { } build) EnterSubPage(build());
                return;
            case ItemKind.Detail:
                EnterDetail(item);
                return;
            case ItemKind.Preset:
                EnterPreset(item);
                return;
            case ItemKind.ReadOnly:
                _toast = L10n.Get(LangScope.Screen, "hint.readonly");
                return;
        }
        item.Activate(_work);
        _dirty = true;
        WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
    }

    // ---------------------------------------------------------------- page constructors
    // 每个二级页都从 Enter* 进入：直接 `_mode = Mode.X` 会绕过页面初始化（空白页陷阱）。

    /// <summary>文本行（玩家昵称）→ 文本输入页。</summary>
    private void EnterTextEdit(MfdItem target)
    {
        _textPage = new Pages.TextEditPage(
            label: () => L10n.Get(LangScope.Screen, target.LabelKey),
            initial: target.GetText?.Invoke(_work) ?? "",
            onCommit: text => CommitTextValue(target, text),
            onCancel: () => { _nav.Clear(); _mode = Mode.Items; },
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_textPage);
        _mode = Mode.TextEdit;
    }

    private void CommitTextValue(MfdItem target, string text)
    {
        target.SetText2?.Invoke(_work, text);
        _dirty = true;
        WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        _nav.Clear();
        _mode = Mode.Items;
        _toast = null;
    }

    /// <summary>数值行 → 手输编辑页。</summary>
    private void EnterEditNumber(MfdItem target)
    {
        var initial = target.ValueText(_work)
            .TrimEnd('X', 'x', 'M', 's', 'k', 'm', 'h', '/', 'p', '°', ' ');
        _editPage = new Pages.NumberEditPage(
            texts: () => (L10n.Get(LangScope.Screen, target.LabelKey), target.Unit),
            settingKey: target.LabelKey,
            initial: initial,
            onCommit: CommitNumber,
            onCancel: () => { _nav.Clear(); _mode = Mode.Items; },
            onPick: () => EnterPickVehicle(target),
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_editPage);
        _mode = Mode.EditNumber;
    }

    /// <summary>数值编辑页 → 选机型页（空名字 = 全局）。</summary>
    private void EnterPickVehicle(MfdItem target)
    {
        _pickPageUI = new Pages.PickVehiclePage(
            onPicked: name =>
            {
                if (string.IsNullOrEmpty(name)) WtHud.Hud.EditScope.UseGlobal();
                else { WtHud.Hud.AirframeDb.ManualVehicle = name; WtHud.Hud.EditScope.UseVehicle(name); }
                _dirty = true;
                WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
                _nav.Clear();

                // 重建编辑页：缓冲必须按新作用域重新取值，否则仍显示旧层的数据
                EnterEditNumber(target);
                Invalidate();
            },
            onCancel: () => { _nav.Clear(); _mode = Mode.EditNumber; },
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_pickPageUI);
        _mode = Mode.PickVehicle;
    }

    /// <summary>颜色预设行 → 色板页。</summary>
    private void EnterPreset(MfdItem target)
    {
        _presetPage = new Pages.PresetPage(
            currentKey: () => target.GetPreset?.Invoke(_work) ?? "green",
            onApply: key => { _nav.Clear(); CommitPreset(target, key); },
            onCancel: () => { _nav.Clear(); _mode = Mode.Items; },
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_presetPage);
        _mode = Mode.Preset;
    }

    /// <summary>应用色板选择：同步主题与草稿，然后回到模块页。</summary>
    private void CommitPreset(MfdItem target, string key)
    {
        if (target.SetPreset is { } set)
        {
            set(_work, key);
            MfdTheme.ApplyColors(_work.Mfd.InkColor, _work.Mfd.AccentColor);
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        }
        _mode = Mode.Items;
        _toast = null;
    }

    /// <summary>键位行 → 实时按键捕获页。</summary>
    private void EnterCaptureKey()
    {
        _keyPage = new Pages.KeyCapturePage(
            label: () => L10n.Get(LangScope.Screen, SelectedItem?.LabelKey ?? ""),
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _mode = Mode.CaptureKey;
    }

    /// <summary>机型行 → 机型详情页；item = null 表示回到已有详情页（重置确认后返回）。</summary>
    private void EnterDetail(MfdItem? item = null)
    {
        if (item != null || _detailPageUI == null)
        {
            _detailPageUI = new Pages.DetailPage(
                vehicleId: item?.LabelKey ?? "",
                cfg: () => _work,
                onBack: () =>
                {
                    _nav.Clear();
                    _mode = Mode.Items;
                    _toast = null;
                },
                onReset: () =>
                {
                    _resetConfirm = true;
                    _closeConfirm = false;
                    _pendingAction = null;
                    EnterConfirm();
                },
                layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
            _nav.Push(_detailPageUI);
        }
        else if (_nav.Current is not Pages.DetailPage)
        {
            // 从确认页返回：栈顶还压着 ConfirmPage。弹掉它，否则 _nav 的栈顶不再代表
            // 当前页（绘制看 _mode 所以画面是对的，栈本身却说了假话）。
            // 不重建页面，详情页的选中位置因此保留。
            _nav.Pop();
        }
        _mode = Mode.Detail;
    }

    /// <summary>俯仰方向检测：需要现场采样，确认后进入采样页。</summary>
    /// <summary>Builds the confirm sheet as a page instead of a mode-specific switch branch.</summary>
    private void EnterConfirm()
    {
        _confirmPage = new Pages.ConfirmPage(
            texts: () =>
            {
                var label = _resetConfirm
                    ? L10n.Get(LangScope.Screen, "confirm.resetAirframe")
                    : _closeConfirm
                        ? L10n.Get(LangScope.Screen, "confirm.discard")
                        : _pendingAction == null ? "" : L10n.Get(LangScope.Screen, _pendingAction.LabelKey);
                var sub = L10n.Get(LangScope.Screen,
                    _resetConfirm ? "confirm.askReset" : _closeConfirm ? "confirm.askDiscard" : "confirm.ask");
                return (label, sub);
            },
            onOk: RunPending,
            onCancel: CancelPending,
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_confirmPage);
        _mode = Mode.Confirm;
    }

    private void RunPending()
    {
        if (_resetConfirm)
        {
            var id = _detailPageUI?.VehicleId ?? "";
            WtHud.Hud.AirframeDb.ResetOverrides(id);

            // 同时清掉该机型在统一覆盖表里的数据，否则详情页仍显示旧的覆盖值
            if (!string.IsNullOrEmpty(id))
            {
                _work.PerVehicleOverrides.Remove(id);
                _cfg.PerVehicleOverrides.Remove(id);
            }
            _resetConfirm = false;
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
            EnterDetail();               // 回到详情页（复用同一页面，数值已更新）
            _toast = L10n.Get(LangScope.Screen, "toast.resetAirframe");
            return;
        }

        if (_closeConfirm)
        {
            _closeConfirm = false;
            _forceClose = true;
            L10n.LoadFromConfig(_cfg.Language);
            Close();
            return;
        }
        _pendingAction?.Run?.Invoke(_work);
        _dirty = true;
        WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        _pendingAction = null;
        _toast = null;
        _mode = Mode.Items;
    }

    private void CancelPending()
    {
        if (_resetConfirm)
        {
            _resetConfirm = false;
            EnterDetail();               // 取消重置：回到详情页
            _toast = null;
            return;
        }

        // 放弃修改的确认被取消：回到当前模块页（_page 始终有效，CurrentPage 不会为 null）
        _closeConfirm = false;
        _pendingAction = null;
        _mode = Mode.Items;
        _toast = null;
    }

    private void SyncDiagProbe()
    {
        var onDiag = _mode == Mode.Items && CurrentPage?.TitleKey == "page.diag";
        if (onDiag) DiagProbe.Start(); else DiagProbe.Stop();
    }


    /// <summary>Builds the menu page as data: one VirtualButton per module.</summary>
    private void EnsureMenuPage()
    {
        if (_menuPage != null && _menuRouter != null) return;

        _realButtons ??= MfdControlFactory.CreateBezelButtons();
        _menuRouter = new MfdInputRouter(new MfdPageLayout(_layout, _layout.Width / 1330f), _realButtons);
        _menuPage = new MfdScreenPage();

        for (var i = 0; i < _pages.Count; i++)
        {
            var index = i;
            var cell = i % 10;                        // 本页内的格子号（0-9）
            // 与 DrawMenu 的排布一致：偶数列在左、奇数列在右，行号 = cell/2。
            // 之前这里写死 Slot/Column = 0，所有模块的矩形都落在第一行，
            // 鼠标点任何位置都只能选中第 0 个；侧键则是 SIDE{全局索引}，也和画面错位。
            var column = cell % 2;
            var row = cell / 2;
            // ★ 侧键名用**格子号**，不是物理键号：SidePressed 收到的是物理键 0-9，
            //   先 CellFromSideKey 换算成格子再发 "SIDE{格子}"，所以注册也必须按格子来。
            //   原来写成 column*5+row（物理键名），结果按左列第二个键会打开左列第三个模块 ——
            //   两套编号互逆，除了 0 号键每一个都错位，点下去直接进错页面。
            var slotName = MenuSideSlotName(cell);
            _menuPage.Buttons.Add(new VirtualButton($"menu{i}", VirtualButtonKind.Action)
            {
                ParentId = "menu",
                Text = () => L10n.Get(LangScope.Screen, _pages[index].MenuKey),
                Slot = row,
                Column = column,
                PageIndex = i / 10,
                Target = MfdCommand.Of("menu.open", index.ToString(),
                                       InputSource.Keyboard),
                ActivateBy = new[] { slotName },
            });
        }
        _menuRouter.SetButtons(_menuPage.Buttons);
    }

    /// <summary>
    /// 主菜单某个格子注册的侧键名。**按格子号**，不是物理键号 ——
    /// 侧键按下时先 <see cref="MfdPageLayout.CellFromSideKey"/> 换成格子再发这个名字。
    /// <para>★ 抽出来是为了能断言：这两套编号互逆，写错时除了 0 号键全部错位，
    /// 而且不报错、只是进错模块 —— 一度就是这样。</para>
    /// </summary>
    internal static string MenuSideSlotName(int cell) => "SIDE" + cell;

    private void EnterPage()
    {
        if (_menuSel < 0 || _menuSel >= _pages.Count) return;
        _page = _menuSel;
        _menuPageIdx = _menuSel / 10;      // 记住是从哪一页的模块进来的，返回时还回那一页
        ItemsPage?.Reset();                // 进模块从头开始（页号归零、选中第一行）
        _toast = null;

        // 快照模块没有行列表：直接进入快照页（页面自己负责列表与选中）
        if (CurrentPage?.TitleKey == "page.snapshots")
        {
            EnterSnapshots();
            return;
        }

        _mode = Mode.Items;
        SyncDiagProbe();
    }

    /// <summary>
    /// 首页「回归快照」模块：进入可恢复快照列表。所有 `_mode = Mode.Snapshots` 都在 Enter* 方法里，
    /// 保证页面一定经过构造（直接赋 mode 会得到空白页）。
    /// </summary>
    private void EnterSnapshots()
    {
        _snapPageUI = new Pages.SnapshotsPage(
            entries: WtHud.Hud.ConfigSnapshots.ListRestorable,
            onPicked: EnterSnapshotConfirm,
            onCancel: () =>
            {
                _nav.Clear();
                _menuSel = _page;
                _mode = Mode.Menu;
                _toast = null;
                SyncDiagProbe();
            },
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_snapPageUI);
        _mode = Mode.Snapshots;
    }

    /// <summary>选中最新的快照后的二次确认；确认前不碰运行配置。</summary>
    private void EnterSnapshotConfirm(WtHud.Hud.ConfigSnapshots.SnapshotEntry entry)
    {
        _confirmPage = new Pages.ConfirmPage(
            texts: () => (
                string.Format(L10n.Get(LangScope.Screen, "snap.confirmAsk"), entry.TimeText),
                L10n.Get(LangScope.Screen, "snap.confirmSub")),
            onOk: () => RestoreSnapshot(entry),
            onCancel: () =>
            {
                _nav.Clear();
                _toast = null;
                _mode = Mode.Snapshots;
            },
            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
        _nav.Push(_confirmPage);
        _mode = Mode.Confirm;
    }

    /// <summary>
    /// 恢复动作（规格 docs/snapshot-restore-spec.md）：反射回填 _cfg + 同步 _work + 清 session 快照
    /// + 写审计文件；不自动 SAVE，返回首页让用户先核对数值。
    /// </summary>
    private void RestoreSnapshot(WtHud.Hud.ConfigSnapshots.SnapshotEntry entry)
    {
        if (!WtHud.Hud.ConfigSnapshots.RestoreFrom(entry.Path, _cfg))
        {
            _toast = L10n.Get(LangScope.Screen, "snap.toast.failed");
            _nav.Clear();
            EnterSnapshots();
            return;
        }

        // 快照是“全局整体替换”：perVehicleOverrides 也一起回退，草稿必须同步到同一份数据
        _work.CopyFrom(_cfg);
        L10n.LoadFromConfig(_cfg.Language);
        MfdTheme.ApplyColors(_work.Mfd.InkColor, _work.Mfd.AccentColor);
        _dirty = true;
        _nav.Clear();
        _menuSel = _page;
        _mode = Mode.Menu;
        EnsureMenuPage();
        _toast = string.Format(L10n.Get(LangScope.Screen, "snap.toast.restored"), entry.TimeText);
    }

    private void Back()
    {
        switch (_mode)
        {
            case Mode.Confirm:
                _confirmPage?.Handle(MfdCommand.Of("nav.back"));
                break;
            case Mode.Items when _subPage != null:
                LeaveSubPage();          // 三级页：退一层，仍在 Items
                break;
            case Mode.Items:
                _menuSel = _page;
                _menuPageIdx = _menuSel / 10;
                _mode = Mode.Menu;
                SyncDiagProbe();
                _toast = null;
                break;
            case Mode.Preset:
                _presetPage?.Handle(MfdCommand.Of("nav.back"));
                break;
            case Mode.Help:
                _helpPageUI?.Handle(MfdCommand.Of("nav.back"));   // onClose -> LeaveHelp
                break;
            case Mode.Detail:
                _detailPageUI?.Handle(MfdCommand.Of("nav.back")); // onBack -> 模块页
                break;
            case Mode.PickVehicle:
                _pickPageUI?.Handle(MfdCommand.Of("nav.back"));
                _toast = null;
                break;
            case Mode.Snapshots:
                _snapPageUI?.Handle(MfdCommand.Of("nav.back"));
                _toast = null;
                break;
            case Mode.EditNumber:
            case Mode.CaptureKey:
            case Mode.TextEdit:
                CancelSecondLevel();
                break;
            default:
                // 主菜单是根层级：BACK 不关闭面板，只提示用 CANCEL 退出
                _toast = L10n.Get(LangScope.Screen, "hint.root");
                break;
        }
    }

    /// <summary>PAGE 键：只在当前列表内翻页；没有下一页时保持不动。</summary>
    private void CyclePage(int delta)
    {
        if (_mode == Mode.Help)
        {
            _helpPageUI?.CyclePage(delta);
            _toast = null;
            return;
        }

        if (_mode == Mode.PickVehicle)
        {
            _pickPageUI?.CyclePage(delta);
            return;
        }

        if (_mode == Mode.Snapshots)
        {
            _snapPageUI?.CyclePage(delta);
            _toast = null;
            return;
        }

        if (_mode != Mode.Menu && _mode != Mode.Items) return;

        if (_mode == Mode.Menu)
        {
            var menuPages = (_pages.Count + 9) / 10;
            if (menuPages <= 1) { _toast = L10n.Get(LangScope.Screen, "page.single"); return; }

            var nextMenu = Math.Clamp(_menuPageIdx + delta, 0, menuPages - 1);
            if (nextMenu == _menuPageIdx) { _toast = L10n.Get(LangScope.Screen, "page.edge"); return; }

            _menuPageIdx = nextMenu;
            _menuSel = nextMenu * 10;       // 翻页后选中该页第一个模块
            EnsureMenuPage();
            _menuRouter?.Select(_menuSel);
            _toast = null;
            return;
        }

        var items = ItemsPage;
        if (items == null) return;
        if (items.PageCount <= 1) { _toast = L10n.Get(LangScope.Screen, "page.single"); return; }
        if (!items.CyclePage(delta)) { _toast = L10n.Get(LangScope.Screen, "page.edge"); return; }

        _toast = null;
    }

    private void PressTop(int index)
    {
        switch (index)
        {
            case 0: Confirm(); break;
            case 1: Back(); break;
            case 2: MoveSelection(-1); break;
            case 3: MoveSelection(1); break;
            case 4:
                EnterHelp();
                break;
        }
        Invalidate();
    }

    private void PressBottom(int index)
    {
        switch (index)
        {
            case 0:
                if (_mode == Mode.Detail) _detailPageUI?.Handle(MfdCommand.Of("reset"));
                else ResetPage();
                break;
            case 1: CyclePage(-1); break;
            case 2: CyclePage(1); break;
            case 3: CancelAndClose(); break;
            case 4: SaveAndClose(); break;
        }
        Invalidate();
    }

    private void ResetPage()
    {
        if (_mode == Mode.Confirm) { _confirmPage?.Handle(MfdCommand.Of("confirm.cancel")); return; }
        if (_mode == Mode.Menu) return;
        if (_mode == Mode.Snapshots)                 // 快照列表是只读的，RESET 不碰配置
        {
            _toast = L10n.Get(LangScope.Screen, "hint.readonly");
            return;
        }

        // 机型作用域：RESET 只清空该机型的覆盖，作用域保持不动（编辑页继续停在该机型）
        if (!WtHud.Hud.EditScope.IsGlobal)
        {
            WtHud.Hud.SettingsRouter.ClearVehicleOverrides(_work);
            WtHud.Hud.SettingsRouter.ClearVehicleOverrides(_cfg);
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
            _toast = L10n.Get(LangScope.Screen, "scope.resetVehicle");
            _mode = Mode.Items;
            SyncDiagProbe();
            return;
        }

        if (CurrentPage?.TitleKey == "page.language")
        {
            L10n.Panel = Lang.En;
            L10n.Screen = Lang.Zh;
            L10n.Alerts = Lang.En;
            L10n.FollowSystem = false;
            return;
        }

        var fresh = new HudConfig();
        _work.VerticalFovDeg = fresh.VerticalFovDeg;
        _work.LayoutOffsetYDeg = fresh.LayoutOffsetYDeg;
        _work.PitchZeroDeg = fresh.PitchZeroDeg;
        _work.CursorHoleHalfPx = fresh.CursorHoleHalfPx;
        _work.HideInCockpit = fresh.HideInCockpit;
        _work.HideInNose = fresh.HideInNose;
        _work.HideOnGround = fresh.HideOnGround;
        _work.ShowPitchLadder = fresh.ShowPitchLadder;
        _work.ShowFpm = fresh.ShowFpm;
        _work.Extrapolate = fresh.Extrapolate;
        _work.RollCoupling = fresh.RollCoupling;
        _work.BankSign = fresh.BankSign;
        _work.PitchSign = fresh.PitchSign;
        _work.StallAoaDeg = fresh.StallAoaDeg;
        _work.AoaWarnDeg = fresh.AoaWarnDeg;
        _work.OverspeedMach = fresh.OverspeedMach;
        _work.OverspeedKmh = fresh.OverspeedKmh;
        _work.BingoFuelSeconds = fresh.BingoFuelSeconds;
        _work.EnemyAirfieldAlertKm = fresh.EnemyAirfieldAlertKm;
    }

    // ---------------------------------------------------------------- headless preview hooks

    /// <summary>Preview helper: jumps straight to a module page.</summary>
    public void PreviewOpen(int pageIndex, int itemIndex = 0)
    {
        _page = Math.Clamp(pageIndex, 0, _pages.Count - 1);
        _mode = Mode.Items;
        // 只把索引钳到 0..9，不按本模块的项数再钳一次：越界索引会高亮一行空 "—"，
        // 这是既有行为（Move/鼠标都能落到空格上），预览必须能复现它。
        if (ItemsPage is { } items)
        {
            items.GoTo(0, 0);
            items.SelectRaw(Math.Clamp(itemIndex, 0, 9));
        }
        _menuSel = _page;
    }

    /// <summary>预览用：按模块内的绝对序号打开某一行（跨页，PreviewOpen 只认单页 0-9）。</summary>
    public void PreviewOpenItem(int pageIndex, int absoluteItem)
    {
        PreviewOpen(pageIndex, 0);
        if (ItemsPage is { } items)
        {
            items.GoTo(absoluteItem / 10, 0);
            items.SelectRaw(Math.Max(0, absoluteItem));
        }
    }

    /// <summary>Preview helper: 选中某行并进入它的下一级页面（出图用）。</summary>
    public void PreviewSub(int itemIndex)
    {
        if (CurrentPage?.Items.ElementAtOrDefault(itemIndex)?.SubPage is { } build)
            EnterSubPage(build());
        Invalidate();
    }

    /// <summary>Preview helper: switch the module's item page (0-based, 10 cells per page).</summary>
    public void PreviewItemPage(int page)
    {
        if (ItemsPage is { } items) items.GoTo(page, items.Selected);
        Invalidate();
    }

    /// <summary>预览主菜单的第 N 页（用于出图核对分页排版）。</summary>
    public void PreviewMenuPage(int page)
    {
        var pages = (_pages.Count + 9) / 10;
        _menuPageIdx = Math.Clamp(page, 0, Math.Max(0, pages - 1));
        _menuSel = _menuPageIdx * 10;
        EnsureMenuPage();
        _menuRouter?.Select(_menuSel);
        Invalidate();
    }

    /// <summary>
    /// 预览/自检用：当前二级页面选中的序号（无页面时 -1）。
    /// 用来验证「侧键 → router → 页面命令」这条链路是否把选中落到了正确的项上。
    /// </summary>
    public int PreviewActiveSelection => ActivePage switch
    {
        Pages.MfdItemsPage p => p.Selected,
        Pages.ConfirmPage p => p.Selected,
        Pages.NumberEditPage p => p.Choice,
        Pages.PresetPage p => p.Selected,
        Pages.PickVehiclePage p => p.Selected,
        Pages.SnapshotsPage p => p.Selected,
        _ => -1,
    };

    /// <summary>预览用：当前一级模块页的页号（没有模块页时 -1）。</summary>
    public int PreviewActivePageIndex => ItemsPage?.PageIndex ?? -1;

    /// <summary>
    /// 预览时模拟按底部的「上页/下页」（index 1 = 上页，2 = 下页）。
    /// 走的是真实按钮路径，包括翻页、选中项重置和边界提示。
    /// </summary>
    public void PreviewBottomKey(int index) => PressBottom(index);

    /// <summary>Preview helper: opens the help structure page.</summary>
    public void PreviewHelp(int page)
    {
        EnterHelp();
        _helpPageUI?.ShowPage(Math.Max(0, page));
    }

    /// <summary>Preview helper: 帮助页翻到最后一页（验证分页与选中跟随）。</summary>
    public void PreviewHelpLast()
    {
        PreviewHelp(0);
        for (var i = 0; i < 8; i++) _helpPageUI?.CyclePage(1);
    }

    /// <summary>Preview helper: 帮助页再按一次 HELP（应关闭帮助并回到原页面）。</summary>
    public void PreviewHelpToggle()
    {
        PreviewHelp(0);
        EnterHelp();
    }

    /// <summary>Preview helper: 详情页按返回（应回到机型列表）。</summary>
    public void PreviewDetailBack()
    {
        PreviewDetail();
        _detailPageUI?.Handle(MfdCommand.Of("nav.back"));
    }

    /// <summary>Preview helper: opens the color preset picker.</summary>
    public void PreviewPreset()
{
        if (SelectedItem is { } item) Activate(item);
}

    /// <summary>Preview helper: opens the numeric edit state for a row.</summary>
    public void PreviewEdit(int pageIndex, int itemIndex)
    {
        PreviewOpen(pageIndex, itemIndex);
        if (SelectedItem is { } item) Activate(item);
    }

    /// <summary>Preview helper: opens the key-capture state for a row.</summary>
    public void PreviewCapture(int pageIndex, int itemIndex)
    {
        PreviewOpen(pageIndex, itemIndex);
        if (SelectedItem is { } item) Activate(item);
        _keyPage?.SetPreview("CTRL+N");
    }

    /// <summary>Preview helper: simulates "CANCEL with unsaved changes".</summary>
    public void PreviewDiscard()
    {
        _dirty = true;
        WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        CancelAndClose();
    }

    /// <summary>Preview helper: opens the confirmation sheet for an action row.</summary>
    public void PreviewConfirm(int pageIndex, int itemIndex)
    {
        PreviewOpen(pageIndex, itemIndex);
        if (SelectedItem is { } item) Activate(item);
    }

    /// <summary>Preview helper: opens the snapshot list page (regression-restore entry).</summary>
    public void PreviewSnapshots()
    {
        var idx = _pages.FindIndex(p => p.TitleKey == "page.snapshots");
        if (idx < 0) return;
        _page = idx;
        _menuSel = idx;
        EnterSnapshots();
    }

    /// <summary>Preview helper: opens the restore confirmation for the newest snapshot.</summary>
    public void PreviewSnapshotConfirm()
    {
        PreviewSnapshots();
        _snapPageUI?.Handle(MfdCommand.Of("confirm.ok"));
    }

    /// <summary>Preview helper: runs the real restore path on the newest snapshot (writes audit, clears session).</summary>
    public void PreviewRestoreNewest()
    {
        PreviewSnapshotConfirm();
        _confirmPage?.Handle(MfdCommand.Of("confirm.ok"));
    }

    /// <summary>Preview helper: opens an airframe detail page (first row of the 机型 module).</summary>
    public void PreviewDetail()
    {
        var idx = _pages.FindIndex(p => p.TitleKey == "page.airframe");
        if (idx < 0) return;
        _page = idx;
        _menuSel = idx;
        _mode = Mode.Items;
        ItemsPage?.GoTo(0, 0);
        if (SelectedItem is { } item) Activate(item);
    }

    /// <summary>Preview helper: opens the detail page of one specific airframe (by id).</summary>
    public void PreviewDetailVehicle(string vehicleId)
    {
        var idx = _pages.FindIndex(p => p.TitleKey == "page.airframe");
        if (idx < 0) return;
        _page = idx;
        _menuSel = idx;
        _mode = Mode.Items;
        ItemsPage?.GoTo(0, 0);
        EnterDetail(new MfdItem { LabelKey = vehicleId, Kind = ItemKind.Detail });
    }

    /// <summary>Preview helper: opens the airframe picker through 数值编辑页 → 手动选择机型.</summary>
    public void PreviewPickVehicle(int pageIndex = 0, int itemIndex = 7)
    {
        PreviewEdit(pageIndex, itemIndex);
        _editPage?.Handle(MfdCommand.Of("edit.pick"));
    }

    /// <summary>Preview helper: 输入 50 后提交 FOV 行（验证 onCommit → CommitNumber 链路）。</summary>
    public void PreviewEditCommit()
    {
        PreviewEdit(0, 7);
        _editPage?.Backspace();
        _editPage?.Backspace();
        _editPage?.AppendChar('5');
        _editPage?.AppendChar('0');
        _editPage?.Handle(MfdCommand.Of("edit.commit"));
    }

    /// <summary>Preview helper: 在选机型页选中第一架飞机并确认（验证 onPicked → 重建编辑页）。</summary>
    public void PreviewPickPicked()
    {
        PreviewPickVehicle();
        _pickPageUI?.SelectCell(1);
        _pickPageUI?.Handle(MfdCommand.Of("confirm.ok"));
    }

    /// <summary>Preview helper: 选机型页按返回（验证 onCancel → 回编辑页）。</summary>
    public void PreviewPickBack()
    {
        PreviewPickVehicle();
        _pickPageUI?.Handle(MfdCommand.Of("nav.back"));
    }

    /// <summary>Preview helper: 在色板页选中「红色」并应用（验证 onApply → CommitPreset）。</summary>
    public void PreviewPresetApply()
    {
        PreviewOpen(4, 0);            // 面板页第 0 行 = 文本颜色（预设）
        PreviewPreset();
        _presetPage?.Select(MfdTheme.Presets.Length - 1);
        _presetPage?.Handle(MfdCommand.Of("confirm.ok"));
    }

    /// <summary>Preview helper: 机型详情页按 RESET（验证 _resetConfirm → ConfirmPage 文案）。</summary>
    public void PreviewDetailReset()
    {
        PreviewDetail();
        PressBottom(0);
    }

    /// <summary>Preview helper: presses a physical side key by index (0-9), exactly like clicking it.</summary>
    public void PreviewSideKey(int index) => SidePressed(Math.Clamp(index, 0, 9));

    /// <summary>预览用：往文本输入页灌入内容（核对长昵称排版）。</summary>
    public void PreviewTextBuffer(string text)
    {
        _textPage?.AppendText(text);
        Invalidate();
    }

    /// <summary>Preview helper: clicks a row of the current confirm sheet (0 = 确认, 1 = 取消).</summary>
    public void PreviewConfirmRowClick(int option, int clicks = 1)
    {
        if (_mode != Mode.Confirm) return;
        var pageLayout = new MfdPageLayout(_layout, _layout.Width / 1330f);
        var row = pageLayout.ListRowRect(0, 2 + Math.Clamp(option, 0, 1));
        TryClickConfirmRow(new PointF(row.X + row.Width / 2, row.Y + row.Height / 2), clicks);
    }

    private void CancelAndClose()
    {
        // 丢弃本次会话的全部修改：回到打开 F1 时的快照
        WtHud.Hud.ConfigSnapshots.EndSession();   // 取消后丢弃本次会话快照

        if (WtHud.Hud.ConfigSnapshots.RestoreCancelPoint(_cfg))
        {
            var fresh = _cfg.Clone();
            foreach (var prop in typeof(HudConfig).GetProperties().Where(pr => pr.CanRead && pr.CanWrite))
                prop.SetValue(_work, prop.GetValue(fresh));
        }

        if (!_dirty || _forceClose)
        {
            L10n.LoadFromConfig(_cfg.Language);
            Close();
            return;
        }
        _pendingAction = null;
        _closeConfirm = true;
        EnterConfirm();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && !_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _pendingAction = null;
            _closeConfirm = true;
            EnterConfirm();          // 必须走构造入口，直接赋 mode 会画到未创建的确认页
            Invalidate();
            return;
        }
        base.OnFormClosing(e);
    }

    private void SaveAndClose()
    {
        if (_mode == Mode.Confirm) _confirmPage?.Handle(MfdCommand.Of("confirm.cancel"));
        L10n.StoreToConfig(_work.Language);
        _cfg.CopyFrom(_work);
        if (_savePath != null) _cfg.SaveTo(_savePath); else _cfg.Save();

        // 以前这里会把「全局告警阈值」写进当前机型档案（StoreAlertOverrides），形成第二套
        // per-aircraft 数据源，和 perVehicleOverrides 打架。现在按机型的值只走覆盖表，
        // 全局值就是全局，不再偷偷改机型档案。

        WtHud.Hud.ConfigSnapshots.SaveHistory(_work, "save");
        WtHud.Hud.ConfigSnapshots.EndSession();   // SAVE 成功后清掉本次会话快照
        Saved = true;
        _dirty = false;
        _forceClose = true;   // 已保存：关闭时不要再弹放弃确认
        Close();
    }

    /// <summary>F1 反复开关时不让贝塞尔底图堆在 GC 队列里。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _bezel.Dispose();
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    /// <summary>外部（F1 再按一次）请求关闭：走 CANCEL 流程。</summary>
    public void RequestClosePanel()
    {
        if (InvokeRequired) { BeginInvoke(new Action(RequestClosePanel)); return; }
        CancelAndClose();
    }

    /// <summary>
    /// Builds the window region from the bezel texture itself: every row keeps only the pixels
    /// that are not black, so the texture's black background never shows over the game.
    /// </summary>
    private void ApplySilhouetteRegion(int width, int height)
    {
        try
        {
            var rects = new List<RECT>();
            var bmp = new Bitmap(_bezel, width, height);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var stride = data.Stride;
            var buffer = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            bmp.UnlockBits(data);

            for (var y = 0; y < height; y++)
            {
                var rowStart = y * stride;
                var x0 = -1;
                var x1 = -1;
                for (var x = 0; x < width; x++)
                {
                    var i = rowStart + x * 4;
                    var lum = buffer[i] + buffer[i + 1] + buffer[i + 2];
                    if (lum <= 36) continue;      // 黑底阈值
                    if (x0 < 0) x0 = x;
                    x1 = x;
                }
                if (x0 < 0) continue;
                rects.Add(new RECT { Left = x0, Top = y, Right = x1 + 1, Bottom = y + 1 });
            }

            if (rects.Count > 0)
            {
                var headerSize = 32;
                var size = headerSize + rects.Count * 16;
                var handle = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try
                {
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 0, 1);            // rdh.dwSize
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 4, 1);            // iType = RDH_RECTANGLES
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 8, rects.Count);  // nCount
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 12, 0);           // nRgnSize
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 16, 0);           // rcBound
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 20, 0);
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 24, width);
                    System.Runtime.InteropServices.Marshal.WriteInt32(handle, 28, height);
                    for (var i = 0; i < rects.Count; i++)
                    {
                        var p = handle + headerSize + i * 16;
                        System.Runtime.InteropServices.Marshal.WriteInt32(p, rects[i].Left);
                        System.Runtime.InteropServices.Marshal.WriteInt32(p + 4, rects[i].Top);
                        System.Runtime.InteropServices.Marshal.WriteInt32(p + 8, rects[i].Right);
                        System.Runtime.InteropServices.Marshal.WriteInt32(p + 12, rects[i].Bottom);
                    }
                    var rgn = ExtCreateRegion(IntPtr.Zero, size, handle);
                    if (rgn != IntPtr.Zero) SetWindowRgn(Handle, rgn, true);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(handle);
                }
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr ExtCreateRegion(IntPtr lpXform, int nCount, IntPtr lpRgnData);
}

























