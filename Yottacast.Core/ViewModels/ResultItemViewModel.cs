namespace Yottacast.Core.ViewModels;

public class ResultItemViewModel
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Category { get; init; } = "";
    public string Shortcut { get; init; } = "";
    public double Score { get; init; }
    public Action? OnActivate { get; init; }
    /// <summary>
    /// When non-null, the item captures LEFT/RIGHT/UP/DOWN arrow keys while selected.
    /// The Window's tunnel handler calls these instead of letting the TextBox move its cursor.
    /// </summary>
    public Action? OnLeft  { get; init; }
    public Action? OnRight { get; init; }
    /// <summary>
    /// Returns true if the key was consumed, false to let the window fall through to list navigation.
    /// </summary>
    public Func<bool>? OnUp    { get; init; }
    public Func<bool>? OnDown  { get; init; }
    /// <summary>
    /// When true, the launcher hides, restores focus to the previous app, and simulates a paste (Cmd+V / Ctrl+V).
    /// Used by EmojiSearch so the copied emoji is immediately pasted into the target app.
    /// </summary>
    public bool PasteAfterActivate { get; init; }
}
