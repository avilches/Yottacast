using System.Runtime.CompilerServices;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// Fake search source that yields random results with scores between 0.5 and 1.0,
/// emitting each item after a 250ms delay. Used for integration testing of the
/// streaming + sorted-insertion pipeline.
/// </summary>
public class RandomSearch : IDeferredSearchSource {
    private static readonly Random Rng = new();

    private static readonly string[] Icons = ["🎲", "🌟", "🔥", "💡", "🎯", "🚀", "✨", "🌈"];

    private static readonly string[] Words = [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo",
        "Foxtrot", "Golf", "Hotel", "India", "Juliet"
    ];

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        var count = Math.Min(5, limit);
        var results = new List<ResultItemViewModel>();
        for (var i = 0; i < count; i++) {
            await Task.Delay(200 + i * 50, ct).ConfigureAwait(false);
            var score = Math.Round(0.5 + Rng.NextDouble() * 0.5, 2);
            var name = Words[Rng.Next(Words.Length)];
            results.Add(new ResultItemViewModel {
                Icon = Icons[Rng.Next(Icons.Length)],
                Title = $"{name} ({query})",
                Subtitle = $"Random result — score {score:F2}",
                Category = "Random",
                Score = score,
            });
            yield return results.ToList();
        }
    }
}
