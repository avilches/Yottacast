using System.Threading.Channels;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

public class SettingsGrpcService(
    UserSettings settings,
    ILogger<SettingsGrpcService> logger) : SettingsService.SettingsServiceBase {

    // Each subscriber owns a bounded channel; its WatchSettings call is the only writer
    // of its stream, so gRPC never sees concurrent WriteAsync on the same stream.
    private readonly List<Channel<SettingsMessage>> _subscribers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        settings.SearchSettingsChanged += BroadcastCurrentSettings;
        settings.AppDirectoriesChanged += BroadcastCurrentSettings;
    }

    private void BroadcastCurrentSettings() {
        SettingsMessage msg;
        try {
            msg = SettingsMapper.ToProto(settings);
        } catch (Exception ex) {
            logger.LogWarning("Failed to build settings broadcast: {Message}", ex.Message);
            return;
        }

        List<Channel<SettingsMessage>> snapshot;
        lock (_lock) { snapshot = [.._subscribers]; }
        foreach (var channel in snapshot) {
            channel.Writer.TryWrite(msg);
        }
    }

    public override Task<SettingsMessage> GetSettings(Empty request, ServerCallContext context) =>
        Task.FromResult(SettingsMapper.ToProto(settings));

    public override Task<Empty> UpdateSettings(
        UpdateSettingsRequest request,
        ServerCallContext context) {

        var oldAppDirectories = settings.AppDirectories.ToList();
        SettingsMapper.ApplyProto(request.Settings, settings);
        settings.Save();
        logger.LogInformation("Settings updated via IPC");
        settings.NotifySearchSettingsChanged();
        if (!oldAppDirectories.SequenceEqual(settings.AppDirectories, StringComparer.OrdinalIgnoreCase)) {
            settings.NotifyAppDirectoriesChanged();
        }
        return Task.FromResult(new Empty());
    }

    public override async Task WatchSettings(
        Empty request,
        IServerStreamWriter<SettingsMessage> responseStream,
        ServerCallContext context) {

        var channel = Channel.CreateUnbounded<SettingsMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        lock (_lock) { _subscribers.Add(channel); }

        try {
            // Send current state immediately
            await responseStream.WriteAsync(SettingsMapper.ToProto(settings));

            await foreach (var msg in channel.Reader.ReadAllAsync(context.CancellationToken)) {
                await responseStream.WriteAsync(msg);
            }
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _subscribers.Remove(channel); }
            channel.Writer.TryComplete();
        }
    }
}
