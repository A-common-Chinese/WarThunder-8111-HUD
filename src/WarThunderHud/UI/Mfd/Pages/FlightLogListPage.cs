// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>
/// 「记录」模块的第一页：列出 exe 同目录 <c>recordings/*.jsonl</c>（最新在最前，最多 30 条）。
/// 每页 10 条（两列 × 五行，与侧键一一对应），UP/DOWN 选择、CONFIRM 交给面板跑 FlightLog 分析。
///
/// <para>只负责「列出来 + 选中 + 翻页」：找 exe、跑进程、解析 JSON 全在
/// <see cref="FlightLogBridge"/>，页面拿到的永远只是文件名/时间/大小。</para>
/// </summary>
internal sealed class FlightLogListPage : MfdPageBase
{
    private readonly Func<IReadOnlyList<FlightLogBridge.FlightLogRecording>> _readEntries;
    private readonly Action<FlightLogBridge.FlightLogRecording> _onPicked;
    private readonly Action _onCancel;
    private readonly MfdPageLayout _layout;
    private readonly string? _folder;

    private IReadOnlyList<FlightLogBridge.FlightLogRecording> _entries =
        Array.Empty<FlightLogBridge.FlightLogRecording>();

    public FlightLogListPage(Func<IReadOnlyList<FlightLogBridge.FlightLogRecording>> entries,
                             Action<FlightLogBridge.FlightLogRecording> onPicked,
                             Action onCancel,
                             MfdPageLayout layout,
                             string? folder = null) : base("flightlog:list")
    {
        _readEntries = entries;
        _onPicked = onPicked;
        _onCancel = onCancel;
        _layout = layout;
        _folder = folder;
    }

    public override string TitleKey => "flylog.title";
    public override string HintKey => "flylog.hint.list";

    /// <summary>当前页码（0 起）。</summary>
    public int PageIndex { get; private set; }

    /// <summary>选中项在整份列表里的下标（0 起，不是页内格号）。</summary>
    public int Selected { get; private set; }

    public int PageCount => Math.Max(1, (_entries.Count + 9) / 10);

    private int LastIndex => Math.Max(0, _entries.Count - 1);

    /// <summary>留给外部的提示（分析失败原因等由面板设置到页脚 toast，这里只留读取口）。</summary>
    public int Count => _entries.Count;

    /// <summary>重新扫描目录并重建 10 个格子按钮；页码/选中项夹回合法范围。</summary>
    public override void Build()
    {
        _entries = _readEntries();

        Buttons.Clear();
        for (var i = 0; i < 10; i++)
        {
            var cell = i;
            Buttons.Add(new VirtualButton($"flylog{cell}", VirtualButtonKind.Action)
            {
                Slot = cell / 2,
                Column = cell % 2,
                Text = () => TextAt(cell),
                Target = MfdCommand.Of("flylog.select", cell.ToString()),
                ActivateBy = new[] { "SIDE" + cell },
            });
        }

        PageIndex = Math.Clamp(PageIndex, 0, PageCount - 1);
        Selected = Math.Clamp(Selected, 0, LastIndex);
    }

    /// <summary>UP/DOWN：整份列表内移动，页码跟着选中项走（30 条记录也能一路选到底）。</summary>
    public void Move(int delta)
    {
        if (_entries.Count == 0) return;
        Selected = Math.Clamp(Selected + delta, 0, LastIndex);
        PageIndex = Selected / 10;
    }

    /// <summary>PAGE：翻页；返回 false 表示没有翻动（调用方给边界提示）。</summary>
    public bool CyclePage(int delta)
    {
        if (PageCount <= 1) return false;
        var next = Math.Clamp(PageIndex + delta, 0, PageCount - 1);
        if (next == PageIndex) return false;
        PageIndex = next;
        Selected = Math.Clamp(PageIndex * 10, 0, LastIndex);
        return true;
    }

    /// <summary>命中格号（0..9）；矩形与 Draw 同源。</summary>
    public int HitRow(PointF p) => _layout.TryHitListCell(p, 10, out var cell) ? cell : -1;

    public void SelectCell(int cell)
    {
        if (cell is < 0 or > 9) return;
        var index = PageIndex * 10 + cell;
        if (index >= _entries.Count) return;      // 空格子不选中
        Selected = index;
    }

