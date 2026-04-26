using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Dictionary;

public class DictionarySource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ILogger<DictionarySource> logger) : IDeferredSearchSource {

    private static readonly HttpClient Http = new(new HttpClientHandler()) {
        Timeout = TimeSpan.FromSeconds(AppDefaults.DictionaryTimeoutSeconds),
        DefaultRequestHeaders = { { "User-Agent", "Yottacast/1.0 (https://yottacast.app)" } }
    };

    private static readonly byte[]? IconBytes = LoadIcon();

    private static byte[]? LoadIcon() {
        var stream = typeof(DictionarySource).Assembly.GetManifestResourceStream(
            "Yottacast.Core.Search.Dictionary.Icons.wiktionary.png");
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (!settings.EnableDictionary) yield break;
        if (string.IsNullOrWhiteSpace(query) || query.StartsWith(':')) yield break;

        string searchWord;
        double score;

        if (settings.DictionaryShowAlways) {
            searchWord = query.Trim();
            score = 2.5;
        } else {
            var prefix = settings.DictionaryPrefix;
            if (string.IsNullOrEmpty(prefix)) yield break;
            var trigger = prefix + " ";
            if (!query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) yield break;
            searchWord = query[trigger.Length..].Trim();
            score = 3.5;
        }

        if (string.IsNullOrEmpty(searchWord)) yield break;

        var allEntries = await DictionaryApiClient.LookupAsync(Http, searchWord, logger, ct);
        if (allEntries is null) yield break;

        var languages = new HashSet<string>(settings.DictionaryLanguages);
        var multiLang = settings.DictionaryLanguages.Count > 1;
        var results = new List<BaseResultItemViewModel>();

        foreach (var (langCode, entries) in allEntries) {
            if (!languages.Contains(langCode)) continue;

            var defs = new List<DictionaryDefinitionEntry>();
            foreach (var entry in entries) {
                foreach (var def in entry.Definitions) {
                    if (defs.Count >= AppDefaults.DictionaryMaxDefinitionsPerItem) break;
                    if (DictionaryApiClient.IsFormOfDefinition(def.Definition)) continue;

                    var cleanDef = DictionaryApiClient.StripHtml(def.Definition);
                    if (string.IsNullOrWhiteSpace(cleanDef)) continue;

                    string? exampleText = null;
                    string? exampleTranslation = null;
                    var example = def.ParsedExamples?.FirstOrDefault();
                    if (example is not null) {
                        var cleaned = DictionaryApiClient.StripHtml(example.Example);
                        if (!string.IsNullOrWhiteSpace(cleaned)) {
                            exampleText = cleaned;
                            if (example.Translation is not null) {
                                var cleanedTr = DictionaryApiClient.StripHtml(example.Translation);
                                if (!string.IsNullOrWhiteSpace(cleanedTr)) exampleTranslation = cleanedTr;
                            }
                        }
                    }

                    defs.Add(new DictionaryDefinitionEntry {
                        PartOfSpeech = entry.PartOfSpeech,
                        Definition = cleanDef,
                        Example = exampleText,
                        ExampleTranslation = exampleTranslation,
                    });
                }
                if (defs.Count >= AppDefaults.DictionaryMaxDefinitionsPerItem) break;
            }

            if (defs.Count == 0) continue;

            var langName = entries.FirstOrDefault()?.Language ?? langCode;
            var capturedUrl = $"https://{langCode}.wiktionary.org/wiki/{Uri.EscapeDataString(searchWord)}";
            results.Add(new DictionaryResultViewModel {
                IconBytes = IconBytes,
                Word = searchWord,
                Language = multiLang ? langName : null,
                Definitions = defs,
                Score = score,
                BypassLimit = true,
                OnActivate = () => {
                    var browser = settings.ActiveBrowser;
                    if (browser is not null)
                        browserDiscovery.OpenUrl(capturedUrl, browser);
                },
            });
        }

        if (results.Count > 0) {
            logger.LogDebug("Dictionary query=\"{Word}\" languages=[{Languages}] results={Count}",
                searchWord, string.Join(",", languages), results.Count);
            yield return results;
        }
    }
}
