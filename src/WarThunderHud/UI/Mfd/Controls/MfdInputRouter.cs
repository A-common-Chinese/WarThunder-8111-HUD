// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>
/// Turns raw input (mouse, bezel keys, arrow keys) into commands. It owns the press visuals,
/// the double-click detection and the same-frame de-duplication, so pages stay pure data.
/// </summary>
internal sealed class MfdInputRouter
{
    private readonly MfdPageLayout _layout;
    private readonly List<VirtualButton> _buttons = new();
    private readonly List<RealButton> _realButtons;

    private int _lastHit = -1;
    private DateTime _lastHitUtc = DateTime.MinValue;
    private string? _lastCommand;
    private DateTime _lastCommandUtc = DateTime.MinValue;

    public MfdInputRouter(MfdPageLayout layout, IEnumerable<RealButton>? realButtons = null)
    {
        _layout = layout;
        _realButtons = realButtons?.ToList() ?? MfdControlFactory.CreateBezelButtons();
    }

    /// <summary>Index of the selected virtual button (page-level highlight).</summary>
    public int SelectedIndex { get; private set; }

    public IReadOnlyList<VirtualButton> Buttons => _buttons;

    public void SetButtons(IEnumerable<VirtualButton> buttons)
    {
        _buttons.Clear();
        _buttons.AddRange(buttons.Where(b => b.IsVisible));
        if (SelectedIndex >= _buttons.Count) SelectedIndex = Math.Max(0, _buttons.Count - 1);
    }

    public void Move(int delta)
    {
        if (_buttons.Count == 0) return;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, _buttons.Count - 1);
    }

    /// <summary>按索引直接选中（主菜单按全局模块索引同步，跨页时不会与 Move 的边界钳制打架）。</summary>
    public void Select(int index)
    {
        if (_buttons.Count == 0) return;
        SelectedIndex = Math.Clamp(index, 0, _buttons.Count - 1);
    }

    /// <summary>Bezel key pressed: activates the button that listens to this slot.</summary>
    /// <param name="pageIndex">-1 = 不按页过滤；主菜单传当前菜单页号 —— 不同页的按钮共用同一批侧键。</param>
    public MfdCommand? PressSlot(string slotName, InputSource source = InputSource.RealButton, int pageIndex = -1)
    {
        var real = _realButtons.FirstOrDefault(b => b.SlotName == slotName);
        if (real != null) real.Pressed = true;

        foreach (var b in _buttons)
        {
            if (pageIndex >= 0 && b.PageIndex != pageIndex) continue;
            if (!b.ActivateBy.Any(x => string.Equals(x, slotName, StringComparison.OrdinalIgnoreCase))) continue;
            SelectedIndex = Math.Max(0, _buttons.IndexOf(b));
            return Emit(b, source);
        }

        // 没有按钮监听该键：当作“选择”使用（侧键）
        if (slotName.StartsWith("SIDE", StringComparison.OrdinalIgnoreCase))
        {
            var cell = int.Parse(slotName[4].ToString());
            var idx = _buttons.FindIndex(b => b.Kind != VirtualButtonKind.Action && b.Slot == cell);
            if (idx >= 0) SelectedIndex = idx;
        }
        return null;
    }

    public void ReleaseSlot(string slotName)
    {
        var real = _realButtons.FirstOrDefault(b => b.SlotName == slotName);
        if (real != null) real.Pressed = false;
    }

    /// <summary>CONFIRM / Enter: activate the selected row.</summary>
    public MfdCommand? ActivateSelected() =>
        SelectedIndex >= 0 && SelectedIndex < _buttons.Count
            ? Emit(_buttons[SelectedIndex], InputSource.Keyboard)
            : null;

    /// <summary>Mouse press inside the glass: select, double-click to activate.</summary>
    /// <param name="pageIndex">-1 = 不按页过滤；主菜单传当前菜单页号，否则会命中另一页同位置的按钮。</param>
    public MfdCommand? MouseDown(PointF p, bool doubleClick, int pageIndex = -1)
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (pageIndex >= 0 && _buttons[i].PageIndex != pageIndex) continue;
            if (!_layout.RectFor(_buttons[i]).Contains(p)) continue;

            var now = DateTime.UtcNow;
            var again = i == _lastHit && (now - _lastHitUtc).TotalMilliseconds < 500;
            _lastHit = i;
            _lastHitUtc = now;
            SelectedIndex = i;

            _buttons[i].Pressed = true;
            if (again || doubleClick) return Emit(_buttons[i], InputSource.Mouse);
            return null;
        }
        return null;
    }

    public void MouseUp()
    {
        foreach (var b in _buttons) b.Pressed = false;
    }

    /// <summary>Screen position of the selected button (for drawing the highlight).</summary>
    public RectangleF? SelectedRect() =>
        SelectedIndex >= 0 && SelectedIndex < _buttons.Count ? _layout.RectFor(_buttons[SelectedIndex]) : null;

    /// <summary>Drops duplicate commands fired in the same instant by different input paths.</summary>
    private MfdCommand? Emit(VirtualButton button, InputSource source)
    {
        var cmd = button.Command(source);
        if (cmd == null) return null;

        var now = DateTime.UtcNow;
        if (cmd.Id == _lastCommand && (now - _lastCommandUtc).TotalMilliseconds < 40) return null;

        _lastCommand = cmd.Id;
        _lastCommandUtc = now;
        return cmd;
    }
}
