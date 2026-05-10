namespace Yottacast.Core;

/// <summary>
/// Default values for all tunable parameters across the application.
/// Single source of truth; individual values can be promoted to UserSettings fields
/// to make them user-configurable without changing call sites.
/// </summary>
public static class AppDefaults {
    // ── Search — global ───────────────────────────────────────────────────────
    /// Debounce before hitting disk after the user stops typing.
    public const int SearchDebouncedMs = 250;
    /// Delay before showing a calculator error hint (e.g. incompatible units) after the user stops typing.
    public const int ErrorHintDelayMs = 1_000;
    /// Minimum query length before any file search is attempted.
    public const int FileSearchMinQueryLength = 2;

    // ── Search — result limits per source (-1 = no limit) ────────────────────
    /// Fallback limit passed to GlobalSearch.SearchInstant/SearchDeferredAsync.
    /// Used by unlimited sources (Limit = -1) as their effective cap, and by deferred sources.
    public const int SearchSourceLimit = 500;
    /// Application search: max matched apps shown.
    public const int AppSearchLimit = 10;
    /// Calculator/converter: always returns 0-1 result — self-limiting by nature.
    public const int CalcSearchLimit = -1;
    /// Emoji search: always returns 0-1 grid item — self-limiting by nature.
    public const int EmojiSearchLimit = -1;
    /// Local path detection: typically 1 result for an exact path.
    public const int LocalPathSearchLimit = 5;
    /// System settings panels.
    public const int SystemSettingsSearchLimit = 5;
    /// Web search engines: no cap — all configured ShowAlways engines are shown.
    public const int WebSearchSourceLimit = -1;
    /// URL detection: no cap — always shown when matched.
    public const int UrlSearchSourceLimit = -1;

    // ── Search — file search ──────────────────────────────────────────────────
    /// Hard timeout for a single Spotlight / Windows Search query.
    public const int FileSearchTimeoutMs = 20_000;
    /// Minimum interval between progressive result snapshots during file search.
    public const int FileSearchSnapshotIntervalMs = 200;

    // ── Search — emoji ────────────────────────────────────────────────────────
    /// Number of columns in the emoji picker grid (default; overridden per theme via Theme.EmojiColumns).
    public const int EmojiColumns = 10;
    /// Number of rows visible at once in the emoji picker grid (default; overridden per theme via Theme.EmojiViewportRows).
    public const int EmojiViewportRows = 8;
    /// Maximum number of favorite emojis shown in the pinned section.
    public const int EmojiMaxFavorites = 4;
    /// Maximum total pinned emojis shown (favorites + most-used combined).
    public const int EmojiMaxPinnedTotal = 10;
    /// Half-life in days for the emoji usage decay score.
    /// After this many days without use, a score is halved.
    public const int EmojiHalfLifeDays = 30;

    // ── Services — launch history ────────────────────────────────────────────────
    /// Half-life in days for launch history decay. After this many days without use, the bonus is halved.
    public const double LaunchHistoryHalfLifeDays = 30.0;
    /// Maximum score bonus a launch history entry can contribute.
    public const double LaunchHistoryMaxBonus = 1.0;

    // ── UI — hints ───────────────────────────────────────────────────────────
    /// Duration a "copied" feedback hint stays visible before auto-clearing.
    public const int CopiedMessageDurationMs = 4_000;

    // ── UI — paste simulation ─────────────────────────────────────────────────
    /// Delay before simulating Cmd+V / Ctrl+V after selecting an emoji.
    public const int PasteDelayMs = 150;

    // ── Search — date ────────────────────────────────────────────────────────
    /// Date search: score for recognized date/daterange results.
    public const double DateSearchScore = 6.0;

    // ── Search — dictionary ────────────────────────────────────────────────
    /// HTTP timeout for dictionary API requests.
    public const int DictionaryTimeoutSeconds = 5;
    /// Default prefix to activate dictionary lookup.
    public const string DictionaryDefaultPrefix = "define";
    /// Maximum number of definitions shown inside a single dictionary result item.
    public const int DictionaryMaxDefinitionsPerItem = 5;
    /// Default languages enabled for dictionary lookups.
    public static readonly List<string> DictionaryDefaultLanguages = ["en"];
    /// Default languages enabled for date search.
    public static readonly List<string> DateSearchDefaultLanguages = ["es-es", "en-us"];
    /// All languages available for date search (locale codes recognized by Chronic/NLP parsers).
    public static readonly (string Code, string Name)[] DateSearchAvailableLanguages = [
        ("es-es", "Español"),
        ("en-us", "English"),
        ("fr-fr", "Français"),
        ("de-de", "Deutsch"),
        ("it-it", "Italiano"),
        ("nl-nl", "Nederlands"),
        ("pt-br", "Português"),
        ("zh-cn", "中文"),
        ("ja-jp", "日本語"),
        ("ko-kr", "한국어"),
        ("tr-tr", "Türkçe"),
    ];
    /// Languages with data available in kaikki.org (subset of DictionaryAvailableLanguages).
    /// These use local SQLite when the DB file is present; others fall back to the Wiktionary API.
    public static readonly HashSet<string> KaikkiLanguages =
        ["en", "es", "fr", "de", "it", "pt", "ru", "tr", "nl", "pl", "th", "ko", "ja", "zh", "el", "id"];

    /// All languages available for dictionary lookups via Wiktionary.
    public static readonly (string Code, string Name)[] DictionaryAvailableLanguages = [
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ru", "Russian"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh", "Chinese"),
        ("tr", "Turkish"),
        ("nl", "Dutch"),
        ("pl", "Polish"),
        ("sv", "Swedish"),
        ("cs", "Czech"),
        ("da", "Danish"),
        ("fi", "Finnish"),
        ("el", "Greek"),
        ("he", "Hebrew"),
        ("hu", "Hungarian"),
        ("id", "Indonesian"),
        ("no", "Norwegian"),
        ("ro", "Romanian"),
        ("th", "Thai"),
        ("uk", "Ukrainian"),
        ("vi", "Vietnamese"),
        ("ca", "Catalan"),
        ("gl", "Galician"),
    ];

    // ── Updates ───────────────────────────────────────────────────────────────
    /// HTTP timeout for the version check request.
    public const int UpdateCheckTimeoutSeconds = 10;

    // ── History ──────────────────────────────────────────────────────────────────
    /// Maximum number of search history entries to keep.
    public const int HistoryMaxItems = 100;

    // ── Search — exchange rates ───────────────────────────────────────────────
    /// Default interval in hours between exchange rate refreshes.
    public const int ExchangeRateRefreshIntervalHours = 4;
    /// HTTP timeout for exchange rate API requests.
    public const int ExchangeRateTimeoutSeconds = 10;

    // ── Search — URL ─────────────────────────────────────────────────────────
    /// HTTP timeout for favicon requests.
    public const int FaviconTimeoutSeconds = 5;

    // ── Search — System Settings ──────────────────────────────────────────────
    /// TTL for dynamic System Settings panels cache (Wi-Fi, VPN, etc).
    public static readonly TimeSpan SystemSettingsDynamicCacheTtl = TimeSpan.FromSeconds(10);

    // ── Window behavior ────────────────────────────────────────────────────────
    /// Default duration in seconds before auto-clearing the search text after hide.
    /// 0 means "always keep" (never auto-clear).
    public const int KeepValueWhenHideDuration = 60;
}
