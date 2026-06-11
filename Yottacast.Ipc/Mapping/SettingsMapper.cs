using Yottacast.Core.Search;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Mapping;

public static class SettingsMapper {
    public static SettingsMessage ToProto(UserSettings s) {
        var msg = new SettingsMessage {
            Browser = s.Browser,
            Terminal = s.Terminal,
            Theme = s.Theme,
            Hotkey = s.Hotkey,
            EnableAppSearch = s.EnableAppSearch,
            EnableCalculator = s.EnableCalculator,
            EnableClipboard = s.EnableClipboard,
            EnableEmoji = s.EnableEmoji,
            EnableFileSearch = s.FileSearchVisibility != SearchSourceVisibility.Disabled,
            EnableWebSearch = s.EnableWebSearch,
            ShowDisabledWebSearchEngines = s.ShowDisabledWebSearchEngines,
            FileSearchOnlySpecificFolders = s.FileSearchOnlySpecificFolders,
            StickyWindow = s.StickyWindow,
            CalculatorCurrencyA = s.CalculatorCurrencyA,
            CalculatorCurrencyB = s.CalculatorCurrencyB,
            CalculatorDecimalPlaces = s.CalculatorDecimalPlaces,
            CalculatorIncludeMetals = s.CalculatorIncludeMetals,
            CalculatorIncludeCrypto = s.CalculatorIncludeCrypto,
            ExchangeRateRefreshIntervalHours = s.ExchangeRateRefreshIntervalHours,
            EnableDictionary = s.EnableDictionary,
            DictionaryPrefix = s.DictionaryPrefix,
            DictionaryShowAlways = s.DictionaryShowAlways,
            EnableHistory = s.EnableHistory,
            HistoryMaxItems = s.HistoryMaxItems,
            KeepValueWhenHide = s.KeepValueWhenHide,
            KeepValueWhenHideDuration = s.KeepValueWhenHideDuration,
            EnableSystemSettings = s.EnableSystemSettings,
            WindowX = s.WindowX,
            WindowY = s.WindowY,
        };
        msg.SearchFolders.AddRange(s.SearchFolders);
        msg.AppDirectories.AddRange(s.AppDirectories);
        msg.DictionaryLanguages.AddRange(s.DictionaryLanguages);
        foreach (var e in s.WebSearchEngines)
            msg.WebSearchEngines.Add(new WebSearchEngineMessage {
                Id = e.Id,
                Enabled = e.Enabled,
                Mode = (int)e.Mode,
                Prefix = e.Prefix,
                QueryUrl = e.QueryUrl ?? "",
            });
        return msg;
    }

    public static void ApplyProto(SettingsMessage msg, UserSettings s) {
        s.Browser = msg.Browser;
        s.Terminal = msg.Terminal;
        s.Theme = msg.Theme;
        s.Hotkey = msg.Hotkey;
        s.EnableAppSearch = msg.EnableAppSearch;
        s.EnableCalculator = msg.EnableCalculator;
        s.EnableClipboard = msg.EnableClipboard;
        s.EnableEmoji = msg.EnableEmoji;
        s.FileSearchVisibility = msg.EnableFileSearch ? SearchSourceVisibility.Always : SearchSourceVisibility.Disabled;
        s.EnableWebSearch = msg.EnableWebSearch;
        s.ShowDisabledWebSearchEngines = msg.ShowDisabledWebSearchEngines;
        s.FileSearchOnlySpecificFolders = msg.FileSearchOnlySpecificFolders;
        s.StickyWindow = msg.StickyWindow;
        s.CalculatorCurrencyA = msg.CalculatorCurrencyA;
        s.CalculatorCurrencyB = msg.CalculatorCurrencyB;
        s.CalculatorDecimalPlaces = msg.CalculatorDecimalPlaces;
        s.CalculatorIncludeMetals = msg.CalculatorIncludeMetals;
        s.CalculatorIncludeCrypto = msg.CalculatorIncludeCrypto;
        s.ExchangeRateRefreshIntervalHours = msg.ExchangeRateRefreshIntervalHours;
        s.EnableDictionary = msg.EnableDictionary;
        s.DictionaryPrefix = msg.DictionaryPrefix;
        s.DictionaryShowAlways = msg.DictionaryShowAlways;
        s.EnableHistory = msg.EnableHistory;
        s.HistoryMaxItems = msg.HistoryMaxItems;
        s.KeepValueWhenHide = msg.KeepValueWhenHide;
        s.KeepValueWhenHideDuration = msg.KeepValueWhenHideDuration;
        s.EnableSystemSettings = msg.EnableSystemSettings;
        s.WindowX = msg.WindowX;
        s.WindowY = msg.WindowY;
        s.SearchFolders = [..msg.SearchFolders];
        s.AppDirectories = [..msg.AppDirectories];
        s.DictionaryLanguages = [..msg.DictionaryLanguages];
        s.WebSearchEngines = msg.WebSearchEngines.Select(e => new WebSearchEngineSettings {
            Id = e.Id,
            Enabled = e.Enabled,
            Mode = (WebSearchMode)e.Mode,
            Prefix = e.Prefix,
            QueryUrl = string.IsNullOrEmpty(e.QueryUrl) ? null : e.QueryUrl,
        }).ToList();
    }
}
