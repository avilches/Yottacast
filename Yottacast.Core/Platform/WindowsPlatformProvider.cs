using System.Diagnostics;
using System.Text;
using Yottacast.Core.Process;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class WindowsPlatformProvider : PlatformProvider {
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

    public override List<string> DefaultSearchFolders() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents"),
            Path.Combine(home, "Videos"),
            Path.Combine(home, "Pictures"),
        ];
    }

    // ── App scanning ──────────────────────────────────────────────────────────

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        foreach (var dir in dirs.Where(Directory.Exists)) {
            foreach (var subDir in Directory.EnumerateDirectories(dir)) {
                var folderName = Path.GetFileName(subDir);
                var exe = Directory.EnumerateFiles(subDir, $"{folderName}.exe").FirstOrDefault()
                       ?? Directory.EnumerateFiles(subDir, "*.exe").FirstOrDefault();
                if (exe is not null) addApp(exe);
            }
        }
        return Task.CompletedTask;
    }

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs.Where(Directory.Exists)) {
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.exe",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => onAdded(e.FullPath);
            watcher.Deleted += (_, e) => onRemoved(e.FullPath);
            watchers.Add(watcher);
        }
        return watchers;
    }

    public override void LaunchApp(string path) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo(path) { UseShellExecute = true });
        } catch { }
    }

    // ── File search ───────────────────────────────────────────────────────────

    public override Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        RunnerBackend backend, IReadOnlyList<string>? folders, CancellationToken ct) {
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

        var script = $$"""
            $c = New-Object -ComObject ADODB.Connection
            $c.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
            $sql = "SELECT System.ItemPathDisplay FROM SystemIndex WHERE CONTAINS(System.FileName, '{{safeQuery}}*') {{scopeFilter}}"
            $rs  = $c.Execute($sql)
            while (-not $rs.EOF) { $rs.Fields.Item(0).Value; [void]$rs.MoveNext() }
            $c.Close()
            """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return CommandRunner.RunAsync(backend, "powershell",
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cwd, onLine, ct);
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string[]> _browserFallbackPaths =
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

    public override string[] KnownBrowserNames => [.. _browserFallbackPaths.Keys];
    public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths => _browserFallbackPaths;

    public override string[] GetBrowserPaths(string name) =>
        _browserFallbackPaths.TryGetValue(name, out var paths) ? paths : [];

    public override void OpenUrl(string url, string browserName) {
        try {
            var exePath = GetBrowserPaths(browserName).FirstOrDefault(File.Exists);
            if (exePath is null) return;
            System.Diagnostics.Process.Start(new ProcessStartInfo {
                FileName = exePath,
                ArgumentList = { url },
                UseShellExecute = false,
            });
        } catch { }
    }

    // ── Terminal ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string[]> _terminalFallbackPaths =
        new Dictionary<string, string[]> {
            ["Windows Terminal"] = [@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe"],
            ["PowerShell"]       = [@"C:\Program Files\PowerShell\7\pwsh.exe",
                                    @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"],
            ["Command Prompt"]   = [@"C:\Windows\System32\cmd.exe"],
            ["Git Bash"]         = [@"C:\Program Files\Git\bin\bash.exe",
                                    @"C:\Program Files (x86)\Git\bin\bash.exe"],
        };

    public override string[] KnownTerminalNames => [.. _terminalFallbackPaths.Keys];
    public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths => _terminalFallbackPaths;

    public override string[] GetTerminalPaths(string name) =>
        _terminalFallbackPaths.TryGetValue(name, out var paths) ? paths : [];

    public override void ExecuteCommand(string command, string terminalName) {
        var paths = GetTerminalPaths(terminalName);
        var exePath = paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p)) ?? "";
        if (string.IsNullOrEmpty(exePath)) return;

        var args = terminalName switch {
            "PowerShell"     => $"-NoExit -Command \"{command.Replace("\"", "\\\"")}\"",
            "Command Prompt" => $"/K \"{command}\"",
            _                => command,
        };

        try {
            System.Diagnostics.Process.Start(new ProcessStartInfo {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
            });
        } catch { }
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    public override string? GetAppIconPath(string appPath) => null;
}
