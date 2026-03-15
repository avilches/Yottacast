using System.Runtime.InteropServices;
using System.Text;
using Yottacast.Core.Process;

namespace Yottacast.Core.Services;

public enum FileSearchMode {
    ByName,
    Interpret
}

public record FileResult(string Name, string Path);

/// <summary>
/// Searches files using the OS native index, streaming results in real-time.
/// Running the subprocess under a PTY (default) forces line-buffering, so results arrive
/// as they are found instead of filling a pipe buffer first.
///   macOS   → Spotlight (mdfind)
///   Windows → Windows Search Index (via PowerShell + OLE DB)
///   Linux   → plocate / locate
/// </summary>
public static class FileSearch {
    public static Task SearchAsync(
        string query, Action<FileResult> onResult, int maxResults = 10,
        FileSearchMode mode = FileSearchMode.ByName,
        RunnerBackend backend = RunnerBackend.Pty,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;

        ICommandRunner runner = backend == RunnerBackend.Pty
            ? PtyRunner.Instance
            : StandardCommandRunner.Instance;

        var count = 0;
        Func<string, bool> onLine = line => {
            onResult(new FileResult(Path.GetFileName(line), line));
            return ++count < maxResults;
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SpotlightAsync(query, mode, onLine, runner, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsSearchAsync(query, onLine, runner, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LocateAsync(query, maxResults, onLine, runner, ct);
        return Task.CompletedTask;
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    private static Task SpotlightAsync(
        string query, FileSearchMode mode,
        Func<string, bool> onLine, ICommandRunner runner, CancellationToken ct) {
        
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] args;
        if (mode == FileSearchMode.Interpret) {
            if (string.IsNullOrEmpty(query)) return Task.CompletedTask;
            args = ["-onlyin", home, "-interpret", query];
        } else {
            var safeQuery = query.Replace("'", "\\'");
            if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;
            var pattern = safeQuery.Contains('*') ? safeQuery : $"*{safeQuery}*";
            var predicate = $"kMDItemFSName == '{pattern}'cd";
            args = ["-onlyin", home, predicate];
        }
        return runner.RunAsync("/usr/bin/mdfind", args, home, onLine, ct);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private static Task WindowsSearchAsync(
        string query,
        Func<string, bool> onLine, ICommandRunner runner, CancellationToken ct) {
        
        var safeQuery = query.Replace("'", "").Replace("\"", "").Replace("*", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;

        var script = $$"""
            $c = New-Object -ComObject ADODB.Connection
            $c.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
            $sql = "SELECT System.ItemPathDisplay FROM SystemIndex WHERE CONTAINS(System.FileName, '{{safeQuery}}*')"
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

    // ── Linux ────────────────────────────────────────────────────────────────

    private static Task LocateAsync(
        string query, int maxResults,
        Func<string, bool> onLine, ICommandRunner runner, CancellationToken ct) {
        var binary = File.Exists("/usr/bin/plocate") ? "/usr/bin/plocate" : "/usr/bin/locate";
        var safeQuery = query.Replace("\"", "");
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return runner.RunAsync(binary,
            ["-b", "-l", maxResults.ToString(), $"*{safeQuery}*"],
            cwd, onLine, ct);
    }
}
