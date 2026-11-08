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
    private enum Mode { Menu, Items, Confirm, EditNumber, CaptureKey, Preset, Help, Detail, PickVehicle, PitchDetect }

    private readonly MfdGeometry _geo;
    private readonly MfdLayout _layout;
    private readonly Bitmap _bezel;
    private readonly HudConfig _cfg;
    private readonly HudConfig _work;
    private readonly HudConfig _baseline;
    private readonly List<MfdPage> _pages;

    private Mode _mode = Mode.Menu;
    private readonly Pages.MfdNavigator _nav = new();
    private Pages.PitchDetectPage? _pitchPage;
    private Pages.ConfirmPage? _confirmPage;
    private Pages.NumberEditPage? _editPage;
    private Pages.PresetPage? _presetPage;
    private Pages.PickVehiclePage? _pickPageUI;
    private int _page;
    private int _menuSel;
    private int _itemSel;
    private MfdItem? _pendingAction;
    private bool _dirty;
    private bool _closeConfirm;
    private bool _forceClose;
    private int _confirmSel;               // 0 = 确认, 1 = 取消
    private int _itemPage;                 // 当前模块内的页号（每页 10 格）
    private (ButtonKind Kind, int Index) _pressed = (ButtonKind.None, -1);
    private string? _toast;
    private string _editBuffer = "";       // 数值手输缓冲
    private string _captureText = "";      // 键位捕获结果
    private readonly HashSet<Keys> _heldKeys = new();   // 捕获时当前按住的键
    private int _helpPage;
    private int _helpSel;
    private List<(int Line, string Key)> _helpEntries = new();
    private Mode _helpPrevMode = Mode.Menu;
    private int _helpPrevPage;
    private int _helpPrevItemSel;
    private bool _helpOpen;
    private MfdItem? _detailTarget;
    private bool _resetConfirm;
    private MfdScreenPage? _menuPage;
    private MfdInputRouter? _menuRouter;
    private List<RealButton>? _realButtons;
    private int _pickSel;
    private int _pickPage;
    private Mode _pickReturn = Mode.EditNumber;
    private MfdItem? _presetTarget;
    private int _presetSel;
    private DateTime _lastClickUtc = DateTime.MinValue;
    private int _lastClickCell = -1;
    private int _lastHitCell = -1;
    private DateTime _lastHitUtc = DateTime.MinValue;

    public bool Saved { get; private set; }

    private readonly string? _savePath;

    /// <param name="savePath">When set, SAVE writes here instead of hud-config.json (scratch mode).</param>
    public MfdFrameForm(HudConfig cfg, string? savePath = null)
    {
        _savePath = savePath;
        _cfg = cfg;
        _baseline = cfg.Clone();
        _work = cfg.Clone();
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
            Mode.CaptureKey => "capture.title",
            Mode.PitchDetect => "row.pitchDetect",
            _ => CurrentPage?.TitleKey ?? "menu",
        };
        var title = L10n.Get(LangScope.Screen, titleKey);
        if (_mode is Mode.Items or Mode.EditNumber)
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
        var pages = _mode == Mode.Items && CurrentPage != null ? (CurrentPage.Items.Count + 9) / 10 : 1;
        var ver = (pages > 1 ? ( _itemPage + 1 ) + "/" + pages + "  " : "") + "v" + Application.ProductVersion;
        g.DrawString(ver, meta, muted, glass.Right - pad - g.MeasureString(ver, meta).Width, glass.Top + 8 * s);

        switch (_mode)
        {
            case Mode.Menu: DrawMenu(g, s, glass, left, pad); break;
            case Mode.Confirm: _confirmPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.EditNumber: _editPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.CaptureKey: DrawCapturePage(g, s, glass, pad); break;
            case Mode.Preset: _presetPage?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.Help: DrawHelpStructure(g, s, glass, pad); break;
            case Mode.Detail: DrawAirframePage(g, s, glass, pad); break;
            case Mode.PickVehicle: _pickPageUI?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
            case Mode.PitchDetect: _nav.Current?.Draw(new Pages.MfdRenderContext { G = g, Scale = s, Glass = glass, Pad = pad }); break;
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
            Mode.EditNumber => "edit.prompt",
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

        for (var i = 0; i < _pages.Count && i < 10; i++)
        {
            var btn = _layout.SideButtons[(i % 2) * 5 + i / 2];
            var cy = btn.Y + btn.Height / 2;
            var x = i % 2 == 0 ? left : glass.Left + glass.Width / 2 + 18 * s;
            var y = cy - rowH / 2;
            var selected = i == _menuSel;

            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);
            if (i % 2 == 0)
            {
                using var mark = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Green);
                g.FillRectangle(mark, x, y + rowH / 2 - 5 * s, 9 * s, 9 * s);
            }
            var page = _pages[i];
            g.DrawString(L10n.Get(LangScope.Screen, page.MenuKey), name, fgBrush, x + 16 * s, y + 5 * s,
                         StringFormat.GenericTypographic);
            var countText = page.Items.Count + (L10n.Screen == Lang.Zh ? " 项" : " ITEMS");
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

    /// <summary>Second-level page: manual numeric entry.</summary>
    private void DrawEditPage(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        using var title = MfdTheme.CjkAt(26f * s);
        using var mono = MfdTheme.MonoBoldAt(56f * s);
        using var hint = MfdTheme.CjkAt(18f * s);
        using var green = new SolidBrush(MfdTheme.Green);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var name = L10n.Get(LangScope.Screen, SelectedItem?.LabelKey ?? "");
        var cx = glass.Left + glass.Width / 2;

        var zh2 = L10n.Screen == Lang.Zh;
        var vehicle = WtHud.Hud.AirframeDb.EffectiveVehicle;
        using var vFont = MfdTheme.CjkAt(17f * s);
        var vText = string.IsNullOrWhiteSpace(vehicle)
            ? (zh2 ? "当前机型：未读取到机型数据" : "AIRFRAME: NOT DETECTED")
            : (zh2 ? "当前机型：" + vehicle : "AIRFRAME: " + vehicle);
        g.DrawString(vText, vFont, string.IsNullOrWhiteSpace(vehicle) ? muted : text,
                     cx - g.MeasureString(vText, vFont).Width / 2, glass.Top + 26 * s,
                     StringFormat.GenericTypographic);

        g.DrawString(name, title, muted, cx - g.MeasureString(name, title).Width / 2, glass.Top + 62 * s,
                     StringFormat.GenericTypographic);

        var shown = _editBuffer.Length == 0 ? "_" : _editBuffer + "_";
        var w = g.MeasureString(shown, mono).Width;
        var boxW = Math.Max(w + 80 * s, 320 * s);
        var boxH = 90 * s;
        var bx = cx - boxW / 2;
        var by = glass.Top + glass.Height * 0.28f;

        using (var boxBg = new SolidBrush(Color.FromArgb(210, 6, 16, 10)))
            g.FillRectangle(boxBg, bx, by, boxW, boxH);
        g.DrawRectangle(new Pen(MfdTheme.Green, 2.5f * s), bx, by, boxW, boxH);
        g.DrawString(shown, mono, green, cx - w / 2, by + 12 * s, StringFormat.GenericTypographic);

        var unit = SelectedItem?.Unit ?? "";
        if (!string.IsNullOrEmpty(unit))
            g.DrawString(unit, title, muted, bx + boxW + 10 * s, by + boxH / 2 - 14 * s, StringFormat.GenericTypographic);

        DrawDecisionRows(g, s, glass, pad);
        // 第三个选项（右侧第 3 键）：手动指定机型，常驻并可高亮
        {
            var rowH3 = 44 * s;
            var colW3 = (glass.Width - pad * 2) / 2 - 34 * s;
            var optX3 = glass.Left + glass.Width / 2 + pad * 0.4f;
            var btn3 = _layout.SideButtons[7];
            var by3 = btn3.Y + btn3.Height / 2 - rowH3 / 2;
            var pickSelected = _confirmSel == 2;

            if (pickSelected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, optX3 - 2 * s, by3, colW3 + 4 * s, rowH3);
            }
            else
            {
                using var frame = new Pen(MfdTheme.Green, 1.5f * s);
                g.DrawRectangle(frame, optX3 - 2 * s, by3, colW3 + 4 * s, rowH3);
            }

            using var opt = MfdTheme.CjkAt(24f * s);
            using var pickFg = new SolidBrush(pickSelected ? MfdTheme.Dark : MfdTheme.Text);
            var pick = zh2 ? "手动指定机型" : "PICK AIRFRAME";
            g.DrawString(pick, opt, pickFg, optX3 + 20 * s, by3 + 2 * s, StringFormat.GenericTypographic);
        }

    }

    /// <summary>Second-level page: pick one of the color presets.</summary>
    private void DrawPresetPage(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        using var title = MfdTheme.CjkAt(24f * s);
        using var label = MfdTheme.CjkAt(22f * s);
        using var hintF = MfdTheme.CjkAt(17f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cx = glass.Left + glass.Width / 2;

        // 与模块页相同的 2 列 × 5 行网格：cell = (列 * 5) + 行
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        var rowH = 40 * s;

        for (var i = 0; i < MfdTheme.Presets.Length; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var btn = _layout.SideButtons[col * 5 + row];
            var x = col == 0
                ? glass.Left + pad
                : glass.Left + glass.Width / 2 + pad * 0.4f;
            var y = btn.Y + btn.Height / 2 - rowH / 2;

            var selected = i == _presetSel;
            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }

            var (key, ink, accent) = MfdTheme.Presets[i];

            // 两个小色块：左=高亮色，右=正文色
            using (var sw = new SolidBrush(accent))
                g.FillRectangle(sw, x + 8 * s, y + 9 * s, 20 * s, rowH - 18 * s);
            using (var sw2 = new SolidBrush(ink))
                g.FillRectangle(sw2, x + 32 * s, y + 9 * s, 20 * s, rowH - 18 * s);

            using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
            g.DrawString(L10n.Get(LangScope.Screen, "color." + key), label, fg,
                         x + 62 * s, y + 4 * s, StringFormat.GenericTypographic);
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

        var tip = L10n.Get(LangScope.Screen, "capture.hint");
        g.DrawString(tip, hint, muted, cx - g.MeasureString(tip, hint).Width / 2, glass.Bottom - 54 * s,
                     StringFormat.GenericTypographic);

        DrawDecisionRows(g, s, glass, pad);
    }

    /// <summary>Enters the help page, remembering where we came from.</summary>
    private void EnterHelp()
    {
        // HELP 再按一次 = 关闭帮助（避免把 Help 存成“返回目标”导致退不出来）
        if (_mode == Mode.Help) { LeaveHelp(); return; }

        _helpPrevMode = _mode;
        _helpPrevPage = _page;
        _helpPrevItemSel = _itemSel;
        _helpPage = 0;
        _mode = Mode.Help;
        _toast = null;
    }

    private void LeaveHelp()
    {
        _mode = _helpPrevMode == Mode.Help ? Mode.Menu : _helpPrevMode;
        _page = _helpPrevPage;
        _itemSel = _helpPrevItemSel;
        _toast = null;
        SyncDiagProbe();
    }

    private const float HelpLineHeight = 30f;

    private string ModuleHint(MfdPage p)
    {
        var hint = L10n.Get(LangScope.Screen, p.MenuHintKey);
        if (hint == p.MenuHintKey || string.IsNullOrWhiteSpace(hint))
            hint = L10n.Screen == Lang.Zh ? $"{p.Items.Count} 项设置" : $"{p.Items.Count} settings";
        return hint;
    }

    private int HelpLinesPerPage(RectangleF glass, float s) =>
        Math.Max(8, (int)((glass.Height - 78 * s) / (HelpLineHeight * s)));

    /// <summary>Flat help content: key reference followed by every module and every item.</summary>
    private List<string> BuildHelpLines()
    {
        var zh = L10n.Screen == Lang.Zh;
        var lines = new List<string>();
        _helpEntries = new List<(int, string)>();
        void Add(string t) => lines.Add(t);
        void AddEntry(string t, string key)
        {
            _helpEntries.Add((lines.Count, key));
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
        for (var m = 0; m < _pages.Count; m++)
        {
            var p = _pages[m];
            var isLast = m == _pages.Count - 1;
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
                var value = it.ValueText(_cfg);
                AddEntry($"      |        {tail} {slot,-4} {label,-20} [{value}]", it.LabelKey);
            }
        }

        return lines;
    }


    /// <summary>Selects the help tree entry under the mouse (only in non-key-only mode).</summary>
    private bool TryClickHelpLine(PointF p)
    {
        if (!_layout.Glass.Contains(p)) return false;

        var s = _layout.Width / 1330f;
        var glass = _layout.Glass;
        var pad = 18 * s;
        var textTop = glass.Top + 72 * s;                 // 页眉 + 虚线之后
        var lineH = HelpLineHeight * s;

        var per = HelpLinesPerPage(glass, s);
        var lines = BuildHelpLines();
        var total = Math.Max(1, (lines.Count + per - 1) / per);
        if (_helpPage >= total) _helpPage = total - 1;

        var rowIndex = (int)((p.Y - textTop) / lineH);
        if (rowIndex < 0) return false;

        var lineIndex = _helpPage * per + rowIndex;
        for (var i = 0; i < _helpEntries.Count; i++)
        {
            if (_helpEntries[i].Line != lineIndex) continue;
            _helpSel = i;
            return true;
        }

        // 点到非结构树行：选中本页最近的条目，保持左右一致
        for (var i = 0; i < _helpEntries.Count; i++)
        {
            if (_helpEntries[i].Line / per == _helpPage) { _helpSel = i; return true; }
        }
        return false;
    }

    /// <summary>Selects the first tree entry that lives on the given page.</summary>
    private void SelectFirstEntryOnPage(int page, int per)
    {
        _helpPage = page;
        for (var i = 0; i < _helpEntries.Count; i++)
        {
            if (_helpEntries[i].Line / per == page) { _helpSel = i; return; }
        }
        _helpSel = 0;
    }

    private void MoveHelpSelection(int delta)
    {
        if (_helpEntries.Count == 0) return;
        _helpSel = Math.Clamp(_helpSel + delta, 0, _helpEntries.Count - 1);
    }

    /// <summary>Draws the description of the selected entry in a right-hand column.</summary>
    private void DrawHelpDescription(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        if (_helpEntries.Count == 0) return;
        _helpSel = Math.Clamp(_helpSel, 0, _helpEntries.Count - 1);
        var key = _helpEntries[_helpSel].Key;
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
            foreach (var line in WrapHelpText(g, body, raw, colW))
            {
                if (y > glass.Bottom - 40 * s) return;
                g.DrawString(line, body, ink, colX, y, StringFormat.GenericTypographic);
                y += 24 * s;
            }
        }
    }

    /// <summary>Wraps help text: Latin words stay whole, CJK characters break individually.</summary>
    private static List<string> WrapHelpText(System.Drawing.Graphics g, Font font, string text, float maxWidth)
    {
        var result = new List<string>();
        var tokens = new List<string>();
        var word = "";
        foreach (var ch in text)
        {
            var isAsciiWord = ch < 0x80 && ch != ' ';
            if (isAsciiWord) { word += ch; continue; }
            if (word.Length > 0) { tokens.Add(word); word = ""; }
            tokens.Add(ch.ToString());
        }
        if (word.Length > 0) tokens.Add(word);

        var line = "";
        foreach (var t in tokens)
        {
            if (t == " ") { line += " "; continue; }
            var candidate = line + t;
            if (g.MeasureString(candidate, font).Width > maxWidth && line.Trim().Length > 0)
            {
                result.Add(line.TrimEnd());
                line = t;
            }
            else line = candidate;
        }
        if (line.Trim().Length > 0) result.Add(line.TrimEnd());
        return result;
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
        _helpPage = Math.Clamp(_helpPage, 0, total - 1);

        var title = zh ? "SYSTEM HELP / 导航手册" : "SYSTEM HELP / NAVIGATION";
        g.DrawString(title, head, accent, left, y, StringFormat.GenericTypographic);
        var pager = $"{_helpPage + 1}/{total}";
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
        if (_helpEntries.Count > 0)
        {
            var selLine = _helpEntries[Math.Clamp(_helpSel, 0, _helpEntries.Count - 1)].Line;
            if (selLine / per != _helpPage) SelectFirstEntryOnPage(_helpPage, per);
        }

        var lineH = HelpLineHeight * s;
        var start = _helpPage * per;
        var selLineIndex = _helpEntries.Count > 0
            ? _helpEntries[Math.Clamp(_helpSel, 0, _helpEntries.Count - 1)].Line
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


    /// <summary>Airframe detail page: capability summary for the selected type.</summary>
    private void DrawAirframePage(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        var zh = L10n.Screen == Lang.Zh;
        var id = _detailTarget?.LabelKey ?? "";
        var prof = WtHud.Hud.AirframeDb.Get(id);

        using var head = MfdTheme.CjkAt(24f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var cjk = MfdTheme.CjkAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var left = glass.Left + pad;
        var y = glass.Top + 16 * s;
        g.DrawString(id, head, accent, left, y, StringFormat.GenericTypographic);

        y += 38 * s;
        void Row(string label, string value)
        {
            g.DrawString(label, cjk, muted, left, y, StringFormat.GenericTypographic);
            g.DrawString(value, body, text, left + 200 * s, y, StringFormat.GenericTypographic);
            y += 28 * s;
        }

        Row(L10n.Get(LangScope.Screen, "airframe.ra"), prof.RadarAltitude ? (zh ? "支持" : "YES") : (zh ? "不支持" : "NO"));
        Row("PULL UP", prof.PullUp ? (zh ? "支持" : "YES") : (zh ? "不支持" : "NO"));
        Row(L10n.Get(LangScope.Screen, "airframe.aoa"), prof.AoaWarn > 0 ? $"{prof.AoaWarn:F0}°" : (zh ? "默认" : "DEFAULT"));
        Row("IAS 极速", prof.OverspeedIas > 0 ? $"{prof.OverspeedIas:F0} km/h" : (zh ? "默认" : "DEFAULT"));
        Row("MACH", prof.OverspeedMach > 0 ? $"{prof.OverspeedMach:F2}" : (zh ? "默认" : "DEFAULT"));
        Row(L10n.Get(LangScope.Screen, "airframe.sink"), prof.SinkRate > 0 ? $"{prof.SinkRate:F0} m/s" : (zh ? "默认" : "DEFAULT"));
        Row(L10n.Get(LangScope.Screen, "airframe.att"), prof.Attitude == "game" ? "AHRS GAME" : "AHRS EST");
        Row(L10n.Get(LangScope.Screen, "airframe.ab"), prof.Airbrake == "key" ? (zh ? "按键读取" : "KEY") : (zh ? "遥测读取" : "TELEMETRY"));
        var shownSign = _work.PitchSignByVehicle.TryGetValue(id, out var storedSign) ? storedSign : _work.PitchSign;
        Row(L10n.Get(LangScope.Screen, "airframe.pitchSign"),
            shownSign == 0 ? (zh ? "自动" : "AUTO") : (shownSign > 0 ? "+1" : "-1"));
        Row(L10n.Get(LangScope.Screen, "airframe.stall"), prof.StallAoa > 0 ? $"{prof.StallAoa:F1}°" : (zh ? "未校准" : "DEFAULT"));

        y += 8 * s;
        foreach (var line in WrapHelpText(g, cjk, L10n.Get(LangScope.Screen, "airframe.note") + ": " + prof.Note,
                     glass.Width - pad * 2))
        {
            g.DrawString(line, cjk, muted, left, y, StringFormat.GenericTypographic);
            y += 26 * s;
        }
    }


    /// <summary>Applies the manually picked airframe and returns to the previous page.</summary>
    private void ApplyPickedVehicle()
    {
        var list = WtHud.Hud.AirframeDb.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (_pickSel >= 0 && _pickSel < list.Count)
        {
            WtHud.Hud.AirframeDb.ManualVehicle = list[_pickSel];
            _dirty = true;
        }
        _mode = _pickReturn;
        _toast = null;
    }

    /// <summary>Second-level page: pick an airframe by hand when detection failed.</summary>
    private void DrawPickVehiclePage(System.Drawing.Graphics g, float s, RectangleF glass, float pad)
    {
        using var head = MfdTheme.CjkAt(20f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);

        var list = WtHud.Hud.AirframeDb.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var pages = Math.Max(1, (list.Count + 9) / 10);
        _pickPage = Math.Clamp(_pickPage, 0, pages - 1);

        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        var rowH = 40 * s;

        for (var i = 0; i < 10; i++)
        {
            var item = _pickPage * 10 + i;
            if (item >= list.Count) break;

            var col = i % 2;
            var row = i / 2;
            var btn = _layout.SideButtons[col * 5 + row];
            var x = col == 0 ? glass.Left + pad : glass.Left + glass.Width / 2 + pad * 0.4f;
            var y = btn.Y + btn.Height / 2 - rowH / 2;

            var selected = item == _pickSel;
            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }
            using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
            g.DrawString(list[item], body, fg, x + 8 * s, y + 8 * s, StringFormat.GenericTypographic);
        }

        var pager = $"{_pickPage + 1}/{pages}";
        g.DrawString(pager, head, accent, glass.Right - pad - g.MeasureString(pager, head).Width,
                     glass.Top + 38 * s, StringFormat.GenericTypographic);
    }

    /// <summary>确认 / 取消 两行，对应左侧第 3、4 个键。</summary>
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
            var selected = i == _confirmSel;
            if (selected)
            {
                using var hi = new SolidBrush(i == 0 ? MfdTheme.Green : MfdTheme.GreenDim);
                g.FillRectangle(hi, left - 6 * s, yy, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);
            var text = L10n.Get(LangScope.Screen, i == 0 ? "confirm.ok" : "confirm.cancel");
            g.DrawString(text, big, fgBrush, left + 20 * s, yy + 2 * s, StringFormat.GenericTypographic);
        }
    }
    /// <summary>Confirmation sheet: only CONFIRM and CANCEL.</summary>

    private void DrawConfirm(System.Drawing.Graphics g, float s, RectangleF glass, float left, float pad)
    {
        var label = _resetConfirm
            ? L10n.Get(LangScope.Screen, "confirm.resetAirframe")
            : _closeConfirm
            ? L10n.Get(LangScope.Screen, "confirm.discard")
            : _pendingAction == null ? "" : L10n.Get(LangScope.Screen, _pendingAction.LabelKey);

        using var big = MfdTheme.CjkAt(28f * s);
        using var small = MfdTheme.MonoAt(15f * s);
        using var white = new SolidBrush(MfdTheme.Text);
        using var muted = new SolidBrush(MfdTheme.Muted);

        var cy = glass.Top + glass.Height * 0.30f;
        var w = g.MeasureString(label, big).Width;
        g.DrawString(label, big, white, glass.Left + glass.Width / 2 - w / 2, cy - 40 * s, StringFormat.GenericTypographic);

        var sub = L10n.Get(LangScope.Screen, _resetConfirm ? "confirm.askReset" : _closeConfirm ? "confirm.askDiscard" : "confirm.ask");
        var sw = g.MeasureString(sub, small).Width;
        g.DrawString(sub, small, muted, glass.Left + glass.Width / 2 - sw / 2, cy + 4 * s, StringFormat.GenericTypographic);

        var rowH = 44 * s;
        var colW = (glass.Width - pad * 2) / 2 - 34 * s;
        for (var i = 0; i < 2; i++)
        {
            var btn = _layout.SideButtons[2 + i];
            var yy = btn.Y + btn.Height / 2 - rowH / 2;
            var x = left;
            var selected = i == _confirmSel;
            if (selected)
            {
                using var hi = new SolidBrush(i == 0 ? MfdTheme.Green : MfdTheme.GreenDim);
                g.FillRectangle(hi, x - 6 * s, yy, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);
            var text = L10n.Get(LangScope.Screen, i == 0 ? "confirm.ok" : "confirm.cancel");
            g.DrawString(text, big, fgBrush, x + 20 * s, yy + 2 * s, StringFormat.GenericTypographic);
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
            var canPick = true;
            foreach (var (btnIdx, choice) in rows)
            {
                if (choice == 2 && !canPick) continue;
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
                _confirmSel = _editPage?.Choice ?? choice;
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
                var item = _pickPage * 10 + i;
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

        // 首页菜单：只有真正点在模块行上才交给 router，其它位置继续走按键逻辑
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Menu)
        {
            EnsureMenuPage();
            var sc = _layout.Width / 1330f;
            var pt = new PointF(e.X, e.Y);

            var hitIndex = -1;
            for (var i = 0; i < _menuPage!.Buttons.Count; i++)
            {
                var pageLayout = new MfdPageLayout(_layout, _layout.Width / 1330f);
                if (pageLayout.RectFor(_menuPage.Buttons[i]).Contains(pt))
                {
                    hitIndex = i;
                    break;
                }
            }

            if (hitIndex >= 0)
            {
                if (_menuRouter!.MouseDown(pt, e.Clicks >= 2) is { Id: "menu.open" } cmdM
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
        if (!_work.Mfd.RealisticKeys && _mode == Mode.Help && TryClickHelpLine(new PointF(e.X, e.Y)))
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
            _presetSel = _presetPage?.Selected ?? rowCell;
                    if (againP || e.Clicks >= 2) ApplyPreset();
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

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_menuRouter != null) _menuRouter.MouseUp();
        _pressed = (ButtonKind.None, -1);
        Invalidate();
    }

    private void SidePressed(int index)
    {
        var cell = (index % 5) * 2 + index / 5;

        if (_mode == Mode.Preset)
        {
            if (cell < MfdTheme.Presets.Length) { _presetPage?.Select(cell); _presetSel = _presetPage?.Selected ?? cell; }
            return;
        }

        if (_mode == Mode.EditNumber)
        {
            _editPage?.Select(index == 2 ? 0 : index == 3 ? 1 : index == 7 ? 2 : -1);
            _confirmSel = _editPage?.Choice ?? 0;
            return;
        }

        if (_mode == Mode.PickVehicle)
        {
            _pickPageUI?.SelectCell(cell);
            return;
        }

        if (_mode == Mode.Confirm)
        {
            if (index == 2) RunPending();
            else if (index == 3) CancelPending();
            return;
        }

        if (_mode is Mode.EditNumber or Mode.CaptureKey)
        {
            if (index == 2)
            {
                if (_mode == Mode.Confirm) RunPending();
                else if (_mode == Mode.EditNumber) Confirm();
                else Confirm();
            }
            else if (index == 3) CancelSecondLevel();
            return;
        }

        if (_mode == Mode.Menu)
        {
            EnsureMenuPage();
            var cmd = _menuRouter!.PressSlot($"SIDE{cell}");
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
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
            if (ch != '\0' && _editBuffer.Length < 10)
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
            case Keys.Up: Move(-1); break;
            case Keys.Down: Move(1); break;
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

    /// <summary>Applies the highlighted color preset and returns to the panel page.</summary>
    private void ApplyPreset()
    {
        if (_presetTarget?.SetPreset is { } set)
        {
            set(_work, MfdTheme.Presets[_presetSel].Key);
            MfdTheme.ApplyColors(_work.Mfd.InkColor, _work.Mfd.AccentColor);
            _dirty = true;
        }
        _presetTarget = null;
        _mode = Mode.Items;
        _toast = null;
    }

    /// <summary>Leaves a second-level page without applying.</summary>
    private void CancelSecondLevel()
    {
        if (_mode == Mode.Confirm) { CancelPending(); return; }
        if (_mode == Mode.Preset) { _mode = Mode.Items; _presetTarget = null; return; }
        _mode = Mode.Items;
        _editBuffer = "";
        _captureText = "";
        _heldKeys.Clear();
        _toast = null;
    }

    private void Move(int delta)
    {
        if (_mode == Mode.PickVehicle)
        {
            var n = WtHud.Hud.AirframeDb.All.Count;
            if (n > 0) _pickSel = Math.Clamp(_pickSel + delta, 0, n - 1);
            return;
        }

        if (_mode == Mode.Help)
        {
            var sScale = _layout.Width / 1330f;
            var per = HelpLinesPerPage(_layout.Glass, sScale);
            BuildHelpLines();
            MoveHelpSelection(delta);
            if (_helpEntries.Count > 0)
                _helpPage = _helpEntries[_helpSel].Line / per;
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
            _presetSel = _presetPage?.Selected ?? 0;
            _toast = null;
            return;
        }
        if (_mode == Mode.Confirm)
        {
            _confirmPage?.Move(delta);
            _confirmSel = _confirmPage?.Selected ?? 0;
            return;
        }
        if (_mode == Mode.Menu)
        {
            _menuSel = (_menuSel + delta + _pages.Count) % _pages.Count;
            EnsureMenuPage();               // 路由器和菜单页是懒建的，必须先建好再同步
            _menuRouter?.Move(delta);       // 否则 CONFIRM 时索引还是 0（永远打开第一项）
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
                _confirmPage?.Handle(MfdCommand.Of("confirm.ok"));
                break;
            case Mode.EditNumber:
                _editPage?.Handle(MfdCommand.Of(
                    _editPage.Choice == 1 ? "edit.cancel" :
                    _editPage.Choice == 2 ? "edit.pick" : "edit.commit"));
                break;
            case Mode.CaptureKey:
                CommitCapture();
                break;
            case Mode.Preset:
                _presetPage?.Handle(MfdCommand.Of("confirm.ok"));
                _mode = Mode.Items;
                break;
            case Mode.PickVehicle:
                _pickPageUI?.Handle(MfdCommand.Of("confirm.ok"));
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

    /// <summary>Parses the manual input and writes it into the selected numeric row.</summary>
    private void CommitNumber()
    {
        if (SelectedItem is not { } item || item.Kind != ItemKind.Number)
        {
            _mode = Mode.Items;
            return;
        }

        var text = _editBuffer.Replace(",", ".").Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            v = Math.Clamp(v, item.Min, item.Max);
            item.SetNumber?.Invoke(_work, v);
            _dirty = true;
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
        }
        _mode = Mode.Items;
        _captureText = "";
        _toast = null;
    }

    private void Activate(MfdItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Action:
                _pendingAction = item;
                _confirmSel = 0;
                EnterConfirm();
                return;
            case ItemKind.Number:
            {
                // 数值：进入手输编辑，CONFIRM 提交
                var target = item;
                var initial = item.ValueText(_work).TrimEnd('X', 'x', 'M', 's', 'k', 'm', 'h', '/', 'p', '°', ' ');
                _confirmSel = 0;
                _editPage = new Pages.NumberEditPage(
                    texts: () => (L10n.Get(LangScope.Screen, target.LabelKey), target.Unit),
                    initial: initial,
                    onCommit: buffer => { _editBuffer = buffer; CommitNumber(); },
                    onCancel: () => { _nav.Clear(); _editBuffer = ""; _mode = Mode.Items; },
                    onPick: () =>
                    {
                        _pickReturn = Mode.EditNumber;
                        _pickPageUI = new Pages.PickVehiclePage(
                            onPicked: name =>
                            {
                                if (string.IsNullOrEmpty(name)) WtHud.Hud.EditScope.UseGlobal();
                                else { WtHud.Hud.AirframeDb.ManualVehicle = name; WtHud.Hud.EditScope.UseVehicle(name); }
                                _dirty = true;
                                _nav.Clear();
                                _mode = Mode.EditNumber;   // 回到编辑页，显示所选数据集的值
                                Invalidate();
                            },
                            onCancel: () => { _nav.Clear(); _mode = Mode.EditNumber; },
                            layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
                        _nav.Push(_pickPageUI);
                        _mode = Mode.PickVehicle;
                    },
                    layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
                _nav.Push(_editPage);
                _mode = Mode.EditNumber;
                return;
            }
            case ItemKind.KeyBinding:
                _captureText = "";
                _mode = Mode.CaptureKey;
                return;
            case ItemKind.Detail:
                _detailTarget = item;
                _mode = Mode.Detail;
                return;
            case ItemKind.Preset:
            {
                _presetTarget = item;
                var target = item;
                _presetPage = new Pages.PresetPage(
                    currentKey: () => target.GetPreset?.Invoke(_work) ?? "green",
                    onApply: key =>
                    {
                        // 和双击走同一条路径：ApplyPreset 会同步主题与草稿
                        _presetSel = Math.Max(0, Array.FindIndex(MfdTheme.Presets, p => p.Key == key));
                        _nav.Clear();
                        ApplyPreset();
                    },
                    onCancel: () => { _nav.Clear(); _presetTarget = null; _mode = Mode.Items; },
                    layout: new MfdPageLayout(_layout, _layout.Width / 1330f));
                _presetSel = _presetPage.Selected;
                _nav.Push(_presetPage);
                _mode = Mode.Preset;
                return;
            }
            case ItemKind.ReadOnly:
                _toast = L10n.Get(LangScope.Screen, "hint.readonly");
                return;
        }
        item.Activate(_work);
        _dirty = true;
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
            var id = _detailTarget?.LabelKey ?? "";
            WtHud.Hud.AirframeDb.ResetOverrides(id);
            _resetConfirm = false;
            _dirty = true;
            _mode = Mode.Detail;
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
        _pendingAction = null;
        _toast = null;

        // 需要现场采样的动作（俯仰方向检测）确认后进入捕捉页
        if (WtHud.Hud.PitchDetect.Active)
        {
            _pitchPage = new Pages.PitchDetectPage(
                onFinished: () => { _nav.Clear(); _mode = Mode.Items; _dirty = true; },
                onCancelled: () => { _nav.Clear(); _mode = Mode.Items; });
            _nav.Push(_pitchPage);
            _pitchPage.StartRepaint(Invalidate);
            _mode = Mode.PitchDetect;
            return;
        }
        _mode = Mode.Items;
    }

    private void CancelPending()
    {
        if (_resetConfirm)
        {
            _resetConfirm = false;
            _mode = Mode.Detail;
            _toast = null;
            return;
        }

        var wasClose = _closeConfirm;
        _closeConfirm = false;
        _pendingAction = null;
        _mode = wasClose ? (CurrentPage != null ? Mode.Items : Mode.Menu) : Mode.Items;
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
        _menuPage = new MfdScreenPage("menu", "menu.title");

        for (var i = 0; i < _pages.Count; i++)
        {
            var index = i;
            var cell = i;
            _menuPage.Buttons.Add(new VirtualButton($"menu{i}", VirtualButtonKind.Action)
            {
                ParentId = "menu",
                Text = () => L10n.Get(LangScope.Screen, _pages[index].MenuKey),
                Slot = 0,                         // 位置由 Column/PageIndex 决定
                Column = 0,
                PageIndex = cell / 10,
                Target = MfdCommand.Of("menu.open", index.ToString(),
                                       InputSource.Keyboard),
                ActivateBy = new[] { $"SIDE{SideSlotOfCell(cell)}" },
            });
        }
        _menuRouter.SetButtons(_menuPage.Buttons);
    }

    /// <summary>Menu cells are filled row-major: cell -> physical side slot (0-9).</summary>
    private static int SideSlotOfCell(int cell) => cell;

    private void EnterPage()
    {
        if (_menuSel < 0 || _menuSel >= _pages.Count) return;
        _page = _menuSel;
        _mode = Mode.Items;
        _itemPage = 0;
        _itemSel = 0;
        _toast = null;
        SyncDiagProbe();
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
                LeaveHelp();
                break;
            case Mode.Detail:
                _mode = Mode.Items;
                _detailTarget = null;
                _toast = null;
                break;
            case Mode.PickVehicle:
                _pickPageUI?.Handle(MfdCommand.Of("nav.back"));
                _toast = null;
                break;
            case Mode.EditNumber:
            case Mode.CaptureKey:
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
            var sScale = _layout.Width / 1330f;
            var per = HelpLinesPerPage(_layout.Glass, sScale);
            var total = Math.Max(1, (BuildHelpLines().Count + per - 1) / per);
            var helpNext = Math.Clamp(_helpPage + delta, 0, total - 1);
            if (helpNext != _helpPage) SelectFirstEntryOnPage(helpNext, per);
            _toast = null;
            return;
        }

        if (_mode == Mode.PickVehicle)
        {
            _pickPageUI?.CyclePage(delta);
            return;
        }

        if (_mode != Mode.Menu && _mode != Mode.Items) return;

        if (_mode == Mode.Menu)
        {
            var menuPages = (_pages.Count + 9) / 10;
            if (menuPages <= 1) { _toast = L10n.Get(LangScope.Screen, "page.single"); return; }
            return;   // 目前模块不足 10 个，单页
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
            case 2: Move(-1); break;
            case 3: Move(1); break;
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
                if (_mode == Mode.Detail)
                {
                    _resetConfirm = true;
                    _closeConfirm = false;
                    _pendingAction = null;
                    _confirmSel = 0;
                    EnterConfirm();
                }
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
        if (_mode == Mode.Confirm) { CancelPending(); return; }
        if (_mode == Mode.Menu) return;

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
        _work.HideWhenInactive = fresh.HideWhenInactive;
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

    /// <summary>Preview helper: opens the help structure page.</summary>
    public void PreviewHelp(int page)
{
        EnterHelp();
        _helpPage = Math.Max(0, page);
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
        CancelAndClose();
    }

    /// <summary>Preview helper: opens the confirmation sheet for an action row.</summary>
    public void PreviewConfirm(int pageIndex, int itemIndex)
    {
        PreviewOpen(pageIndex, itemIndex);
        if (SelectedItem is { } item) Activate(item);
    }

    private void CancelAndClose()
    {
        if (!_dirty || _forceClose)
        {
            L10n.LoadFromConfig(_cfg.Language);
            Close();
            return;
        }
        _pendingAction = null;
        _closeConfirm = true;
        _confirmSel = 0;                 // 默认停在“取消”，避免误按
        EnterConfirm();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && !_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _pendingAction = null;
            _closeConfirm = true;
            _confirmSel = 0;
            _mode = Mode.Confirm;
            Invalidate();
            return;
        }
        base.OnFormClosing(e);
    }

    private void SaveAndClose()
    {
        if (_mode == Mode.Confirm) CancelPending();
        L10n.StoreToConfig(_work.Language);
        _cfg.CopyFrom(_work);
        if (_savePath != null) _cfg.SaveTo(_savePath); else _cfg.Save();

        // 告警类阈值改动同时写进当前机型条目（按机型持久化）
        if (Math.Abs(_cfg.AoaWarnDeg - _baseline.AoaWarnDeg) > 0.001
            || Math.Abs(_cfg.OverspeedKmh - _baseline.OverspeedKmh) > 0.001
            || Math.Abs(_cfg.OverspeedMach - _baseline.OverspeedMach) > 0.001)
        {
            WtHud.Hud.AirframeDb.StoreAlertOverrides(
                _cfg.AoaWarnDeg, _cfg.OverspeedKmh, _cfg.OverspeedMach);
        }

        if (WtHud.Hud.PitchDetect.PendingSave is var (veh, sign))
        {
            _cfg.PitchSignByVehicle[veh] = sign;
            WtHud.Hud.PitchDetect.ClearPendingSave();
        }

        Saved = true;
        _dirty = false;
        _forceClose = true;   // 已保存：关闭时不要再弹放弃确认
        Close();
    }

    /// <summary>Rounds the window region so the texture's black corners never show.</summary>
    private void ApplyRoundedRegion(int width, int height, float fit)
    {
        try
        {
            // 机框圆角半径约 26px（贴图 1330 宽），按缩放换算
            var r = (int)(26 * fit);
            var rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, r * 2, r * 2);
            SetWindowRgn(Handle, rgn, true);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);

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

























