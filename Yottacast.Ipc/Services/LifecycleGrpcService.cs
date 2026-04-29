using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

/// <summary>
/// Tracks and broadcasts the daemon startup state.
/// SearchGrpcService calls SetInstantReady() and SetFullyReady() during startup.
/// </summary>
public class LifecycleGrpcService(IHostApplicationLifetime lifetime)
    : LifecycleService.LifecycleServiceBase {

    private volatile StatusResponse.Types.State _state = StatusResponse.Types.State.Starting;
    private readonly List<IServerStreamWriter<StatusResponse>> _watchers = [];
    private readonly Lock _lock = new();

    public void SetInstantReady() => Transition(StatusResponse.Types.State.InstantReady);
    public void SetFullyReady()   => Transition(StatusResponse.Types.State.FullyReady);

    private void Transition(StatusResponse.Types.State next) {
        List<IServerStreamWriter<StatusResponse>> snapshot;
        lock (_lock) {
            _state = next;
            snapshot = [.._watchers];
        }
        var response = new StatusResponse { State = next };
        foreach (var writer in snapshot) {
            _ = writer.WriteAsync(response);  // fire-and-forget; dead streams will fail silently
        }
    }

    public override Task<StatusResponse> GetStatus(Empty request, ServerCallContext context) =>
        Task.FromResult(new StatusResponse { State = _state });

    public override async Task WatchStatus(
        Empty request,
        IServerStreamWriter<StatusResponse> responseStream,
        ServerCallContext context) {

        StatusResponse.Types.State current;
        lock (_lock) {
            current = _state;
            _watchers.Add(responseStream);
        }

        // Send current state immediately so the client doesn't miss it
        await responseStream.WriteAsync(new StatusResponse { State = current });

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _watchers.Remove(responseStream); }
        }
    }

    public override Task<Empty> Shutdown(Empty request, ServerCallContext context) {
        lifetime.StopApplication();
        return Task.FromResult(new Empty());
    }
}
