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

    // ── App scanning (Windows) ────────────────────────────────────────────────
    /// Maximum directory depth (relative to a scanned root) at which Windows app
    /// executables are discovered. Depth 0 = exe directly in the root, depth 3 covers
    /// nested layouts like Google\Chrome\Application\chrome.exe. Both the initial scan
    /// and the FileSystemWatcher apply this same limit so they stay consistent.
    public const int WindowsAppScanMaxDepth = 3;
    /// Substrings (case-insensitive) that mark a Windows .exe as a non-launchable helper
    /// (uninstallers, crash handlers, updaters). Matching executables are skipped by both
    /// the initial scan and the watcher.
    public static readonly string[] WindowsAppExeExcludeSubstrings = [
        "unins", "uninstall", "setup", "installer", "update", "crashpad",
        "crashhandler", "crashreport", "helper", "notification_helper",
    ];

    // ── Search — application scoring ─────────────────────────────────────────
    /// Minimum score for any app that has a match. Ensures apps appear above all file matches
    /// except exact full-name-with-extension (3.85). Must be > AppMaxFileScore (3.50) and < AppFileExactScore (3.85).
    public const double AppMinScore = 3.6;

    // ── Search — algebra (nerdamer TryAlgebra) ────────────────────────────────
    /// Minimum query length to attempt symbolic algebra evaluation.
    /// Prevents false positives like "1p" or "2x" (single-letter variables in tiny queries).
    public const int AlgebraMinQueryLength = 3;

    /// Score for symbolic algebra results. Sits just above an app with exact-prefix match (4.0)
    /// and below an exact app name match (4.4). A single LaunchHistory bonus on the app (~+0.35)
    /// pushes the app above algebra, so frequently-used apps win after one use.
    public const double AlgebraResultScore = 4.01;

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
    /// Delay before reclaiming window focus after Cmd+Enter / Cmd+Click (keep-window-open).
    /// Needs to be long enough for the OS to hand focus to the launched app first.
    public const int RegainFocusDelayMs = 200;

    // ── Search — date ────────────────────────────────────────────────────────
    /// Date search: score for recognized date/daterange results.
    public const double DateSearchScore = 6.0;
    /// Minimum fraction of the query that the recognized text must cover to be accepted (0–1).
    /// Prevents false positives where the recognizer matches a short substring (e.g. "2-5" inside "x^2-5x+6=0").
    public const double DateSearchMinCoverage = 0.9;
    /// Default format string for ISO date cells in date search results.
    public const string DateIsoFormat = "yyyy-MM-dd";
    /// Default format string for the long date cell in date search results.
    public const string DateLongFormat = "d MMMM yyyy (dddd)";

    // ── Search — dictionary ────────────────────────────────────────────────
    /// HTTP timeout for dictionary API requests.
    public const int DictionaryTimeoutSeconds = 5;
    /// Default prefix to activate dictionary lookup.
    public const string DictionaryDefaultPrefix = "define";
    /// Maximum number of definitions shown inside a single dictionary result item.
    public const int DictionaryMaxDefinitionsPerItem = 5;
    /// Default languages enabled for dictionary lookups.
    public static readonly List<string> DictionaryDefaultLanguages = ["en"];
    /// All languages available for date recognition (locale codes recognized by Microsoft.Recognizers.Text).
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
    /// Decimal places always used for FIAT currency results (e.g. 91.23 EUR).
    public const int FiatCurrencyDecimalPlaces = 2;
    /// Decimal places always used for cryptocurrency results (e.g. 0.00010000 BTC).
    public const int CryptoCurrencyDecimalPlaces = 8;
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

    // ── Drag-and-drop ─────────────────────────────────────────────────────────
    /// Pixel distance the cursor must travel with the left button held before a drag is initiated.
    /// Below this threshold, click+release is treated as a normal click (selection).
    public const double DragStartThresholdPx = 50.0;
    /// Minimum time (ms) the button must be held before a movement-triggered drag is accepted.
    /// Prevents accidental drags from fast clicks with slight cursor wobble.
    public const int DragMinPressDurationMs = 150;
    /// Time (ms) the button must be held without releasing to trigger a drag via long-press,
    /// regardless of cursor movement distance.
    public const int DragLongPressMs = 500;

    // ── File Editor ────────────────────────────────────────────────────────────
    /// Width of the inline editor panel in pixels.
    public const double EditorWidth = 680;
    /// Height of the inline editor panel in pixels (≈ max launcher height with full results).
    public const double EditorHeight = 640;
    /// Maximum file size in MB the editor will open.
    public const int EditorMaxFileSizeMb = 5;
    /// Number of bytes read to detect binary content (null-byte heuristic).
    public const int EditorBinaryDetectionBytes = 8_192;
    /// Default set of file extensions the editor will open (without leading dot).
    public static readonly string[] FileEditorDefaultExtensions = [
        "txt", "md", "markdown", "log", "csv",
        "cs", "fs", "vb",
        "py", "rb", "go", "rs", "java", "kt", "swift", "c", "cpp", "h",
        "js", "ts", "jsx", "tsx", "vue",
        "json", "yaml", "yml", "toml", "ini", "cfg", "conf", "config", "env",
        "xml", "html", "htm", "css", "scss", "less",
        "sh", "bash", "zsh", "fish", "ps1",
        "gitignore", "gitattributes", "editorconfig", "dockerfile",
    ];

    // ── Clipboard history ─────────────────────────────────────────────────────
    /// Maximum number of clipboard history entries to keep.
    public const int ClipboardHistoryMaxEntries = 200;
    /// Maximum age in days for clipboard history entries.
    public const int ClipboardHistoryMaxDays = 30;
    /// Half-life in days for clipboard history usage decay score.
    public const double ClipboardHistoryHalfLifeDays = 30.0;
    /// Score cap for clipboard history usage bonus.
    public const double ClipboardHistoryMaxBonus = 0.5;
    /// Debounce in ms before writing clipboard history to disk.
    public const int ClipboardHistoryDebounceMs = 1_000;
    /// Polling interval in ms for the clipboard monitor.
    public const int ClipboardMonitorIntervalMs = 500;
}
