// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd.Controls;

/// <summary>Shared behaviour of every clickable element on the panel.</summary>
public abstract class MfdControl
{
    protected MfdControl(string id) => Id = id;

    public string Id { get; }

    /// <summary>Owning page; used for scoping commands and for the back stack.</summary>
    public string? ParentId { get; init; }

    /// <summary>Command fired when the control is activated.</summary>
    public MfdCommand? Target { get; init; }

    public Func<bool>? Enabled { get; init; }
    public Func<bool>? Visible { get; init; }
    public Func<bool>? Selected { get; init; }

    /// <summary>Transient press visual; never persisted (this is what caused stuck buttons before).</summary>
    public bool Pressed { get; set; }

    public bool IsEnabled => Enabled?.Invoke() ?? true;
    public bool IsVisible => Visible?.Invoke() ?? true;
    public bool IsSelected => Selected?.Invoke() ?? false;

    /// <summary>Builds the command this control would emit right now (null when disabled).</summary>
    public MfdCommand? Command(InputSource source) =>
        IsEnabled ? (Target is null ? null : Target with { Source = source }) : null;
}
