using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

/// <summary>
/// Singleton service that exposes icon lookup and change notifications over gRPC.
/// GetIcon returns cached icon bytes on-demand (triggering async preload on miss).
/// WatchIconsLoaded streams generic notifications when any icon becomes available,
/// so clients can re-request icons for visible results.
/// </summary>
public class IconGrpcService(
    AppIconCache appIconCache,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    ILogger<IconGrpcService> logger) : IconService.IconServiceBase {

    private readonly List<IServerStreamWriter<IconLoadedEvent>> _watchers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        appIconCache.IconLoaded += () => BroadcastIconLoaded("");
        fileIconCache.IconLoaded += () => BroadcastIconLoaded("");
        userDocumentSearch.BadgeIconLoaded += () => BroadcastIconLoaded("");
    }

    private async void BroadcastIconLoaded(string iconId) {
        List<IServerStreamWriter<IconLoadedEvent>> snapshot;
        lock (_lock) { snapshot = [.._watchers]; }

        var evt = new IconLoadedEvent { IconId = iconId };
        List<IServerStreamWriter<IconLoadedEvent>> failed = [];
        foreach (var writer in snapshot) {
            try {
                await writer.WriteAsync(evt);
            } catch {
                failed.Add(writer);
            }
        }
        if (failed.Count > 0) {
            lock (_lock) { foreach (var w in failed) _watchers.Remove(w); }
        }
    }

    public override Task<IconResponse> GetIcon(IconRequest request, ServerCallContext context) {
        byte[]? bytes = request.Type switch {
            "app"   => appIconCache.Get(request.IconId),
            "file"  => fileIconCache.Get(request.IconId),
            "badge" => userDocumentSearch.GetBadge(request.IconId),
            _       => null,
        };

        if (bytes is null) {
            // Trigger async preload so it will be available on next request
            if (request.Type == "app")
                appIconCache.PreloadAsync(request.IconId);

            logger.LogDebug("GetIcon miss: type={Type} id={Id}", request.Type, request.IconId);
            return Task.FromResult(new IconResponse { Available = false });
        }

        return Task.FromResult(new IconResponse {
            Available = true,
            PngData = ByteString.CopyFrom(bytes),
        });
    }

    public override async Task WatchIconsLoaded(
        Empty request,
        IServerStreamWriter<IconLoadedEvent> responseStream,
        ServerCallContext context) {

        lock (_lock) { _watchers.Add(responseStream); }
        logger.LogDebug("WatchIconsLoaded: client connected (total={Count})", _watchers.Count);

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            int remaining;
            lock (_lock) { _watchers.Remove(responseStream); remaining = _watchers.Count; }
            logger.LogDebug("WatchIconsLoaded: client disconnected (total={Count})", remaining);
        }
    }
}
