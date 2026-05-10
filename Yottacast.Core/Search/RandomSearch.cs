using System.Runtime.CompilerServices;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// Debug search source: activates when the query starts with "/random".
/// Emits 6 random file-like results every 250 ms for 10 seconds, useful
/// for stress-testing the streaming + sorted-insertion pipeline.
/// </summary>
public class RandomSearch(ClipboardService clipboard) : IDeferredSearchSource {
    private static readonly Random Rng = new();

    private static readonly string[] Icons = ["🎲", "🌟", "🔥", "💡", "🎯", "🚀", "✨", "🌈"];

    private static readonly string[] Words = [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo",
        "Foxtrot", "Golf", "Hotel", "India", "Juliet"
    ];

    private static readonly string[] Extensions = ["pdf", "txt", "png", "docx", "zip", "mp4", "csv", "json"];

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (!query.StartsWith("/random", StringComparison.OrdinalIgnoreCase))
            yield break;

        var results = new List<ResultItemViewModel>();
        var end = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < end) {
            await Task.Delay(250, ct).ConfigureAwait(false);
            for (var i = 0; i < 6; i++) {
                var score = Math.Round(Rng.NextDouble(), 2);
                var name  = Words[Rng.Next(Words.Length)];
                var ext   = Extensions[Rng.Next(Extensions.Length)];
                var dir   = string.Join("/", Enumerable.Range(0, 2).Select(_ => Words[Rng.Next(Words.Length)]));
                var path  = $"/{dir}/{name}-{Rng.Next(1000):D4}.{ext}";
                results.Add(new ResultItemViewModel {
                    Icon     = Icons[Rng.Next(Icons.Length)],
                    Title    = $"{name}-{Rng.Next(1000):D4}.{ext}",
                    Subtitle = path,
                    Category = "Random",
                    Score    = score,
                    Actions  = [
                        new() {
                            Label        = "Open",
                            Hotkey       = ActionHotkey.Enter,
                            ShowInFooter = true,
                            ShowInMenu   = true,
                            ClosesWindow = true,
                            Execute      = () => { /* debug: no-op */ },
                        },
                        new() {
                            Label        = "Copy path",
                            Hotkey       = ActionHotkey.MetaC,
                            ShowInFooter = true,
                            ShowInMenu   = true,
                            HintProvider = () => "Path copied!",
                            Execute      = () => clipboard.CopyText(path),
                        },
                    ],
                });
            }
            yield return results.ToList();
        }
    }
}
