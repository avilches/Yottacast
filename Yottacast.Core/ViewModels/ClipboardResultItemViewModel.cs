namespace Yottacast.Core.ViewModels;

public class ClipboardResultItemViewModel : ResultItemViewModel
{
    public required string FullText { get; init; }

    // Absolute time shown in the preview status bar ("Today 16:45", "Yesterday 09:00", "2 Jun, 14:30")
    public required string CopiedAt { get; init; }

    // Hide score debug display — clipboard items are ordered by date, score is not meaningful
    public new string ScoreDisplayText => "";
    public new string ScoreTooltipText => "";
}
