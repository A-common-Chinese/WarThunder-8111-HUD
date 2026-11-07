// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd;

/// <summary>
/// One screen of the panel: a title, its virtual buttons and the commands they emit.
/// Pages never touch WinForms input; they only handle <see cref="MfdCommand"/>.
/// </summary>
internal sealed class MfdScreenPage
{
    public MfdScreenPage(string id, string titleKey)
    {
        Id = id;
        TitleKey = titleKey;
    }

    public string Id { get; }
    public string TitleKey { get; }
    public List<VirtualButton> Buttons { get; } = new();

    /// <summary>Handles a command from any control; returns true when the page consumed it.</summary>
    public bool Handle(MfdCommand cmd, Action<MfdScreenPage>? push, Action? pop)
    {
        switch (cmd.Id)
        {
            case "nav.back":
                pop?.Invoke();
                return true;
        }
        return false;
    }
}

/// <summary>Simple page stack: BACK pops, ENTER/push pushes, so navigation is never ad-hoc.</summary>
internal sealed class MfdScreenPageStack
{
    private readonly List<MfdScreenPage> _pages = new();

    public MfdScreenPage? Current => _pages.Count > 0 ? _pages[^1] : null;
    public int Count => _pages.Count;

    public void Push(MfdScreenPage page) => _pages.Add(page);

    public MfdScreenPage? Pop()
    {
        if (_pages.Count == 0) return null;
        var top = _pages[^1];
        _pages.RemoveAt(_pages.Count - 1);
        return top;
    }

    public void Clear() => _pages.Clear();
}
