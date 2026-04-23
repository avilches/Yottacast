using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Cli;

internal static class Program {
    private static readonly Serilog.ILogger SerilogLogger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(outputTemplate: "[{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    private static readonly ILoggerFactory LoggerFactory = new SerilogLoggerFactory(SerilogLogger);

    private static readonly ProcessRunner Runner = new(LoggerFactory.CreateLogger<ProcessRunner>());

    private static readonly PlatformProvider Platform =
        OperatingSystem.IsMacOS()
            ? new MacOsPlatformProvider(LoggerFactory.CreateLogger<MacOsPlatformProvider>())
            : OperatingSystem.IsWindows()
                ? new WindowsPlatformProvider(Runner, LoggerFactory.CreateLogger<WindowsPlatformProvider>())
                : new LinuxPlatformProvider(Runner, LoggerFactory.CreateLogger<LinuxPlatformProvider>());

    private static readonly UserSettings Settings = UserSettings.Load(Platform, LoggerFactory.CreateLogger<UserSettings>());
    private static readonly AppIconCache IconCache = new(Platform, LoggerFactory.CreateLogger<AppIconCache>());
    private static readonly FileIconCache FileIconCache = new(Platform, LoggerFactory.CreateLogger<FileIconCache>());
    private static readonly ApplicationSearch AppSearch = new(Settings, Platform, IconCache, LoggerFactory.CreateLogger<ApplicationSearch>());
    private static readonly BrowserDiscovery Browsers = new(Settings, Platform, LoggerFactory.CreateLogger<BrowserDiscovery>());
    private static readonly TerminalDiscovery Terminals = new(Settings, Platform, LoggerFactory.CreateLogger<TerminalDiscovery>());
    private static readonly FileSearch FileSearch = new(Platform);

    private static async Task Main(string[] args) {
        AppSearch.Start();
        await AppSearch.WhenReady();

        if (args.Length == 0) {
            await RunInteractiveAsync();
            return;
        }

        await DispatchAsync(args);
        Console.WriteLine();
    }

    private static async Task RunInteractiveAsync() {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Yottacast CLI — interactive mode  (type 'help' or 'exit')");
        Console.ResetColor();

        while (true) {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("\nyc> ");
            Console.ResetColor();

            var line = Console.ReadLine();
            if (line is null) break; // EOF / Ctrl+D

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (parts[0].Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

            await DispatchAsync(parts);
        }
    }

    private static async Task DispatchAsync(string[] args) {
        switch (args[0].ToLowerInvariant()) {
            case "b":
            case "browsers":
                CmdBrowsers();
                break;

            case "t":
            case "terminals":
                CmdTerminals();
                break;

            case "s":
            case "search":
                if (args.Length < 2) {
                    Warn("search requires a query argument.  Usage: search <query>");
                    break;
                }
                await CmdSearchAsync(string.Join(" ", args[1..]));
                break;

            case "r":
            case "run":
                if (args.Length < 2) {
                    Warn("run requires a binary argument.");
                    break;
                }
                var runArgs = args.Length >= 3 ? string.Join(" ", args[2..]) : string.Empty;
                await CmdRunAsync(args[1], runArgs);
                break;

            case "a":
            case "apps":
                CmdApps();
                break;

            case "help":
                Usage();
                break;

            default:
                Warn($"Unknown command: {args[0]}");
                Usage();
                break;
        }
    }

    // ─── commands ────────────────────────────────────────────────────────────────

    static void CmdBrowsers() {
        Header("Browser Discovery");
        var installed = Browsers.Discover();
        if (installed.Count == 0) {
            Warn("No browsers found.");
            return;
        }
        foreach (var b in installed)
            Ok($"{b.Name,-20} → {b.ExecutablePath}");
        Console.WriteLine($"\n  {installed.Count} installed");
    }

    static void CmdTerminals() {
        Header("Terminal Discovery");
        var installed = Terminals.Discover();
        if (installed.Count == 0) {
            Warn("No terminals found.");
            return;
        }
        foreach (var t in installed)
            Ok($"{t.Name,-20} → {t.ExecutablePath}");
        Console.WriteLine($"\n  {installed.Count} installed");
    }

    static async Task CmdRunAsync(string binary, string runArgs) {
        Header($"RunAsync {binary} {runArgs}");

        var argArray = string.IsNullOrEmpty(runArgs) ? [] : runArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await Runner.RunAsync(
            binary, argArray, Environment.CurrentDirectory,
            line => { Console.WriteLine(line); return true; },
            cts.Token);

        Header($"Exit code: {result.ExitCode} ({result.Elapsed.TotalMilliseconds:F0} ms)");
        if (result.Cancelled) Warn("Process was cancelled or timed out.");
    }

    static void CmdApps() {
        Header("Applications in AppStorage");
        var apps = AppSearch.FindAll().OrderBy(a => a.Path).ToList();
        if (apps.Count == 0) {
            Warn("No applications found.");
            return;
        }
        foreach (var app in apps)
            Ok($"{app.Name,-40} {app.Path}");
        Console.WriteLine($"\n  {apps.Count} application(s)");
    }

    static async Task CmdSearchAsync(string query) {
        Header($"File Search: \"{query}\"");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await FileSearch.SearchAsync(query, r => Ok($"{r.Name,-40} {r.Path}"), 30, ct: cts.Token);
        sw.Stop();
        Console.WriteLine($"\n  {sw.Elapsed.TotalMilliseconds:F0} ms");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────

    static void Usage() {
        Console.WriteLine("""
                          Yottacast CLI — service tester

                          USAGE:
                            yc browsers
                            yc terminals
                            yc apps
                            yc search <query words...>
                            yc run <binary> [args...]

                          EXAMPLES:
                            yc browsers
                            yc terminals
                            yc apps
                            yc search my project readme
                            yc run ls -l
                          """);
    }

    static void Header(string title) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n── {title} ──────────────────────────────────");
        Console.ResetColor();
    }

    static void Ok(string msg) {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  ✓ ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    static void Warn(string msg) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  ! ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    static void Miss(string msg) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ✗ ");
        Console.WriteLine(msg);
        Console.ResetColor();
    }
}
