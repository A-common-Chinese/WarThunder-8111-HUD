// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WtHud.Hud;

namespace WtHud.Graphics;

/// <summary>
/// Draws HUD primitives onto an arbitrary Direct2D render target.
/// Kept separate from the device/window plumbing so the same drawing code serves
/// both the offscreen PNG preview and the live overlay swapchain.
/// </summary>
public sealed class HudDrawer : IDisposable
{
    private readonly ID2D1Factory1 _factory;
    private readonly IDWriteFactory _write;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly Dictionary<(double Size, HudAlign Align), IDWriteTextFormat> _formats = new();

    public ID2D1RenderTarget Target { get; }

    public HudDrawer(ID2D1Factory1 factory, IDWriteFactory write, ID2D1RenderTarget target)
    {
        _factory = factory;
        _write = write;
        Target = target;
        Target.AntialiasMode = AntialiasMode.PerPrimitive;
        Target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        _brush = Target.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
    }

    public void RenderFrame(IReadOnlyList<HudPrim> prims, byte r, byte g, byte b, byte a)
    {
        Target.BeginDraw();
        Target.Clear(new Color4(r / 255f, g / 255f, b / 255f, a / 255f));
        foreach (var p in prims) DrawPrim(p);
        Target.EndDraw().CheckError();
    }

    private void DrawPrim(HudPrim p)
    {
        _brush.Color = new Color4(p.Color.R / 255f, p.Color.G / 255f, p.Color.B / 255f, p.Color.A / 255f);

        switch (p.Kind)
        {
            case HudPrimKind.Line:
                if (p.Points.Count >= 2)
                {
                    Target.DrawLine(
                        new System.Numerics.Vector2((float)p.Points[0].X, (float)p.Points[0].Y),
                        new System.Numerics.Vector2((float)p.Points[1].X, (float)p.Points[1].Y),
                        _brush, (float)p.Width);
                }
                break;

            case HudPrimKind.Polyline:
                DrawPolyline(p);
                break;

            case HudPrimKind.Circle:
                if (p.Points.Count >= 1)
                {
                    var c = new System.Numerics.Vector2((float)p.Points[0].X, (float)p.Points[0].Y);
                    Target.DrawEllipse(new Ellipse(c, (float)p.Radius, (float)p.Radius), _brush, (float)p.Width);
                }
                break;

            case HudPrimKind.Arc:
                DrawArc(p);
                break;

            case HudPrimKind.Text:
                DrawTextPrim(p);
                break;
        }
    }

    private void DrawPolyline(HudPrim p)
    {
        if (p.Points.Count < 2) return;
        using var geometry = _factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(new System.Numerics.Vector2((float)p.Points[0].X, (float)p.Points[0].Y), FigureBegin.Hollow);
        for (int i = 1; i < p.Points.Count; i++)
            sink.AddLine(new System.Numerics.Vector2((float)p.Points[i].X, (float)p.Points[i].Y));
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        Target.DrawGeometry(geometry, _brush, (float)p.Width);
    }

    private void DrawArc(HudPrim p)
    {
        if (p.Points.Count < 1) return;
        var center = new System.Numerics.Vector2((float)p.Points[0].X, (float)p.Points[0].Y);
        int steps = Math.Max(8, (int)(Math.Abs(p.SweepDeg) / 3.0));
        using var geometry = _factory.CreatePathGeometry();
        using var sink = geometry.Open();
        for (int i = 0; i <= steps; i++)
        {
            double a = (p.StartDeg + p.SweepDeg * i / steps) * Math.PI / 180.0;
            var pt = new System.Numerics.Vector2(
                center.X + (float)(Math.Cos(a) * p.Radius),
                center.Y + (float)(Math.Sin(a) * p.Radius));
            if (i == 0) sink.BeginFigure(pt, FigureBegin.Hollow);
            else sink.AddLine(pt);
        }
        sink.EndFigure(FigureEnd.Open);
        sink.Close();
        Target.DrawGeometry(geometry, _brush, (float)p.Width);
    }

    private void DrawTextPrim(HudPrim p)
    {
        var fmt = GetFormat(p.FontSize, p.Align);
        // Vortice.Mathematics.Rect is (x, y, width, height).
        float height = (float)(p.FontSize * 2.0);
        float width = Math.Max((float)(p.Text.Length * p.FontSize * 0.85 + 24), (float)(p.FontSize * 3));
        var origin = new System.Numerics.Vector2((float)p.Points[0].X, (float)p.Points[0].Y);
        float left = p.Align switch
        {
            HudAlign.Left => origin.X,
            HudAlign.Right => origin.X - width,
            _ => origin.X - width / 2,
        };
        Target.DrawText(p.Text, fmt, new Rect(left, origin.Y - height / 2, width, height), _brush,
            DrawTextOptions.None, MeasuringMode.Natural);
    }

    private IDWriteTextFormat GetFormat(double size, HudAlign align)
    {
        var key = (Math.Round(size, 1), align);
        if (_formats.TryGetValue(key, out var cached)) return cached;

        var fmt = _write.CreateTextFormat("Consolas", null, FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, (float)size);
        fmt.TextAlignment = align switch
        {
            HudAlign.Left => TextAlignment.Leading,
            HudAlign.Right => TextAlignment.Trailing,
            _ => TextAlignment.Center,
        };
        fmt.ParagraphAlignment = ParagraphAlignment.Center;
        fmt.WordWrapping = WordWrapping.NoWrap;
        _formats[key] = fmt;
        return fmt;
    }

    public void Dispose()
    {
        _brush.Dispose();
        foreach (var f in _formats.Values) f.Dispose();
    }
}


