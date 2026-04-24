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
    /// Maximum results returned per search source.
    public const int SearchSourceLimit = 10;

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
    /// Maximum rows of favorite emojis shown at the top of the default grid.
    public const int EmojiMaxFavoriteRows = 2;
    /// Maximum rows of most-used emojis shown after favorites in the default grid.
    public const int EmojiMaxMostUsedRows = 2;

    // ── UI — paste simulation ─────────────────────────────────────────────────
    /// Delay before simulating Cmd+V / Ctrl+V after selecting an emoji.
    public const int PasteDelayMs = 150;

    // ── Search — dictionary ────────────────────────────────────────────────
    /// HTTP timeout for dictionary API requests.
    public const int DictionaryTimeoutSeconds = 5;
    /// Default prefix to activate dictionary lookup.
    public const string DictionaryDefaultPrefix = "define";
    /// Default languages enabled for dictionary lookups.
    public static readonly List<string> DictionaryDefaultLanguages = ["en"];
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
}
