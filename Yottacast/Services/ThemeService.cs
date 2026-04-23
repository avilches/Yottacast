using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;

namespace Yottacast.Services;

public record ThemeOption(string Id, string DisplayName);

public sealed class ThemeService(ILogger<ThemeService> logger) : IDisposable {
    private const string UserThemePrefix = "user:";

    private static string ThemesFolder =>
        Path.Combine(AppContext.BaseDirectory, "Themes");

    private FileSystemWatcher? _activeThemeWatcher;
    private CancellationTokenSource? _debounceCts;
    private string? _activeThemeId;

    public event Action? ThemesChanged;

    public static bool IsUserTheme(string id) => id.StartsWith(UserThemePrefix);

    private static string UserThemeFilePath(string id) =>
        Path.Combine(AppPaths.PluginsDir, "theme." + id[UserThemePrefix.Length..] + ".json");

    public IReadOnlyList<ThemeOption> AvailableThemes() {
        var themes = new List<ThemeOption>();
        var seen   = new HashSet<string>();
        try {
            foreach (var file in Directory.GetFiles(ThemesFolder, "*.json").OrderBy(f => f)) {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!seen.Add(id)) continue;
                try {
                    var json = JsonNode.Parse(File.ReadAllText(file));
                    var displayName = json?["name"]?.GetValue<string>() ?? id;
                    themes.Add(new ThemeOption(id, displayName));
                } catch {
                    themes.Add(new ThemeOption(id, id));
                }
            }
        } catch (Exception ex) {
            logger.LogWarning("Could not load themes: {Message}", ex.Message);
        }

        // Scan user themes from plugins directory (theme.*.json files with required "id" field)
        try {
            if (Directory.Exists(AppPaths.PluginsDir)) {
                foreach (var file in Directory.GetFiles(AppPaths.PluginsDir, "theme.*.json").OrderBy(f => f)) {
                    try {
                        var json = JsonNode.Parse(File.ReadAllText(file));
                        var themeId = json?["id"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(themeId)) {
                            logger.LogWarning("User theme {File}: missing required 'id' field, skipping", Path.GetFileName(file));
                            continue;
                        }
                        var id = UserThemePrefix + themeId;
                        if (!seen.Add(id)) continue;
                        var displayName = json?["name"]?.GetValue<string>() ?? themeId;
                        themes.Add(new ThemeOption(id, displayName));
                    } catch {
                        // Skip files that can't be parsed
                    }
                }
            }
        } catch (Exception ex) {
            logger.LogWarning("Could not load user themes: {Message}", ex.Message);
        }

        if (themes.Count == 0)
            themes.Add(new ThemeOption("dark-default", "Dark Default"));

