using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DateTime;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Date;

/// <summary>
/// Instant search source that detects dates and date ranges in natural language using
/// Microsoft.Recognizers.Text.DateTime. Detection runs against all available languages;
/// output language and cell formats are controlled by AppLanguage / DateIsoFormat / DateLongFormat.
/// </summary>
public class DateSearch(UserSettings settings, ClipboardService clipboard, ILogger<DateSearch> logger)
    : IInstantSearchSource
{
    public int Limit => 1;
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit)
    {
        if (!settings.DateSearchEnabled) return [];
        if (string.IsNullOrWhiteSpace(query)) return [];

        try {
                // Detect against ALL available languages; first match wins.
            var result = AppDefaults.DateSearchAvailableLanguages
                .SelectMany(l => DateTimeRecognizer.RecognizeDateTime(query, l.Code))
                .DistinctBy(r => r.Text)
                .FirstOrDefault(r => r.TypeName is "datetimeV2.date" or "datetimeV2.daterange"
                                                 or "datetimeV2.datetime" or "datetimeV2.datetimerange"
                                  && r.Text.Length >= query.Length * AppDefaults.DateSearchMinCoverage);

            if (result is null) return [];

            return BuildViewModel(result, settings, clipboard);
        }
        catch (Exception ex) {
            logger.LogError(ex, "DateSearch: error recognizing date in query \"{Query}\"", query);
            return [];
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

        var isoDate  = date.ToString(settings.DateIsoFormat);
        var longDate = date.ToString(settings.DateLongFormat, CultureInfo.InvariantCulture);
        var diff     = (date.Date - DateTime.Today).Days;
        var relDate  = FormatRelative(diff);

        // Drop any cell whose value duplicates what the user already typed.
        var allCells = new[] { isoDate, longDate, relDate };
        var cells    = allCells.Where(c => !c.Equals(recognizedText, StringComparison.OrdinalIgnoreCase))
                               .ToArray();
        var subtitles = new string[cells.Length]; // all empty — no subtitle adds value for single dates

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon          = "📅",
            Category      = "Date",
            Score         = AppDefaults.DateSearchScore,
            Cells         = cells,
            CellSubtitles = subtitles,
            OnLeft        = () => vm.MoveCellLeft(),
            OnRight       = () => vm.MoveCellRight(),
            Actions = [
                new() {
                    Label        = "Copy date",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute      = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
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
        var duration = (end.Date - start.Date).Days + 1;

        var rangeCell    = $"From {isoStart} to {isoEnd}";
        var durationCell = $"{duration} days";

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon          = "📅",
            Category      = "Date Range",
            Score         = AppDefaults.DateSearchScore,
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
                    Label        = "Copy date",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute      = () => clipboard.CopyText(vm.Cells[vm.SelectedCell]),
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
