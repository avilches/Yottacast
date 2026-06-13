using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class WindowsPlatformProvider(ProcessRunner runner, ILogger<WindowsPlatformProvider> logger) : PlatformProvider {

    // ── Dark mode ─────────────────────────────────────────────────────────────

    public override bool? IsSystemDarkMode() {
        try {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 0;
#pragma warning restore CA1416
            return null;
        } catch {
            return null;
        }
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    public override List<string> DefaultAppDirectories() => [
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
    ];

    public override List<string> DefaultSearchFolders() => [
        "$HOME/Downloads",
        "$HOME/Desktop",
        "$HOME/Documents",
        "$HOME/Videos",
        "$HOME/Pictures",
    ];

    // ── App scanning ──────────────────────────────────────────────────────────

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        foreach (var dir in dirs.Where(Directory.Exists)) {
            ScanExesRecursive(dir, dir, 0, addApp, ct);
        }
        return Task.CompletedTask;
    }

    private static void ScanExesRecursive(
        string root, string dir, int depth, Action<string> addApp, CancellationToken ct) {
        if (ct.IsCancellationRequested) return;
        try {
            foreach (var exe in Directory.EnumerateFiles(dir, "*.exe")) {
                if (IsLaunchableAppExe(exe)) addApp(exe);
            }
        } catch { /* access denied on a subdir: skip it */ }

        if (depth >= AppDefaults.WindowsAppScanMaxDepth) return;
        try {
            foreach (var subDir in Directory.EnumerateDirectories(dir)) {
                ScanExesRecursive(root, subDir, depth + 1, addApp, ct);
            }
        } catch { /* access denied: skip */ }
    }

    /// <summary>
    /// Shared predicate between the initial scan and the watcher: filters out
    /// uninstallers, updaters and crash-handler executables that are not real apps.
    /// </summary>
    internal static bool IsLaunchableAppExe(string exePath) {
        var name = Path.GetFileNameWithoutExtension(exePath);
        foreach (var excluded in AppDefaults.WindowsAppExeExcludeSubstrings) {
            if (name.Contains(excluded, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs.Where(Directory.Exists)) {
            var root = dir;
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.exe",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => { if (WithinScanScope(root, e.FullPath)) onAdded(e.FullPath); };
            watcher.Deleted += (_, e) => { if (WithinScanScope(root, e.FullPath)) onRemoved(e.FullPath); };
            watchers.Add(watcher);
        }
        return watchers;
    }

    /// <summary>
    /// True when <paramref name="exePath"/> is a launchable app exe within the same depth
    /// limit the initial scan applies, so the watcher and the scan stay consistent.
    /// </summary>
    private static bool WithinScanScope(string root, string exePath) {
        if (!IsLaunchableAppExe(exePath)) return false;
        var relative = Path.GetRelativePath(root, exePath);
        // Segments minus the file itself = directory depth of the exe.
        var depth = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
        return depth <= AppDefaults.WindowsAppScanMaxDepth;
    }

    public override void LaunchApp(string path) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo(path) { UseShellExecute = true });
        } catch { }
    }

    public override void RevealInFileManager(string directoryPath) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo("explorer.exe", $"\"{directoryPath}\"") { UseShellExecute = false });
        } catch { }
    }

    public override void OpenFile(string filePath) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo(filePath) { UseShellExecute = true });
        } catch { }
    }

    // ── Running apps ──────────────────────────────────────────────────────────

    public override IReadOnlyList<RunningAppInfo> GetRunningApps() {
        try {
            return Process.GetProcesses()
                .Select(p => {
                    try {
                        var path = p.MainModule?.FileName;
                        return path is null ? null : new RunningAppInfo(path, p.Id);
                    } catch {
                        return null;
                    }
                })
                .OfType<RunningAppInfo>()
                .ToList();
        } catch {
            return [];
        }
    }

    public override void QuitApp(int pid) {
        try { Process.GetProcessById(pid).CloseMainWindow(); } catch { }
    }

    public override void ForceQuitApp(int pid) {
        try { Process.GetProcessById(pid).Kill(); } catch { }
    }

    // ── File search ───────────────────────────────────────────────────────────

    public override Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct) {
        var count = 0;
        Func<string, bool> onLine = line => {
            onResult(new FileResult(Path.GetFileName(line), line));
            return ++count < maxResults;
        };

        var safeQuery = query.Replace("'", "").Replace("\"", "").Replace("*", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;

        var scopeFilter = folders?.Count > 0
            ? "AND (" + string.Join(" OR ", folders.Select(f =>
                $"System.ItemPathDisplay LIKE '{f.Replace("'", "''")}%'")) + ")"
            : "";

        var queryTokens = safeQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var containsClause = string.Join(" AND ", queryTokens.Select(t => $"CONTAINS(System.FileName, '{t}*')"));

        var script = $$"""
            $c = New-Object -ComObject ADODB.Connection
            $c.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
            $sql = "SELECT System.ItemPathDisplay FROM SystemIndex WHERE {{containsClause}} {{scopeFilter}}"
            $rs  = $c.Execute($sql)
            while (-not $rs.EOF) { $rs.Fields.Item(0).Value; [void]$rs.MoveNext() }
            $c.Close()
            """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return runner.RunAsync("powershell",
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cwd, onLine, ct);
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string[]> _browserKnownPaths =
        new Dictionary<string, string[]> {
            ["Google Chrome"]   = [@"C:\Program Files\Google\Chrome\Application\chrome.exe",
                                   @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"],
            ["Mozilla Firefox"] = [@"C:\Program Files\Mozilla Firefox\firefox.exe",
                                   @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"],
            ["Microsoft Edge"]  = [@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"],
            ["Brave Browser"]   = [@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"],
            ["Opera"]           = [@"C:\Program Files\Opera\opera.exe"],
            ["Vivaldi"]         = [@"C:\Program Files\Vivaldi\Application\vivaldi.exe"],
        };

    public override string[] KnownBrowserNames => [.. _browserKnownPaths.Keys];
    public override IReadOnlyDictionary<string, string[]> BrowserKnownPaths => _browserKnownPaths;

    public override void OpenUrl(string url, string browserName) {
        try {
            var paths = _browserKnownPaths.TryGetValue(browserName, out var p) ? p : [];
            var exePath = paths.FirstOrDefault(File.Exists);
            if (exePath is null) return;
            Process.Start(new ProcessStartInfo {
                FileName = exePath,
                ArgumentList = { url },
                UseShellExecute = false,
            });
        } catch { }
    }

    // ── Terminal ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string[]> _terminalKnownPaths =
        new Dictionary<string, string[]> {
            // The WindowsApps execution-alias stub (no glob) is the canonical, always-present
            // launcher for an installed Windows Terminal; the glob under Program Files\WindowsApps
            // resolves the versioned package install when the alias is unavailable.
            ["Windows Terminal"] = [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             @"Microsoft\WindowsApps\wt.exe"),
                @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe",
            ],
            ["PowerShell"]       = [@"C:\Program Files\PowerShell\7\pwsh.exe",
                                    @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"],
            ["Command Prompt"]   = [@"C:\Windows\System32\cmd.exe"],
            ["Git Bash"]         = [@"C:\Program Files\Git\bin\bash.exe",
                                    @"C:\Program Files (x86)\Git\bin\bash.exe"],
        };

    public override string[] KnownTerminalNames => [.. _terminalKnownPaths.Keys];
    public override IReadOnlyDictionary<string, string[]> TerminalKnownPaths => _terminalKnownPaths;

    /// <summary>
    /// Resolves a known terminal path to an existing executable. A literal path is returned if it
    /// exists; a path containing a <c>*</c> glob is expanded (the glob may only appear in a single
    /// directory segment, e.g. <c>...\WindowsApps\Microsoft.WindowsTerminal*\wt.exe</c>).
    /// Returns null when nothing matches.
    /// </summary>
    public override string? ResolveKnownPath(string knownPath) {
        if (!knownPath.Contains('*'))
            return File.Exists(knownPath) ? knownPath : null;

        var fileName = Path.GetFileName(knownPath);
        var dirPattern = Path.GetDirectoryName(knownPath);
        if (dirPattern is null) return null;

        var parent = Path.GetDirectoryName(dirPattern);
        var dirGlob = Path.GetFileName(dirPattern);
        if (parent is null || string.IsNullOrEmpty(dirGlob) || !Directory.Exists(parent)) return null;

        try {
            foreach (var match in Directory.EnumerateDirectories(parent, dirGlob)) {
                var candidate = Path.Combine(match, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        } catch { /* access denied: treat as not found */ }
        return null;
    }

    public override void ExecuteCommand(string command, string terminalName) {
        var paths = _terminalKnownPaths.TryGetValue(terminalName, out var p) ? p : Array.Empty<string>();
        var exePath = paths.Select(ResolveKnownPath).FirstOrDefault(r => r is not null);
        if (string.IsNullOrEmpty(exePath)) return;

        try {
            System.Diagnostics.Process.Start(new ProcessStartInfo {
                FileName = exePath,
                Arguments = BuildTerminalArgs(command, terminalName),
                UseShellExecute = true,
            });
        } catch { }
    }

    /// <summary>
    /// Builds the command-line arguments to run <paramref name="command"/> in the given terminal.
    /// Git Bash must receive the command via <c>-c "..."</c>; a bare argument is interpreted as a
    /// script path and the command never runs.
    /// </summary>
    internal static string BuildTerminalArgs(string command, string terminalName) => terminalName switch {
        "PowerShell"     => $"-NoExit -Command \"{command.Replace("\"", "\\\"")}\"",
        "Command Prompt" => $"/K \"{command}\"",
        "Git Bash"       => $"-c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        _                => command,
    };

    // ── Icon ──────────────────────────────────────────────────────────────────

}
