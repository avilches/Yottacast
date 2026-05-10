using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DateTime;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Date;

/// <summary>
/// Instant search source that detects dates and date ranges in natural language using
/// Microsoft.Recognizers.Text.DateTime. Activating the result (or using left/right arrows)
/// copies the selected cell to the clipboard.
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
        if (string.IsNullOrWhiteSpace(query) || settings.DateSearchLanguages.Count == 0) return [];

        try {
            var recognized = settings.DateSearchLanguages
                .SelectMany(lang => DateTimeRecognizer.RecognizeDateTime(query, lang))
                .DistinctBy(r => r.Text)
                .FirstOrDefault(r => r.TypeName is "datetimeV2.date" or "datetimeV2.daterange"
                                                 or "datetimeV2.datetime" or "datetimeV2.datetimerange");

            if (recognized is null) return [];

            return BuildViewModel(recognized, clipboard);
        }
        catch (Exception ex) {
            logger.LogError(ex, "DateSearch: error recognizing date in query \"{Query}\"", query);
            return [];
        }
    }

    private static IReadOnlyList<BaseResultItemViewModel> BuildViewModel(
        Microsoft.Recognizers.Text.ModelResult recognized, ClipboardService clipboard)
    {
        if (recognized.Resolution is not IDictionary<string, object> resolution) return [];
        if (resolution.TryGetValue("values", out var valuesObj) is false) return [];
        if (valuesObj is not List<Dictionary<string, string>> valuesList || valuesList.Count == 0) return [];

        var values = valuesList[0];
        var typeName = recognized.TypeName;

        if (typeName is "datetimeV2.date" or "datetimeV2.datetime") {
            return BuildDateViewModel(values, clipboard);
        }

        if (typeName is "datetimeV2.daterange" or "datetimeV2.datetimerange") {
            return BuildDateRangeViewModel(values, clipboard);
        }

        return [];
    }

    private static IReadOnlyList<BaseResultItemViewModel> BuildDateViewModel(
        Dictionary<string, string> values, ClipboardService clipboard)
    {
        if (!values.TryGetValue("value", out var valueStr)) return [];
        if (!DateTime.TryParse(valueStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return [];

        var isoCell      = date.ToString("yyyy-MM-dd");
        var longCell     = date.ToString("d 'de' MMMM 'de' yyyy (dddd)", CultureInfo.GetCultureInfo("es-ES"));
        var diff         = (date.Date - DateTime.Today).Days;
        var relativeCell = diff switch {
            0    => "hoy",
            1    => "mañana",
            -1   => "ayer",
            > 0  => $"en {diff} días",
            _    => $"hace {Math.Abs(diff)} días",
        };

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon     = "📅",
            Category = "Date",
            Score    = AppDefaults.DateSearchScore,
            Cells    = [isoCell, longCell, relativeCell],
            OnLeft   = () => vm.MoveCellLeft(),
            OnRight  = () => vm.MoveCellRight(),
            Actions  = [
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

    private static IReadOnlyList<BaseResultItemViewModel> BuildDateRangeViewModel(
        Dictionary<string, string> values, ClipboardService clipboard)
    {
        if (!values.TryGetValue("start", out var startStr) || !values.TryGetValue("end", out var endStr)) return [];
        if (!DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return [];
        if (!DateTime.TryParse(endStr,   CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))   return [];

        var isoStart     = start.ToString("yyyy-MM-dd");
        var isoEnd       = end.ToString("yyyy-MM-dd");
        var isoRange     = $"{isoStart}/{isoEnd}";
        var duration     = (end.Date - start.Date).Days + 1;
        var durationCell = $"{duration} días";

        DateSearchResultViewModel vm = null!;
        vm = new DateSearchResultViewModel {
            Icon     = "📅",
            Category = "Date Range",
            Score    = AppDefaults.DateSearchScore,
            Cells    = [isoStart, isoEnd, isoRange, durationCell],
            OnLeft   = () => vm.MoveCellLeft(),
            OnRight  = () => vm.MoveCellRight(),
            Actions  = [
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
}
