using System.Runtime.InteropServices;
using System.Text;
using Yottacast.Core.Process;

namespace Yottacast.Core.Services;

public record FileResult(string Name, string Path);

/// <summary>
/// Searches user documents using the OS native index, streaming results in real-time.
/// Running the subprocess under a PTY (default) forces line-buffering, so results arrive
/// as they are found instead of filling a pipe buffer first.
///   macOS   → Spotlight (mdfind)
///   Windows → Windows Search Index (via PowerShell + OLE DB)
///   Linux   → plocate / locate
/// </summary>
public static class FileSearch {
    public static Task SearchAsync(
        string query, Action<FileResult> onResult, int maxResults = 10,
        RunnerBackend backend = RunnerBackend.Pty,
        IReadOnlyList<string>? searchFolders = null,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;

        var count = 0;
        Func<string, bool> onLine = line => {
            onResult(new FileResult(System.IO.Path.GetFileName(line), line));
            return ++count < maxResults;
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SpotlightAsync(query, onLine, backend, searchFolders, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsSearchAsync(query, onLine, backend, searchFolders, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LocateAsync(query, maxResults, onLine, backend, searchFolders, ct);
        return Task.CompletedTask;
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    private static Task SpotlightAsync(
        string query, Func<string, bool> onLine, RunnerBackend backend,
        IReadOnlyList<string>? searchFolders, CancellationToken ct) {

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = searchFolders?.Where(Directory.Exists).ToList();
        var scope = folders?.Count > 0 ? folders : [home];
        var onlyInArgs = scope.SelectMany(f => new[] { "-onlyin", f }).ToList();

        var safeQuery = query.Replace("'", "\\'");
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;
        var pattern = safeQuery.Contains('*') ? safeQuery : $"*{safeQuery}*";
        var predicate = $"kMDItemFSName == '{pattern}'cd";
        return CommandRunner.RunAsync(backend, "/usr/bin/mdfind", [.. onlyInArgs, predicate], home, onLine, ct);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private static Task WindowsSearchAsync(
        string query,
        Func<string, bool> onLine, RunnerBackend backend,
        IReadOnlyList<string>? searchFolders, CancellationToken ct) {

        var safeQuery = query.Replace("'", "").Replace("\"", "").Replace("*", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;

        var scopeFilter = searchFolders?.Count > 0
            ? "AND (" + string.Join(" OR ", searchFolders.Select(f =>
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

    // ── Linux ────────────────────────────────────────────────────────────────

    private static Task LocateAsync(
        string query, int maxResults,
        Func<string, bool> onLine, RunnerBackend backend,
        IReadOnlyList<string>? searchFolders, CancellationToken ct) {
        var binary = File.Exists("/usr/bin/plocate") ? "/usr/bin/plocate" : "/usr/bin/locate";
        var safeQuery = query.Replace("\"", "");
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Func<string, bool> filteredOnLine = string.IsNullOrEmpty(searchFolders?.FirstOrDefault())
            ? onLine
            : line => searchFolders!.Any(f => line.StartsWith(f, StringComparison.Ordinal)) && onLine(line);

        return CommandRunner.RunAsync(backend, binary,
            ["-b", "-l", maxResults.ToString(), $"*{safeQuery}*"],
            cwd, filteredOnLine, ct);
    }
}
