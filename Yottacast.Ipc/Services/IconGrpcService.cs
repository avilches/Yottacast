using System.Threading.Channels;
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

    // Each subscriber owns a bounded channel; its WatchIconsLoaded call is the only writer
    // of its stream, so gRPC never sees concurrent WriteAsync on the same stream.
    private readonly List<Channel<IconLoadedEvent>> _subscribers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        appIconCache.IconLoaded += () => BroadcastIconLoaded("");
        fileIconCache.IconLoaded += () => BroadcastIconLoaded("");
        userDocumentSearch.BadgeIconLoaded += () => BroadcastIconLoaded("");
    }

    private void BroadcastIconLoaded(string iconId) {
        try {
            var evt = new IconLoadedEvent { IconId = iconId };
            List<Channel<IconLoadedEvent>> snapshot;
            lock (_lock) { snapshot = [.._subscribers]; }
            foreach (var channel in snapshot) {
                channel.Writer.TryWrite(evt);
            }
        } catch (Exception ex) {
            logger.LogWarning("Failed to broadcast icon-loaded event: {Message}", ex.Message);
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

        var channel = Channel.CreateUnbounded<IconLoadedEvent>(
            new UnboundedChannelOptions { SingleReader = true });
        int connected;
        lock (_lock) { _subscribers.Add(channel); connected = _subscribers.Count; }
        logger.LogDebug("WatchIconsLoaded: client connected (total={Count})", connected);

        try {
            await foreach (var evt in channel.Reader.ReadAllAsync(context.CancellationToken)) {
                await responseStream.WriteAsync(evt);
            }
        } catch (OperationCanceledException) { }
        finally {
            int remaining;
            lock (_lock) { _subscribers.Remove(channel); remaining = _subscribers.Count; }
            channel.Writer.TryComplete();
            logger.LogDebug("WatchIconsLoaded: client disconnected (total={Count})", remaining);
        }
    }
}
