using System.Runtime.InteropServices;

namespace WtHud.Input;

/// <summary>
/// A single key binding with optional CTRL/SHIFT/ALT prefix, parsed from strings like
/// "V", "Z", "TAB", "CTRL+N", "SHIFT+TAB", "ALT+F1".
/// Bindings are polled passively with GetAsyncKeyState, so the game still receives every key.
/// </summary>
public readonly struct KeyBinding
{
    public uint VirtualKey { get; }
    public bool Ctrl { get; }
    public bool Shift { get; }
    public bool Alt { get; }
    public string Text { get; }

    private KeyBinding(uint vk, bool ctrl, bool shift, bool alt, string text)
    {
        VirtualKey = vk;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
        Text = text;
    }

    public static KeyBinding Parse(string? text, uint fallbackVk, string fallbackText)
    {
        if (string.IsNullOrWhiteSpace(text)) return new KeyBinding(fallbackVk, false, false, false, fallbackText);

        string raw = text.Trim().ToUpperInvariant().Replace(" ", "");
        bool ctrl = false, shift = false, alt = false;
        while (true)
        {
            if (raw.StartsWith("CTRL+") || raw.StartsWith("CONTROL+")) { ctrl = true; raw = raw[(raw.IndexOf('+') + 1)..]; }
            else if (raw.StartsWith("SHIFT+")) { shift = true; raw = raw[6..]; }
            else if (raw.StartsWith("ALT+")) { alt = true; raw = raw[4..]; }
            else break;
        }

        uint vk = NameToVk(raw);
        if (vk == 0)
        {
            vk = fallbackVk;
            raw = fallbackText;
        }
        return new KeyBinding(vk, ctrl, shift, alt, text.Trim().ToUpperInvariant());
    }

    public static uint NameToVk(string name)
    {
        if (name.Length == 1)
        {
            char c = name[0];
            if (c >= 'A' && c <= 'Z') return (uint)c;
            if (c >= '0' && c <= '9') return (uint)c;
        }

        if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name[1..], out int f) && f >= 1 && f <= 12)
            return (uint)(0x70 + f - 1);

        return name switch
        {
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "HOME" => 0x24,
            "END" => 0x23,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "BACKSPACE" => 0x08,
            "LSHIFT" or "SHIFT" => 0x10,
            "LCTRL" or "CTRL" => 0x11,
            "LALT" or "ALT" => 0x12,
            _ => 0,
        };
    }

    private static bool Down(uint vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool PressedSince(uint vk) => (GetAsyncKeyState(vk) & 0x0001) != 0;

    public bool IsDown()
    {
        if (VirtualKey == 0 || !Down(VirtualKey)) return false;
        if (Ctrl != (Down(0x11) || Down(0xA2) || Down(0xA3))) return false;
        if (Shift != (Down(0x10) || Down(0xA0) || Down(0xA1))) return false;
        if (Alt != (Down(0x12) || Down(0xA4) || Down(0xA5))) return false;
        return true;
    }

    /// <summary>True if the key was pressed since the previous call (catches very short taps).</summary>
    public bool PressedSinceLastPoll() => VirtualKey != 0 && PressedSince(VirtualKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(uint vKey);
}
