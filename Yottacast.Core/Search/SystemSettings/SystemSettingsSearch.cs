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
    IReadOnlyList<string>? thirdPartyDirs = null)
    : IInstantSearchSource {

    private static readonly IReadOnlyList<string> DefaultThirdPartyDirs = [
        AppPaths.SystemPreferencePanesDir,
        AppPaths.UserPreferencePanesDir,
    ];

    private readonly IReadOnlyList<string> _thirdPartyDirs =
        thirdPartyDirs ?? DefaultThirdPartyDirs;
    private readonly List<SystemSettingsPanel> _panels = [];
    private readonly TaskCompletionSource _readyTcs = new();

    public void Start() {
        if (!settings.EnableSystemSettings) {
            _readyTcs.TrySetResult();
            return;
        }
        Task.Run(Load);
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _panels.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableSystemSettings) return [];
        return _panels
            .Select(p => (panel: p, score: NameMatcher.Score(p.Name, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => BuildResult(x.panel, x.score))
            .ToList();
    }

    private ResultItemViewModel BuildResult(SystemSettingsPanel panel, double score) {
        var identifier = panel.UrlIdentifier;
        var subtitle = panel.ParentName is { } parent
            ? $"System Settings › {parent}"
            : panel.IsBuiltin
                ? "System Settings"
                : "System Settings · Preference Pane";
        return new ResultItemViewModel {
            Icon      = "⚙️",
            IconBytes = iconCache.Get(AppPaths.SystemSettingsAppPath),
            Title     = panel.Name,
            Subtitle  = subtitle,
            Category  = "System Settings",
            Score     = score,
            OnActivate = () => {
                logger.LogInformation("SystemSettings: open panel={Panel}", panel.Name);
                platform.LaunchApp($"x-apple.systempreferences:{identifier}");
            },
        };
    }

    private void Load() {
        try {
            foreach (var panel in BuiltinPanels.All)
                _panels.Add(panel);

            foreach (var dir in _thirdPartyDirs) {
                if (!Directory.Exists(dir)) continue;
                foreach (var bundlePath in Directory.EnumerateDirectories(dir, "*.prefPane")) {
                    var plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
                    var parsed = TryReadPlist(plistPath);
                    if (parsed is null) continue;
                    var (name, bundleId) = parsed.Value;
                    if (_panels.Any(p => p.UrlIdentifier == bundleId)) continue;
                    _panels.Add(new SystemSettingsPanel(name, bundleId, IsBuiltin: false));
                }
            }

            iconCache.PreloadAsync(AppPaths.SystemSettingsAppPath);
            logger.LogInformation("SystemSettings: loaded {Count} panels", _panels.Count);
        } catch (Exception ex) {
            logger.LogWarning(ex, "SystemSettings: error loading panels, using partial results");
        } finally {
            _readyTcs.TrySetResult();
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
