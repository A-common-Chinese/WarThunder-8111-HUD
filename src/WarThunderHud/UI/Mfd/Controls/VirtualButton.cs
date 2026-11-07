// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>Kind of a screen button.</summary>
public enum VirtualButtonKind
{
    /// <summary>Runs its command once (confirm / cancel / open page).</summary>
    Action,
    /// <summary>Flips a boolean and stays highlighted while on.</summary>
    Toggle,
    /// <summary>One of a group; the selected one is highlighted.</summary>
    Radio,
    /// <summary>Read-only row: selectable but not executable.</summary>
    ReadOnly,
}

/// <summary>
/// A button drawn on the MFD screen. Position is expressed semantically (side slot / top / bottom /
/// list index) and resolved by the page layout, so drawing and hit-testing can never disagree.
/// </summary>
public sealed class VirtualButton : MfdControl
{
    public VirtualButton(string id, VirtualButtonKind kind = VirtualButtonKind.Action) : base(id) =>
        Kind = kind;

    public VirtualButtonKind Kind { get; }

    /// <summary>Label factory; may depend on language and current value.</summary>
    public Func<string> Text { get; init; } = () => "";

    /// <summary>Optional value shown on the right side of the row (e.g. "ON", "43°").</summary>
    public Func<string>? Value { get; init; }

    /// <summary>Which physical keys activate this button (side slots 0-9, "TOP0".."TOP4", "BOT0".."BOT4").</summary>
    public IReadOnlyList<string> ActivateBy { get; init; } = Array.Empty<string>();

    /// <summary>Logical row slot; resolved to a rectangle by the page layout.</summary>
    public int Slot { get; init; }

    /// <summary>Column of a two-column list page (0 = left, 1 = right).</summary>
    public int Column { get; init; }

    /// <summary>List page number this row lives on (0-based).</summary>
    public int PageIndex { get; init; }
}
