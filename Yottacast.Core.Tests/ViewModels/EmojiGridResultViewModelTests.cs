using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class EmojiGridResultViewModelTests {
    private static EmojiGridResultViewModel Build() => new() {
        Cells = [
            new EmojiCellViewModel { Char = "😀", Section = EmojiSection.Default, Category = "Smileys" },
            new EmojiCellViewModel { Char = "🐶", Section = EmojiSection.Default, Category = "Animals" },
        ],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstEmoji() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("😀", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.SelectNext();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("🐶", text.Value);
    }
}
