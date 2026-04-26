namespace Yottacast.Core.ViewModels;

public class CalculatorResultItemViewModel : BaseResultItemViewModel {
    public string Icon { get; init; } = "";
    public string? TitleLong { get; init; }
    public string Subtitle { get; init; } = "";
    public string Category { get; init; } = "";
}