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
    private enum Mode { Menu, Items, Confirm, EditNumber, CaptureKey, TextEdit, Preset, Help, Detail, PickVehicle, PitchDetect, Snapshots }

    private readonly MfdGeometry _geo;
    private readonly MfdLayout _layout;
    private readonly Bitmap _bezel;
    private readonly HudConfig _cfg;
    private readonly HudConfig _work;
    private readonly List<MfdPage> _pages;

    private Mode _mode = Mode.Menu;
    private readonly Pages.MfdNavigator _nav = new();
    private Pages.PitchDetectPage? _pitchPage;
    private Pages.ConfirmPage? _confirmPage;
    private Pages.NumberEditPage? _editPage;
    private Pages.TextEditPage? _textPage;
    private Pages.PresetPage? _presetPage;
    private Pages.PickVehiclePage? _pickPageUI;
    private Pages.SnapshotsPage? _snapPageUI;
    private int _page;
    private int _menuSel;
    private int _itemSel;
    private int _menuPageIdx;              // 主菜单页号（每页 10 个模块，与模块内页号独立）
    private MfdItem? _pendingAction;
    private bool _dirty;
    private bool _closeConfirm;
    private bool _forceClose;
    private int _itemPage;                 // 当前模块内的页号（每页 10 格）
    private (ButtonKind Kind, int Index) _pressed = (ButtonKind.None, -1);
    private string? _toast;
    private string _captureText = "";      // 键位捕获结果
    private readonly HashSet<Keys> _heldKeys = new();   // 捕获时当前按住的键
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
    private int _lastHitCell = -1;
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

    private MfdPage? CurrentPage => _page >= 0 && _page < _pages.Count ? _pages[_page] : null;

    /// <summary>当前占着面板的二级页面（没有就是 null）。</summary>
    private Pages.MfdPageBase? ActivePage => _mode switch
    {
        Mode.Confirm => _confirmPage,
        Mode.EditNumber => _editPage,
        Mode.TextEdit => _textPage,
        Mode.Preset => _presetPage,
        Mode.Help => _helpPageUI,
        Mode.Detail => _detailPageUI,
        Mode.PickVehicle => _pickPageUI,
        Mode.PitchDetect => _pitchPage,
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

    private MfdItem? SelectedItem
    {
        get
        {
            var page = CurrentPage;
            if (page == null || _itemSel < 0 || _itemSel >= page.Items.Count) return null;
            return page.Items[_itemSel];
        }
    }

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
        var titleKey = _mode switch
        {
            Mode.Menu => "menu",
            Mode.Confirm => "confirm.title",
            Mode.EditNumber => "edit.title",
            Mode.TextEdit => "tedit.title",
            Mode.CaptureKey => "capture.title",
            Mode.PitchDetect => "row.pitchDetect",
            _ => CurrentPage?.TitleKey ?? "menu",
        };
        var title = L10n.Get(LangScope.Screen, titleKey);
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
            Mode.Items when CurrentPage != null => (_itemPage + 1, (CurrentPage.Items.Count + 9) / 10),
            _ => (1, 1),
        };
        var ver = (pages > 1 ? pageNo + "/" + pages + "  " : "") + "v" + Application.ProductVersion;
        g.DrawString(ver, meta, muted, glass.Right - pad - g.MeasureString(ver, meta).Width, glass.Top + 8 * s);

        switch (_mode)
        {
            case Mode.Menu: DrawMenu(g, s, glass, left, pad); break;
            case Mode.Confirm: _confirmPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.EditNumber: _editPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.TextEdit: _textPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.CaptureKey: DrawCapturePage(g, s, glass, pad); break;
            case Mode.Preset: _presetPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Help: _helpPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Detail: _detailPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.PickVehicle: _pickPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.PitchDetect: _nav.Current?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Snapshots: _snapPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            default: DrawItems(g, s, glass, left, pad); break;
        }

        using var hint = MfdTheme.MonoAt(14f * s);
        var hintKey = _mode switch
        {
            Mode.Menu => "hint.menu",
            Mode.Confirm => "hint.confirm",
            Mode.Preset => "selectPreset",
            Mode.Help => "help.pagerHint",
            Mode.PickVehicle => "pick.hint",
            Mode.PitchDetect => "pitchdetect.hint",
            Mode.Snapshots => "snap.hint",
            Mode.EditNumber => "edit.prompt",
            Mode.TextEdit => "tedit.hint",
            Mode.CaptureKey => "capture.hint",
            _ => "hint.item",
        };
        var hintText = _toast ?? L10n.Get(LangScope.Screen, hintKey);
        g.DrawString(hintText, hint, muted,
            glass.Left + glass.Width / 2 - g.MeasureString(hintText, hint).Width / 2, glass.Bottom - 20 * s);
    }

    private void DrawMenu(System.Drawing.Graphics g, float s, RectangleF glass, float left, float pad)
    {
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        var rowH = 40 * s;
        using var name = MfdTheme.CjkAt(24f * s);
        using var hintF = MfdTheme.MonoAt(13f * s);

        // 主菜单也分页：每页 10 个模块（两列五行，正好用满 10 个侧键）。
        // 页号与模块内页号（_itemPage）互相独立，这样模块再多也不会被静默截断。
        var first = _menuPageIdx * 10;
        for (var cell = 0; cell < 10; cell++)
        {
            var i = first + cell;
            if (i >= _pages.Count) break;

            var btn = _layout.SideButtons[(cell % 2) * 5 + cell / 2];
            var cy = btn.Y + btn.Height / 2;
            var x = cell % 2 == 0 ? left : glass.Left + glass.Width / 2 + 18 * s;
            var y = cy - rowH / 2;
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
            var countText = (page.LiveCount?.Invoke() ?? page.Items.Count)
                            + (L10n.Screen == Lang.Zh ? " 项" : " ITEMS");
            var hw = g.MeasureString(countText, hintF).Width;
            g.DrawString(countText, hintF, fgBrush, x + colW - hw, y + 12 * s, StringFormat.GenericTypographic);
        }
    }

    private void DrawItems(System.Drawing.Graphics g, float s, RectangleF glass, float left, float pad)
    {
        var page = CurrentPage!;
        var offset = _itemPage * 10;
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        var rowH = 40 * s;
        using var label = MfdTheme.CjkAt(24f * s);
        using var value = MfdTheme.MonoBoldAt(22f * s);

        for (var i = 0; i < 10; i++)
        {
            var btn = _layout.SideButtons[(i % 2) * 5 + i / 2];
            var cy = btn.Y + btn.Height / 2;
            var x = i % 2 == 0 ? left : glass.Left + glass.Width / 2 + 18 * s;
            var y = cy - rowH / 2;
            var selected = (offset + i) == _itemSel;

            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);

            var itemIndex = offset + i;
            if (itemIndex < page.Items.Count)
            {
                var item = page.Items[itemIndex];
                if (i % 2 == 0)
                {
                    using var mark = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Green);
                    g.FillRectangle(mark, x, y + rowH / 2 - 5 * s, 9 * s, 9 * s);
                }
                g.DrawString(L10n.Get(LangScope.Screen, item.LabelKey), label, fgBrush,
                             x + 16 * s, y + 4 * s, StringFormat.GenericTypographic);
                var val = item.ValueText(_work);
                var vw = g.MeasureString(val, value).Width;
                g.DrawString(val, value, fgBrush, x + colW - vw, y + 6 * s, StringFormat.GenericTypographic);
            }
            else
            {
                using var dim = new SolidBrush(Color.FromArgb(60, MfdTheme.Muted));
                g.DrawString("—", label, dim, x + 16 * s, y + 4 * s, StringFormat.GenericTypographic);
            }
        }
    }



    /// <summary>Second-level page: live key capture.</summary>
    private void DrawCapturePage(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        using var title = MfdTheme.CjkAt(26f * s);
        using var mono = MfdTheme.MonoBoldAt(44f * s);
        using var hint = MfdTheme.CjkAt(18f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var name = L10n.Get(LangScope.Screen, SelectedItem?.LabelKey ?? "");
        var cx = glass.Left + glass.Width / 2;

        g.DrawString(name, title, muted, cx - g.MeasureString(name, title).Width / 2, glass.Top + 60 * s,
                     StringFormat.GenericTypographic);

        var prompt = L10n.Get(LangScope.Screen, "capture.prompt");
        g.DrawString(prompt, hint, text, cx - g.MeasureString(prompt, hint).Width / 2,
                     glass.Top + glass.Height * 0.26f, StringFormat.GenericTypographic);

        var shown = string.IsNullOrEmpty(_captureText) ? "—" : _captureText;
        var w = g.MeasureString(shown, mono).Width;
        var by = glass.Top + glass.Height * 0.36f;
        g.DrawString(shown, mono, green, cx - w / 2, by, StringFormat.GenericTypographic);

        DrawDecisionRows(g, s, glass, pad);
    }

    /// <summary>HELP 键：进入帮助页（再按一次退出）。页面的内容/翻页/选中都在 HelpPage 里。</summary>
    private void EnterHelp()
    {
        // HELP 再按一次 = 关闭帮助（避免把 Help 存成“返回目标”导致退不出来）
        if (_mode == Mode.Help) { LeaveHelp(); return; }

        _helpPrevMode = _mode;
        _helpPrevPage = _page;
        _helpPrevItemSel = _itemSel;
        _helpPageUI = new Pages.HelpPage(
            pages: () => _pages,
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
        _itemSel = _helpPrevItemSel;
        _toast = null;
        SyncDiagProbe();
    }



    /// <summary>
    /// 键位捕获页底部的 确认 / 取消 两行（对应左侧第 3、4 个键）。捕获页没有「选中项」，
    /// 第一行恒为主动作，所以这里不再依赖任何遗留选中字段。
    /// </summary>
    private void DrawDecisionRows(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        var rowH = 44 * s;
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        var left = glass.Left + pad;
        using var big = MfdTheme.CjkAt(28f * s);

        for (var i = 0; i < 2; i++)
        {
            var btn = _layout.SideButtons[2 + i];
            var yy = btn.Y + btn.Height / 2 - rowH / 2;
            var primary = i == 0;         // 第一行 = 主动作（确认），第二行只是取消键提示
            if (primary)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, left - 6 * s, yy, colW + 12 * s, rowH);
            }
            using var fgBrush = new SolidBrush(primary ? MfdTheme.Dark : MfdTheme.Text);
            var text = L10n.Get(LangScope.Screen, primary ? "confirm.ok" : "confirm.cancel");
            g.DrawString(text, big, fgBrush, left + 20 * s, yy + 2 * s, StringFormat.GenericTypographic);
        }
    }

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

        // 编辑页：点击 确认 / 取消 / 手动指定机型 三个选项
        if (!_work.Mfd.RealisticKeys && _mode == Mode.EditNumber)
        {
            var sScale = _layout.Width / 1330f;
            var rowH = 44 * sScale;
            var colW = (_layout.Glass.Width - 18 * sScale * 2) / 2 - 34 * sScale;
            var rows = new (int Btn, int Choice)[] { (2, 0), (3, 1), (7, 2) };
            foreach (var (btnIdx, choice) in rows)
            {
                var btn = _layout.SideButtons[btnIdx];
                var x = btnIdx < 5
                    ? _layout.Glass.Left + 18 * sScale
                    : _layout.Glass.Left + _layout.Glass.Width / 2 + 7 * sScale;
                var y = btn.Y + btn.Height / 2 - rowH / 2;
                if (e.X < x - 6 * sScale || e.X > x + colW + 6 * sScale) continue;
                if (e.Y < y || e.Y > y + rowH) continue;

                var nowC = DateTime.UtcNow;
                var againC = choice == _lastHitCell && (nowC - _lastHitUtc).TotalMilliseconds < 500;
                _lastHitCell = choice;
                _lastHitUtc = nowC;
                _editPage?.Select(choice);
                if (againC || e.Clicks >= 2) Confirm();
                Invalidate();
                return;
            }
        }

        // 选机型页：非拟真模式下点击行即选中（同一行 400ms 内再点一次 = 指定）
        if (!_work.Mfd.RealisticKeys && _mode == Mode.PickVehicle)
        {
            var count = WtHud.Hud.AirframeDb.All.Count + 1;   // +1 = 「全局」项
            var sScale = _layout.Width / 1330f;
            var colW = (_layout.Glass.Width - 18 * sScale * 2) / 2 - 34 * sScale;
            var rowH = 40 * sScale;
            for (var i = 0; i < 10; i++)
            {
                var item = (_pickPageUI?.PageIndex ?? 0) * 10 + i;
                if (item >= count) break;
                var col = i % 2;
                var row = i / 2;
                var btn = _layout.SideButtons[col * 5 + row];
                var x0 = col == 0
                    ? _layout.Glass.Left + 18 * sScale
                    : _layout.Glass.Left + _layout.Glass.Width / 2 + 7 * sScale;
                var y0 = btn.Y + btn.Height / 2 - rowH / 2;
                if (e.X < x0 - 6 * sScale || e.X > x0 + colW + 6 * sScale) continue;
                if (e.Y < y0 || e.Y > y0 + rowH) continue;

                var now = DateTime.UtcNow;
                var again = item == _lastHitCell && (now - _lastHitUtc).TotalMilliseconds < 400;
                _lastHitCell = item;
                _lastHitUtc = now;
                _pickPageUI?.SelectCell(i);
                if (again || e.Clicks >= 2) _pickPageUI?.Handle(MfdCommand.Of("confirm.ok"));
                Invalidate();
                return;
            }
        }

        // 快照列表：单击选中，同一行 400ms 内再点 = 进入恢复确认（与选机型页一致）
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Snapshots &&
            RowHitTest(new PointF(e.X, e.Y), out var snapCell))
        {
            var nowS = DateTime.UtcNow;
            var againS = snapCell == _lastHitCell && (nowS - _lastHitUtc).TotalMilliseconds < 400;
            _lastHitCell = snapCell;
            _lastHitUtc = nowS;
            _snapPageUI?.SelectCell(snapCell);
            if (againS || e.Clicks >= 2) _snapPageUI?.Handle(MfdCommand.Of("confirm.ok"));
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

        // 拟真按键模式：屏幕行不接受鼠标，只能用实体按键/方向键
        if (!_work.Mfd.RealisticKeys &&
            RowHitTest(new PointF(e.X, e.Y), out var rowCell))
        {
            if (_mode == Mode.Preset)
            {
                if (rowCell < MfdTheme.Presets.Length)
                {
                    var nowP = DateTime.UtcNow;
                    var againP = rowCell == _lastHitCell &&
                                 (nowP - _lastHitUtc).TotalMilliseconds < 400;
                    _lastHitCell = rowCell;
                    _lastHitUtc = nowP;
                    _presetPage?.Select(rowCell);
                    if (againP || e.Clicks >= 2) _presetPage?.Handle(MfdCommand.Of("confirm.ok"));
                    Invalidate();
                }
                return;
            }

            var valid = _mode == Mode.Menu
                ? rowCell < _pages.Count
                : rowCell < CurrentPage!.Items.Count;
            if (!valid) { _lastHitCell = -1; return; }     // 空单元格：不选也不确认

            var now = DateTime.UtcNow;
            var repeated = rowCell == _lastHitCell &&
                           (now - _lastHitUtc).TotalMilliseconds < 400;
            _lastHitCell = rowCell;
            _lastHitUtc = now;

            if (_mode == Mode.Menu)
            {
                _menuSel = rowCell;
                if (repeated || e.Clicks >= 2) EnterPage();
            }
            else if (_mode == Mode.Items)
            {
                _itemSel = _itemPage * 10 + rowCell;
                if (repeated || e.Clicks >= 2) Confirm();
            }
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

    /// <summary>Hit test for the ten screen cells (used for click-to-select).</summary>
    private bool RowHitTest(PointF p, out int cell)
    {
        cell = -1;
        if (!_layout.Glass.Contains(p)) return false;
        var s = _layout.Width / 1330f;
        var left = _layout.Glass.Left + 18 * s;
        var rightStart = _layout.Glass.Left + _layout.Glass.Width / 2 + 18 * s;
        var rowH = 40 * s;

        for (var i = 0; i < 10; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var btn = _layout.SideButtons[col * 5 + row];
            var cy = btn.Y + btn.Height / 2;
            var x0 = col == 0 ? left - 6 * s : rightStart - 6 * s;
            var x1 = x0 + (_layout.Glass.Width - 36 * s) / 2 - 22 * s;
            if (p.Y >= cy - rowH / 2 && p.Y <= cy + rowH / 2 && p.X >= x0 && p.X <= x1)
            {
                cell = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 二级确认页的两行（0 = 确认、1 = 取消）命中测试。矩形与 <c>ConfirmPage.Draw</c> 用的是同一个
    /// <see cref="MfdPageLayout.ListRowRect(int,int)"/>，所以画在哪里就能点在哪里。
    /// </summary>
    private bool ConfirmRowHitTest(PointF p, out int option)
    {
        option = -1;
        var s = _layout.Width / 1330f;
        var pageLayout = new MfdPageLayout(_layout, s);
        for (var i = 0; i < 2; i++)
        {
            var row = pageLayout.ListRowRect(0, 2 + i);
            row.Inflate(6 * s, 0);          // 高亮条比行矩形左右各宽 6*s
            if (row.Contains(p)) { option = i; return true; }
        }
        return false;
    }

    /// <summary>确认页行的点击处理（OnMouseDown 与预览钩子共用）：单击选中，双击执行。</summary>
    private bool TryClickConfirmRow(PointF point, int clicks)
    {
        if (!ConfirmRowHitTest(point, out var option)) return false;

        var now = DateTime.UtcNow;
        var repeated = option == _lastHitCell && (now - _lastHitUtc).TotalMilliseconds < 400;
        _lastHitCell = option;
        _lastHitUtc = now;

        _confirmPage?.Select(option);

        if (repeated || clicks >= 2)
            _confirmPage?.Handle(MfdCommand.Of(option == 0 ? "confirm.ok" : "confirm.cancel"));
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

        // 二级页面（确认 / 数值 / 文本 / 预设 / 机型 / 快照）把按钮交给各自的 router：
        // 页面在 Build() 里声明「哪个槽位、哪个侧键、发什么命令」，这里只转发，
        // 不再由表单硬编码 index==2/3/7 的含义。
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

        if (_mode != Mode.Items) return;      // 编辑/捕获时侧键不改变选择

        var page = CurrentPage!;
        var itemIdx = _itemPage * 10 + cell;
        if (itemIdx < page.Items.Count) _itemSel = itemIdx;
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
            if (e.KeyCode == Keys.Escape) { _mode = Mode.Items; _captureText = ""; _heldKeys.Clear(); Invalidate(); return; }

            if (_heldKeys.Count == 0) _captureText = "";   // 新一轮组合从空开始
            _heldKeys.Add(e.KeyCode);
            _captureText = FormatHeld();
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
        _heldKeys.Remove(e.KeyCode);
        // 全部松开后保持最后显示的文字（冻结），下次按下重新开始
        e.Handled = true;
        Invalidate();
    }

    private static readonly Keys[] ModifierOrder = { Keys.ControlKey, Keys.ShiftKey, Keys.Menu };

    private string FormatHeld()
    {
        var parts = new List<string>();
        foreach (var mod in ModifierOrder)
            if (_heldKeys.Contains(mod)) parts.Add(ModName(mod));
        foreach (var key in _heldKeys.OrderBy(k => (int)k))
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

    private static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "NUM" + (k - Keys.NumPad0),
        >= Keys.A and <= Keys.Z => ((char)('A' + (k - Keys.A))).ToString(),
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        _ => k.ToString().ToUpperInvariant(),
    };

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
        _captureText = "";
        _heldKeys.Clear();
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
            var baseIdx = _itemPage * 10;
            _itemSel = baseIdx + ((_itemSel - baseIdx + delta + 10) % 10);
        }
        _toast = null;
    }

    private void Step(int direction)
    {
        if (_mode == Mode.Items) SelectedItem?.Activate(_work, direction);
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
            case Mode.PitchDetect:
                _nav.Current?.Handle(MfdCommand.Of("confirm.ok"));
                _toast = _pitchPage?.ResultToast;
                _mode = Mode.Items;
                break;
            default:
                if (SelectedItem is { } item) Activate(item);
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
        if (SelectedItem is { } item && item.Kind == ItemKind.KeyBinding && !string.IsNullOrWhiteSpace(_captureText))
        {
            item.SetText2?.Invoke(_work, _captureText);
            _dirty = true;
            WtHud.Hud.ConfigSnapshots.SaveSession(_work, "edit");
        }
        _mode = Mode.Items;
        _captureText = "";
        _toast = null;
    }

    private void Activate(MfdItem item)
    {
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
        _captureText = "";
        _heldKeys.Clear();
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
        _mode = Mode.Detail;
    }

    /// <summary>俯仰方向检测：需要现场采样，确认后进入采样页。</summary>
    private void EnterPitchDetect()
    {
        _pitchPage = new Pages.PitchDetectPage(
            onFinished: () => { _nav.Clear(); _mode = Mode.Items; _dirty = true; },
            onCancelled: () => { _nav.Clear(); _mode = Mode.Items; });
        _nav.Push(_pitchPage);
        _pitchPage.StartRepaint(Invalidate);
        _mode = Mode.PitchDetect;
    }

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

        // 需要现场采样的动作（俯仰方向检测）确认后进入采样页
        if (WtHud.Hud.PitchDetect.Active)
        {
            EnterPitchDetect();
            return;
        }
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
            var slotName = "SIDE" + (column * 5 + row);
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

    private void EnterPage()
    {
        if (_menuSel < 0 || _menuSel >= _pages.Count) return;
        _page = _menuSel;
        _menuPageIdx = _menuSel / 10;      // 记住是从哪一页的模块进来的，返回时还回那一页
        _itemPage = 0;
        _itemSel = 0;
        _toast = null;

        // 快照模块没有行列表：直接进入快照页（页面自己负责列表与选中）
        if (_pages[_page].TitleKey == "page.snapshots")
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
            case Mode.PitchDetect:
                _nav.Current?.Handle(MfdCommand.Of("nav.back"));
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

        var page = CurrentPage!;
        var pages = (page.Items.Count + 9) / 10;
        if (pages <= 1) { _toast = L10n.Get(LangScope.Screen, "page.single"); return; }

        var next = Math.Clamp(_itemPage + delta, 0, pages - 1);
        if (next == _itemPage) { _toast = L10n.Get(LangScope.Screen, "page.edge"); return; }

        _itemPage = next;
        _itemSel = next * 10;
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
        _itemSel = Math.Clamp(itemIndex, 0, 9);
        _menuSel = _page;
    }

    /// <summary>预览用：按模块内的绝对序号打开某一行（跨页，PreviewOpen 只认单页 0-9）。</summary>
    public void PreviewOpenItem(int pageIndex, int absoluteItem)
    {
        PreviewOpen(pageIndex, 0);
        _itemPage = Math.Max(0, absoluteItem / 10);
        _itemSel = Math.Max(0, absoluteItem);
    }

    /// <summary>Preview helper: switch the module's item page (0-based, 10 cells per page).</summary>
    public void PreviewItemPage(int page)
    {
        _itemPage = Math.Max(0, page);
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
        Pages.ConfirmPage p => p.Selected,
        Pages.NumberEditPage p => p.Choice,
        Pages.PresetPage p => p.Selected,
        Pages.PickVehiclePage p => p.Selected,
        Pages.SnapshotsPage p => p.Selected,
        _ => -1,
    };

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
        _captureText = "CTRL+N";
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
        _itemSel = 0;
        _mode = Mode.Items;
        if (SelectedItem is { } item) Activate(item);
    }

    /// <summary>Preview helper: opens the detail page of one specific airframe (by id).</summary>
    public void PreviewDetailVehicle(string vehicleId)
    {
        var idx = _pages.FindIndex(p => p.TitleKey == "page.airframe");
        if (idx < 0) return;
        _page = idx;
        _menuSel = idx;
        _itemSel = 0;
        _mode = Mode.Items;
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

    /// <summary>Preview helper: 直接进入俯仰方向采样页（验证 EnterPitchDetect 构造）。</summary>
    public void PreviewPitch()
    {
        EnterPitchDetect();
    }

    /// <summary>Preview helper: 机型详情页按 RESET（验证 _resetConfirm → ConfirmPage 文案）。</summary>
    public void PreviewDetailReset()
    {
        PreviewDetail();
        PressBottom(0);
    }

    /// <summary>Preview helper: presses a physical side key by index (0-9), exactly like clicking it.</summary>
    public void PreviewSideKey(int index) => SidePressed(Math.Clamp(index, 0, 9));

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

        if (WtHud.Hud.PitchDetect.PendingSave is var (veh, sign))
        {
            WtHud.Hud.SettingsRouter.SetFor(_cfg, veh, "row.pitchSign", sign);
            WtHud.Hud.PitchDetect.ClearPendingSave();
        }

        WtHud.Hud.ConfigSnapshots.SaveHistory(_work, "save");
        WtHud.Hud.ConfigSnapshots.EndSession();   // SAVE 成功后清掉本次会话快照
        Saved = true;
        _dirty = false;
        _forceClose = true;   // 已保存：关闭时不要再弹放弃确认
        Close();
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

























