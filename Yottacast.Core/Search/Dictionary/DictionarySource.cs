using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Dictionary;

public class DictionarySource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ClipboardService clipboard,
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

    public void Start() {
        // Convert any JSONL found in DictionaryDir, regardless of configured languages.
        // This pre-builds DBs for all available languages so they are ready if the user enables them later.
        // While conversion runs, searches for that language fall back to the API (atomic rename ensures
        // a partial DB is never visible to LocalDictionaryDb.Exists).
        if (!Directory.Exists(AppPaths.DictionaryDir)) return;
        foreach (var jsonlPath in Directory.EnumerateFiles(AppPaths.DictionaryDir, "*.jsonl")) {
            var dbPath = Path.ChangeExtension(jsonlPath, ".db");
            if (!File.Exists(dbPath))
                _ = ConvertInBackground(jsonlPath, dbPath);
        }
    }

    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    private async Task ConvertInBackground(string jsonlPath, string dbPath) {
        try {
            Directory.CreateDirectory(AppPaths.DictionaryDir);
            await LocalDictionaryConverter.ConvertAsync(jsonlPath, dbPath, logger);
        } catch (Exception ex) {
            logger.LogError(ex, "Dictionary: conversion failed for {File}", Path.GetFileName(jsonlPath));
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (!settings.EnableDictionary) yield break;
        if (string.IsNullOrWhiteSpace(query) || query.StartsWith(':')) yield break;

        string searchWord;
        double score;

        if (settings.DictionaryShowAlways) {
            searchWord = query.Trim();
            score = 0.3;
        } else {
            var prefix = settings.DictionaryPrefix;
            if (string.IsNullOrEmpty(prefix)) yield break;
            var trigger = prefix + " ";
            if (!query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) yield break;
            searchWord = query[trigger.Length..].Trim();
            score = 3.7;
        }

        if (string.IsNullOrEmpty(searchWord)) yield break;

        var languages = new HashSet<string>(settings.DictionaryLanguages);
        var multiLang = settings.DictionaryLanguages.Count > 1;
        var results = new List<BaseResultItemViewModel>();

        // ── Local DB lookup ──────────────────────────────────────────────────
        var apiLanguages = new HashSet<string>();
        foreach (var langCode in languages) {
            var dbPath = AppPaths.DictionaryDb(langCode);
            if (!LocalDictionaryDb.Exists(dbPath)) {
                apiLanguages.Add(langCode);
                continue;
            }

            using var db = new LocalDictionaryDb(dbPath);
            var localEntries = db.Lookup(searchWord);
            if (localEntries.Count == 0) {
                logger.LogDebug("Dictionary [{Lang}] local: \"{Word}\" not found", langCode, searchWord);
                continue;
            }

            var defs = BuildDefsFromLocal(localEntries);
            if (defs.Count == 0) continue;

            logger.LogDebug("Dictionary [{Lang}] local: \"{Word}\" → {Count} definitions", langCode, searchWord, defs.Count);
            var langName = GetLangName(langCode);
            var capturedUrl = $"https://{langCode}.wiktionary.org/wiki/{Uri.EscapeDataString(searchWord)}";
            var capturedDef = defs[0].Definition;
            results.Add(new DictionaryResultViewModel {
                IconBytes = IconBytes,
                Word = searchWord,
                Language = multiLang ? langName : null,
                Definitions = defs,
                Score = score,
                OnActivate = () => {
                    var browser = settings.ActiveBrowser;
                    if (browser is not null)
                        browserDiscovery.OpenUrl(capturedUrl, browser);
                },
                OnCopy = () => clipboard.CopyText(capturedDef),
                CopiedMessage = "Definition copied!",
            });
        }

        // ── API fallback for languages without a local DB ────────────────────
        if (apiLanguages.Count > 0) {
            logger.LogDebug("Dictionary API fallback for [{Langs}]: \"{Word}\"", string.Join(",", apiLanguages), searchWord);
            var allEntries = await DictionaryApiClient.LookupAsync(Http, searchWord, logger, ct);
            if (allEntries is not null) {
                foreach (var (langCode, entries) in allEntries) {
                    if (!apiLanguages.Contains(langCode)) continue;

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

                    logger.LogDebug("Dictionary [{Lang}] API: \"{Word}\" → {Count} definitions", langCode, searchWord, defs.Count);
                    var langName = entries.FirstOrDefault()?.Language ?? langCode;
                    var capturedUrl = $"https://{langCode}.wiktionary.org/wiki/{Uri.EscapeDataString(searchWord)}";
                    var capturedDef = defs[0].Definition;
                    results.Add(new DictionaryResultViewModel {
                        IconBytes = IconBytes,
                        Word = searchWord,
                        Language = multiLang ? langName : null,
                        Definitions = defs,
                        Score = score,
                        OnActivate = () => {
                            var browser = settings.ActiveBrowser;
                            if (browser is not null)
                                browserDiscovery.OpenUrl(capturedUrl, browser);
                        },
                        OnCopy = () => clipboard.CopyText(capturedDef),
                        CopiedMessage = "Definition copied!",
                    });
                }
            }
        }

        if (results.Count > 0)
            yield return results;
    }

    private static List<DictionaryDefinitionEntry> BuildDefsFromLocal(List<LocalDictionaryEntry> entries) {
        var defs = new List<DictionaryDefinitionEntry>();
        foreach (var entry in entries) {
            bool firstDef = true;
            foreach (var def in entry.Definitions) {
                if (defs.Count >= AppDefaults.DictionaryMaxDefinitionsPerItem) return defs;
                defs.Add(new DictionaryDefinitionEntry {
                    PartOfSpeech = entry.Pos,
                    Definition = def,
                    Example = firstDef ? entry.Example : null,
                    ExampleTranslation = null,
                });
                firstDef = false;
            }
        }
        return defs;
    }

    private static string GetLangName(string langCode) {
        foreach (var (code, name) in AppDefaults.DictionaryAvailableLanguages)
            if (code == langCode) return name;
        return langCode;
    }
}
