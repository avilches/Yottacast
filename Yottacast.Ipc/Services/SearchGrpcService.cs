using System.Collections.Concurrent;
using Grpc.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

/// <summary>
/// Singleton service that exposes GlobalSearch over gRPC.
/// Maintains a result registry (latest snapshot) to execute actions by result ID.
/// The clipboard callback captures copied text and returns it in ActivateResponse
/// instead of touching the system clipboard directly.
/// </summary>
public class SearchGrpcService(
    GlobalSearch globalSearch,
    ClipboardService clipboardService,
    ILogger<SearchGrpcService> logger) : SearchService.SearchServiceBase {

    // Registry: latest snapshot of results, keyed by sequential string ID ("0", "1", ...)
    private readonly ConcurrentDictionary<string, BaseResultItemViewModel> _registry = new();

    // Captured clipboard text from the last Activate call
    private string? _lastCopiedText;

    public void Initialize() {
        clipboardService.Initialize(text => _lastCopiedText = text);
    }

    private SearchResponse BuildResponse(
        IReadOnlyList<BaseResultItemViewModel> items,
        string? hint,
        bool isSearching) {

        _registry.Clear();
        var response = new SearchResponse {
            Hint = hint ?? "",
            IsSearching = isSearching,
        };

        for (int i = 0; i < items.Count; i++) {
            var id = i.ToString();
            _registry[id] = items[i];
            response.Results.Add(ResultMapper.Map(items[i], id));
        }

        return response;
    }

    public override Task<SearchResponse> SearchInstant(
        SearchRequest request,
        ServerCallContext context) {

        var (items, hint) = globalSearch.SearchInstant(request.Query, request.Limit);
        var response = BuildResponse(items, hint, isSearching: false);
        return Task.FromResult(response);
    }

    public override async Task SearchDeferred(
        SearchRequest request,
        IServerStreamWriter<SearchResponse> responseStream,
        ServerCallContext context) {

        var ct = context.CancellationToken;
        try {
            await foreach (var snapshot in globalSearch
                .SearchDeferredAsync(request.Query, request.Limit, ct)
                .WithCancellation(ct)) {

                var response = BuildResponse(snapshot, hint: null, isSearching: true);
                await responseStream.WriteAsync(response, ct);
            }

            // Final message: deferred search complete
            var final = new SearchResponse { IsSearching = false };
            final.Results.AddRange(_registry
                .OrderBy(kv => int.Parse(kv.Key))
                .Select(kv => ResultMapper.Map(kv.Value, kv.Key)));
            await responseStream.WriteAsync(final, ct);

        } catch (OperationCanceledException) {
            logger.LogDebug("Deferred search cancelled for query '{Query}'", request.Query);
        }
    }

    public override Task<ActivateResponse> Activate(
        ActivateRequest request,
        ServerCallContext context) {

        if (!_registry.TryGetValue(request.ResultId, out var vm)) {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Result '{request.ResultId}' not found in current session"));
        }

        _lastCopiedText = null;

        // For emoji_grid, set selected index before activating
        if (vm is EmojiGridResultViewModel emojiGrid) {
            emojiGrid.SelectByIndex(request.EmojiIndex);
        }

        switch (request.Action) {
            case ActionType.Default:
                vm.OnActivate?.Invoke();
                break;
            case ActionType.Copy:
                vm.OnCopy?.Invoke();
                break;
            case ActionType.Favorite:
                vm.OnToggleFavorite?.Invoke();
                break;
        }

        return Task.FromResult(new ActivateResponse {
            PasteAfterActivate = vm.PasteAfterActivate,
            ClipboardText = _lastCopiedText ?? "",
        });
    }

    public override Task<NavigateResponse> Navigate(
        NavigateRequest request,
        ServerCallContext context) {

        if (!_registry.TryGetValue(request.ResultId, out var vm)) {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Result '{request.ResultId}' not found in current session"));
        }

        bool consumed = request.Direction switch {
            Direction.Left  => vm.OnLeft?.Invoke()  ?? false,
            Direction.Right => vm.OnRight?.Invoke() ?? false,
            Direction.Up    => vm.OnUp?.Invoke()    ?? false,
            Direction.Down  => vm.OnDown?.Invoke()  ?? false,
            _ => false,
        };

        // For emoji_grid, return updated selected index after navigation
        int newIndex = request.CurrentIndex;
        if (vm is EmojiGridResultViewModel emojiGrid2)
            newIndex = emojiGrid2.SelectedEmojiIndex;

        return Task.FromResult(new NavigateResponse {
            Consumed = consumed,
            NewIndex = newIndex,
        });
    }
}
