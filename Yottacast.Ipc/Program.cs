using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Search.Dictionary;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Core.Platform;
using Yottacast.Ipc.Services;

// ── PID file guard ───────────────────────────────────────────────────────────
Directory.CreateDirectory(AppPaths.CacheDir);

if (File.Exists(AppPaths.IpcPidFile)) {
    var pidStr = await File.ReadAllTextAsync(AppPaths.IpcPidFile);
    if (int.TryParse(pidStr.Trim(), out var existingPid)) {
        try {
            System.Diagnostics.Process.GetProcessById(existingPid);
            Console.Error.WriteLine(
                $"yottacast-core already running (PID {existingPid}). Exiting.");
            return 1;
        } catch (ArgumentException) {
            // Stale PID file — process no longer running
        }
    }
}
await File.WriteAllTextAsync(AppPaths.IpcPidFile, Environment.ProcessId.ToString());

// ── Host ─────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => {
    if (File.Exists(AppPaths.IpcSocket))
        File.Delete(AppPaths.IpcSocket);

    options.ListenUnixSocket(AppPaths.IpcSocket, listenOptions => {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ── DI ───────────────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// Platform
builder.Services.AddSingleton<PlatformProvider, MacOsPlatformProvider>();

// Core services
builder.Services.AddSingleton<ClipboardService>();
builder.Services.AddSingleton<AppIconCache>();
builder.Services.AddSingleton<FileIconCache>();
builder.Services.AddSingleton<PluginService>();
builder.Services.AddSingleton<HistoryService>();

// UserSettings (loaded from disk)
builder.Services.AddSingleton(sp => {
    var platform = sp.GetRequiredService<PlatformProvider>();
    return UserSettings.Load(platform);
});

// Search sources
builder.Services.AddSingleton<ApplicationSearch>();
builder.Services.AddSingleton<CalculatorSearch>();
builder.Services.AddSingleton<EmojiSearch>();
builder.Services.AddSingleton<WebSearchSource>();
builder.Services.AddSingleton<UserDocumentSearch>();
builder.Services.AddSingleton<DictionarySource>();

// Register interfaces for GlobalSearch
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<ApplicationSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<CalculatorSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<EmojiSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<WebSearchSource>());
builder.Services.AddSingleton<IDeferredSearchSource>(sp =>
    sp.GetRequiredService<UserDocumentSearch>());
builder.Services.AddSingleton<IDeferredSearchSource>(sp =>
    sp.GetRequiredService<DictionarySource>());

builder.Services.AddSingleton<GlobalSearch>();

// gRPC services (singleton to maintain state)
builder.Services.AddSingleton<LifecycleGrpcService>();
builder.Services.AddSingleton<SearchGrpcService>();
builder.Services.AddSingleton<SettingsGrpcService>();
builder.Services.AddSingleton<IconGrpcService>();

// ── App ───────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapGrpcService<LifecycleGrpcService>();
app.MapGrpcService<SearchGrpcService>();
app.MapGrpcService<SettingsGrpcService>();
app.MapGrpcService<IconGrpcService>();

// ── Startup sequence ─────────────────────────────────────────────────────────
var lifecycle    = app.Services.GetRequiredService<LifecycleGrpcService>();
var search       = app.Services.GetRequiredService<SearchGrpcService>();
var settingsSvc  = app.Services.GetRequiredService<SettingsGrpcService>();
var iconSvc      = app.Services.GetRequiredService<IconGrpcService>();
var globalSearch = app.Services.GetRequiredService<GlobalSearch>();
var appLifetime  = app.Services.GetRequiredService<IHostApplicationLifetime>();

search.Initialize();
settingsSvc.Initialize();
iconSvc.Initialize();

var startupLogger = app.Services.GetRequiredService<ILogger<WebApplication>>();

_ = Task.Run(async () => {
    try {
        globalSearch.Start();
        await globalSearch.WhenInstantReady();
        lifecycle.SetInstantReady();
        await globalSearch.WhenReady();
        lifecycle.SetFullyReady();
    } catch (Exception ex) {
        startupLogger.LogCritical(ex, "Startup sequence failed — shutting down");
        appLifetime.StopApplication();
    }
});

// ── Graceful shutdown ─────────────────────────────────────────────────────────
appLifetime.ApplicationStopping.Register(() => {
    globalSearch.Stop().GetAwaiter().GetResult();
    try { File.Delete(AppPaths.IpcPidFile); } catch { }
    try { File.Delete(AppPaths.IpcSocket); }  catch { }
});

Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    appLifetime.StopApplication();
};

PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => appLifetime.StopApplication());

app.Run();
return 0;
