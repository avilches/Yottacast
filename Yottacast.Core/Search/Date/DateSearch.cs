using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DateTime;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Date;

/// <summary>
/// Instant search source that detects dates and date ranges in natural language using
/// Microsoft.Recognizers.Text.DateTime. Detection runs against all available languages;
/// output language and cell formats are controlled by AppLanguage / DateIsoFormat / DateLongFormat.
/// Recognition runs in a background task because Microsoft.Recognizers.Text lazily compiles
/// per-language grammars on first use (~1s cold start across 11 languages). Search returns
/// synchronously with the cached result for the current query and fires ResultChanged when
/// the background task lands; the view model then re-issues the search to pick it up.
/// </summary>
public class DateSearch(UserSettings settings, ClipboardService clipboard, ILogger<DateSearch> logger)
    : IInstantSearchSource
{
    // Indefinite timex shapes the recognizer emits for a bare month ("dec" -> XXXX-12) or a bare
    // weekday ("monday" -> XXXX-WXX-1). These carry no day/week the user actually typed, so they
    // are too noisy to surface on their own; qualified forms ("3 de mayo", "next monday") keep a
    // concrete day/year in their timex and are not matched here.
    private static readonly Regex BareIndefiniteTimex = new(@"^XXXX-\d{2}$|^XXXX-WXX-\d$", RegexOptions.Compiled);

    private readonly Lock _lock = new();
    private string? _currentQuery;
    private IReadOnlyList<BaseResultItemViewModel> _currentResult = [];
    private CancellationTokenSource? _cts;

    /// <summary>Fires (on a thread-pool thread) when background recognition completes for the latest query, with or without a match.</summary>
    public event Action? ResultChanged;

    public int Limit => 1;
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() {
        lock (_lock) {
            _cts?.Cancel();
            _cts = null;
            _currentQuery = null;
            _currentResult = [];
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit)
    {
        if (!settings.DateSearchEnabled) return [];
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Numeric dates ("2025-12-24", "24-12-2025") are parsed synchronously, independent of the
        // configured languages and the recognizer's cold start. A letterless query that is not a
        // valid numeric date stays calculator/number input.
        if (!query.Any(char.IsLetter)) {
            var parsed = NumericDateParser.TryParse(query, settings.DateNumericOrder);
            IReadOnlyList<BaseResultItemViewModel> numericResult =
                parsed is { } p ? BuildNumericDateViewModel(query, p, settings, clipboard) : [];
            lock (_lock) {
                _cts?.Cancel();
                _cts = null;
                _currentQuery = query;
                _currentResult = numericResult;
            }
            return numericResult;
        }

        if (settings.DateSearchLanguages.Count == 0) return [];

        lock (_lock) {
            if (_currentQuery == query) return _currentResult;
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _currentQuery = query;
            _currentResult = [];
            _ = Task.Run(() => RecognizeInBackground(query, cts.Token));
            return [];
        }
    }

    private void RecognizeInBackground(string query, CancellationToken ct) {
        try {
            // Detect against the user-configured languages; first match wins.
            var recognized = settings.DateSearchLanguages
                .SelectMany(code => DateTimeRecognizer.RecognizeDateTime(query, code))
                .DistinctBy(r => r.Text)
                .FirstOrDefault(r => r.TypeName is "datetimeV2.date" or "datetimeV2.daterange"
                                                 or "datetimeV2.datetime" or "datetimeV2.datetimerange"
                                  && r.Text.Length >= query.Length * AppDefaults.DateSearchMinCoverage);

            if (ct.IsCancellationRequested) return;

            var result = recognized is null ? [] : BuildViewModel(recognized, settings, clipboard);

            lock (_lock) {
                if (_currentQuery != query) return;
                _currentResult = result;
            }
            ResultChanged?.Invoke();
        }
        catch (Exception ex) {
            logger.LogError(ex, "DateSearch: error recognizing date in query \"{Query}\"", query);
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<BaseResultItemViewModel> BuildViewModel(
        Microsoft.Recognizers.Text.ModelResult recognized,
        UserSettings settings, ClipboardService clipboard)
    {
        if (recognized.Resolution is not IDictionary<string, object> resolution) return [];
        if (!resolution.TryGetValue("values", out var valuesObj)) return [];
        if (valuesObj is not List<Dictionary<string, string>> valuesList || valuesList.Count == 0) return [];

        var values = valuesList[0];

        // Drop bare month / weekday matches ("dec", "monday") — too noisy to surface alone.
        if (values.TryGetValue("timex", out var timex) && BareIndefiniteTimex.IsMatch(timex)) return [];

        return recognized.TypeName switch {
            "datetimeV2.date" or "datetimeV2.datetime" =>
                BuildDateViewModel(recognized.Text, values, settings, clipboard),
            "datetimeV2.daterange" or "datetimeV2.datetimerange" =>
                BuildDateRangeViewModel(recognized.Text, values, settings, clipboard),
            _ => [],
        };
    }

    // ── Single date ───────────────────────────────────────────────────────────

    private static IReadOnlyList<BaseResultItemViewModel> BuildDateViewModel(
        string recognizedText, Dictionary<string, string> values,
        UserSettings settings, ClipboardService clipboard)
    {
        if (!values.TryGetValue("value", out var valueStr)) return [];
        if (!DateTime.TryParse(valueStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return [];
        return BuildDateResult(recognizedText, date, isoSubtitle: "", settings, clipboard);
    }

    private static IReadOnlyList<BaseResultItemViewModel> BuildNumericDateViewModel(
        string query, NumericDateParser.Result parsed,
        UserSettings settings, ClipboardService clipboard)
        => BuildDateResult(query, parsed.Date, NumericDateParser.FormatLabel(parsed.Format), settings, clipboard);

    private static IReadOnlyList<BaseResultItemViewModel> BuildDateResult(
        string recognizedText, DateTime date, string isoSubtitle,
        UserSettings settings, ClipboardService clipboard)
    {
        var isoDate  = date.ToString(settings.DateIsoFormat);
        var longDate = date.ToString(settings.DateLongFormat, CultureInfo.InvariantCulture);
        var diff     = (date.Date - DateTime.Today).Days;
        var relDate  = FormatRelative(diff);

        // (cell, subtitle) pairs; drop any cell that duplicates what the user already typed.
        var pairs = new[] { (Cell: isoDate, Subtitle: isoSubtitle), (Cell: longDate, Subtitle: ""), (Cell: relDate, Subtitle: "") }
            .Where(p => !p.Cell.Equals(recognizedText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var cells     = pairs.Select(p => p.Cell).ToArray();
        var subtitles = pairs.Select(p => p.Subtitle).ToArray();

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon          = "📅",
            Category      = "Date",
            Score         = AppDefaults.DateSearchScore,
            ScoreReason   = "Fecha detectada",
            Cells         = cells,
            CellSubtitles = subtitles,
            OnLeft        = () => vm.MoveCellLeft(),
            OnRight       = () => vm.MoveCellRight(),
            Actions = [
                new() {
                    Label           = "Close and paste",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute         = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
                },
                new() {
                    Label        = "Copy date",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => "Copiado",
                    Execute      = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
                },
            ],
        };
        vm.GetDragPayload = vm.BuildDragPayload;
        return [vm];
    }

    // ── Date range ────────────────────────────────────────────────────────────

    private static IReadOnlyList<BaseResultItemViewModel> BuildDateRangeViewModel(
        string recognizedText, Dictionary<string, string> values,
        UserSettings settings, ClipboardService clipboard)
    {
        if (!values.TryGetValue("start", out var startStr) || !values.TryGetValue("end", out var endStr)) return [];
        if (!DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return [];
        if (!DateTime.TryParse(endStr,   CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))   return [];

        var isoStart = start.ToString(settings.DateIsoFormat);
        var isoEnd   = end.ToString(settings.DateIsoFormat);
        // The recognizer reports the end day inclusive for explicit "X to Y" ranges (timex tuple,
        // e.g. "(...,...,P4D)") but exclusive — first day of the next period — for whole-period
        // ranges (month/year, e.g. "2025-12"). Only add the inclusive day in the former case, so a
        // whole month reads as 31 days, not 32.
        var inclusiveEnd = values.TryGetValue("timex", out var timex) && timex.StartsWith('(');
        var duration = (end.Date - start.Date).Days + (inclusiveEnd ? 1 : 0);

        var rangeCell    = $"From {isoStart} to {isoEnd}";
        var durationCell = $"{duration} days";

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon          = "📅",
            Category      = "Date Range",
            Score         = AppDefaults.DateSearchScore,
            ScoreReason   = "Fecha detectada",
            Cells         = isoStart == isoEnd
                                ? [isoStart, isoEnd, durationCell]
                                : [isoStart, isoEnd, rangeCell, durationCell],
            CellSubtitles = isoStart == isoEnd
                                ? ["Start date", "End date", ""]
                                : ["Start date", "End date", "", ""],
            OnLeft  = () => vm.MoveCellLeft(),
            OnRight = () => vm.MoveCellRight(),
            Actions = [
                new() {
                    Label           = "Close and paste",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute         = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
                },
                new() {
                    Label        = "Copy date",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => "Copiado",
                    Execute      = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
                },
            ],
        };
        vm.GetDragPayload = vm.BuildDragPayload;
        return [vm];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatRelative(int diff) => diff switch {
        0   => "today",
        1   => "tomorrow",
        -1  => "yesterday",
        > 0 => $"in {diff} days",
        _   => $"{Math.Abs(diff)} days ago",
    };
}
