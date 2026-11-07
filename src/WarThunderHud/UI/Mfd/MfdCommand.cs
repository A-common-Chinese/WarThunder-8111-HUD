// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.UI.Mfd;

/// <summary>Where a command came from, so the router can de-duplicate within one frame.</summary>
public enum InputSource { Mouse, Keyboard, RealButton, System }

/// <summary>
/// Every control emits a command instead of calling page methods directly. A page handles the
/// commands it knows; unknown ones bubble up to the page stack (BACK / CLOSE).
/// </summary>
public sealed record MfdCommand(string Id, string? Arg = null, InputSource Source = InputSource.System)
{
    public static MfdCommand Of(string id, string? arg = null, InputSource src = InputSource.System) =>
        new(id, arg, src);
}
