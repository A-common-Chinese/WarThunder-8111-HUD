// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>Physical button group on the bezel.</summary>
public enum RealButtonGroup { SideLeft, SideRight, Top, Bottom }

/// <summary>
/// One of the 25 physical keys on the bezel. It has no persistent selection state: only the
/// momentary <see cref="MfdControl.Pressed"/> flag, so a press can never get stuck.
/// </summary>
public sealed class RealButton : MfdControl
{
    public RealButton(string id, RealButtonGroup group, int index) : base(id)
    {
        Group = group;
        Index = index;
    }

    public RealButtonGroup Group { get; }

    /// <summary>Index inside its group: side keys 0-4 (top to bottom), top/bottom keys 0-4.</summary>
    public int Index { get; }

    /// <summary>Side slot number used by <see cref="VirtualButton.ActivateBy"/> (0-9), or -1.</summary>
    public int SideSlot => Group switch
    {
        RealButtonGroup.SideLeft => Index,
        RealButtonGroup.SideRight => 5 + Index,
        _ => -1,
    };

    /// <summary>Slot name used by ActivateBy (e.g. "TOP0", "BOT3", "SIDE7").</summary>
    public string SlotName => Group switch
    {
        RealButtonGroup.Top => $"TOP{Index}",
        RealButtonGroup.Bottom => $"BOT{Index}",
        RealButtonGroup.SideLeft => $"SIDE{Index}",
        _ => $"SIDE{5 + Index}",
    };
}
