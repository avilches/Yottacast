namespace Yottacast.Core.ViewModels;

public class DictionaryResultViewModel : BaseResultItemViewModel {
    public byte[]? IconBytes { get; init; }
    public string Word { get; init; } = "";
    public string PartOfSpeech { get; init; } = "";
    /// <summary>Null when only one language is configured; otherwise the language display name.</summary>
    public string? Language { get; init; }
    public string Definition { get; init; } = "";
    public string? Example { get; init; }
}
