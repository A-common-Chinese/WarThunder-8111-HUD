// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>
/// The single source of truth for control rectangles. Both drawing and hit-testing ask this
/// class, so a control can never be drawn somewhere it cannot be clicked.
/// </summary>
internal sealed class MfdPageLayout
{
    private readonly MfdLayout _layout;
    private readonly float _s;
    private readonly float _pad;

    public MfdPageLayout(MfdLayout layout, float scale, float pad = 18f)
    {
        _layout = layout;
        _s = scale;
        _pad = pad * scale;
    }

    public RectangleF Glass => _layout.Glass;
    internal MfdLayout Layout => _layout;
    public float Scale => _s;
    public float Pad => _pad;

    public const float RowHeightDip = 44f;

    /// <summary>Row rectangle of a two-column list page (row = side-button row 0-4).</summary>
    public RectangleF ListRowRect(int column, int row)
    {
        var colW = (Glass.Width - _pad * 2) / 2 - 34 * _s;
        var x = column == 0 ? Glass.Left + _pad : Glass.Left + Glass.Width / 2 + _pad * 0.4f;
        var btn = _layout.SideButtons[Math.Clamp(column * 5 + row, 0, _layout.SideButtons.Length - 1)];
        var h = RowHeightDip * _s;
        return new RectangleF(x, btn.Y + btn.Height / 2 - h / 2, colW, h);
    }

    /// <summary>Rectangle of a screen button, resolved from its logical slot.</summary>
    public RectangleF RectFor(VirtualButton button) => button.Kind == VirtualButtonKind.Action && button.Slot >= 20
        ? BottomRect(button.Slot - 20)
        : ListRowRect(button.Column, button.Slot);

    /// <summary>Rectangle of a bezel button (glow strip + hit area).</summary>
    public RectangleF RectFor(RealButton button)
    {
        var r = button.Group switch
        {
            RealButtonGroup.Top => _layout.TopButtons[Math.Clamp(button.Index, 0, _layout.TopButtons.Length - 1)],
            RealButtonGroup.Bottom => _layout.BottomButtons[Math.Clamp(button.Index, 0, _layout.BottomButtons.Length - 1)],
            RealButtonGroup.SideLeft => _layout.SideButtons[Math.Clamp(button.Index, 0, 4)],
            _ => _layout.SideButtons[Math.Clamp(5 + button.Index, 5, 9)],
        };
        return r;
    }

    private RectangleF BottomRect(int index)
    {
        var b = _layout.BottomButtons[Math.Clamp(index, 0, _layout.BottomButtons.Length - 1)];
        return new RectangleF(b.X - 6 * _s, b.Y, b.Width + 12 * _s, b.Height);
    }

    /// <summary>Maps a virtual button slot name (SIDE0-9 / TOP0-4 / BOT0-4) to a rectangle.</summary>
    public RectangleF RectForSlot(string slot)
    {
        if (slot.StartsWith("SIDE", StringComparison.OrdinalIgnoreCase))
            return ListRowRect(slot[4] is >= '5' ? 1 : 0, slot[4] is >= '5' ? slot[4] - '5' : slot[4] - '0');
        if (slot.StartsWith("TOP", StringComparison.OrdinalIgnoreCase))
            return RectFor(new RealButton("t", RealButtonGroup.Top, slot[3] - '0'));
        if (slot.StartsWith("BOT", StringComparison.OrdinalIgnoreCase))
            return RectFor(new RealButton("b", RealButtonGroup.Bottom, slot[3] - '0'));
        return RectangleF.Empty;
    }
}
