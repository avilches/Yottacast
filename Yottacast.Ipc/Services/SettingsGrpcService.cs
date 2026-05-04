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

    private readonly List<IServerStreamWriter<SettingsMessage>> _watchers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        settings.SearchSettingsChanged += BroadcastCurrentSettings;
        settings.AppDirectoriesChanged += BroadcastCurrentSettings;
    }

    private async void BroadcastCurrentSettings() {
        List<IServerStreamWriter<SettingsMessage>> snapshot;
        lock (_lock) { snapshot = [.._watchers]; }

        var msg = SettingsMapper.ToProto(settings);
        List<IServerStreamWriter<SettingsMessage>> failed = [];
        foreach (var writer in snapshot) {
            try {
                await writer.WriteAsync(msg);
            } catch {
                failed.Add(writer);
            }
        }
        if (failed.Count > 0) {
            lock (_lock) { foreach (var w in failed) _watchers.Remove(w); }
        }
    }

    public override Task<SettingsMessage> GetSettings(Empty request, ServerCallContext context) =>
        Task.FromResult(SettingsMapper.ToProto(settings));

    public override Task<Empty> UpdateSettings(
        UpdateSettingsRequest request,
        ServerCallContext context) {

        SettingsMapper.ApplyProto(request.Settings, settings);
        settings.Save();
        logger.LogInformation("Settings updated via IPC");
        settings.NotifySearchSettingsChanged();
        return Task.FromResult(new Empty());
    }

    public override async Task WatchSettings(
        Empty request,
        IServerStreamWriter<SettingsMessage> responseStream,
        ServerCallContext context) {

        lock (_lock) { _watchers.Add(responseStream); }

        // Send current state immediately
        await responseStream.WriteAsync(SettingsMapper.ToProto(settings));

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _watchers.Remove(responseStream); }
        }
    }
}
