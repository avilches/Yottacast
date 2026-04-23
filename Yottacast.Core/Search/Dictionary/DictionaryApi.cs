using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Search.Dictionary;

public record DictionaryApiResponse {
    [JsonPropertyName("word")]
    public string Word { get; init; } = "";

    [JsonPropertyName("phonetics")]
    public List<DictionaryPhonetic> Phonetics { get; init; } = [];

    [JsonPropertyName("meanings")]
    public List<DictionaryMeaning> Meanings { get; init; } = [];

    [JsonPropertyName("sourceUrls")]
    public List<string> SourceUrls { get; init; } = [];
}

public record DictionaryPhonetic {
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("audio")]
    public string? Audio { get; init; }
}

public record DictionaryMeaning {
    [JsonPropertyName("partOfSpeech")]
    public string PartOfSpeech { get; init; } = "";

    [JsonPropertyName("definitions")]
    public List<DictionaryDefinition> Definitions { get; init; } = [];

    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; init; } = [];
}

public record DictionaryDefinition {
    [JsonPropertyName("definition")]
    public string Definition { get; init; } = "";

    [JsonPropertyName("example")]
    public string? Example { get; init; }

    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; init; } = [];
}

public static class DictionaryApiClient {
    private const string BaseUrl = "https://api.dictionaryapi.dev/api/v2/entries/";

    public static async Task<DictionaryApiResponse?> LookupAsync(HttpClient http, string word, string language, ILogger logger, CancellationToken ct) {
        try {
            var url = BaseUrl + language + "/" + Uri.EscapeDataString(word);
            var responses = await http.GetFromJsonAsync<List<DictionaryApiResponse>>(url, ct);
            return responses?.FirstOrDefault();
        } catch (HttpRequestException ex) {
            logger.LogDebug("Dictionary API error for '{Word}' [{Language}]: {Message}", word, language, ex.Message);
            return null;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning("Dictionary API unexpected error for '{Word}' [{Language}]: {Message}", word, language, ex.Message);
            return null;
        }
    }
}