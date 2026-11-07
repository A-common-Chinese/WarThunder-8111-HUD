// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Drawing;
using System.Reflection;
using System.Text.Json;

namespace WtHud.UI.Mfd;

/// <summary>
/// Geometry of the MFD bezel, expressed in normalised (0..1) image coordinates so the panel can
/// be drawn at any size. Values come from the Blender model render.
/// </summary>
internal sealed class MfdGeometry
{
    public RectangleF Glass { get; init; }
    public RectangleF Panel { get; init; }
    public RectangleF[] SideButtons { get; init; } = Array.Empty<RectangleF>();
    public RectangleF[] TopButtons { get; init; } = Array.Empty<RectangleF>();
    public RectangleF[] BottomButtons { get; init; } = Array.Empty<RectangleF>();
    public float TopLabelY { get; init; }
    public float BottomLabelY { get; init; }

    private sealed record Raw(
        float[] Glass, float[] Panel,
        float[][] SideButtons, float[][] TopButtons, float[][] BottomButtons,
        float TopLabelY, float BottomLabelY);

    public static MfdGeometry Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("mfd-geometry.json", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return Fallback();

        using var stream = asm.GetManifestResourceStream(name)!;
        var raw = JsonSerializer.Deserialize<Raw>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (raw == null)
            return Fallback();

        return new MfdGeometry
        {
            Glass = ToRect(raw.Glass),
            Panel = ToRect(raw.Panel),
            SideButtons = raw.SideButtons.Select(ToRect).ToArray(),
            TopButtons = raw.TopButtons.Select(ToRect).ToArray(),
            BottomButtons = raw.BottomButtons.Select(ToRect).ToArray(),
            TopLabelY = raw.TopLabelY,
            BottomLabelY = raw.BottomLabelY,
        };
    }

    private static RectangleF ToRect(float[] v) => new(v[0], v[1], v[2] - v[0], v[3] - v[1]);

    private static MfdGeometry Fallback() => new()
    {
        Glass = new RectangleF(0.211f, 0.229f, 0.578f, 0.541f),
        Panel = new RectangleF(0.065f, 0.065f, 0.870f, 0.870f),
        SideButtons = Array.Empty<RectangleF>(),
        TopButtons = Array.Empty<RectangleF>(),
        BottomButtons = Array.Empty<RectangleF>(),
        TopLabelY = 0.30f,
        BottomLabelY = 0.90f,
    };

    /// <summary>Scaled rectangles for a given panel size (normalised → pixels).</summary>
    public MfdLayout ScaleTo(float width, float height)
    {
        RectangleF R(RectangleF r) => new(r.X * width, r.Y * height, r.Width * width, r.Height * height);
        return new MfdLayout
        {
            Width = width,
            Height = height,
            Glass = R(Glass),
            Panel = R(Panel),
            SideButtons = SideButtons.Select(R).ToArray(),
            TopButtons = TopButtons.Select(R).ToArray(),
            BottomButtons = BottomButtons.Select(R).ToArray(),
            TopLabelY = TopLabelY * height,
            BottomLabelY = BottomLabelY * height,
        };
    }
}

internal sealed class MfdLayout
{
    public float Width { get; init; }
    public float Height { get; init; }
    public RectangleF Glass { get; init; }
    public RectangleF Panel { get; init; }
    public RectangleF[] SideButtons { get; init; } = Array.Empty<RectangleF>();
    public RectangleF[] TopButtons { get; init; } = Array.Empty<RectangleF>();
    public RectangleF[] BottomButtons { get; init; } = Array.Empty<RectangleF>();
    public float TopLabelY { get; init; }
    public float BottomLabelY { get; init; }
}
