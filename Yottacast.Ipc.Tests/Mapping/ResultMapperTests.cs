using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Tests.Mapping;

public class ResultMapperTests {
    [Fact]
    public void Map_ResultItemViewModel_MapsBaseFields() {
        var vm = new ResultItemViewModel {
            Score = 0.9,
            Title = "Safari",
            Subtitle = "/Applications/Safari.app",
            Category = "Applications",
            Icon = "/Applications/Safari.app",
            BypassLimit = false,
            PasteAfterActivate = false,
        };

        var msg = ResultMapper.Map(vm, "0");

        Assert.Equal("0", msg.Id);
        Assert.Equal("app", msg.Type);
        Assert.Equal("Safari", msg.Title);
        Assert.Equal("/Applications/Safari.app", msg.Subtitle);
        Assert.Equal("Applications", msg.Category);
        Assert.Equal("/Applications/Safari.app", msg.IconId);
        Assert.Equal(0.9, msg.Score, precision: 5);
    }

    [Fact]
    public void Map_CalculatorResultItemViewModel_SetsTypeCalc() {
        var vm = new CalculatorResultItemViewModel {
            Score = 1.0,
            Title = "42",
            Subtitle = "2 * 21",
            Category = "Calculator",
            Icon = "🧮",
        };

        var msg = ResultMapper.Map(vm, "1");

        Assert.Equal("calc", msg.Type);
        Assert.Equal("42", msg.Title);
        Assert.Equal("🧮", msg.IconId);
        Assert.Equal("2 * 21", msg.Subtitle);
    }

    [Fact]
    public void Map_EmojiGridResultViewModel_MapsAllCells() {
        var cells = new List<EmojiCellViewModel> {
            new() { Char = "😀", Name = "grinning", Category = "smileys", Keywords = ["happy"], Section = EmojiSection.Default },
            EmojiCellViewModel.Placeholder,
        };
        var vm = new EmojiGridResultViewModel {
            Score = 1.0,
            Title = "Emoji",
            Cells = cells,
            Icon = "",
        };

        var msg = ResultMapper.Map(vm, "2");

        Assert.Equal("emoji_grid", msg.Type);
        Assert.Equal(2, msg.EmojiCells.Count);
        Assert.Equal("😀", msg.EmojiCells[0].Char);
        Assert.Equal("grinning", msg.EmojiCells[0].Name);
        Assert.True(msg.EmojiCells[1].IsPlaceholder);
    }

    [Fact]
    public void Map_ConversionResultItemViewModel_MapsConversionBlock() {
        var vm = new ConversionResultItemViewModel {
            Score = 1.0,
            Title = "100 km → miles",
            Category = "Converter",
            Icon = "📐",
            FromShort = "100 km",
            FromLong = "100 kilometers",
            ToShort = "62.137 mi",
            ToLong = "62.137 miles",
            FromWasNormalized = false,
        };

        var msg = ResultMapper.Map(vm, "3");

        Assert.Equal("conversion", msg.Type);
        Assert.NotNull(msg.Conversion);
        Assert.Equal("100 km", msg.Conversion.FromShort);
        Assert.Equal("62.137 mi", msg.Conversion.ToShort);
        Assert.False(msg.Conversion.FromWasNormalized);
    }

    [Fact]
    public void Map_DictionaryResultViewModel_MapsDefinitions() {
        var vm = new DictionaryResultViewModel {
            Score = 0.8,
            Title = "apple",
            Definitions = [
                new DictionaryDefinitionEntry {
                    PartOfSpeech = "noun",
                    Definition = "A round fruit.",
                    Example = "I ate an apple.",
                }
            ],
        };

        var msg = ResultMapper.Map(vm, "4");

        Assert.Equal("dict", msg.Type);
        Assert.Single(msg.Definitions);
        Assert.Equal("noun", msg.Definitions[0].PartOfSpeech);
        Assert.Equal("A round fruit.", msg.Definitions[0].Definition);
        Assert.Equal("I ate an apple.", msg.Definitions[0].Example);
    }
}
