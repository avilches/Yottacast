namespace Yottacast.Core.Search.Application;

/// <summary>Represents an installed application.</summary>
public sealed class AppInfo {
    public string Name { get; }
    public string Path { get; }

    internal AppInfo(string name, string path) {
        Name = name;
        Path = path;
    }
}
