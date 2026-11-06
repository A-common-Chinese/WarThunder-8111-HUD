// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Text.RegularExpressions;

namespace WtHud.Input;

/// <summary>
/// Reads a War Thunder controls export (.blk) and translates its DirectInput scan codes into the
/// binding strings this HUD uses ("H", "CTRL+N", "TAB", ...). Only the actions the HUD cares about
/// are mapped; everything else in the file is ignored.
/// </summary>
public static class WtControlsFile
{
    /// <summary>Action id in the .blk file -> config property name used by SettingsForm.</summary>
    public static readonly (string Action, string Field, string Label)[] Supported =
    {
        ("ID_AIR_BRAKE", "AirbrakeKey", "减速板"),
        ("ID_AIR_BRAKE_HELICOPTER", "AirbrakeKey", "减速板（直升机）"),
        ("ID_MPSTATSCREEN", "ScoreboardKey", "计分板"),
    };

    /// <summary>Parses action id -> scan codes from a controls .blk export.</summary>
    public static Dictionary<string, int[]> Parse(string text)
    {
        var map = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (Match block in Regex.Matches(text, @"(ID_[A-Z0-9_]+)\s*\{([^}]*)\}", RegexOptions.IgnoreCase))
        {
            string action = block.Groups[1].Value.ToUpperInvariant();
            var codes = Regex.Matches(block.Groups[2].Value, @"keyboardKey:i=(\d+)")
                             .Select(m => int.Parse(m.Groups[1].Value))
                             .ToArray();
            if (codes.Length > 0) map[action] = codes;
        }
        return map;
    }

    /// <summary>Turns DirectInput scan codes into a binding string, or null when unmappable.</summary>
    public static string? BuildBinding(IReadOnlyList<int> scanCodes)
    {
        bool ctrl = false, shift = false, alt = false;
        string? key = null;

        foreach (int code in scanCodes)
        {
            switch (code)
            {
                case 0x1D or 0x9D: ctrl = true; continue;      // left/right ctrl
                case 0x2A or 0x36: shift = true; continue;     // left/right shift
                case 0x38 or 0xB8: alt = true; continue;       // left/right alt
            }

            string? name = ScanCodeName(code);
            if (name != null && key == null) key = name;
        }

        if (key == null) return null;

        string prefix = (ctrl ? "CTRL+" : "") + (alt ? "ALT+" : "") + (shift ? "SHIFT+" : "");
        return prefix + key;
    }

    /// <summary>DirectInput scan code (SET 1) -> key name understood by <see cref="KeyBinding"/>.</summary>
    private static string? ScanCodeName(int code) => code switch
    {
        0x01 => "ESCAPE",
        0x02 => "1", 0x03 => "2", 0x04 => "3", 0x05 => "4", 0x06 => "5",
        0x07 => "6", 0x08 => "7", 0x09 => "8", 0x0A => "9", 0x0B => "0",
        0x0C => "MINUS", 0x0D => "EQUALS",
        0x0E => "BACK", 0x0F => "TAB",
        0x10 => "Q", 0x11 => "W", 0x12 => "E", 0x13 => "R", 0x14 => "T",
        0x15 => "Y", 0x16 => "U", 0x17 => "I", 0x18 => "O", 0x19 => "P",
        0x1A => "LBRACKET", 0x1B => "RBRACKET", 0x1C => "ENTER",
        0x1E => "A", 0x1F => "S", 0x20 => "D", 0x21 => "F", 0x22 => "G",
        0x23 => "H", 0x24 => "J", 0x25 => "K", 0x26 => "L",
        0x27 => "SEMICOLON", 0x28 => "APOSTROPHE", 0x29 => "GRAVE", 0x2B => "BACKSLASH",
        0x2C => "Z", 0x2D => "X", 0x2E => "C", 0x2F => "V", 0x30 => "B",
        0x31 => "N", 0x32 => "M", 0x33 => "COMMA", 0x34 => "PERIOD", 0x35 => "SLASH",
        0x37 => "NUMPADMULTIPLY",
        0x39 => "SPACE", 0x3A => "CAPSLOCK",
        0x3B => "F1", 0x3C => "F2", 0x3D => "F3", 0x3E => "F4", 0x3F => "F5",
        0x40 => "F6", 0x41 => "F7", 0x42 => "F8", 0x43 => "F9", 0x44 => "F10",
        0x57 => "F11", 0x58 => "F12",
        0x45 => "NUMLOCK", 0x46 => "SCROLL", 0x47 => "NUMPAD7", 0x48 => "NUMPAD8",
        0x49 => "NUMPAD9", 0x4A => "NUMPADSUBTRACT", 0x4B => "NUMPAD4", 0x4C => "NUMPAD5",
        0x4D => "NUMPAD6", 0x4E => "NUMPADADD", 0x4F => "NUMPAD1", 0x50 => "NUMPAD2",
        0x51 => "NUMPAD3", 0x52 => "NUMPAD0", 0x53 => "NUMPADDECIMAL",
        0x9C => "NUMPADENTER", 0x86 => "OEM102",
        0xC7 => "HOME", 0xC8 => "UP", 0xC9 => "PRIOR", 0xCB => "LEFT",
        0xCD => "RIGHT", 0xCF => "END", 0xD0 => "DOWN", 0xD1 => "NEXT",
        0xD2 => "INSERT", 0xD3 => "DELETE",
        _ => null,
    };
}
