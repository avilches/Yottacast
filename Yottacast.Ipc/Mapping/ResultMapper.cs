using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Mapping;

public static class ResultMapper {
    public static ResultMessage Map(BaseResultItemViewModel vm, string id) {
        var msg = new ResultMessage {
            Id = id,
            Title = vm.Title,
            Score = vm.Score,
            BypassLimit = vm.BypassLimit,
            PasteAfterActivate = vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter)?.PasteAfterClose ?? false,
        };

        switch (vm) {
            case EmojiGridResultViewModel emoji:
                msg.Type = "emoji_grid";
                foreach (var cell in emoji.Cells)
                    msg.EmojiCells.Add(MapEmojiCell(cell));
                msg.SelectedEmojiIndex = emoji.SelectedEmojiIndex;
                break;

            case ConversionResultItemViewModel conv:
                msg.Type = "conversion";
                msg.Category = conv.Category;
                msg.IconId = conv.Icon;
                msg.Conversion = new ConversionMessage {
                    FromShort = conv.FromShort,
                    FromLong = conv.FromLong ?? "",
                    ToShort = conv.ToShort,
                    ToLong = conv.ToLong ?? "",
                    NormFromShort = conv.NormFromShort ?? "",
                    NormFromLong = conv.NormFromLong ?? "",
                    FromWasNormalized = conv.FromWasNormalized,
                    RatesAreStale = conv.RatesAreStale,
                    SelectedCell = (int)conv.SelectedCell,
                };
                break;

            case DictionaryResultViewModel dict:
                msg.Type = "dict";
                foreach (var def in dict.Definitions)
                    msg.Definitions.Add(new DictionaryDefinitionMessage {
                        PartOfSpeech = def.PartOfSpeech,
                        Definition = def.Definition,
                        Example = def.Example ?? "",
                        ExampleTranslation = def.ExampleTranslation ?? "",
                    });
                break;

            case CalculatorResultItemViewModel calc:
                msg.Type = "calc";
                msg.Subtitle = calc.Subtitle;
                msg.Category = calc.Category;
                msg.IconId = calc.Icon;
                break;

            case ResultItemViewModel item:
                msg.Type = DetermineType(item);
                msg.Subtitle = item.Subtitle;
                msg.Category = item.Category;
                // For app/file types, icon_id is the path (subtitle), used as key for IconService.GetIcon.
                // item.Icon is an Avalonia UI-side display string (emoji), not usable as an icon path.
                msg.IconId = msg.Type is "app" or "file" ? item.Subtitle : item.Icon;
                break;
        }

        return msg;
    }

    private static string DetermineType(ResultItemViewModel item) => item.Category switch {
        "Application" or "Applications" => "app",
        "Files" or "Documents" => "file",
        "Web" or "Web Search" => "web",
        // Throw instead of silently labelling unknown categories as "app" with an invalid IconId.
        // If a newly-registered daemon source produces a category not handled here (e.g. Clipboard,
        // Date), this surfaces the missing mapping loudly instead of mislabelling the result.
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.Category,
            "No IPC result type mapping for this category. Add a case in ResultMapper.DetermineType."),
    };

    private static EmojiCellMessage MapEmojiCell(EmojiCellViewModel cell) {
        var msg = new EmojiCellMessage {
            Char = cell.Char,
            Name = cell.Name,
            Category = cell.Category,
            Section = (int)cell.Section,
            UsageCount = cell.UsageCount,
            IsFavorite = cell.IsFavorite,
            IsPlaceholder = cell.IsPlaceholder,
        };
        msg.Keywords.AddRange(cell.Keywords);
        return msg;
    }
}
