namespace Yottacast.Services;

internal static class Program {
    static async Task Main(string[] args) {
        if (args.Length == 0) {
            await RunInteractiveAsync();
            return;
        }

        await DispatchAsync(args);
        Console.WriteLine();
    }

    static async Task RunInteractiveAsync() {
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

    static async Task DispatchAsync(string[] args) {
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
        var candidates = BrowserDiscovery.GetCandidatePaths();
        if (candidates.Count == 0) {
            Warn("No browsers found.");
            return;
        }
        int found = 0;
        foreach (var (name, path) in candidates) {
            bool exists = Directory.Exists(path) || File.Exists(path);
            if (exists) {
                Ok($"{name,-20} → {path}");
                found++;
            } else Miss($"{name,-20} → {path}");
        }
        Console.WriteLine($"\n  {found}/{candidates.Count} installed");
    }

    static void CmdTerminals() {
        Header("Terminal Discovery");
        var candidates = TerminalDiscovery.GetCandidatePaths();
        if (candidates.Count == 0) {
            Warn("No terminals found.");
            return;
        }
        int found = 0;
        foreach (var (name, path) in candidates) {
            bool exists = Directory.Exists(path) || File.Exists(path);
            if (exists) {
                Ok($"{name,-20} → {path}");
                found++;
            } else Miss($"{name,-20} → {path}");
        }
        Console.WriteLine($"\n  {found}/{candidates.Count} installed");
    }

    static async Task CmdRunAsync(string binary, string runArgs) {
        Header($"RunAsync {binary} {runArgs}");

        using var handle = ProcessRunner.Execute(binary, runArgs,
            timeout: TimeSpan.FromSeconds(10),
            onLine: Console.WriteLine);

        var result = await handle.Completion;
        Console.Write("\r                    \r"); // clear spinner
        Console.WriteLine($"  Elapsed   : {result.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  Exit code : {result.ExitCode}");
        if (result.Cancelled) Warn("Process was cancelled or timed out.");
    }

    static async Task CmdSearchAsync(string query) {
        Header($"File Search: \"{query}\"");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await FileSearch.SearchAsync(query, r => Ok($"{r.Name,-40} {r.Path}"), 100, ct: cts.Token);
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
                            yc search <query words...>
                            yc run <binary> [args...]

                          EXAMPLES:
                            yc browsers
                            yc terminals
                            yc search my project readme
                            yc run ls -l
                          """);
    }

    static void Header(string title) {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n── {title} ──────────────────────────────────");
        Console.ResetColor();
    }

    static void Log(string msg) {
        Console.ResetColor();
        Console.WriteLine(msg);
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