// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd;

/// <summary>
/// One screen of the panel: its virtual buttons. Pages never touch WinForms input; input is
/// routed by <see cref="Controls.MfdInputRouter"/>.
/// </summary>
internal sealed class MfdScreenPage
{
    public List<VirtualButton> Buttons { get; } = new();
}
