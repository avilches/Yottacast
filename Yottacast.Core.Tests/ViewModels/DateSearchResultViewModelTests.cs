using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class DateSearchResultViewModelTests {
    private static DateSearchResultViewModel Build() => new() {
        Cells = ["2026-05-22", "May 22, 2026", "Friday"],
    };

    [Fact]
    public void BuildDragPayload_DefaultsToFirstCell() {
        var vm = Build();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("2026-05-22", text.Value);
    }

    [Fact]
    public void BuildDragPayload_FollowsSelection() {
        var vm = Build();
        vm.MoveCellRight();
        var text = Assert.IsType<DragPayload.Text>(vm.BuildDragPayload());
        Assert.Equal("May 22, 2026", text.Value);
    }
}
