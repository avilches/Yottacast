using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Clipboard;

public class ClipboardHistorySearch(
    UserSettings settings,
    ClipboardHistoryStore store,
    ClipboardService clipboard,
    ILogger<ClipboardHistorySearch> logger)
    : IInstantSearchSource, ISearchModeSource
{
    public event Action? ResultChanged;

    public int Limit => -1;

    public void Start() => store.EntriesChanged += OnEntriesChanged;
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop()
    {
        store.EntriesChanged -= OnEntriesChanged;
        return Task.CompletedTask;
    }

    private void OnEntriesChanged() => ResultChanged?.Invoke();

    public bool IsActiveIn(SearchMode mode) => mode switch {
        SearchMode.All       => settings.ClipboardSearchVisibility == SearchSourceVisibility.Always,
        SearchMode.Clipboard => settings.ClipboardSearchVisibility == SearchSourceVisibility.ModeOnly,
        _                    => false,
    };

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit)
    {
        if (query.StartsWith(':')) return [];

        var entries = store.GetAll();
        if (string.IsNullOrEmpty(query))
            return entries
                .Take(limit < 0 ? entries.Count : limit)
                .Select((e, i) => BuildResult(e, score: AppDefaults.ClipboardHistoryUnfilteredBaseScore - i))
                .ToList();

        return entries
            .Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(e => BuildResult(e, ComputeScore(e, query)))
            .OrderByDescending(r => r.Score)
            .Take(limit < 0 ? int.MaxValue : limit)
            .ToList();
    }

    private double ComputeScore(ClipboardHistoryEntry entry, string query)
    {
        double matchScore;
        if (entry.Text.Equals(query, StringComparison.OrdinalIgnoreCase))
            matchScore = AppDefaults.ClipboardHistoryExactMatchScore;
        else if (entry.Text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            matchScore = AppDefaults.ClipboardHistoryPrefixMatchScore;
        else
            matchScore = AppDefaults.ClipboardHistoryContainsMatchScore;

        var ageDays = Math.Max(0, (DateTimeOffset.UtcNow - entry.LastUsedAt).TotalDays);
        // True half-life decay: the usage bonus halves exactly every ClipboardHistoryHalfLifeDays.
        var decay = Math.Pow(0.5, ageDays / AppDefaults.ClipboardHistoryHalfLifeDays);
        var usageBonus = Math.Min(Math.Log(entry.UsageCount + 1) * decay, AppDefaults.ClipboardHistoryMaxBonus);

        return matchScore + usageBonus;
    }

    private ClipboardResultItemViewModel BuildResult(ClipboardHistoryEntry entry, double score)
    {
        var displayText = entry.Text.Replace('\n', '·').Replace('\r', '·');
        if (displayText.Length > 60) displayText = displayText[..60] + "…";

        var subtitle = FormatRelativeTime(entry.CopiedAt);
        var capturedText = entry.Text;

        return new ClipboardResultItemViewModel
        {
            FullText = capturedText,
            Title    = displayText,
            Subtitle = subtitle,
            Category = "Clipboard",
            Score    = score,
            Actions  =
            [
                new()
                {
                    Label           = "Paste",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute = () =>
                    {
                        logger.LogInformation("ClipboardHistory: paste \"{Text}\"",
                            capturedText.Length > 40 ? capturedText[..40] + "…" : capturedText);
                        clipboard.CopyText(capturedText);
                        store.RecordUsage(capturedText);
                    },
                },
                new()
                {
                    Label        = "Delete",
                    Hotkey       = ActionHotkey.Delete,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    ClosesWindow = false,
                    Execute      = () =>
                    {
                        logger.LogInformation("ClipboardHistory: delete \"{Text}\"",
                            capturedText.Length > 40 ? capturedText[..40] + "…" : capturedText);
                        store.Remove(capturedText);
                    },
                },
            ],
        };
    }

    private static string FormatRelativeTime(DateTimeOffset time)
    {
        var diff = DateTimeOffset.UtcNow - time;
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24)   return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 2)     return "yesterday";
        if (diff.TotalDays < 7)     return $"{(int)diff.TotalDays} days ago";
        return time.LocalDateTime.ToString("d MMM");
    }
}
