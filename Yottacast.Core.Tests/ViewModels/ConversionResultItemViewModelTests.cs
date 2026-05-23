using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class ConversionResultItemViewModelTests {
    private static Yottacast.Core.ViewModels.ConversionResultItemViewModel Build() => new() {
        FromShort         = "1 m",
        NormFromShort     = "100 cm",
        ToShort           = "3.28 ft",
        FromWasNormalized = true,
    };

    [Fact]
    public void BuildDragPayload_DefaultsToToCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("3.28 ft", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelectedCell_NormFrom() {
        var vm = Build();
        vm.MoveCellLeft();
        Assert.Equal(ConversionCell.NormFrom, vm.SelectedCell);
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("100 cm", text.Value);
    }
}