    /// <summary>当前选中记录；空列表或越界返回 null。</summary>
    public FlightLogBridge.FlightLogRecording? SelectedRecording =>
        Selected >= 0 && Selected < _entries.Count ? _entries[Selected] : null;

    public override bool Handle(MfdCommand cmd)
    {
        switch (cmd.Id)
        {
            case "flylog.select":
                if (int.TryParse(cmd.Arg, out var cell)) SelectCell(cell);
                return true;
            case "confirm.ok":
                if (SelectedRecording is { } rec) _onPicked(rec);
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

        // 列表可能被外部改写（分析完成回来、目录变化）：画之前先夹紧
        PageIndex = Math.Clamp(PageIndex, 0, PageCount - 1);
        Selected = Math.Clamp(Selected, 0, LastIndex);

        if (_entries.Count == 0)
        {
            using var emptyFont = MfdTheme.CjkAt(20f * s);
            using var pathFont = MfdTheme.MonoAt(15f * s);
            using var muted = new SolidBrush(MfdTheme.Muted);
            using var center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            var inner = new RectangleF(glass.X + pad, glass.Y + pad,
                                       glass.Width - pad * 2, glass.Height - pad * 2);
            g.DrawString(L10n.Get(LangScope.Screen, "flylog.empty"), emptyFont, muted, inner, center);
            // 空列表要把「去哪里放记录」说清楚；路径可能很长，中间省略，别顶出玻璃区
            var folder = _folder ?? FlightLogBridge.DefaultRecordingsFolder;
            var maxW = glass.Width - pad * 2;
            var shown = FitText(g, pathFont, folder, maxW);
            var size = g.MeasureString(shown, pathFont);
            g.DrawString(shown, pathFont, muted,
                glass.Left + glass.Width / 2 - size.Width / 2, glass.Top + glass.Height / 2 + 26 * s,
                StringFormat.GenericTypographic);
            return;
        }

        using var time = MfdTheme.MonoAt(17f * s);
        using var sizeFont = MfdTheme.MonoAt(16f * s);
        for (var i = 0; i < 10; i++)
        {
            var index = PageIndex * 10 + i;
            if (index >= _entries.Count) break;

            var row = _layout.ListRowRect(i % 2, i / 2);
            var selected = index == Selected;
            if (selected)
            {
                using var hi = new SolidBrush(MfdTheme.Green);
                g.FillRectangle(hi, row);
            }
            using var fg = new SolidBrush(selected ? MfdTheme.Dark : MfdTheme.Text);

            var entry = _entries[index];
            g.DrawString(entry.TimeText, time, fg, row.X + 10 * s, row.Y + 9 * s,
                         StringFormat.GenericTypographic);

            var sizeText = FlightLogBridge.FormatSize(entry.Size);
            var sizeW = g.MeasureString(sizeText, sizeFont).Width;
            g.DrawString(sizeText, sizeFont, fg, row.Right - sizeW - 10 * s, row.Y + 10 * s,
                         StringFormat.GenericTypographic);
        }
    }

    /// <summary>页内第 cell 格对应的整份列表下标；空格返回 -1。</summary>
    private int IndexAt(int cell)
    {
        var index = PageIndex * 10 + cell;
        return index < _entries.Count ? index : -1;
    }

    /// <summary>格子按钮的文本（侧键路由用；屏幕上是 Draw 直接画两列细节）。</summary>
    private string TextAt(int cell)
    {
        var index = IndexAt(cell);
        return index < 0 ? "" : _entries[index].TimeText;
    }

    /// <summary>超宽文本从中间省略（路径重要的是盘符和最后一级目录）。</summary>
    private static string FitText(System.Drawing.Graphics g, System.Drawing.Font font, string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var width = g.MeasureString(text, font).Width;
        if (width <= maxWidth) return text;

        var keep = Math.Clamp((int)(text.Length * maxWidth / width * 0.92), 8, text.Length);
        var head = keep / 2;
        var tail = keep - head - 1;
        if (tail < 1) tail = 1;
        return text[..head] + "…" + text[^tail..];
    }
}
