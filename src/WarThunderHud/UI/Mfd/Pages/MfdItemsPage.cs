// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.Hud;
using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 一级模块页：两列 × 五行、每页 10 格、可翻页的行列表。
///
/// 这些活以前散在 <c>MfdFrameForm</c> 里四处：<c>DrawItems</c> 画、<c>RowHitTest</c> 点、
/// <c>SidePressed</c> 选、<c>_itemSel</c>/<c>_itemPage</c> 存状态，改一处要同步另外三处。
/// 现在绘制、命中、选中、翻页都归这里，行矩形一律取自 <see cref="MfdPageLayout.ListRowRect"/>。
///
/// 页面只负责「选中哪一行」。「按下确认之后做什么」（开二级页、弹二次确认、就地切值）
/// 仍由表单的 <c>Activate</c> 决定 —— 那部分要碰导航和其它页面，不该塞进来。
/// </summary>
internal sealed class MfdItemsPage : MfdPageBase
{
    private readonly MfdPage _data;
    private readonly Func<HudConfig> _cfg;
    private readonly MfdPageLayout _layout;
    private readonly Action<MfdItem> _onActivate;

    public MfdItemsPage(MfdPage data, Func<HudConfig> cfg, MfdPageLayout layout, Action<MfdItem> onActivate)
        : base("items:" + data.TitleKey)
    {
        _data = data;
        _cfg = cfg;
        _layout = layout;
        _onActivate = onActivate;
    }

    public MfdPage Data => _data;
    public override string TitleKey => _data.TitleKey;
    public override string HintKey => "hint.item";

    /// <summary>模块内的页号（每页 10 格，0 起）。</summary>
    public int PageIndex { get; private set; }

    /// <summary>模块内的绝对选中序号 —— 不是格号（第 2 页第 1 格 = 10）。</summary>
    public int Selected { get; private set; }

    public int PageCount => (_data.Items.Count + 9) / 10;

    public MfdItem? SelectedItem =>
        Selected >= 0 && Selected < _data.Items.Count ? _data.Items[Selected] : null;

    /// <summary>进入模块时回到第一页第一行（历史行为：每次进模块都从头开始）。</summary>
    public void Reset()
    {
        PageIndex = 0;
        Selected = 0;
        Build();
    }

    /// <summary>按绝对序号定位（帮助页返回、预览钩子用）。</summary>
    public void GoTo(int pageIndex, int absoluteItem)
    {
        PageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, PageCount - 1));
        Selected = Math.Clamp(absoluteItem, 0, Math.Max(0, _data.Items.Count - 1));
        Build();
    }

    /// <summary>
    /// 鼠标直接点某格：不过越界检查，保留历史行为 —— 最后一页不满时点空格会高亮一行 "—"，
    /// 此时按确认没有反应（<see cref="SelectedItem"/> 为 null）。侧键走 <see cref="Handle"/>，
    /// 那边是带检查的。
    /// </summary>
    public void SelectRaw(int absoluteIndex) => Selected = absoluteIndex;

    /// <summary>UP/DOWN：在本页 10 格内循环。</summary>
    public void Move(int delta)
    {
        var baseIdx = PageIndex * 10;
        Selected = baseIdx + ((Selected - baseIdx + delta + 10) % 10);
    }

    /// <summary>LEFT/RIGHT：把值加/减一档（只对能就地改的项生效）。</summary>
    public void Step(int direction) => SelectedItem?.Activate(_cfg(), direction);

    /// <summary>翻页。返回 false 表示没有下一页/已到边界，由调用方给提示。</summary>
    public bool CyclePage(int delta)
    {
        if (PageCount <= 1) return false;
        var next = Math.Clamp(PageIndex + delta, 0, PageCount - 1);
        if (next == PageIndex) return false;
        PageIndex = next;
        Selected = next * 10;
        Build();                    // 换页 = 换一批格子，按钮也要跟着换
        return true;
    }

    /// <summary>屏幕 10 格的命中测试，返回格号（0..9）；没命中的 -1。</summary>
    public int HitCell(PointF p)
    {
        var s = _layout.Scale;
        if (!_layout.Glass.Contains(p)) return -1;
        for (var i = 0; i < 10; i++)
        {
            // 横向外扩 6*s 给高亮块留量（绘制时也是 x-6 .. 宽+12）
            var hit = RectangleF.Inflate(_layout.ListRowRect(i % 2, i / 2), 6 * s, 0);
            if (hit.Contains(p)) return i;
        }
        return -1;
    }

    /// <summary>按下确认：交给表单去开页/改值。</summary>
    public void ActivateSelected()
    {
        if (SelectedItem is { } item) _onActivate(item);
    }

    /// <summary>只造当前页的 10 个格子按钮 —— 换页就重建，所以不需要按页号过滤。</summary>
    public override void Build()
    {
        Buttons.Clear();
        var offset = PageIndex * 10;
        for (var cell = 0; cell < 10; cell++)
        {
            var idx = offset + cell;
            if (idx >= _data.Items.Count) continue;
            var item = _data.Items[idx];
            Buttons.Add(new VirtualButton("item" + idx, VirtualButtonKind.Action)
            {
                Slot = cell / 2,                    // 行
                Column = cell % 2,                  // 列
                Text = () => L10n.Get(LangScope.Screen, item.LabelKey),
                Target = MfdCommand.Of("items.select", idx.ToString(), InputSource.RealButton),
                ActivateBy = new[] { "SIDE" + cell },
            });
        }
    }

    public override bool Handle(MfdCommand cmd)
    {
        if (cmd.Id != "items.select") return false;
        if (!int.TryParse(cmd.Arg, out var idx)) return false;
        if (idx < 0 || idx >= _data.Items.Count) return false;
        Selected = idx;
        return true;
    }

    public override void Draw(MfdRenderContext ctx)
    {
        var g = ctx.G;
        var s = ctx.Scale;
        var offset = PageIndex * 10;

        using var label = MfdTheme.CjkAt(24f * s);
        using var value = MfdTheme.MonoBoldAt(22f * s);
        var cfg = _cfg();

        for (var i = 0; i < 10; i++)
        {
            var row = _layout.ListRowRect(i % 2, i / 2);
            var x = row.X;
            var y = row.Y;
            var rowH = row.Height;
            var colW = row.Width;
            var selected = (offset + i) == Selected;

            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, x - 6 * s, y, colW + 12 * s, rowH);
            }
            var fg = selected ? MfdTheme.Dark : MfdTheme.Text;
            using var fgBrush = new SolidBrush(fg);

            var itemIndex = offset + i;
            if (itemIndex < _data.Items.Count)
            {
                var item = _data.Items[itemIndex];
                if (i % 2 == 0)
                {
                    using var mark = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Green);
                    g.FillRectangle(mark, x, y + rowH / 2 - 5 * s, 9 * s, 9 * s);
                }
                g.DrawString(L10n.Get(LangScope.Screen, item.LabelKey), label, fgBrush,
                             x + 16 * s, y + 4 * s, StringFormat.GenericTypographic);
                var val = item.ValueText(cfg);
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
}