        return themes;
    }

    /// <summary>
    /// Subscribes to the central PluginService for directory changes and sets up
    /// the active theme file watcher for hot-reload.
    /// </summary>
    public void StartWatching(PluginService pluginService) {
        pluginService.PluginsChanged += () => ThemesChanged?.Invoke();

        if (_activeThemeId != null)
            WatchActiveTheme(_activeThemeId);
    }

    private void WatchActiveTheme(string themeId) {
        _activeThemeWatcher?.Dispose();
        _activeThemeWatcher = null;
        _activeThemeId = themeId;

        if (!IsUserTheme(themeId)) return;

        var fileName = "theme." + themeId[UserThemePrefix.Length..] + ".json";
        _activeThemeWatcher = new FileSystemWatcher(AppPaths.PluginsDir, fileName) {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _activeThemeWatcher.Changed += (_, _) => {
            var cts = new CancellationTokenSource();
            var prevCts = Interlocked.Exchange(ref _debounceCts, cts);
            prevCts?.Cancel();
            var ct = cts.Token;
            Task.Delay(300, ct).ContinueWith(_ => {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => Apply(themeId));
            }, ct);
        };
    }

    public void Apply(string themeName) {
        try {
            var themePath = IsUserTheme(themeName)
                ? UserThemeFilePath(themeName)
                : Path.Combine(ThemesFolder, $"{themeName}.json");
            if (!File.Exists(themePath)) {
                logger.LogWarning("Theme file not found: {Path}, using built-in default", themePath);
                ApplyBuiltinDefault();
                return;
            }

            var json = JsonNode.Parse(File.ReadAllText(themePath));
            if (json == null || Application.Current == null) {
                ApplyBuiltinDefault();
                return;
            }

            var app = Application.Current;

            var variant = json["variant"]?.GetValue<string>();
            app.RequestedThemeVariant = variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

            // ── Window ──
            var window = json["window"];
            if (window != null) {
                SetBrush(app,      "Theme.Window.Background",  window["background"]);
                SetDouble(app,     "Theme.Window.Width",        window["width"]);
                SetCornerRadius(app, "Theme.Window.CornerRadius", window["cornerRadius"]);
                SetFontFamily(app, "Theme.Window.FontFamily",  window["fontFamily"]);
            }

            // ── Search ──
            var search = json["search"];
            if (search != null) {
                SetBrush(app,      "Theme.Search.Color",       search["text"]?["color"]);
                SetDouble(app,     "Theme.Search.Size",        search["text"]?["size"]);
                SetFontFamily(app, "Theme.Search.FontFamily",  search["text"]?["fontFamily"]);
                SetBrush(app,      "Theme.Search.Placeholder", search["placeholder"]?["color"]);
                SetBrush(app,      "Theme.Search.Caret",       search["caret"]?["color"]);
                SetBrush(app,      "Theme.Search.Selection",   search["selection"]?["color"]);
                SetBrush(app,      "Theme.Search.Hint",        search["hint"]?["color"]);
            }

            // ── Divider / Spinner ──
            SetBrush(app, "Theme.Divider.Color", json["divider"]?["color"]);
            SetBrush(app, "Theme.Spinner.Color", json["spinner"]?["color"]);

            // ── Results ──
            var results = json["results"];
            if (results != null) {
                SetCornerRadius(app, "Theme.Results.CornerRadius", results["cornerRadius"]);
                SetBrush(app,  "Theme.Results.Title.Color",     results["title"]?["color"]);
                SetDouble(app, "Theme.Results.Title.Size",      results["title"]?["size"]);
                SetBrush(app,  "Theme.Results.Subtitle.Color",  results["subtitle"]?["color"]);
                SetDouble(app, "Theme.Results.Subtitle.Size",   results["subtitle"]?["size"]);
                SetBrush(app,  "Theme.Results.Category.Color",  results["category"]?["color"]);
                SetDouble(app, "Theme.Results.Category.Size",   results["category"]?["size"]);
                SetBrush(app,  "Theme.Results.Icon.Background", results["icon"]?["background"]);
                SetCornerRadius(app, "Theme.Results.Icon.CornerRadius", results["icon"]?["cornerRadius"]);
                SetBrush(app,  "Theme.Results.Shortcut.Color",      results["shortcut"]?["color"]);
                SetBrush(app,  "Theme.Results.Shortcut.Background", results["shortcut"]?["background"]);
                SetDouble(app, "Theme.Results.Shortcut.Size",       results["shortcut"]?["size"]);
                SetCornerRadius(app, "Theme.Results.Shortcut.CornerRadius", results["shortcut"]?["cornerRadius"]);

                var sel = results["selection"];
                if (sel != null) {
                    SetBrush(app, "Theme.Results.Selection.Background",     sel["background"]);
                    SetBrush(app, "Theme.Results.Selection.HoverBackground", sel["hoverBackground"]);
                    SetBrush(app, "Theme.Results.Selection.Color",          sel["color"]);
                    SetBrush(app, "Theme.Results.Selection.IconBackground", sel["iconBackground"]);
                }

                SetBrush(app, "Theme.Results.Hover.Background", results["hover"]?["background"]);
            }

            // ── Calculator ──
            var calc = json["calculator"];
            if (calc != null) {
                SetFontFamily(app, "Theme.Calc.FontFamily",        calc["fontFamily"]);
                SetBrush(app,      "Theme.Calc.Expression.Color",  calc["expression"]?["color"]);
                SetDouble(app,     "Theme.Calc.Expression.Size",   calc["expression"]?["size"]);
                SetBrush(app,      "Theme.Calc.Result.Color",      calc["result"]?["color"]);
                SetDouble(app,     "Theme.Calc.Result.Size",       calc["result"]?["size"]);
                SetBrush(app,      "Theme.Calc.Subtitle.Color",    calc["subtitle"]?["color"]);
                SetDouble(app,     "Theme.Calc.Subtitle.Size",     calc["subtitle"]?["size"]);
                SetOpacity(app,    "Theme.Calc.Subtitle.Opacity",  calc["subtitle"]?["opacity"]);
                SetBrush(app,      "Theme.Calc.Separator.Color",   calc["separator"]?["color"]);
                SetBrush(app,      "Theme.Calc.Cell.Background",   calc["cell"]?["background"]);
                SetCornerRadius(app, "Theme.Calc.Cell.CornerRadius", calc["cell"]?["cornerRadius"]);
            }

            // ── Converter ──
            var conv = json["converter"];
            if (conv != null) {
                SetFontFamily(app, "Theme.Conv.FontFamily",        conv["fontFamily"]);
                SetBrush(app,      "Theme.Conv.Value.Color",       conv["value"]?["color"]);
                SetDouble(app,     "Theme.Conv.Value.Size",        conv["value"]?["size"]);
                SetBrush(app,      "Theme.Conv.Subtitle.Color",    conv["subtitle"]?["color"]);
                SetDouble(app,     "Theme.Conv.Subtitle.Size",     conv["subtitle"]?["size"]);
                SetOpacity(app,    "Theme.Conv.Subtitle.Opacity",  conv["subtitle"]?["opacity"]);
                SetBrush(app,      "Theme.Conv.Arrow.Color",       conv["arrow"]?["color"]);
                SetBrush(app,      "Theme.Conv.Hint.Color",        conv["hint"]?["color"]);
                SetDouble(app,     "Theme.Conv.Hint.Size",         conv["hint"]?["size"]);
                SetCornerRadius(app, "Theme.Conv.Cell.CornerRadius", conv["cell"]?["cornerRadius"]);
            }

            // ── Emoji ──
            var emoji = json["emoji"];
            if (emoji != null) {
                SetInt(app,    "Theme.Emoji.Columns",          emoji["columns"]);
                SetInt(app,    "Theme.Emoji.ViewportRows",     emoji["viewportRows"]);
                SetDouble(app, "Theme.Emoji.Cell.Size",        emoji["cell"]?["size"]);
                SetCornerRadius(app, "Theme.Emoji.Cell.CornerRadius", emoji["cell"]?["cornerRadius"]);
                SetDouble(app, "Theme.Emoji.Char.Size",        emoji["char"]?["size"]);
                SetFontFamily(app, "Theme.Emoji.Char.FontFamily", emoji["char"]?["fontFamily"]);
                SetBrush(app,  "Theme.Emoji.Name.Color",       emoji["name"]?["color"]);
                SetDouble(app, "Theme.Emoji.Name.Size",        emoji["name"]?["size"]);
                SetBrush(app,  "Theme.Emoji.Keywords.Color",   emoji["keywords"]?["color"]);
                SetDouble(app, "Theme.Emoji.Keywords.Size",    emoji["keywords"]?["size"]);
                SetOpacity(app, "Theme.Emoji.Keywords.Opacity", emoji["keywords"]?["opacity"]);
            }

            // ── No Results ──
            var noResults = json["noResults"];
            if (noResults != null) {
                SetBrush(app,  "Theme.NoResults.Title.Color",    noResults["title"]?["color"]);
                SetDouble(app, "Theme.NoResults.Title.Size",     noResults["title"]?["size"]);
                SetBrush(app,  "Theme.NoResults.Subtitle.Color", noResults["subtitle"]?["color"]);
                SetDouble(app, "Theme.NoResults.Subtitle.Size",  noResults["subtitle"]?["size"]);
            }

            // ── Footer ──
            var footer = json["footer"];
            if (footer != null) {
                SetBrush(app,  "Theme.Footer.Border", footer["border"]);
                SetBrush(app,  "Theme.Footer.Color",  footer["text"]?["color"]);
                SetDouble(app, "Theme.Footer.Size",   footer["text"]?["size"]);
            }

            // ── ESC Badge ──
            var esc = json["escBadge"];
            if (esc != null) {
                SetBrush(app,  "Theme.Esc.Background",    esc["background"]);
                SetCornerRadius(app, "Theme.Esc.CornerRadius", esc["cornerRadius"]);
                SetBrush(app,  "Theme.Esc.Color",         esc["text"]?["color"]);
                SetDouble(app, "Theme.Esc.Size",          esc["text"]?["size"]);
            }

            // ── Update Banner ──
            var update = json["updateBanner"];
            if (update != null) {
                SetBrush(app,  "Theme.Update.Background", update["background"]);
                SetBrush(app,  "Theme.Update.Color",      update["text"]?["color"]);
                SetDouble(app, "Theme.Update.Size",       update["text"]?["size"]);
            }

            logger.LogInformation("Theme applied: {ThemeName}", json["name"]?.GetValue<string>() ?? themeName);
            WatchActiveTheme(themeName);
        } catch (Exception ex) {
            logger.LogWarning("Theme error applying '{ThemeName}': {Message}", themeName, ex.Message);
            ApplyBuiltinDefault();
        }
    }

    // Hardcoded fallback — mirrors dark-default.json so the app never fails to start.
    public void ApplyBuiltinDefault() {
        if (Application.Current is not { } app) return;
        logger.LogInformation("Theme applying built-in default");

        app.RequestedThemeVariant = ThemeVariant.Dark;

        static SolidColorBrush B(string hex) => new(Color.Parse(hex));

        // ── Window ──
        app.Resources["Theme.Window.Background"]    = B("#F21C1C22");
        app.Resources["Theme.Window.Width"]          = 730.0;
        app.Resources["Theme.Window.CornerRadius"]   = new CornerRadius(14);
        app.Resources["Theme.Window.FontFamily"]     = FontFamily.Default;

        // ── Search ──
        app.Resources["Theme.Search.Color"]       = B("#FFFFFF");
        app.Resources["Theme.Search.Size"]        = 18.0;
        app.Resources["Theme.Search.FontFamily"]  = FontFamily.Default;
        app.Resources["Theme.Search.Placeholder"] = B("#505055");
        app.Resources["Theme.Search.Caret"]       = B("#5E8FFF");
        app.Resources["Theme.Search.Selection"]   = B("#3560EE");
        app.Resources["Theme.Search.Hint"]        = B("#FF3B30");

        // ── Divider / Spinner ──
        app.Resources["Theme.Divider.Color"] = B("#2A2A30");
        app.Resources["Theme.Spinner.Color"] = B("#505055");

        // ── Results ──
        app.Resources["Theme.Results.CornerRadius"]              = new CornerRadius(8);
        app.Resources["Theme.Results.Title.Color"]               = B("#EAEAEE");
        app.Resources["Theme.Results.Title.Size"]                = 14.0;
        app.Resources["Theme.Results.Subtitle.Color"]            = B("#505055");
        app.Resources["Theme.Results.Subtitle.Size"]             = 12.0;
        app.Resources["Theme.Results.Category.Color"]            = B("#606068");
        app.Resources["Theme.Results.Category.Size"]             = 12.0;
        app.Resources["Theme.Results.Icon.Background"]           = B("#252529");
        app.Resources["Theme.Results.Icon.CornerRadius"]         = new CornerRadius(8);
        app.Resources["Theme.Results.Shortcut.Color"]            = B("#505055");
        app.Resources["Theme.Results.Shortcut.Background"]       = B("#252529");
        app.Resources["Theme.Results.Shortcut.Size"]             = 12.0;
        app.Resources["Theme.Results.Shortcut.CornerRadius"]     = new CornerRadius(5);
        app.Resources["Theme.Results.Selection.Background"]      = B("#2C5AF0");
        app.Resources["Theme.Results.Selection.HoverBackground"] = B("#3564FF");
        app.Resources["Theme.Results.Selection.Color"]           = B("#FFFFFF");
        app.Resources["Theme.Results.Selection.IconBackground"]  = B("#30FFFFFF");
        app.Resources["Theme.Results.Hover.Background"]          = B("#20FFFFFF");

        // ── Calculator ──
        app.Resources["Theme.Calc.FontFamily"]        = FontFamily.Default;
        app.Resources["Theme.Calc.Expression.Color"]  = B("#EAEAEE");
        app.Resources["Theme.Calc.Expression.Size"]   = 20.0;
        app.Resources["Theme.Calc.Result.Color"]      = B("#EAEAEE");
        app.Resources["Theme.Calc.Result.Size"]       = 20.0;
        app.Resources["Theme.Calc.Subtitle.Color"]    = B("#EAEAEE");
        app.Resources["Theme.Calc.Subtitle.Size"]     = 13.0;
        app.Resources["Theme.Calc.Subtitle.Opacity"]  = 0.55;
        app.Resources["Theme.Calc.Separator.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Calc.Cell.Background"]   = B("#252529");
        app.Resources["Theme.Calc.Cell.CornerRadius"]  = new CornerRadius(6);

        // ── Converter ──
        app.Resources["Theme.Conv.FontFamily"]        = FontFamily.Default;
        app.Resources["Theme.Conv.Value.Color"]       = B("#EAEAEE");
        app.Resources["Theme.Conv.Value.Size"]        = 20.0;
        app.Resources["Theme.Conv.Subtitle.Color"]    = B("#EAEAEE");
        app.Resources["Theme.Conv.Subtitle.Size"]     = 13.0;
        app.Resources["Theme.Conv.Subtitle.Opacity"]  = 0.55;
        app.Resources["Theme.Conv.Arrow.Color"]       = B("#EAEAEE");
        app.Resources["Theme.Conv.Hint.Color"]        = B("#505055");
        app.Resources["Theme.Conv.Hint.Size"]         = 12.0;
        app.Resources["Theme.Conv.Cell.CornerRadius"]  = new CornerRadius(6);

        // ── Emoji ──
        app.Resources["Theme.Emoji.Columns"]          = AppDefaults.EmojiColumns;
        app.Resources["Theme.Emoji.ViewportRows"]     = AppDefaults.EmojiViewportRows;
        app.Resources["Theme.Emoji.Cell.Size"]        = 48.0;
        app.Resources["Theme.Emoji.Cell.CornerRadius"] = new CornerRadius(8);
        app.Resources["Theme.Emoji.Char.Size"]        = 28.0;
        app.Resources["Theme.Emoji.Char.FontFamily"]  = new FontFamily("Apple Color Emoji, Segoe UI Emoji, Noto Color Emoji");
        app.Resources["Theme.Emoji.Name.Color"]       = B("#EAEAEE");
        app.Resources["Theme.Emoji.Name.Size"]        = 14.0;
        app.Resources["Theme.Emoji.Keywords.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Emoji.Keywords.Size"]    = 12.0;
        app.Resources["Theme.Emoji.Keywords.Opacity"] = 0.55;

        // ── No Results ──
        app.Resources["Theme.NoResults.Title.Color"]    = B("#505055");
        app.Resources["Theme.NoResults.Title.Size"]     = 16.0;
        app.Resources["Theme.NoResults.Subtitle.Color"] = B("#36363C");
        app.Resources["Theme.NoResults.Subtitle.Size"]  = 14.0;

        // ── Footer ──
        app.Resources["Theme.Footer.Border"] = B("#1E1E24");
        app.Resources["Theme.Footer.Color"]  = B("#606068");
        app.Resources["Theme.Footer.Size"]   = 12.0;

        // ── ESC Badge ──
        app.Resources["Theme.Esc.Background"]   = B("#252529");
        app.Resources["Theme.Esc.CornerRadius"]  = new CornerRadius(6);
        app.Resources["Theme.Esc.Color"]         = B("#444448");
        app.Resources["Theme.Esc.Size"]          = 12.0;

        // ── Update Banner ──
        app.Resources["Theme.Update.Background"] = B("#2C5AF0");
        app.Resources["Theme.Update.Color"]      = B("#FFFFFF");
        app.Resources["Theme.Update.Size"]       = 12.0;
    }

    private static void SetBrush(Application app, string key, JsonNode? node) {
        if (node == null) return;
        if (Color.TryParse(node.GetValue<string>(), out var color))
            app.Resources[key] = new SolidColorBrush(color);
    }

    private static void SetDouble(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = node.GetValue<double>();
    }

    private static void SetInt(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = node.GetValue<int>();
    }

    private static void SetOpacity(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = node.GetValue<double>();
    }

    private static void SetFontFamily(Application app, string key, JsonNode? node) {
        if (node == null) return;
        var value = node.GetValue<string>();
        app.Resources[key] = string.IsNullOrWhiteSpace(value)
            ? FontFamily.Default
            : new FontFamily(value);
    }

    private static void SetCornerRadius(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = new CornerRadius(node.GetValue<double>());
    }

    public void Dispose() {
        _activeThemeWatcher?.Dispose();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
