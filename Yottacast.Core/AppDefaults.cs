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
    public const int ErrorHintDelayMs = 3_000;
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
    /// Max emojis shown when the query is bare ':' (no filter text).
    public const int EmojiDefaultLimit = 20;
    /// Number of columns in the emoji picker grid.
    public const int EmojiColumns = 8;

    // ── UI — paste simulation ─────────────────────────────────────────────────
    /// Delay before simulating Cmd+V / Ctrl+V after selecting an emoji.
    public const int PasteDelayMs = 150;

    // ── Search — dictionary ────────────────────────────────────────────────
    /// HTTP timeout for dictionary API requests.
    public const int DictionaryTimeoutSeconds = 5;
    /// Default prefix to activate dictionary lookup.
    public const string DictionaryDefaultPrefix = "define";

    // ── Updates ───────────────────────────────────────────────────────────────
    /// HTTP timeout for the version check request.
    public const int UpdateCheckTimeoutSeconds = 10;
}
