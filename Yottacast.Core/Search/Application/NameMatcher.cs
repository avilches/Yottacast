namespace Yottacast.Core.Search.Application;

public record NameMatchResult(
    double Score,
    string? Reason,
    IReadOnlyList<(int Start, int Length)>? Ranges
);

public static class NameMatcher {
    public static double Score(string name, string query) =>
        Match(name, query).Score;

    public static double Score(IReadOnlyList<string> tokens, string name, string query) {
        // Keep this overload for callers that pre-computed tokens.
        // Since Match() recomputes tokens internally, this overload just delegates.
        return Match(name, query).Score;
    }

    public static NameMatchResult Match(string name, string query) {
        var tokensWithPos = SplitTokensWithPositions(name);
        var tokens = tokensWithPos.Select(t => t.Token).ToList();

        var result = MatchWith(tokensWithPos, tokens, name, query);
        // All-lowercase query: also try as initials (same as if typed in uppercase)
        if (result.Score < 1.0 && query.All(char.IsLower)) {
            var upper = MatchWith(tokensWithPos, tokens, name, query.ToUpperInvariant());
            if (upper.Score > result.Score) result = upper;
        }
        return result;
    }

    private static NameMatchResult MatchWith(
        IReadOnlyList<(string Token, int Start)> tokensWithPos,
        IReadOnlyList<string> tokens,
        string name,
        string query) {

        // Exact match: typing the full name is the strongest possible signal
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            return new NameMatchResult(1.1, "Nombre exacto", [(0, name.Length)]);

        var queryHumps = SplitTokens(query);

        // Query made of only separators (e.g. "-", "_", "--") yields no humps;
        // without this guard the CamelHump loop below would match any name with score 1.0.
        if (queryHumps.Count == 0)
            return new NameMatchResult(0, null, null);

        // CamelHump: each query hump must be prefix of successive tokens
        for (var start = 0; start <= tokens.Count - queryHumps.Count; start++) {
            var match = true;
            for (var h = 0; h < queryHumps.Count; h++) {
                if (!tokens[start + h].StartsWith(queryHumps[h], StringComparison.OrdinalIgnoreCase)) {
                    match = false;
                    break;
                }
            }
            if (match) {
                var ranges = new List<(int Start, int Length)>();
                for (var h = 0; h < queryHumps.Count; h++)
                    ranges.Add((tokensWithPos[start + h].Start, queryHumps[h].Length));
                if (start == 0)
                    return new NameMatchResult(1.0, "CamelHump inicio", ranges);
                else
                    return new NameMatchResult(0.8, "CamelHump interior", ranges);
            }
        }

        // Initials fallback: "AM" → "Activity Monitor" (A+M)
        if (tokens.Count > 0) {
            var initials = string.Concat(tokens.Select(t => t[0]));
            if (initials.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
                var ranges = new List<(int Start, int Length)>();
                for (var j = 0; j < query.Length; j++)
                    ranges.Add((tokensWithPos[j].Start, 1));
                return new NameMatchResult(0.6, "Iniciales", ranges);
            }
        }

        // Multi-word abbreviation: "smifa" → tokens ["smiling","face",...] by consuming
        if (tokens.Count > 1) {
            for (var start = 0; start < tokensWithPos.Count; start++) {
                var (success, ranges) = TryMatchAbbrevWithRanges(tokensWithPos, start, query);
                if (success)
                    return new NameMatchResult(0.4, "Abreviatura", ranges);
            }
        }

        // Internal substring (3+ chars only)
        if (query.Length >= 3) {
            var idx = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return new NameMatchResult(0.2, "Substring", [(idx, query.Length)]);
        }

        return new NameMatchResult(0, null, null);
    }

    private static (bool Success, List<(int Start, int Length)>? Ranges) TryMatchAbbrevWithRanges(
        IReadOnlyList<(string Token, int Start)> tokensWithPos,
        int startToken,
        string query) {

        var tokenOffset = 0;
        var posInToken = 0;
        // Track individual char hits as (absolutePos, tokenOffset) so we can merge later
        var hits = new List<(int AbsPos, int TokOffset)>();

        for (var i = 0; i < query.Length; i++) {
            var tokIdx = startToken + tokenOffset;
            if (tokIdx >= tokensWithPos.Count) return (false, null);
            var ch  = char.ToLowerInvariant(query[i]);
            var tch = char.ToLowerInvariant(tokensWithPos[tokIdx].Token[posInToken]);
            if (ch == tch) {
                var absPos = tokensWithPos[tokIdx].Start + posInToken;
                hits.Add((absPos, tokenOffset));
                posInToken++;
                if (posInToken == tokensWithPos[tokIdx].Token.Length) {
                    tokenOffset++;
                    posInToken = 0;
                }
            } else if (posInToken > 0) {
                // consumed some chars of this token → jump to next token and retry char
                tokenOffset++;
                posInToken = 0;
                i--;
            } else {
                return (false, null);
            }
        }

        // Merge consecutive positions within the same token into single ranges
        var ranges = new List<(int Start, int Length)>();
        var j = 0;
        while (j < hits.Count) {
            var rangeStart = hits[j].AbsPos;
            var len = 1;
            var tok = hits[j].TokOffset;
            while (j + len < hits.Count
                   && hits[j + len].TokOffset == tok
                   && hits[j + len].AbsPos == hits[j].AbsPos + len)
                len++;
            ranges.Add((rangeStart, len));
            j += len;
        }

        return (true, ranges);
    }

    private static IReadOnlyList<(string Token, int Start)> SplitTokensWithPositions(string name) {
        var result = new List<(string Token, int Start)>();
        var wordStart = 0;
        var len = name.Length;

        while (wordStart < len) {
            // Skip separators
            while (wordStart < len && (name[wordStart] == ' ' || name[wordStart] == '-' || name[wordStart] == '_'))
                wordStart++;
            if (wordStart >= len) break;

            // Find end of word
            var wordEnd = wordStart;
            while (wordEnd < len && name[wordEnd] != ' ' && name[wordEnd] != '-' && name[wordEnd] != '_')
                wordEnd++;

            var word = name[wordStart..wordEnd];

            if (word.All(char.IsUpper)) {
                // All-uppercase word → each char is its own token
                for (var k = 0; k < word.Length; k++)
                    result.Add((word[k].ToString(), wordStart + k));
            } else {
                // CamelCase split within word
                var start = 0;
                for (var i = 1; i < word.Length; i++) {
                    if (char.IsLower(word[i - 1]) && char.IsUpper(word[i])) {
                        result.Add((word[start..i], wordStart + start));
                        start = i;
                    }
                }
                result.Add((word[start..], wordStart + start));
            }

            wordStart = wordEnd;
        }

        return result;
    }

    public static IReadOnlyList<string> SplitTokens(string name) {
        var tokens = new List<string>();
        foreach (var word in name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)) {
            // All-uppercase word → each char is its own hump ("AM" → ["A","M"])
            if (word.All(char.IsUpper)) {
                foreach (var c in word)
                    tokens.Add(c.ToString());
                continue;
            }

            var start = 0;
            for (var i = 1; i < word.Length; i++) {
                if (char.IsLower(word[i - 1]) && char.IsUpper(word[i])) {
                    tokens.Add(word[start..i]);
                    start = i;
                }
            }
            tokens.Add(word[start..]);
        }
        return tokens;
    }
}
