namespace Yottacast.Core.Search;

public interface ISearchHintProvider {
    string? LastHint { get; }
    SearchHintKind LastHintKind { get; }
}
