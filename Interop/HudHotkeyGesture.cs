using Avalonia.Input;

namespace EndfieldChargePlus.Interop;

internal readonly record struct HudHotkeyGesture(uint Modifiers, uint VirtualKey, string Text)
{
    internal static bool TryParse(string? text, out HudHotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint flag = parts[i].ToUpperInvariant() switch { "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        if ((modifiers & 3) == 0) return false;
        string key = parts[^1].ToUpperInvariant();
        uint vk = key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') ? key[0] : 0u;
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 24 && function != 12)
            vk = (uint)(0x70 + function - 1);
        if (vk == 0) return false;
        string canonical = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "")
            + ((modifiers & 4) != 0 ? "Shift+" : "") + (vk >= 0x70 ? $"F{vk - 0x70 + 1}" : key);
        gesture = new(modifiers, vk, canonical);
        return true;
    }

    internal static bool TryCapture(Key key, KeyModifiers modifiers, out HudHotkeyGesture gesture)
    {
        string name = key is >= Key.A and <= Key.Z ? key.ToString()
            : key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString()
            : key is >= Key.F1 and <= Key.F24 ? key.ToString() : "";
        string text = ((modifiers & KeyModifiers.Control) != 0 ? "Ctrl+" : "")
            + ((modifiers & KeyModifiers.Alt) != 0 ? "Alt+" : "")
            + ((modifiers & KeyModifiers.Shift) != 0 ? "Shift+" : "") + name;
        gesture = default;
        return (modifiers & KeyModifiers.Meta) == 0 && name.Length > 0 && TryParse(text, out gesture);
    }
}
