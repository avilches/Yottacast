using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class AlgebraResultItemViewModelTests {
    private static AlgebraResultItemViewModel Build() => new() {
        Cells = [
            new AlgebraCell("simplify", "x + 1"),
            new AlgebraCell("factor",   "(x+1)"),
            new AlgebraCell("expand",   "x + 1"),
        ],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("x + 1", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.MoveCellRight();
        Assert.Equal(1, vm.SelectedCell);
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("(x+1)", text.Value);
    }
}
