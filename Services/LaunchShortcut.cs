using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniLauncher.Services;

public sealed record LaunchShortcut(string Text, uint Modifiers, uint VirtualKey)
{
    public static LaunchShortcut? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            uint modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 2, "ALT" => 1, "SHIFT" => 4, "WIN" or "WINDOWS" => 8,
                _ => throw new FormatException("Use Ctrl, Alt, Shift, or Win followed by a letter, number, or F1–F11.")
            };
            if ((modifiers & modifier) != 0) throw new FormatException("Each modifier can appear only once.");
            modifiers |= modifier;
        }
        var key = parts[^1].ToUpperInvariant();
        uint vk;
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) vk = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 11) vk = (uint)(111 + f);
        else throw new FormatException("Choose a letter, number, or F1–F11 (F12 is reserved by Windows).");
        if ((modifiers & (1 | 2 | 8)) == 0) throw new FormatException("Include Ctrl, Alt, or Win in the shortcut.");
        var labels = new List<string>();
        if ((modifiers & 2) != 0) labels.Add("Ctrl");
        if ((modifiers & 1) != 0) labels.Add("Alt");
        if ((modifiers & 4) != 0) labels.Add("Shift");
        if ((modifiers & 8) != 0) labels.Add("Win");
        labels.Add(key);
        return new LaunchShortcut(string.Join("+", labels), modifiers, vk);
    }

    public static string Normalize(string? text) => Parse(text)?.Text ?? "";
}
