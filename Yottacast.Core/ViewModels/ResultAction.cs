// Yottacast.Core/ViewModels/ResultAction.cs
namespace Yottacast.Core.ViewModels;

public sealed class ResultAction {
    /// <summary>Display label shown in overlay and footer (e.g. "Open", "Copy path").</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Optional dynamic label. When non-null, its return value overrides <see cref="Label"/> in
    /// the footer and options menu. Used when the label depends on metadata that is resolved
    /// asynchronously (e.g. "Open in Preview" once the default app for an extension is known).
    /// </summary>
    public Func<string>? LabelProvider { get; init; }

    /// <summary>Optional hotkey. Null = action only accessible via overlay or mouse.</summary>
    public ActionHotkey? Hotkey { get; init; }

    /// <summary>Whether to show this action's hint in the footer bar. Only meaningful when Hotkey != null.</summary>
    public bool ShowInFooter { get; init; }

    /// <summary>Whether to include this action in the Tab overlay menu.</summary>
    public bool ShowInMenu { get; init; }

    /// <summary>Whether to close the overlay after executing (when opened via Tab).</summary>
    public bool ClosesMenu { get; init; }

    /// <summary>Whether to hide the Yottacast window after executing.</summary>
    public bool ClosesWindow { get; init; }

    /// <summary>
    /// Whether to simulate Cmd+V / Ctrl+V after closing the window.
    /// Only meaningful when ClosesWindow = true.
    /// </summary>
    public bool PasteAfterClose { get; init; }

    /// <summary>
    /// When true, the main window calls RefreshSearch() after Execute().
    /// Used by EmojiSearch's Favorite action to re-rank the emoji grid.
    /// </summary>
    public bool RequiresRefresh { get; init; }

    /// <summary>
    /// Returns a message shown in the search hint area after executing (e.g. "Path copied!").
    /// Null = no message. Only shown when ClosesWindow = false.
    /// </summary>
    public Func<string?>? HintProvider { get; init; }

    /// <summary>The action callback invoked on execution.</summary>
    public required Action Execute { get; init; }
}
