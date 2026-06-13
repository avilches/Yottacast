using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Proto;
using Yottacast.Ipc.Services;

namespace Yottacast.Ipc.Tests.Services;

public class SearchGrpcServiceTests {
    private sealed class FakeInstantSource(IReadOnlyList<BaseResultItemViewModel> items) : IInstantSearchSource {
        private IReadOnlyList<BaseResultItemViewModel> _items = items;
        public void SetItems(IReadOnlyList<BaseResultItemViewModel> items) => _items = items;
        public void Start() { }
        public Task WhenReady() => Task.CompletedTask;
        public Task Stop() => Task.CompletedTask;
        public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) => _items;
        public int Limit => -1;
    }

    private static ResultItemViewModel ItemThatCopies(ClipboardService clipboard, string title, string copyText) =>
        new() {
            Title = title,
            Score = 1.0,
            Category = "Application",
            Actions = [
                new ResultAction {
                    Label = "Copy",
                    Hotkey = ActionHotkey.Enter,
                    Execute = () => clipboard.CopyText(copyText),
                },
            ],
        };

    private static (SearchGrpcService Service, FakeInstantSource Source, ClipboardService Clipboard) BuildService(
        IReadOnlyList<BaseResultItemViewModel>? items = null) {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var source = new FakeInstantSource(items ?? []);
        var globalSearch = new GlobalSearch([source], []);
        var service = new SearchGrpcService(globalSearch, clipboard, NullLogger<SearchGrpcService>.Instance);
        service.Initialize();
        return (service, source, clipboard);
    }

    [Fact]
    public async Task Activate_ReturnsCopiedTextOfTheActivatedResult() {
        var (service, source, clipboard) = BuildService();
        source.SetItems([ItemThatCopies(clipboard, "first", "hello")]);

        var search = await service.SearchInstant(new SearchRequest { Query = "x", Limit = 10 }, null!);
        var activate = await service.Activate(
            new ActivateRequest { ResultId = "0", Action = ActionType.Default, Generation = search.Generation },
            null!);

        Assert.Equal("hello", activate.ClipboardText);
    }

    [Fact]
    public async Task Activate_StaleGeneration_ThrowsFailedPrecondition() {
        var (service, source, clipboard) = BuildService();
        source.SetItems([ItemThatCopies(clipboard, "first", "hello")]);

        var first = await service.SearchInstant(new SearchRequest { Query = "x", Limit = 10 }, null!);
        // A new snapshot bumps the generation; the result with id "0" now points elsewhere.
        source.SetItems([ItemThatCopies(clipboard, "second", "world")]);
        await service.SearchInstant(new SearchRequest { Query = "y", Limit = 10 }, null!);

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.Activate(
            new ActivateRequest { ResultId = "0", Action = ActionType.Default, Generation = first.Generation },
            null!));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task SearchInstant_IncrementsGenerationOnEachSnapshot() {
        var (service, _, _) = BuildService();

        var a = await service.SearchInstant(new SearchRequest { Query = "x", Limit = 10 }, null!);
        var b = await service.SearchInstant(new SearchRequest { Query = "y", Limit = 10 }, null!);

        Assert.NotEqual(a.Generation, b.Generation);
    }

    [Fact]
    public async Task Navigate_StaleGeneration_ThrowsFailedPrecondition() {
        var (service, source, clipboard) = BuildService();
        source.SetItems([ItemThatCopies(clipboard, "first", "hello")]);

        var first = await service.SearchInstant(new SearchRequest { Query = "x", Limit = 10 }, null!);
        await service.SearchInstant(new SearchRequest { Query = "y", Limit = 10 }, null!);

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.Navigate(
            new NavigateRequest { ResultId = "0", Direction = Direction.Left, Generation = first.Generation },
            null!));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task Activate_ConcurrentCalls_DoNotMixCopiedText() {
        var (service, source, clipboard) = BuildService();

        // Two distinct results, each copying its own text. A copy gate forces both Execute()
        // callbacks to overlap in time so a shared field would surface the race.
        var gate = new SemaphoreSlim(0, 2);
        var bothEntered = new SemaphoreSlim(0, 2);

        ResultItemViewModel GatedItem(string title, string copyText) => new() {
            Title = title,
            Score = 1.0,
            Category = "Application",
            Actions = [
                new ResultAction {
                    Label = "Copy",
                    Hotkey = ActionHotkey.Enter,
                    Execute = () => {
                        bothEntered.Release();
                        gate.Wait();
                        clipboard.CopyText(copyText);
                    },
                },
            ],
        };

        source.SetItems([GatedItem("a", "AAA"), GatedItem("b", "BBB")]);
        var snap = await service.SearchInstant(new SearchRequest { Query = "x", Limit = 10 }, null!);

        var taskA = Task.Run(() => service.Activate(
            new ActivateRequest { ResultId = "0", Action = ActionType.Default, Generation = snap.Generation }, null!));
        var taskB = Task.Run(() => service.Activate(
            new ActivateRequest { ResultId = "1", Action = ActionType.Default, Generation = snap.Generation }, null!));

        // Wait until both Execute callbacks are inside, then release them together.
        await bothEntered.WaitAsync();
        await bothEntered.WaitAsync();
        gate.Release(2);

        var resA = await taskA;
        var resB = await taskB;

        Assert.Equal("AAA", resA.ClipboardText);
        Assert.Equal("BBB", resB.ClipboardText);
    }
}
