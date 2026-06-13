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
    /// Internal navigation callbacks for results with spatial layouts (emoji grids, unit converters).
    /// When non-null, the item captures arrow key presses while selected, allowing left/right/up/down
    /// navigation within the result's own content.
    /// Returns true if the key was consumed, false to fall through to the default list/cursor handler.
    /// These are NOT part of <see cref="Actions"/> — they don't appear in the footer or overlay menu.
    /// </summary>
    public Func<bool>? OnLeft  { get; init; }
    public Func<bool>? OnRight { get; init; }
    public Func<bool>? OnUp   { get; init; }
    public Func<bool>? OnDown { get; init; }

    /// <summary>
    /// When true, this result is excluded from the <c>SearchSourceLimit</c> cap and always appears
    /// in the results list regardless of how many other results exist.
    /// Used by WebSearch and Dictionary to guarantee visibility.
    /// This is NOT part of <see cref="Actions"/> — it is a result-list structural flag.
    /// </summary>
    public bool BypassLimit { get; init; }

    // ============================================================================
    // Highlighting (set by each search source when creating the ViewModel)
    // ============================================================================

    /// <summary>Character ranges in Title that matched the query. Null means no highlighting.</summary>
    public IReadOnlyList<(int Start, int Length)>? TitleRanges { get; init; }

    /// <summary>Character ranges in Subtitle that matched the query. Null means no highlighting.</summary>
    public IReadOnlyList<(int Start, int Length)>? SubtitleRanges { get; init; }

    // ============================================================================
    // Score debug (ScoreReason set by source; others calculated in RefreshResults)
    // ============================================================================

    /// <summary>Human-readable explanation of why this item got its score. e.g. "CamelHump inicio (×4)"</summary>
    public string? ScoreReason { get; init; }

    /// <summary>Frequency/recency bonus from launch history. Set by MainWindowViewModel.RefreshResults().</summary>
    public double FrequencyBonus { get; set; }

    /// <summary>Launch count behind <see cref="FrequencyBonus"/>. Set by RefreshResults(); used to build the lazy tooltip.</summary>
    public int FrequencyCount { get; set; }

    /// <summary>Age in days of the last launch behind <see cref="FrequencyBonus"/>. Set by RefreshResults(); used to build the lazy tooltip.</summary>
    public double FrequencyAgeDays { get; set; }

    /// <summary>
    /// Formatted total score for the Alt-debug badge (e.g. "2.40"). Computed lazily: only the
    /// realized (visible) rows that bind it pay the formatting cost, not every merged result on
    /// each keystroke. Depends on <see cref="Score"/> + <see cref="FrequencyBonus"/>.
    /// </summary>
    public string ScoreDisplayText => $"{Score + FrequencyBonus:F2}";

    /// <summary>
    /// Multi-line tooltip for the Alt-debug score badge. Computed lazily (see <see cref="ScoreDisplayText"/>):
    /// only built when a realized row's tooltip binding is read.
    /// </summary>
    public string ScoreTooltipText {
        get {
            var reason    = string.IsNullOrEmpty(ScoreReason) ? "—" : ScoreReason;
            var bonusLine = FrequencyBonus > 0.001
                ? $"+{FrequencyBonus:F2}: {FrequencyCount} lanzamiento{(FrequencyCount != 1 ? "s" : "")}, hace {(int)FrequencyAgeDays} día{((int)FrequencyAgeDays != 1 ? "s" : "")}"
                : "Sin historial de uso";
            return $"Score {Score:F2}: {reason}\n{bonusLine}";
        }
    }

    // ============================================================================
    // Drag-and-drop (set by each search source / VM when the item is draggable)
    // ============================================================================

    /// <summary>
    /// If non-null, the item is draggable. The view invokes this on drag start and
    /// translates the returned <see cref="DragPayload"/> into a platform IDataObject.
    /// Returning null cancels the drag silently. Read on the UI thread.
    /// </summary>
    public Func<DragPayload?>? GetDragPayload { get; set; }
}
