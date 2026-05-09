using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Clipboard;

/// <summary>
/// IEmptyStateSource that inspects the clipboard each time the window opens.
/// If the clipboard contains a valid URL or local filesystem path, shows it
/// as a result with "· from clipboard" in the subtitle.
/// </summary>
public class ClipboardSearch(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    FaviconCache faviconCache,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ClipboardService clipboardService,
    ILogger<ClipboardSearch> logger) : IEmptyStateSource
{
    private BaseResultItemViewModel? _cached;
    private readonly object _cacheLock = new();
    private Action? _onFaviconLoaded;

    public event Action? ResultsChanged;

    public void Start()
    {
        _onFaviconLoaded = () => ResultsChanged?.Invoke();
        faviconCache.FaviconLoaded += _onFaviconLoaded;
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop()
    {
        if (_onFaviconLoaded is not null)
        {
            faviconCache.FaviconLoaded -= _onFaviconLoaded;
            _onFaviconLoaded = null;
        }
        lock (_cacheLock)
        {
            _cached = null;
        }
        return Task.CompletedTask;
    }

    public void OnWindowShown(string? clipboardText)
    {
        lock (_cacheLock)
        {
            _cached = Build(clipboardText);
            if (_cached is not null)
                logger.LogDebug("ClipboardSearch: clipboard hit for \"{Text}\"", clipboardText);
        }
    }

    public void OnSearchStarted()
    {
        lock (_cacheLock)
        {
            _cached = null;
        }
    }

    public IReadOnlyList<BaseResultItemViewModel> GetResults()
    {
        lock (_cacheLock)
        {
            return _cached is null ? [] : [_cached];
        }
    }

    private BaseResultItemViewModel? Build(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (UrlSearch.TryNormalizeUrl(text, out var url))
            return BuildUrlResult(url);

        if (LocalPathSearch.IsLocalPath(text))
            return BuildLocalPathResult(text);

        return null;
    }

    private ResultItemViewModel BuildUrlResult(string url)
    {
        var host = new Uri(url).Host;
        var browser = settings.ActiveBrowser;
        var browserLabel = browser?.Name ?? "browser";
        var iconBytes = faviconCache.GetOrLoad(host);
        var capturedUrl = url;

        return new ResultItemViewModel
        {
            IconBytes  = iconBytes,
            Title      = url.Length > 80 ? url[..77] + "…" : url,
            Subtitle   = $"Open in {browserLabel} · from clipboard",
            Category   = "Web",
            Score      = 4.0,
            OnActivate = () =>
            {
                if (browser is null) return;
                logger.LogInformation("ClipboardSearch: open URL \"{Url}\"", capturedUrl);
                browserDiscovery.OpenUrl(capturedUrl, browser);
            },
        };
    }

    private ResultItemViewModel? BuildLocalPathResult(string text)
    {
        var expanded = PlatformProvider.ExpandPath(text);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return null;

        var title = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(title)) title = expanded;

        var capturedPath = expanded;
        return new ResultItemViewModel
        {
            IconBytes     = fileIconCache.GetOrPreload(expanded),
            Title         = title,
            Subtitle      = $"{expanded} · from clipboard",
            Category      = "Files",
            Score         = 4.0,
            OnActivate    = () =>
            {
                logger.LogInformation("ClipboardSearch: open path \"{Path}\"", capturedPath);
                platform.LaunchApp(capturedPath);
            },
            OnCopy        = () => clipboardService.CopyText(capturedPath),
            CopiedMessage = "Path copied!",
        };
    }
}
