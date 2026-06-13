using System.Threading.Channels;
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
    // Each subscriber owns a bounded channel; its WatchStatus call is the only writer
    // of its stream, so gRPC never sees concurrent WriteAsync on the same stream.
    private readonly List<Channel<StatusResponse>> _subscribers = [];
    private readonly Lock _lock = new();

    public void SetInstantReady() => Transition(StatusResponse.Types.State.InstantReady);
    public void SetFullyReady()   => Transition(StatusResponse.Types.State.FullyReady);

    private void Transition(StatusResponse.Types.State next) {
        var response = new StatusResponse { State = next };
        List<Channel<StatusResponse>> snapshot;
        lock (_lock) {
            _state = next;
            snapshot = [.._subscribers];
        }
        foreach (var channel in snapshot) {
            channel.Writer.TryWrite(response);
        }
    }

    public override Task<StatusResponse> GetStatus(Empty request, ServerCallContext context) =>
        Task.FromResult(new StatusResponse { State = _state });

    public override async Task WatchStatus(
        Empty request,
        IServerStreamWriter<StatusResponse> responseStream,
        ServerCallContext context) {

        var channel = Channel.CreateUnbounded<StatusResponse>(
            new UnboundedChannelOptions { SingleReader = true });
        StatusResponse.Types.State current;
        lock (_lock) {
            current = _state;
            _subscribers.Add(channel);
        }

        try {
            // Send current state immediately so the client doesn't miss it
            await responseStream.WriteAsync(new StatusResponse { State = current });

            await foreach (var response in channel.Reader.ReadAllAsync(context.CancellationToken)) {
                await responseStream.WriteAsync(response);
            }
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _subscribers.Remove(channel); }
            channel.Writer.TryComplete();
        }
    }

    public override Task<Empty> Shutdown(Empty request, ServerCallContext context) {
        lifetime.StopApplication();
        return Task.FromResult(new Empty());
    }
}
