using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yottacast.Services;

public enum FileSearchMode {
    ByName,
    Interpret
}

public record FileResult(string Name, string Path);
    
/// <summary>
/// Searches files using the OS native index:
///   macOS   → Spotlight (mdfind)
///   Windows → Windows Search Index (via OLE DB / PowerShell)
///   Linux   → plocate / locate
/// </summary>
public static class FileSearch {
    public static Task SearchAsync(
        string query, Action<FileResult> onResult, int maxResults = 20, FileSearchMode mode = FileSearchMode.ByName, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SpotlightAsync(query, maxResults, mode, onResult, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsSearchAsync(query, maxResults, onResult, ct);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LocateAsync(query, maxResults, onResult, ct);
        return Task.CompletedTask;
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    private static async Task SpotlightAsync(
        string query, int maxResults, FileSearchMode mode, Action<FileResult> onResult, CancellationToken ct) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var safePath = home.Replace("\"", "");
        string args;
        if (mode == FileSearchMode.Interpret) {
            // -interpret → full-text search using natural language / Spotlight query syntax
            var safeQuery = query.Replace("\"", "\\\"");
            if (string.IsNullOrEmpty(safeQuery)) return;
            args = $"-onlyin \"{safePath}\" -interpret \"{safeQuery}\"";
        } else {
            // Predicate kMDItemFSName == "*query*"cd → substring match on filename
            //   c = case-insensitive, d = diacritic-insensitive
            // Much faster than -interpret (full-text) and correct unlike -name (exact match).
            // UseShellExecute=false: pass predicate as a double-quoted arg; inner * are literal.
            // Escape ' and \ for mdfind predicate; * is allowed (used for wildcards).
            // If the query already contains *, use it verbatim; otherwise wrap with *...*
            var safeQuery = query.Replace("\\", @"\\").Replace("'", "\\'");
            if (string.IsNullOrEmpty(safeQuery)) return;
            
            var pattern = safeQuery.Contains('*') ? safeQuery : $"*{safeQuery}*";
            var predicate = $"kMDItemFSName == '{pattern}'cd";
            args = $"-onlyin \"{safePath}\" \"{predicate}\"";
        }
        int count = 0;
        var limitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var handle = ProcessRunner.Execute("mdfind", args, onLine: line => {
            if (string.IsNullOrWhiteSpace(line)) return;
            onResult(new FileResult(Path.GetFileName(line), line));
            if (++count >= maxResults) limitCts.Cancel();
        }, ct: limitCts.Token);
        await handle.Completion;
        limitCts.Dispose(); // disposed after Completion → no more onLine callbacks can fire
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    private static async Task WindowsSearchAsync(
        string query, int maxResults, Action<FileResult> onResult, CancellationToken ct) {
        // Strip characters that break OLE DB CONTAINS syntax
        var safeQuery = query.Replace("'", "").Replace("\"", "").Replace("*", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return;

        // PowerShell script – uses ADODB.Connection against the Windows Search index.
        // $$""" → single $ is literal (PowerShell vars); {{expr}} is C# interpolation.
        // Encoded as UTF-16LE base64 via -EncodedCommand to avoid quoting nightmares.
        var script = $$"""
                       $c = New-Object -ComObject ADODB.Connection
                       $c.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
                       $sql = "SELECT TOP {{maxResults}} System.ItemPathDisplay FROM SystemIndex WHERE CONTAINS(System.FileName, '{{safeQuery}}*')"
                       $rs  = $c.Execute($sql)
                       while (-not $rs.EOF) { $rs.Fields.Item(0).Value; [void]$rs.MoveNext() }
                       $c.Close()
                       """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var count = 0;
        var limitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var handle = ProcessRunner.Execute("powershell", $"-NoProfile -NonInteractive -EncodedCommand {encoded}", onLine: line => {
            if (string.IsNullOrWhiteSpace(line)) return;
            onResult(new FileResult(Path.GetFileName(line), line));
            if (++count >= maxResults) limitCts.Cancel();
        }, ct: limitCts.Token);
        await handle.Completion;
    }

    // ── Linux ────────────────────────────────────────────────────────────────

    private static async Task LocateAsync(
        string query, int maxResults, Action<FileResult> onResult, CancellationToken ct) {
        // plocate is faster and more common on modern distros; fall back to locate
        var binary = File.Exists("/usr/bin/plocate") ? "plocate" : "locate";
        // -b  → match only the basename
        // -l  → limit results
        var safeQuery = query.Replace("\"", "");
        var count = 0;
        var limitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var handle = ProcessRunner.Execute(binary, $"-b -l {maxResults} \"*{safeQuery}*\"", onLine: line => {
            if (string.IsNullOrWhiteSpace(line)) return;
            onResult(new FileResult(Path.GetFileName(line), line));
            count++;
            if (++count >= maxResults) limitCts.Cancel();
        }, ct: limitCts.Token);
        await handle.Completion;
    }
}