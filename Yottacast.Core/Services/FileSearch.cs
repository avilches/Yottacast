using System.Runtime.InteropServices;
using System.Text;
using Pty.Net;

namespace Yottacast.Services;

public enum FileSearchMode {
    ByName,
    Interpret
}

public record FileResult(string Name, string Path);

/// <summary>
/// Searches files using the OS native index, streaming results in real-time via PTY.
/// Running the subprocess under a PTY forces line-buffering, so results arrive
/// as they are found instead of filling a pipe buffer first.
///   macOS   → Spotlight (mdfind)
///   Windows → Windows Search Index (via PowerShell + OLE DB)
///   Linux   → plocate / locate
/// </summary>
public static class FileSearch {
    public static Task SearchAsync(
        string query, Action<FileResult> onResult, int maxResults = 20,
        FileSearchMode mode = FileSearchMode.ByName, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SpotlightAsync(query, maxResults, mode, onResult, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsSearchAsync(query, maxResults, onResult, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LocateAsync(query, maxResults, onResult, ct);
        return Task.CompletedTask;
    }

    // ── PTY helper ────────────────────────────────────────────────────────────

    private static async Task RunWithPtyAsync(
        string binary, string[] args, string cwd,
        int maxResults, Action<FileResult> onResult, CancellationToken ct) {

        var options = new PtyOptions {
            Name = "xterm-256color",
            App = binary,
            CommandLine = args,
            Cwd = cwd,
            Rows = 24,
            Cols = 220,
            Environment = new Dictionary<string, string>(),
        };

        IPtyConnection pty;
        try {
            pty = await PtyProvider.SpawnAsync(options, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            return;
        }

        try {
            using var reader = new StreamReader(pty.ReaderStream, Encoding.UTF8, leaveOpen: true);
            int count = 0;
            try {
                while (true) {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    line = line.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    onResult(new FileResult(Path.GetFileName(line), line));
                    if (++count >= maxResults) {
                        try { pty.Kill(); } catch { }
                        break;
                    }
                }
            } catch (OperationCanceledException) {
                try { pty.Kill(); } catch { }
            } catch {
                // process exited or stream closed — normal end
            }
        } finally {
            try { pty.Dispose(); } catch { }
        }
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    private static Task SpotlightAsync(
        string query, int maxResults, FileSearchMode mode, Action<FileResult> onResult, CancellationToken ct) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] args;
        if (mode == FileSearchMode.Interpret) {
            if (string.IsNullOrEmpty(query)) return Task.CompletedTask;
            // Args are passed directly to mdfind via execv — no shell quoting needed
            args = ["-onlyin", home, "-interpret", query];
        } else {
            // Only escape ' because it delimits the string inside the mdfind predicate syntax.
            // No shell escaping needed: args are passed as an array, not through a shell.
            var safeQuery = query.Replace("'", "\\'");
            if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;
            var pattern = safeQuery.Contains('*') ? safeQuery : $"*{safeQuery}*";
            var predicate = $"kMDItemFSName == '{pattern}'cd";
            args = ["-onlyin", home, predicate];
        }

        return RunWithPtyAsync("/usr/bin/mdfind", args, home, maxResults, onResult, ct);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private static Task WindowsSearchAsync(
        string query, int maxResults, Action<FileResult> onResult, CancellationToken ct) {
        // Strip characters that break OLE DB CONTAINS syntax
        var safeQuery = query.Replace("'", "").Replace("\"", "").Replace("*", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;

        // $$"""...""" → single $ is literal (PowerShell vars); {{expr}} is C# interpolation.
        // Encoded as UTF-16LE base64 via -EncodedCommand to avoid quoting issues.
        var script = $$"""
            $c = New-Object -ComObject ADODB.Connection
            $c.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
            $sql = "SELECT TOP {{maxResults}} System.ItemPathDisplay FROM SystemIndex WHERE CONTAINS(System.FileName, '{{safeQuery}}*')"
            $rs  = $c.Execute($sql)
            while (-not $rs.EOF) { $rs.Fields.Item(0).Value; [void]$rs.MoveNext() }
            $c.Close()
            """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return RunWithPtyAsync("powershell",
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cwd, maxResults, onResult, ct);
    }

    // ── Linux ────────────────────────────────────────────────────────────────

    private static Task LocateAsync(
        string query, int maxResults, Action<FileResult> onResult, CancellationToken ct) {
        var binary = File.Exists("/usr/bin/plocate") ? "/usr/bin/plocate" : "/usr/bin/locate";
        var safeQuery = query.Replace("\"", "");
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return RunWithPtyAsync(binary,
            ["-b", "-l", maxResults.ToString(), $"*{safeQuery}*"],
            cwd, maxResults, onResult, ct);
    }
}
