using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Dictionary;

public class DictionarySource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ILogger<DictionarySource> logger) : IDeferredSearchSource {

    private static readonly HttpClient Http = new() {
        Timeout = TimeSpan.FromSeconds(AppDefaults.DictionaryTimeoutSeconds)
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

        var languages = settings.DictionaryLanguages;

        var tasks = languages.Select(lang =>
            DictionaryApiClient.LookupAsync(Http, searchWord, lang, logger, ct)).ToArray();
        var responses = await Task.WhenAll(tasks);

        var results = new List<BaseResultItemViewModel>();
        var phonetic = (string?)null;

        for (var i = 0; i < responses.Length; i++) {
            var response = responses[i];
            if (response is null) continue;
            var lang = languages[i];

            phonetic ??= response.Phonetics.FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;
            var sourceUrl = response.SourceUrls.FirstOrDefault();

            foreach (var meaning in response.Meanings) {
                foreach (var def in meaning.Definitions.Take(3)) {
                    if (results.Count >= limit) break;

                    var title = $"{response.Word} ({meaning.PartOfSpeech}) [{lang}]: {def.Definition}";
                    var subtitle = def.Example is not null
                        ? $"\"{def.Example}\""
                        : phonetic ?? "";

                    var capturedUrl = sourceUrl;
                    results.Add(new ResultItemViewModel {
                        IconBytes = IconBytes,
                        Title = title,
                        Subtitle = subtitle,
                        Category = "Definition",
                        Score = score,
                        OnActivate = capturedUrl != null
                            ? () => {
                                var browser = settings.ActiveBrowser;
                                if (browser is not null)
                                    browserDiscovery.OpenUrl(capturedUrl, browser);
                            }
                            : null,
                    });
                }
                if (results.Count >= limit) break;
            }
            if (results.Count >= limit) break;
        }

        if (results.Count > 0) {
            logger.LogDebug("Dictionary query=\"{Word}\" languages={Languages} results={Count}", searchWord, string.Join(",", languages), results.Count);
            yield return results;
        }
    }
}
