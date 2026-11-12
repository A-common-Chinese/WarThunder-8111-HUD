// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 回归快照列表：每页 10 条（2 列 × 5 行），CONFIRM 选中项进入二次确认。
/// 页面只负责“列出来 + 选中 + 翻页”，真正恢复由面板经 onPicked 回调完成。
/// </summary>
internal sealed class SnapshotsPage : MfdPageBase
{
    private readonly Func<IReadOnlyList<WtHud.Hud.ConfigSnapshots.SnapshotEntry>> _readEntries;
    private readonly Action<WtHud.Hud.ConfigSnapshots.SnapshotEntry> _onPicked;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;
    private IReadOnlyList<WtHud.Hud.ConfigSnapshots.SnapshotEntry> _entries =
        Array.Empty<WtHud.Hud.ConfigSnapshots.SnapshotEntry>();

    public SnapshotsPage(Func<IReadOnlyList<WtHud.Hud.ConfigSnapshots.SnapshotEntry>> entries,
                         Action<WtHud.Hud.ConfigSnapshots.SnapshotEntry> onPicked,
                         Action onCancel,
                         MfdPageLayout layout) : base("snapshots")
    {
        _readEntries = entries;
        _onPicked = onPicked;
        _onCancel = onCancel;
        _layout = layout;
    }

    public override string TitleKey => "snap.title";
    public override string HintKey => "snap.hint";

    /// <summary>当前页码（0 起）。</summary>
    public int PageIndex { get; private set; }

    /// <summary>选中项在整份列表里的下标（0 起，不是页内格号）。</summary>
    public int Selected { get; private set; }

    /// <summary>总页数；空列表也算一页，避免除零与负数夹紧。</summary>
    private int Pages => Math.Max(1, (_entries.Count + 9) / 10);

    /// <summary>最后一个合法下标；空列表时为 0（Selected 仍吸附在 0）。</summary>
    private int LastIndex => Math.Max(0, _entries.Count - 1);

    /// <summary>重新读取快照列表，重建 10 个侧键按钮，然后把页码/选中项夹紧到合法范围。</summary>
    public override void Build()
    {
        _entries = _readEntries();

        Buttons.Clear();
        for (var i = 0; i < 10; i++)
        {
            var cell = i;
            Buttons.Add(new VirtualButton($"snap{cell}", VirtualButtonKind.Action)
            {
                // 与 Draw 的 ListRowRect(i % 2, i / 2) 一致：Slot 是行、Column 是列。
                // （原来写成 Slot = cell % 5 / Column = cell / 5，矩形会落到另一列。）
                Slot = cell / 2,
                Column = cell % 2,
                Text = () => DisplayAt(cell),
                Target = MfdCommand.Of("snap.select", cell.ToString()),
                ActivateBy = new[] { "SIDE" + cell },
            });
        }

        PageIndex = Math.Clamp(PageIndex, 0, Pages - 1);
        Selected = Math.Clamp(Selected, 0, LastIndex);
    }

    /// <summary>上下移动选中项；越界即停（不跨页），空列表保持 0。</summary>
    public void Move(int delta) => Selected = Math.Clamp(Selected + delta, 0, LastIndex);

    /// <summary>侧键/点击选中某一格（0-9）；空单元格或越界忽略。</summary>
    /// <summary>命中格号（0..9）；矩形与 Draw 同源。</summary>
    public int HitRow(PointF p) => _layout.TryHitListCell(p, 10, out var cell) ? cell : -1;

    public void SelectCell(int cell)
    {
        if (cell is < 0 or > 9) return;
        var index = PageIndex * 10 + cell;
        if (index >= _entries.Count) return;      // 空单元格：不选中
        Selected = index;
    }

    /// <summary>翻页：页码夹紧到 [0, 页数-1]，选中项保持原值但同样夹紧。</summary>
    public void CyclePage(int delta)
    {
        PageIndex = Math.Clamp(PageIndex + delta, 0, Pages - 1);
        Selected = Math.Clamp(Selected, 0, LastIndex);
    }

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "snap.select":
                if (int.TryParse(cmd.Arg, out var cell)) SelectCell(cell);
                return true;
            case "confirm.ok":
                var entry = Current;
                if (entry is not null) _onPicked(entry);    // 空列表不触发回调
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
        var glass = ctx.Glass;
        var pad = ctx.Pad;

        // 快照列表可能被外部改写：画之前先夹紧，避免页码/选中项指向不存在的条目
        PageIndex = Math.Clamp(PageIndex, 0, Pages - 1);
        Selected = Math.Clamp(Selected, 0, LastIndex);

        if (_entries.Count == 0)
        {
            // 空列表：玻璃区中央一句提示，不画任何格子
            using var emptyFont = MfdTheme.CjkAt(20f * s);
            using var muted = new SolidBrush(MfdTheme.Muted);
            using var center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            var inner = new RectangleF(glass.X + pad, glass.Y + pad,
                                       glass.Width - pad * 2, glass.Height - pad * 2);
            g.DrawString(L10n.Get(LangScope.Screen, "snap.empty"), emptyFont, muted, inner, center);
            return;
        }

        using var body = MfdTheme.MonoAt(19f * s);
        for (var i = 0; i < 10; i++)
        {
            var index = PageIndex * 10 + i;
            if (index >= _entries.Count) break;      // 空单元格不画

            var row = _layout.ListRowRect(i % 2, i / 2);
            var selected = index == Selected;
            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, row);
            }
            using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);
            g.DrawString(_entries[index].Display, body, fg, row.X + 10 * s, row.Y + 6 * s,
                         StringFormat.GenericTypographic);
        }
    }

    /// <summary>当前选中条目；空列表或下标越界时返回 null。</summary>
    private WtHud.Hud.ConfigSnapshots.SnapshotEntry? Current =>
        Selected >= 0 && Selected < _entries.Count ? _entries[Selected] : null;

    /// <summary>页内第 cell 格的显示文本（页内格号 → 整份列表下标；空格返回空串）。</summary>
    private string DisplayAt(int cell)
    {
        var index = PageIndex * 10 + cell;
        return index >= 0 && index < _entries.Count ? _entries[index].Display : "";
    }
}
