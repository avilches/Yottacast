namespace Yottacast.Core.Search.WebSearch;

public enum WebSearchMode { PrefixOnly, ShowAlways }

public record WebSearchEngine {
    public required string Id          { get; init; }
    public required string Name        { get; init; }
    public required string QueryUrl    { get; init; }  // string.Format placeholder: {0}
    public string? IconResource        { get; init; }  // embedded resource name, or null
}

public record WebSearchEngineSettings {
    public required string Id    { get; init; }
    public bool Enabled          { get; init; } = true;
    public WebSearchMode Mode    { get; init; } = WebSearchMode.PrefixOnly;
    public string Prefix         { get; init; } = "";
    /// <summary>Custom query URL override. Null means "use engine default".</summary>
    public string? QueryUrl      { get; init; }
}

public static class WebSearchDefaults {
    public static readonly IReadOnlyList<WebSearchEngine> Engines = [
        // General
        new() { Id = "google",        Name = "Google",        QueryUrl = "https://www.google.com/search?q={0}",                           IconResource = "Yottacast.Core.Search.WebSearch.Icons.google.png" },
        new() { Id = "bing",          Name = "Bing",          QueryUrl = "https://www.bing.com/search?q={0}",                             IconResource = "Yottacast.Core.Search.WebSearch.Icons.bing.png" },
        new() { Id = "duckduckgo",    Name = "DuckDuckGo",    QueryUrl = "https://duckduckgo.com/?q={0}",                                 IconResource = "Yottacast.Core.Search.WebSearch.Icons.duckduckgo.png" },
        // Shopping
        new() { Id = "amazon",        Name = "Amazon",        QueryUrl = "https://www.amazon.com/s?k={0}",                                IconResource = "Yottacast.Core.Search.WebSearch.Icons.amazon.png" },
        // Video
        new() { Id = "youtube",       Name = "YouTube",       QueryUrl = "https://www.youtube.com/results?search_query={0}",              IconResource = "Yottacast.Core.Search.WebSearch.Icons.youtube.png" },
        new() { Id = "twitch",        Name = "Twitch",        QueryUrl = "https://www.twitch.tv/search?term={0}",                         IconResource = "Yottacast.Core.Search.WebSearch.Icons.twitch.png" },
        // Social
        new() { Id = "reddit",        Name = "Reddit",        QueryUrl = "https://www.reddit.com/search/?q={0}",                          IconResource = "Yottacast.Core.Search.WebSearch.Icons.reddit.png" },
        new() { Id = "x",             Name = "X",             QueryUrl = "https://x.com/search?q={0}",                                   IconResource = "Yottacast.Core.Search.WebSearch.Icons.x.png" },
        new() { Id = "linkedin",      Name = "LinkedIn",      QueryUrl = "https://www.linkedin.com/search/results/all/?keywords={0}",    IconResource = "Yottacast.Core.Search.WebSearch.Icons.linkedin.png" },
        new() { Id = "pinterest",     Name = "Pinterest",     QueryUrl = "https://www.pinterest.com/search/pins/?q={0}",                  IconResource = "Yottacast.Core.Search.WebSearch.Icons.pinterest.png" },
        new() { Id = "tiktok",        Name = "TikTok",        QueryUrl = "https://www.tiktok.com/search?q={0}",                           IconResource = "Yottacast.Core.Search.WebSearch.Icons.tiktok.png" },
        // Knowledge
        new() { Id = "wikipedia",     Name = "Wikipedia",     QueryUrl = "https://en.wikipedia.org/wiki/Special:Search?search={0}",      IconResource = "Yottacast.Core.Search.WebSearch.Icons.wikipedia.png" },
        new() { Id = "wolframalpha",  Name = "Wolfram Alpha",  QueryUrl = "https://www.wolframalpha.com/input?i={0}",                    IconResource = "Yottacast.Core.Search.WebSearch.Icons.wolframalpha.png" },
        // Dev
        new() { Id = "github",        Name = "GitHub",        QueryUrl = "https://github.com/search?q={0}",                              IconResource = "Yottacast.Core.Search.WebSearch.Icons.github.png" },
        new() { Id = "stackoverflow", Name = "Stack Overflow", QueryUrl = "https://stackoverflow.com/search?q={0}",                     IconResource = "Yottacast.Core.Search.WebSearch.Icons.stackoverflow.png" },
        new() { Id = "npm",           Name = "npm",           QueryUrl = "https://www.npmjs.com/search?q={0}",                            IconResource = "Yottacast.Core.Search.WebSearch.Icons.npm.png" },
        new() { Id = "pypi",          Name = "PyPI",          QueryUrl = "https://pypi.org/search/?q={0}",                               IconResource = "Yottacast.Core.Search.WebSearch.Icons.pypi.png" },
        new() { Id = "mdn",           Name = "MDN",           QueryUrl = "https://developer.mozilla.org/en-US/search?q={0}",             IconResource = "Yottacast.Core.Search.WebSearch.Icons.mdn.png" },
        // Entertainment
        new() { Id = "imdb",          Name = "IMDb",          QueryUrl = "https://www.imdb.com/find?q={0}",                              IconResource = "Yottacast.Core.Search.WebSearch.Icons.imdb.png" },
        new() { Id = "spotify",       Name = "Spotify",       QueryUrl = "https://open.spotify.com/search/{0}",                          IconResource = "Yottacast.Core.Search.WebSearch.Icons.spotify.png" },
        // Maps
        new() { Id = "googlemaps",    Name = "Google Maps",   QueryUrl = "https://www.google.com/maps/search/{0}",                       IconResource = "Yottacast.Core.Search.WebSearch.Icons.googlemaps.png" },
    ];

    /// <summary>Default per-engine user settings applied on first run or for newly added engines.</summary>
    public static WebSearchEngineSettings DefaultSettingsFor(string id) => id switch {
        // General
        "google"        => new() { Id = id, Enabled = true,  Mode = WebSearchMode.ShowAlways,  Prefix = "g"   },
        "bing"          => new() { Id = id, Enabled = false, Mode = WebSearchMode.PrefixOnly,  Prefix = "b"   },
        "duckduckgo"    => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "d"   },
        // Shopping
        "amazon"        => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "a"   },
        // Video
        "youtube"       => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "y"   },
        "twitch"        => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "tw"  },
        // Social
        "reddit"        => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "r"   },
        "x"             => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "x"   },
        "linkedin"      => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "li"  },
        "pinterest"     => new() { Id = id, Enabled = false, Mode = WebSearchMode.PrefixOnly,  Prefix = "pin" },
        "tiktok"        => new() { Id = id, Enabled = false, Mode = WebSearchMode.PrefixOnly,  Prefix = "tt"  },
        // Knowledge
        "wikipedia"     => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "w"   },
        "wolframalpha"  => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "wa"  },
        // Dev
        "github"        => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "gh"  },
        "stackoverflow" => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "so"  },
        "npm"           => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "npm" },
        "pypi"          => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "py"  },
        "mdn"           => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "mdn" },
        // Entertainment
        "imdb"          => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "imdb"},
        "spotify"       => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "sp"  },
        // Maps
        "googlemaps"    => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = "map" },
        _               => new() { Id = id, Enabled = true,  Mode = WebSearchMode.PrefixOnly,  Prefix = ""    },
    };
}
