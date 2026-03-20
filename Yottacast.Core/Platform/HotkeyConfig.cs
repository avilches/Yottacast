namespace Yottacast.Core.Platform;

/// <summary>
/// Represents a global hotkey combination: optional modifiers (Alt, Ctrl, Shift, Meta) plus a key name.
/// Key names mirror SharpHook's KeyCode enum with the "Vc" prefix removed (e.g. VcSpace → "Space").
/// Serialised as a human-readable string: "Alt+Space", "Ctrl+Shift+A", etc.
/// </summary>
public record HotkeyConfig(bool Alt, bool Ctrl, bool Shift, bool Meta, string KeyName) {
    public static HotkeyConfig Default => new(true, false, false, false, "Space");

    /// <summary>
    /// Parse a string like "Alt+Space" or "Ctrl+Shift+F1". Case-insensitive.
    /// Returns null if the input is null, empty, or contains no recognisable non-modifier token.
    /// </summary>
    public static HotkeyConfig? Parse(string? s) {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var tokens = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool alt = false, ctrl = false, shift = false, meta = false;
        string? keyName = null;

        foreach (var token in tokens) {
            switch (token.ToLowerInvariant()) {
                case "alt":
                case "option":
                case "options":
                    alt   = true; break;
                case "ctrl":
                case "control":
                    ctrl  = true; break;
                case "shift": shift = true; break;
                case "meta":
                case "cmd":
                case "command":
                case "win":
                case "windows":
                    meta  = true; break;
                default:
                    // Last non-modifier token wins (in case of duplicates)
                    keyName = token;
                    break;
            }
        }

        return keyName is null ? null : new HotkeyConfig(alt, ctrl, shift, meta, keyName);
    }

    /// <summary>
    /// Returns the canonical string form: modifiers in Ctrl→Alt→Shift→Meta order, then the key.
    /// Example: "Alt+Space", "Ctrl+Shift+A".
    /// </summary>
    public override string ToString() {
        var parts = new List<string>(5);
        if (Ctrl)  parts.Add("Ctrl");
        if (Alt)   parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Meta)  parts.Add("Meta");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }
}
