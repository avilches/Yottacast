using System.Net;
using System.Net.Sockets;
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
        ResultItemViewModel? urlResult = null;
        string? urlToValidate = null;

        lock (_cacheLock)
        {
            _cached = Build(clipboardText);
            if (_cached is not null)
                logger.LogDebug("ClipboardSearch: clipboard hit for \"{Text}\"", clipboardText);

            if (settings.EnableUrlValidation
                && _cached is ResultItemViewModel vm
                && clipboardText is not null
                && UrlSearch.TryNormalizeUrl(clipboardText, out var normalizedUrl))
            {
                urlResult = vm;
                urlToValidate = normalizedUrl;
            }
        }

        if (urlResult is not null && urlToValidate is not null)
            _ = CheckUrlReachabilityAsync(urlToValidate, urlResult);
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

    private async Task CheckUrlReachabilityAsync(string url, ResultItemViewModel original)
    {
        var host = new Uri(url).Host;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug("ClipboardSearch: DNS {Host} failed: {Message}", host, ex.Message);
            lock (_cacheLock)
            {
                if (!ReferenceEquals(_cached, original)) return;
                _cached = null;
            }
            ResultsChanged?.Invoke();
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
            InfoTag    = "from clipboard",
            Subtitle   = $"Open in {browserLabel}",
            Category   = "Web",
            Score      = 4.0,
            Actions = [
                new() {
                    Label        = "Open",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute      = () =>
                    {
                        if (browser is null) return;
                        logger.LogInformation("ClipboardSearch: open URL \"{Url}\"", capturedUrl);
                        browserDiscovery.OpenUrl(capturedUrl, browser);
                    },
                },
                new() {
                    Label                   = "Open (background)",
                    Hotkey                  = ActionHotkey.MetaEnter,
                    ShowInFooter            = true,
                    ShowInMenu              = true,
                    ClosesMenu              = true,
                    ClosesWindow            = false,
                    RegainFocusAfterExecute = true,
                    Execute                 = () =>
                    {
                        if (browser is null) return;
                        logger.LogInformation("ClipboardSearch: open URL \"{Url}\" in background", capturedUrl);
                        browserDiscovery.OpenUrl(capturedUrl, browser);
                    },
                },
            ],
        };
    }

    private ResultItemViewModel? BuildLocalPathResult(string text)
    {
        var expanded = PlatformProvider.ExpandPath(text);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return null;

        var title = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(title)) title = expanded;

        var capturedPath = expanded;
        var actions = new List<ResultAction> {
            new() {
                Label        = "Open",
                Hotkey       = ActionHotkey.Enter,
                ShowInFooter = true,
                ShowInMenu   = true,
                ClosesMenu   = true,
                ClosesWindow = true,
                Execute      = () =>
                {
                    logger.LogInformation("ClipboardSearch: open path \"{Path}\"", capturedPath);
                    platform.LaunchApp(capturedPath);
                },
            },
            new() {
                Label                   = "Open (background)",
                Hotkey                  = ActionHotkey.MetaEnter,
                ShowInFooter            = true,
                ShowInMenu              = true,
                ClosesMenu              = true,
                ClosesWindow            = false,
                RegainFocusAfterExecute = true,
                Execute                 = () =>
                {
                    logger.LogInformation("ClipboardSearch: open path \"{Path}\" in background", capturedPath);
                    platform.LaunchApp(capturedPath);
                },
            },
            new() {
                Label        = "Copy path",
                Hotkey       = ActionHotkey.MetaC,
                ShowInFooter = true,
                ShowInMenu   = true,
                ClosesMenu   = true,
                HintProvider = () => "Path copied!",
                Execute      = () => clipboardService.CopyText(capturedPath),
            },
        };

        if (IsEditableExtension(expanded)) {
            actions.Add(new ResultAction {
                Label        = "Preview",
                Hotkey       = ActionHotkey.MetaP,
                ShowInFooter = true,
                ShowInMenu   = true,
                ClosesMenu   = true,
                Execute      = () => { },
            });
            actions.Add(new ResultAction {
                Label        = "Edit",
                Hotkey       = ActionHotkey.MetaE,
                ShowInFooter = true,
                ShowInMenu   = true,
                ClosesMenu   = true,
                Execute      = () => { },
            });
        }

        return new FileResultItemViewModel
        {
            IconBytes = fileIconCache.GetOrPreload(expanded),
            Title     = title,
            InfoTag   = "from clipboard",
            Subtitle  = expanded,
            ItemPath  = capturedPath,
            Category  = "Files",
            Score     = 4.0,
            Actions   = actions,
        };
    }

    private bool IsEditableExtension(string filePath)
    {
        if (!settings.EnableFileEditor) return false;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return !string.IsNullOrEmpty(ext)
            && settings.FileEditorExtensions.Any(e =>
                e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
}
