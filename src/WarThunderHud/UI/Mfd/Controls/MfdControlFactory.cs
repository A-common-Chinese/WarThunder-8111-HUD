// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>Creates the standard set of 25 bezel keys once, so slots never drift.</summary>
public static class MfdControlFactory
{
    public static List<RealButton> CreateBezelButtons()
    {
        var list = new List<RealButton>(25);
        for (var i = 0; i < 5; i++) list.Add(new RealButton($"sideL{i}", RealButtonGroup.SideLeft, i));
        for (var i = 0; i < 5; i++) list.Add(new RealButton($"sideR{i}", RealButtonGroup.SideRight, i));
        for (var i = 0; i < 5; i++) list.Add(new RealButton($"top{i}", RealButtonGroup.Top, i));
        for (var i = 0; i < 5; i++) list.Add(new RealButton($"bot{i}", RealButtonGroup.Bottom, i));
        return list;
    }

    /// <summary>Finds the real button a virtual button listens to.</summary>
    public static RealButton? Match(IEnumerable<RealButton> buttons, VirtualButton target, string slot) =>
        buttons.FirstOrDefault(b => b.SlotName == slot);
}
