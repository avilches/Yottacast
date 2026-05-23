using Xunit;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.ViewModels;

public class DictionaryResultViewModelTests {
    [Fact]
    public void GetDragPayload_ReturnsWordAsText() {
        var word = "ephemeral";
        var vm = new DictionaryResultViewModel {
            Word = word,
            GetDragPayload = () => new DragPayload.Text(word),
        };
        var text = Assert.IsType<DragPayload.Text>(vm.GetDragPayload!());
        Assert.Equal("ephemeral", text.Value);
    }
}
