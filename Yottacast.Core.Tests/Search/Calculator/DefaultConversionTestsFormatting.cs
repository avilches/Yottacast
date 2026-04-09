using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Yottacast.Core.Tests.Search.Calculator;

// Auto-formats DefaultConversionTests.cs so the TheoryData rows stay column-aligned.
//
// Each TheoryData<string, string> block is rewritten to align:
//   - the query column (key)
//   - the "from" cell, sub-aligned by short/long on " / "
//   - the optional "norm" cell (when any row in the block has " > NORM "), same sub-align
//   - the "to" cell, sub-aligned the same way
// Cells without a long form (no " / ") leave the long sub-column blank with spaces.
// Rows in a mixed block without a norm cell get spaces where " > NORM " would be.
//
// At assert time CalculatorSearch tests collapse runs of spaces, so the visual padding
// here has no functional effect — the on-disk source just stays human-readable.
//
// Local run: if the file is not aligned the test rewrites it on disk and fails so the
// developer re-runs. CI run (CI=true): the test only fails, never mutates the source.
public class DefaultConversionTestsFormatting {

    private static string GetSiblingFilePath([CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "DefaultConversionTests.cs");

    [Fact]
    public void DefaultConversionTests_IsColumnAligned() {
        var path = GetSiblingFilePath();
        Assert.True(File.Exists(path), $"Source file not found at {path}");
        var original = File.ReadAllText(path);
        var formatted = TheoryDataAligner.Format(original);
        if (formatted == original) return;

        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
            StringComparison.OrdinalIgnoreCase);
        if (isCi) {
            Assert.Fail("DefaultConversionTests.cs TheoryData rows are not column-aligned. " +
                        "Run `dotnet test` locally to auto-format the file.");
        }
        File.WriteAllText(path, formatted);
        Assert.Fail("DefaultConversionTests.cs was re-aligned on disk. Re-run the tests.");
    }
}

internal static class TheoryDataAligner {

    private static readonly Regex HeaderRe = new(
        @"^(\s*)public static TheoryData<string,\s*string>\s+\w+\s*=>\s*new\(\)\s*\{\s*$",
        RegexOptions.Compiled);

    private static readonly Regex EndRe = new(@"^\s*\};\s*$", RegexOptions.Compiled);

    private static readonly Regex RowRe = new(
        @"^(\s*)\{\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\}\s*,?\s*(//.*)?\s*$",
        RegexOptions.Compiled);

    public static string Format(string source) {
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";
        var lines = source.Split(newline).ToList();
        var i = 0;
        while (i < lines.Count) {
            if (!HeaderRe.IsMatch(lines[i])) { i++; continue; }
            var j = i + 1;
            while (j < lines.Count && !EndRe.IsMatch(lines[j])) j++;
            if (j >= lines.Count) break;
            var bodyCount = j - i - 1;
            var body = lines.GetRange(i + 1, bodyCount);
            var newBody = ReformatBody(body);
            lines.RemoveRange(i + 1, bodyCount);
            lines.InsertRange(i + 1, newBody);
            i = i + 1 + newBody.Count + 1;
        }
        return string.Join(newline, lines);
    }

    private sealed record Row(
        string Indent, string Query,
        string FromShort, string FromLong,
        string Norm, string NormShort, string NormLong,
        string ToShort, string ToLong,
        string Comment);

    private static (string Short, string Long) SplitSlash(string cell) {
        var idx = cell.IndexOf(" / ", StringComparison.Ordinal);
        return idx < 0 ? (cell, "") : (cell[..idx], cell[(idx + 3)..]);
    }

    private static List<string> ReformatBody(List<string> body) {
        var parsed = new List<(bool IsRow, string Raw, Row? Row)>();
        foreach (var line in body) {
            var m = RowRe.Match(line);
            if (!m.Success) { parsed.Add((false, line, null)); continue; }

            var indent = m.Groups[1].Value;
            var q = m.Groups[2].Value;
            var expected = m.Groups[3].Value;
            var comment = m.Groups[4].Success ? m.Groups[4].Value : "";

            // Normalize: collapse runs of spaces in the existing string so the parse
            // is independent of any previous padding.
            expected = string.Join(' ', expected.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            var arrow = expected.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0) { parsed.Add((false, line, null)); continue; }
            var left = expected[..arrow];
            var to = expected[(arrow + 4)..];

            string frm, norm;
            var gtIdx = left.IndexOf(" > ", StringComparison.Ordinal);
            if (gtIdx >= 0) { frm = left[..gtIdx]; norm = left[(gtIdx + 3)..]; }
            else { frm = left; norm = ""; }

            var (fs, fl) = SplitSlash(frm);
            var (ns, nl) = norm.Length > 0 ? SplitSlash(norm) : ("", "");
            var (ts, tl) = SplitSlash(to);

            parsed.Add((true, line, new Row(indent, q, fs, fl, norm, ns, nl, ts, tl, comment)));
        }

        var rows = parsed.Where(p => p.IsRow).Select(p => p.Row!).ToList();
        if (rows.Count == 0) return body;

        var qw = rows.Max(r => r.Query.Length);

        var fsw = rows.Max(r => r.FromShort.Length);
        var flw = rows.Max(r => r.FromLong.Length);
        var fHasLong = rows.Any(r => r.FromLong.Length > 0);

        var nsw = rows.Max(r => r.NormShort.Length);
        var nlw = rows.Max(r => r.NormLong.Length);
        var nHasLong = rows.Any(r => r.NormLong.Length > 0);
        var hasNorm = rows.Any(r => r.Norm.Length > 0);

        var tsw = rows.Max(r => r.ToShort.Length);
        var tlw = rows.Max(r => r.ToLong.Length);
        var tHasLong = rows.Any(r => r.ToLong.Length > 0);

        static string RenderCell(string s, string l, int sw, int lw, bool hasLong) {
            var sp = s.PadRight(sw);
            if (!hasLong) return sp;
            return l.Length > 0
                ? $"{sp} / {l.PadRight(lw)}"
                : $"{sp}   {string.Empty.PadRight(lw)}";
        }

        var output = new List<string>();
        foreach (var p in parsed) {
            if (!p.IsRow) { output.Add(p.Raw); continue; }
            var r = p.Row!;
            var fromCell = RenderCell(r.FromShort, r.FromLong, fsw, flw, fHasLong);
            var toCell = RenderCell(r.ToShort, r.ToLong, tsw, tlw, tHasLong);

            string inner;
            if (hasNorm) {
                if (r.Norm.Length > 0) {
                    var normCell = RenderCell(r.NormShort, r.NormLong, nsw, nlw, nHasLong);
                    inner = $"{fromCell} > {normCell} -> {toCell}";
                } else {
                    var normTotal = nsw + (nHasLong ? 3 + nlw : 0);
                    inner = $"{fromCell}   {string.Empty.PadRight(normTotal)} -> {toCell}";
                }
            } else {
                inner = $"{fromCell} -> {toCell}";
            }
            inner = inner.TrimEnd();

            var qStr = $"\"{r.Query}\",";
            var rendered = $"{r.Indent}{{ {qStr.PadRight(qw + 3)} \"{inner}\" }},";
            if (r.Comment.Length > 0) rendered = rendered + " " + r.Comment;
            output.Add(rendered);
        }
        return output;
    }
}
