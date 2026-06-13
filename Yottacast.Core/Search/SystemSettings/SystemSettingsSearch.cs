using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.SystemSettings;

public sealed class SystemSettingsSearch(
    UserSettings settings,
    PlatformProvider platform,
    AppIconCache iconCache,
    ILogger<SystemSettingsSearch> logger,
    IReadOnlyList<string>? thirdPartyDirs = null,
    TimeSpan? dynamicCacheTtl = null)
    : IInstantSearchSource {

    private static readonly IReadOnlyList<string> DefaultThirdPartyDirs = [
        AppPaths.SystemPreferencePanesDir,
        AppPaths.UserPreferencePanesDir,
    ];

    private readonly IReadOnlyList<string> _thirdPartyDirs =
        thirdPartyDirs ?? DefaultThirdPartyDirs;
    private readonly TimeSpan _cacheTtl = dynamicCacheTtl ?? AppDefaults.SystemSettingsDynamicCacheTtl;
    private readonly TaskCompletionSource _readyTcs = new();
    private CancellationTokenSource? _backgroundCts;

    private volatile IReadOnlyList<SystemSettingsPanel> _panels = [];
    private volatile IReadOnlyList<SystemSettingsPanel> _dynamicCache = [];

    public int Limit => AppDefaults.SystemSettingsSearchLimit;

    public void Start() {
        if (!settings.EnableSystemSettings || !settings.EnableAppSearch) {
            _readyTcs.TrySetResult();
            return;
        }
        _backgroundCts = new CancellationTokenSource();
        _ = Task.Run(() => LoadAndRefreshLoop(_backgroundCts.Token));
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _backgroundCts?.Cancel();
        _backgroundCts = null;
        _panels = [];
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableSystemSettings || !settings.EnableAppSearch) return [];
        return _panels.Concat(_dynamicCache)
            .Select(p => (panel: p, match: NameMatcher.Match(p.Name, query)))
            .Where(x => x.match.Score > 0)
            .OrderByDescending(x => x.match.Score)
            .Take(limit)
            .Select(x => BuildResult(
                x.panel,
                x.match.Score * 4,
                x.match.Reason != null ? $"{x.match.Reason} (×4)" : null,
                x.match.Ranges))
            .ToList();
    }

    private void RefreshDynamicCache() {
        var items = new List<SystemSettingsPanel>();

        var wifi = platform.GetCurrentWifiNetworkName();
        if (wifi is not null)
            items.Add(new SystemSettingsPanel(
                $"Wi-Fi · {wifi}",
                "com.apple.preference.network",
                IsBuiltin: true,
                ParentName: "Network"));

        foreach (var vpn in platform.GetActiveVpnNames())
            items.Add(new SystemSettingsPanel(
                $"VPN · {vpn}",
                "com.apple.preference.network",
                IsBuiltin: true,
                ParentName: "Network"));

        _dynamicCache = items;
    }

    private ResultItemViewModel BuildResult(SystemSettingsPanel panel, double score,
        string? scoreReason = null,
        IReadOnlyList<(int Start, int Length)>? titleRanges = null) {
        var identifier = panel.UrlIdentifier;
        var subtitle = panel.ParentName is { } parent
            ? $"System Settings › {parent}"
            : panel.IsBuiltin
                ? "System Settings"
                : "System Settings · Preference Pane";
        return new ResultItemViewModel {
            Icon        = "⚙️",
            IconBytes   = iconCache.Get(AppPaths.SystemSettingsAppPath),
            Title       = panel.Name,
            Subtitle    = subtitle,
            ItemPath    = identifier,
            Category    = "System Settings",
            Score       = score,
            ScoreReason = scoreReason,
            TitleRanges = titleRanges,
            Actions = [
                new() {
                    Label        = "Open",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute      = () => {
                        logger.LogInformation("SystemSettings: open panel={Panel}", panel.Name);
                        platform.LaunchApp($"x-apple.systempreferences:{identifier}");
                    },
                },
            ],
        };
    }

    private async Task LoadAndRefreshLoop(CancellationToken ct) {
        try {
            Load();
            try { RefreshDynamicCache(); }
            catch (Exception ex) { logger.LogWarning(ex, "SystemSettings: error in initial dynamic cache refresh"); }
        } finally {
            _readyTcs.TrySetResult();
        }

        if (_cacheTtl <= TimeSpan.Zero) return;

        while (!ct.IsCancellationRequested) {
            try {
                await Task.Delay(_cacheTtl, ct);
            } catch (OperationCanceledException) {
                return;
            }
            try {
                RefreshDynamicCache();
            } catch (Exception ex) {
                logger.LogWarning(ex, "SystemSettings: error refreshing dynamic cache");
            }
        }
    }

    private void Load() {
        var panels = new List<SystemSettingsPanel>();
        try {
            foreach (var panel in BuiltinPanels.All)
                panels.Add(panel);

            foreach (var dir in _thirdPartyDirs) {
                if (!Directory.Exists(dir)) continue;
                foreach (var bundlePath in Directory.EnumerateDirectories(dir, "*.prefPane")) {
                    var plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
                    var parsed = TryReadPlist(plistPath);
                    if (parsed is null) continue;
                    var (name, bundleId) = parsed.Value;
                    if (panels.Any(p => p.UrlIdentifier == bundleId)) continue;
                    panels.Add(new SystemSettingsPanel(name, bundleId, IsBuiltin: false));
                }
            }

            iconCache.PreloadAsync(AppPaths.SystemSettingsAppPath);
            logger.LogInformation("SystemSettings: loaded {Count} panels", panels.Count);
        } catch (Exception ex) {
            logger.LogWarning(ex, "SystemSettings: error loading panels, using partial results");
        } finally {
            _panels = panels;
        }
    }

    private static (string Name, string BundleId)? TryReadPlist(string plistPath) {
        try {
            var xmlSettings = new System.Xml.XmlReaderSettings {
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
                XmlResolver   = null,
            };
            using var reader = System.Xml.XmlReader.Create(plistPath, xmlSettings);
            var doc      = System.Xml.Linq.XDocument.Load(reader);
            var dict     = doc.Root?.Element("dict");
            if (dict is null) return null;

            var children = dict.Elements().ToList();
            string? displayName = null, bundleName = null, bundleId = null;

            for (var i = 0; i + 1 < children.Count; i += 2) {
                if (children[i].Name != "key") continue;
                var value = children[i + 1].Value;
                switch (children[i].Value) {
                    case "CFBundleDisplayName": displayName = value; break;
                    case "CFBundleName":        bundleName  = value; break;
                    case "CFBundleIdentifier":  bundleId    = value; break;
                }
            }

            if (string.IsNullOrWhiteSpace(bundleId)) return null;
            var bundleDirName = Path.GetDirectoryName(Path.GetDirectoryName(plistPath)!);
            var name = displayName ?? bundleName
                ?? Path.GetFileNameWithoutExtension(bundleDirName ?? plistPath);
            return (name!, bundleId);
        } catch {
            return null;
        }
    }
}
