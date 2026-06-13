namespace Yottacast.Core.Search.Application;

/// <summary>Represents an installed application.</summary>
public sealed class AppInfo {
    public string Name { get; }
    public string Path { get; }

    /// <summary>Name tokenization precomputed at discovery, reused on every keystroke by NameMatcher.</summary>
    public MatchableName MatchName { get; }

    internal AppInfo(string name, string path) {
        Name = name;
        Path = path;
        MatchName = NameMatcher.Tokenize(name);
    }
}
