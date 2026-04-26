using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Dictionary;

public record WiktionaryEntry {
    [JsonPropertyName("partOfSpeech")]
    public string PartOfSpeech { get; init; } = "";

    [JsonPropertyName("language")]
    public string Language { get; init; } = "";

    [JsonPropertyName("definitions")]
    public List<WiktionaryDefinition> Definitions { get; init; } = [];
}

public record WiktionaryDefinition {
    [JsonPropertyName("definition")]
    public string Definition { get; init; } = "";

    [JsonPropertyName("parsedExamples")]
    public List<WiktionaryExample>? ParsedExamples { get; init; }
}

public record WiktionaryExample {
    [JsonPropertyName("example")]
    public string Example { get; init; } = "";

    [JsonPropertyName("translation")]
    public string? Translation { get; init; }
}

public static class DictionaryApiClient {
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex OlTagRegex = new(@"<ol[\s>].*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex FirstLiRegex = new(@"<li[^>]*>(.*?)</li>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CollapseSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Returns true if the HTML represents a grammatical inflection form, not a real definition.</summary>
    public static bool IsFormOfDefinition(string html) =>
        html.Contains("form-of-definition", StringComparison.Ordinal);

    public static string StripHtml(string html) {
        string candidate;
        var olMatch = OlTagRegex.Match(html);
        if (olMatch.Success) {
            // Text before <ol> is the main definition; if empty, fall back to first <li>
            candidate = HtmlTagRegex.Replace(html[..olMatch.Index], "").Trim();
            if (string.IsNullOrWhiteSpace(candidate)) {
                var liMatch = FirstLiRegex.Match(olMatch.Value);
                candidate = liMatch.Success
                    ? HtmlTagRegex.Replace(liMatch.Groups[1].Value, "").Trim()
                    : "";
            }
        } else {
            candidate = HtmlTagRegex.Replace(html, "").Trim();
        }
        return CollapseSpaceRegex.Replace(candidate, " ").Trim();
    }

    public static async Task<List<WiktionaryEntry>?> LookupAsync(
        HttpClient http, string word, string langCode, ILogger logger, CancellationToken ct) {
        try {
            var url = $"https://{langCode}.wiktionary.org/api/rest_v1/page/definition/{Uri.EscapeDataString(word)}";
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                logger.LogDebug("Wiktionary API {Status} for '{Word}' [{Lang}]", response.StatusCode, word, langCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            var allEntries = JsonSerializer.Deserialize<Dictionary<string, List<WiktionaryEntry>>>(json);
            return allEntries?.GetValueOrDefault(langCode);
        } catch (HttpRequestException ex) {
            logger.LogDebug("Wiktionary API error for '{Word}' [{Lang}]: {Message}", word, langCode, ex.Message);
            return null;
        } catch (JsonException ex) {
            logger.LogDebug("Wiktionary API parse error for '{Word}' [{Lang}]: {Message}", word, langCode, ex.Message);
            return null;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning("Wiktionary API unexpected error for '{Word}' [{Lang}]: {Message}", word, langCode, ex.Message);
            return null;
        }
    }
}
