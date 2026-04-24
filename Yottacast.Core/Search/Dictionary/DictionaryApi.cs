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
}

public static class DictionaryApiClient {
    private const string BaseUrl = "https://en.wiktionary.org/api/rest_v1/page/definition/";

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static string StripHtml(string html) => HtmlTagRegex.Replace(html, "").Trim();

    public static async Task<Dictionary<string, List<WiktionaryEntry>>?> LookupAsync(
        HttpClient http, string word, ILogger logger, CancellationToken ct) {
        try {
            var url = BaseUrl + Uri.EscapeDataString(word);
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                logger.LogDebug("Wiktionary API {Status} for '{Word}'", response.StatusCode, word);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<Dictionary<string, List<WiktionaryEntry>>>(json);
        } catch (HttpRequestException ex) {
            logger.LogDebug("Wiktionary API error for '{Word}': {Message}", word, ex.Message);
            return null;
        } catch (JsonException ex) {
            logger.LogDebug("Wiktionary API parse error for '{Word}': {Message}", word, ex.Message);
            return null;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning("Wiktionary API unexpected error for '{Word}': {Message}", word, ex.Message);
            return null;
        }
    }
}
