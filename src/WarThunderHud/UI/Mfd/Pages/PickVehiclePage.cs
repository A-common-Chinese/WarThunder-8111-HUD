// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>Paged airframe picker: ten cells per page, CONFIRM applies the highlighted entry.</summary>
internal sealed class PickVehiclePage : MfdPageBase
{
    private readonly Action<string> _onPicked;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;
    private List<string> _list = new();

    public PickVehiclePage(Action<string> onPicked, Action onCancel, MfdPageLayout layout) : base("pickvehicle")
    {
        _onPicked = onPicked;
        _onCancel = onCancel;
        _layout = layout;
    }

    public override string TitleKey => "pick.title";
    public override string HintKey => "pick.hint";

    public int PageIndex { get; private set; }
    public int Selected { get; private set; }

    public override void Build()
    {
        _list = WtHud.Hud.AirframeDb.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        _list.Insert(0, "");        // 空字符串 = 全局（第一页第一项）
        Buttons.Clear();
        for (var i = 0; i < 10; i++)
        {
            var cell = i;
            // 与 Draw 的 ListRowRect(i % 2, i / 2) 必须一致：Slot 是行、Column 是列。
            // 以前写成 Slot = cell % 5 / Column = cell / 5，矩形会落到另一列，
            // 因为没人读这些按钮所以一直没被发现。
            Buttons.Add(new VirtualButton($"pick{cell}", VirtualButtonKind.Action)
            {
                Slot = cell / 2,
                Column = cell % 2,
                Text = () => cell < _list.Count ? _list[cell] : "",
                Target = MfdCommand.Of("pick.select", cell.ToString()),
                ActivateBy = new[] { "SIDE" + cell },
            });
        }
    }

    public void Move(int delta) => Selected = Math.Clamp(Selected + delta, 0, Math.Max(0, _list.Count - 1));

    /// <summary>
    /// 命中格号。★ 必须走 ListRowRect —— 以前这里用自算的矩形，右列起点比绘制早 11*s，
    /// 那一列右侧一小块点了没反应。
    /// </summary>
    public int HitRow(PointF p)
    {
        // 只认本页真正有内容的格子：末页下面几行是空的，点了不该有反应
        int count = Math.Clamp(_list.Count - PageIndex * 10, 0, 10);
        if (count == 0) return -1;
        return _layout.TryHitListCell(p, count, out var cell) ? cell : -1;
    }

    public void SelectCell(int cell)
    {
        var index = PageIndex * 10 + cell;
        if (index < _list.Count) Selected = index;
    }

    public void CyclePage(int delta)
    {
        var pages = Math.Max(1, (_list.Count + 9) / 10);
        PageIndex = Math.Clamp(PageIndex + delta, 0, pages - 1);
        Selected = Math.Min(Selected, Math.Max(0, _list.Count - 1));
    }

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "pick.select":
                if (int.TryParse(cmd.Arg, out var cell)) SelectCell(cell);
                return true;
            case "confirm.ok":
                if (Selected >= 0 && Selected < _list.Count) _onPicked(_list[Selected]);
                return true;
            case "nav.back":
                _onCancel();
                return true;
        }
        return false;
    }

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        using var head = MfdTheme.CjkAt(20f * s);
        using var body = MfdTheme.MonoAt(19f * s);
        using var text = new SolidBrush(MfdTheme.Text);
        using var accent = new SolidBrush(MfdTheme.Green);

        var pages = Math.Max(1, (_list.Count + 9) / 10);
        PageIndex = Math.Clamp(PageIndex, 0, pages - 1);

        for (var i = 0; i < 10; i++)
        {
            var index = PageIndex * 10 + i;
            if (index >= _list.Count) break;

            var row = _layout.ListRowRect(i % 2, i / 2);
            var selected = index == Selected;
            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, row);
            }
            using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
            var label = string.IsNullOrEmpty(_list[index])
                ? L10n.Get(LangScope.Screen, "scope.global")
                : _list[index];
            g.DrawString(label, body, fg, row.X + 10 * s, row.Y + 6 * s,
                         StringFormat.GenericTypographic);
        }
    }
}
