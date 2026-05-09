namespace Yottacast.Core.ViewModels;

public class ResultItemViewModel : BaseResultItemViewModel {
    public string Icon { get; init; } = "";
    public byte[]? IconBytes { get; set; }
    public byte[]? BadgeIconBytes { get; set; }
    public string Subtitle { get; init; } = "";
    public string Category { get; init; } = "";
    public string Shortcut { get; init; } = "";
    /// <summary>Filesystem or system path identifying this item for launch history tracking. Null for items without a stable path.</summary>
    public string? ItemPath { get; init; }
}
