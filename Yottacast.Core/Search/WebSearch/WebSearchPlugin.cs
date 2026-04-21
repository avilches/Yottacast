namespace Yottacast.Core.Search.WebSearch;

/// <summary>
/// A user-installed WebSearch plugin loaded from a JSON file in AppPaths.PluginsDir.
/// </summary>
/// <remarks>
/// Plugin JSON format:
/// <code>
/// {
///   "type": "WebSearch",
///   "id": "hackernews",
///   "name": "Hacker News",
///   "queryUrl": "https://hn.algolia.com/?q={0}",
///   "iconUrl": "https://news.ycombinator.com/favicon.ico",
///   "defaultPrefix": "hn",
///   "defaultEnabled": true,
///   "defaultMode": "PrefixOnly",
///   "showAlwaysPattern": null
/// }
/// </code>
/// </remarks>
public record WebSearchPlugin {
    public required string Id          { get; init; }
    public required string Name        { get; init; }
    public required string QueryUrl    { get; init; }  // string.Format placeholder: {0}
    public string? IconUrl             { get; init; }  // Remote URL; downloaded and cached on disk
    public string DefaultPrefix        { get; init; } = "";
    public bool DefaultEnabled         { get; init; } = true;
    public WebSearchMode DefaultMode   { get; init; } = WebSearchMode.PrefixOnly;
    /// <summary>Optional regex. When Mode is ShowAlways, query must match this pattern. Null = no validation.</summary>
    public string? ShowAlwaysPattern   { get; init; }
}
