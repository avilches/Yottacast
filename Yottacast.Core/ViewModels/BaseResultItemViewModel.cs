// Yottacast.Core/ViewModels/BaseResultItemViewModel.cs
namespace Yottacast.Core.ViewModels;

/// <summary>
/// Shared base for all result items. Contains properties needed for scoring,
/// key-event routing, and the unified action list.
/// </summary>
public abstract class BaseResultItemViewModel {
    public double Score { get; init; }
    public string Title { get; init; } = "";

    /// <summary>
    /// All available actions for this result. The UI derives footer hints, overlay menu,
    /// and keyboard shortcuts from this list.
    /// </summary>
    public IReadOnlyList<ResultAction> Actions { get; init; } = [];

    /// <summary>
    /// When non-null, the item captures LEFT/RIGHT/UP/DOWN arrow keys while selected.
    /// Used for grids (Emoji) and multi-cell converters.
    /// Returns true if the key was consumed, false to fall through to the default handler.
    /// </summary>
    public Func<bool>? OnLeft  { get; init; }
    public Func<bool>? OnRight { get; init; }
    public Func<bool>? OnUp   { get; init; }
    public Func<bool>? OnDown { get; init; }

    /// <summary>
    /// When true, the item is never discarded by the SearchSourceLimit cap.
    /// Used by WebSearch and Dictionary to always appear in results.
    /// </summary>
    public bool BypassLimit { get; init; }
}
