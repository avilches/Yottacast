namespace Yottacast.Core.Search.Application;

public static class NameMatcher {
    public static double Score(string name, string query) =>
        Score(SplitTokens(name), name, query);

    public static double Score(IReadOnlyList<string> tokens, string name, string query) {
        var result = ScoreWith(tokens, name, query);
        // All-lowercase query: also try as initials (same as if typed in uppercase)
        if (result < 1.0 && query.All(char.IsLower))
            result = Math.Max(result, ScoreWith(tokens, name, query.ToUpperInvariant()));
        return result;
    }

    private static double ScoreWith(IReadOnlyList<string> tokens, string name, string query) {
        var queryHumps = SplitTokens(query);

        // CamelHump: each query hump must be prefix of successive tokens
        for (var start = 0; start <= tokens.Count - queryHumps.Count; start++) {
            var match = true;
            for (var h = 0; h < queryHumps.Count; h++) {
                if (!tokens[start + h].StartsWith(queryHumps[h], StringComparison.OrdinalIgnoreCase)) {
                    match = false;
                    break;
                }
            }
            if (match) return start == 0 ? 1.0 : 0.8;
        }

        // Initials fallback: "mon" → "Microsoft OneNote" (M+O+N)
        if (tokens.Count > 0) {
            var initials = string.Concat(tokens.Select(t => t[0]));
            if (initials.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 0.6;
        }

        // Multi-word abbreviation: "smifa" → tokens ["smiling","face",...] by consuming
        // query chars greedily as prefixes of consecutive tokens.
        // Generalises single-char initials to multi-char prefixes per token.
        if (tokens.Count > 1 && MatchesWordAbbreviation(tokens, query)) return 0.4;

        // Internal substring (3+ chars only)
        if (query.Length >= 3 && name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 0.2;

        return 0;
    }

    // Tries starting from each token so "face" can match "smiling face" from token 1.
    private static bool MatchesWordAbbreviation(IReadOnlyList<string> tokens, string query) {
        for (var start = 0; start < tokens.Count; start++)
            if (TryMatchAbbrev(tokens, start, query)) return true;
        return false;
    }

    private static bool TryMatchAbbrev(IReadOnlyList<string> tokens, int startToken, string query) {
        var tokenIdx = startToken;
        var posInToken = 0;
        for (var i = 0; i < query.Length; i++) {
            if (tokenIdx >= tokens.Count) return false;
            var ch  = char.ToLowerInvariant(query[i]);
            var tch = char.ToLowerInvariant(tokens[tokenIdx][posInToken]);
            if (ch == tch) {
                posInToken++;
                if (posInToken == tokens[tokenIdx].Length) { tokenIdx++; posInToken = 0; }
            } else if (posInToken > 0) {
                // consumed some chars of this token → jump to next token and retry char
                tokenIdx++; posInToken = 0; i--;
            } else {
                return false;
            }
        }
        return true;
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