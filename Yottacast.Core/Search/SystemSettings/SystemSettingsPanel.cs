using Yottacast.Core.Search.Application;

namespace Yottacast.Core.Search.SystemSettings;

public sealed record SystemSettingsPanel(
    string Name,
    string UrlIdentifier,
    bool IsBuiltin = true,
    string? ParentName = null) {

    private MatchableName? _matchName;

    /// <summary>
    /// Name tokenization, computed once and cached, reused on every keystroke by NameMatcher.
    /// Lazy because panels are records built in bulk; double-compute under a race is idempotent.
    /// </summary>
    public MatchableName MatchName => _matchName ??= NameMatcher.Tokenize(Name);
}
