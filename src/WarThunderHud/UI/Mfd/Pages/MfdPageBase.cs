// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

using WtHud.UI.Mfd.Controls;

namespace WtHud.UI.Mfd.Pages;

/// <summary>Everything a page needs to paint itself.</summary>
internal sealed class MfdRenderContext
{
    public required System.Drawing.Graphics G { get; init; }
    public required float Scale { get; init; }
    public required System.Drawing.RectangleF Glass { get; init; }
    public required float Pad { get; init; }
}

/// <summary>
/// One screen of the panel. Pages own their layout, their commands and their painting, so adding a
/// page never means editing another switch statement.
/// </summary>
internal abstract class MfdPageBase
{
    protected MfdPageBase(string id) => Id = id;

    public string Id { get; }
    public abstract string TitleKey { get; }
    public abstract string HintKey { get; }

    /// <summary>Virtual buttons, one per side key (index = side key slot).</summary>
    public List<VirtualButton> Buttons { get; } = new();

    /// <summary>(Re)builds the buttons from current state.</summary>
    public virtual void Build() { }

    /// <summary>Handles a command; returns true when the page consumed it.</summary>
    public abstract bool Handle(MfdCommand cmd);

    public abstract void Draw(MfdRenderContext ctx);
}

/// <summary>Page stack: BACK pops, actions push. Keeps navigation out of the form.</summary>
internal sealed class MfdNavigator
{
    private readonly List<MfdPageBase> _stack = new();

    public MfdPageBase? Current => _stack.Count > 0 ? _stack[^1] : null;
    public int Depth => _stack.Count;
    public bool IsEmpty => _stack.Count == 0;

    public void Push(MfdPageBase page)
    {
        page.Build();
        _stack.Add(page);
    }

    public MfdPageBase? Pop()
    {
        if (_stack.Count == 0) return null;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return top;
    }

    public void Clear() => _stack.Clear();
}
