using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class CalculatorResultItemViewModelTests {
    [Fact]
    public void GetDragPayload_AssignedAtConstruction_ReturnsText() {
        var subject = new CalculatorResultItemViewModel {
            Title = "42",
            GetDragPayload = () => new DragPayload.Text("42"),
        };

        var payload = subject.GetDragPayload!();
        var text = Assert.IsType<DragPayload.Text>(payload);
        Assert.Equal("42", text.Value);
    }
}
