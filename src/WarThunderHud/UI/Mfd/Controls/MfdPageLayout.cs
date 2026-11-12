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

    /// <summary>行高（DIP）。必须与历史上真正画出来的高度一致，否则「画在哪里」和
    /// 「点在哪里」会差几个像素 —— 这里统一取 40，与模块页/主菜单原有绘制一致。</summary>
    public const float RowHeightDip = 40f;

    /// <summary>
    /// Physical side key (0-4 = left column top-down, 5-9 = right column) to the row-major cell
    /// number used by two-column list pages (0 = left row 0, 1 = right row 0, 2 = left row 1 …).
    /// </summary>
    public static int CellFromSideKey(int sideKey) => (sideKey % 5) * 2 + sideKey / 5;

    /// <summary>Inverse of <see cref="CellFromSideKey"/>.</summary>
    public static int SideKeyFromCell(int cell) => (cell % 2) * 5 + cell / 2;

    /// <summary>Row rectangle of a two-column list page (row = side-button row 0-4).</summary>
    public RectangleF ListRowRect(int column, int row)
    {
        var colW = (Glass.Width - _pad * 2) / 2 - 34 * _s;
        // 两列都按同一个内边距缩进：右列曾经写成 _pad * 0.4，比实际绘制的 18*s 窄 10.8*s，
        // 导致命中区域向左多出一块。
        var x = column == 0 ? Glass.Left + _pad : Glass.Left + Glass.Width / 2 + _pad;
        var btn = _layout.SideButtons[Math.Clamp(column * 5 + row, 0, _layout.SideButtons.Length - 1)];
        var h = RowHeightDip * _s;
        return new RectangleF(x, btn.Y + btn.Height / 2 - h / 2, colW, h);
    }

    /// <summary>
    /// 两列列表的命中测试，返回格号（列优先，与 <see cref="ListRowRect"/> 同源）。
    /// 横向外扩 6*s 是给高亮块留的量 —— 绘制时高亮也是 x-6 .. 宽+12。
    /// <para>★ 命中必须走这里。以前每个页面自己算一遍坐标，PickVehicle 的右列写成
    /// <c>+7*s</c> 而绘制是 <c>+18*s</c>，那一列右边一小块点了没反应。</para>
    /// </summary>
    public bool TryHitListCell(PointF p, int cellCount, out int cell)
    {
        cell = -1;
        if (!Glass.Contains(p)) return false;
        for (var i = 0; i < cellCount; i++)
        {
            var hit = RectangleF.Inflate(ListRowRect(i % 2, i / 2), 6 * _s, 0);
            if (hit.Contains(p)) { cell = i; return true; }
        }
        return false;
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
