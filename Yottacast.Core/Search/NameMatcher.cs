namespace Yottacast.Core.Search;

public static class NameMatcher
{
    public static double Score(string name, string query)
    {
        var tokens = SplitTokens(name);
        var queryHumps = SplitTokens(query);

        // CamelHump: each query hump must be prefix of successive tokens
        for (var start = 0; start <= tokens.Count - queryHumps.Count; start++)
        {
            var match = true;
            for (var h = 0; h < queryHumps.Count; h++)
            {
                if (!tokens[start + h].StartsWith(queryHumps[h], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match) return start == 0 ? 1.0 : 0.8;
        }

        // Initials fallback: "mon" → "Microsoft OneNote" (M+O+N)
        if (tokens.Count > 0)
        {
            var initials = string.Concat(tokens.Select(t => t[0]));
            if (initials.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 0.6;
        }

        // Internal substring (2+ chars only)
        if (query.Length >= 3 && name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 0.2;

        return 0;
    }

    public static IReadOnlyList<string> SplitTokens(string name)
    {
        var tokens = new List<string>();
        foreach (var word in name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            // All-uppercase word → each char is its own hump ("AM" → ["A","M"])
            if (word.All(char.IsUpper))
            {
                foreach (var c in word)
                    tokens.Add(c.ToString());
                continue;
            }

            var start = 0;
            for (var i = 1; i < word.Length; i++)
            {
                if (char.IsLower(word[i - 1]) && char.IsUpper(word[i]))
                {
                    tokens.Add(word[start..i]);
                    start = i;
                }
            }
            tokens.Add(word[start..]);
        }
        return tokens;
    }
}