namespace Yottacast.Core.ViewModels;

public class ClipboardResultItemViewModel : ResultItemViewModel
{
    public required string FullText { get; init; }

    // Hide score debug display — clipboard items are ordered by date, score is not meaningful
    public new string ScoreDisplayText => "";
    public new string ScoreTooltipText => "";
}
