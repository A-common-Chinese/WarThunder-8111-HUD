// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Drawing.Drawing2D;

namespace WtHud.UI.Mfd;

/// <summary>Colors, fonts and small drawing helpers shared by the MFD panel.</summary>
internal static class MfdTheme
{
    // 高亮色（原绿色部分）与正文色，可在面板里改
    public static Color Green { get; private set; } = Color.FromArgb(124, 255, 154);
    public static Color GreenDim { get; private set; } = Color.FromArgb(96, 200, 128);
    public static Color Text { get; private set; } = Color.FromArgb(226, 240, 230);
    public static Color Ink { get; private set; } = Color.FromArgb(150, 170, 154);
    public static readonly Color Muted = Color.FromArgb(140, 168, 148);
    public static readonly Color InkDim = Color.FromArgb(116, 136, 120);
    public static readonly Color ScreenBg = Color.FromArgb(5, 12, 8);
    public static readonly Color Grid = Color.FromArgb(9, 20, 13);
    public static readonly Color Divider = Color.FromArgb(34, 70, 46);
    public static readonly Color Dark = Color.FromArgb(8, 26, 14);
    public static readonly Color Backdrop = Color.Black;


    /// <summary>可选颜色预设：名称 → (正文色, 高亮色)。</summary>
    public static readonly (string Key, Color Ink, Color Accent)[] Presets =
    {
        ("white",  Color.FromArgb(238, 244, 240), Color.FromArgb(255, 255, 255)),
        ("green",  Color.FromArgb(226, 240, 230), Color.FromArgb(124, 255, 154)),
        ("purple", Color.FromArgb(226, 214, 244), Color.FromArgb(198, 140, 255)),
        ("blue",   Color.FromArgb(214, 232, 250), Color.FromArgb(120, 190, 255)),
        ("red",    Color.FromArgb(250, 224, 224), Color.FromArgb(255, 120, 120)),
    };

    /// <summary>Applies the configured text (ink) and highlight (accent) colors.</summary>
    public static void ApplyColors(string inkKey, string accentKey)
    {
        var ink = ColorFor(inkKey);
        var accent = ColorFor(accentKey);
        Text = ink;
        Ink = Blend(ink, 0.72f);
        Green = accent;
        GreenDim = Blend(accent, 0.78f);
    }

    private static Color ColorFor(string key)
    {
        foreach (var p in Presets)
        {
            if (p.Key == key) return p.Accent;      // 用该预设的高亮色作为代表色
        }
        return Presets[1].Accent;
    }

    private static Color Blend(Color c, float k) =>
        Color.FromArgb(
            (int)(c.R * k), (int)(c.G * k), (int)(c.B * k));

    public static readonly Font Mono;
    public static readonly Font MonoBold;
    public static readonly Font Cjk;
    public static readonly Font CjkBold;

    static MfdTheme()
    {
        Mono = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        MonoBold = new Font("Consolas", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        Cjk = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        CjkBold = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    public static Font MonoAt(float px) => new("Consolas", px, FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font MonoBoldAt(float px) => new("Consolas", px, FontStyle.Bold, GraphicsUnit.Pixel);
    public static Font CjkAt(float px) => new("Microsoft YaHei", px, FontStyle.Regular, GraphicsUnit.Pixel);

    public static void Smooth(System.Drawing.Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static RectangleF Scale(RectangleF r, float scale) =>
        new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);
}



