using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Services;

namespace Yottacast.Services;

public record ThemeOption(string Id, string DisplayName);

public sealed class ThemeService(ILogger<ThemeService> logger, EmojiLayoutConfig emojiLayoutConfig) : IDisposable {
    private const string UserThemePrefix = "user:";

    private string _themesFolder = null!;

    private string ThemesFolder => _themesFolder ??= ResolveThemesFolder();

    private string ResolveThemesFolder() {
        var baseDir = AppContext.BaseDirectory;
        var sep = Path.DirectorySeparatorChar;
        if (baseDir.Contains(sep + "bin" + sep)) {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Themes"));
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "dark-default.json"))) {
                logger.LogInformation("Dev mode: watching themes from source tree at {Path}", candidate);
                return candidate;
            }
        }
        return Path.Combine(baseDir, "Themes");
    }

    private FileSystemWatcher? _activeThemeWatcher;
    private CancellationTokenSource? _debounceCts;
    private string? _activeThemeId;
    private PluginService? _pluginService;

    // Maps theme id → actual file path on disk (populated by AvailableThemes, covers all themes)
    private readonly Dictionary<string, string> _themePaths = new();

    public event Action? ThemesChanged;

    public static bool IsUserTheme(string id) => id.StartsWith(UserThemePrefix);

    private string? ThemeFilePath(string id) => _themePaths.GetValueOrDefault(id);

    public IReadOnlyList<ThemeOption> AvailableThemes() {
        var themes = new List<ThemeOption>();
        var seen   = new HashSet<string>();
        _themePaths.Clear();

        // Built-in themes
        try {
            foreach (var file in Directory.GetFiles(ThemesFolder, "*.json").OrderBy(f => f)) {
                try {
                    var json = JsonNode.Parse(File.ReadAllText(file));
                    var id = json?["id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id))
                        id = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(id)) continue;
                    _themePaths[id] = file;
                    var displayName = json?["name"]?.GetValue<string>() ?? id;
                    themes.Add(new ThemeOption(id, displayName));
                } catch {
                    var id = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(id)) continue;
                    _themePaths[id] = file;
                    themes.Add(new ThemeOption(id, id));
                }
            }
        } catch (Exception ex) {
            logger.LogWarning("Could not load themes: {Message}", ex.Message);
        }

        // User themes from plugins directory (theme.*.json files with required "id" field)
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
                        if (!seen.Add(id)) {
                            logger.LogWarning("User theme {File}: duplicate id '{Id}', skipping", Path.GetFileName(file), themeId);
                            continue;
                        }
                        _themePaths[id] = file;
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
        _pluginService = pluginService;
        pluginService.PluginsChanged += OnPluginsChanged;

        if (_activeThemeId != null)
            WatchActiveTheme(_activeThemeId);
    }

    private void OnPluginsChanged() => ThemesChanged?.Invoke();

    private void WatchActiveTheme(string themeId) {
        _activeThemeWatcher?.Dispose();
        _activeThemeWatcher = null;
        _activeThemeId = themeId;

        var filePath = ThemeFilePath(themeId);
        if (filePath == null) return;
        var watchDir = Path.GetDirectoryName(filePath)!;
        _activeThemeWatcher = new FileSystemWatcher(watchDir, Path.GetFileName(filePath)) {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        void OnChanged(object? _, FileSystemEventArgs __) {
            var cts = new CancellationTokenSource();
            var prevCts = Interlocked.Exchange(ref _debounceCts, cts);
            if (prevCts != null) {
                try {
                    prevCts.Cancel();
                } catch (ObjectDisposedException) {
                    // Already disposed by a concurrent Dispose(); nothing to cancel.
                }
                prevCts.Dispose();
            }
            var ct = cts.Token;
            Task.Delay(AppDefaults.PluginReloadDebounceMs, ct).ContinueWith(_ => {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => Apply(themeId));
            }, ct);
        }
        _activeThemeWatcher.Changed += OnChanged;
        _activeThemeWatcher.Created += OnChanged;
        _activeThemeWatcher.Renamed += OnChanged;  // editores con guardado atómico (write-rename)
    }

    public void Apply(string themeName) {
        try {
            // Ensure the path cache is populated (may be empty on first Apply at startup)
            if (!_themePaths.ContainsKey(themeName))
                AvailableThemes();
            var themePath = ThemeFilePath(themeName);
            if (themePath == null || !File.Exists(themePath)) {
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

            // Apply defaults first so all tokens always have a value,
            // even if the theme JSON omits some sections.
            ApplyBuiltinDefault();

            var variant = json["variant"]?.GetValue<string>();
            app.RequestedThemeVariant = variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

            // ── Window ──
            var window = json["window"];
            if (window != null) {
                SetBrush(app,      "Theme.Window.Background",  window["background"]);
                SetDouble(app,     "Theme.Window.Width",        window["width"]);
                SetCornerRadius(app, "Theme.Window.CornerRadius", window["cornerRadius"]);
                SetFontFamily(app, "Theme.Window.FontFamily",  window["fontFamily"]);
                // Derived: corner radius variants for footer and editor panel
                if (window["cornerRadius"] != null) {
                    var cr = window["cornerRadius"]!.GetValue<double>();
                    app.Resources["Theme.Window.CornerRadius.Bottom"]      = new CornerRadius(0, 0, cr, cr);
                    app.Resources["Theme.Window.CornerRadius.BottomRight"] = new CornerRadius(0, 0, cr, 0);
                }
            }

            // ── Search ──
            var search = json["search"];
            if (search != null) {
                SetBrush(app,      "Theme.Search.Background",  search["background"]);
                var input = search["input"];
                if (input != null) {
                    SetCornerRadius(app, "Theme.Search.Input.CornerRadius",      input["cornerRadius"]);
                    SetThickness(app,    "Theme.Search.Input.Margin",            input["margin"]);
                    SetThickness(app,    "Theme.Search.Input.Padding",           input["padding"]);
                    SetBrush(app,        "Theme.Search.Input.BorderColor",       input["border"]?["color"]);
                    SetThickness(app,    "Theme.Search.Input.BorderThickness",   input["border"]?["thickness"]);
                }
                SetBrush(app,      "Theme.Search.Color",       search["text"]?["color"]);
                SetDouble(app,     "Theme.Search.Size",        search["text"]?["size"]);
                SetFontFamily(app, "Theme.Search.FontFamily",  search["text"]?["fontFamily"]);
                SetBrush(app,      "Theme.Search.Placeholder", search["placeholder"]?["color"]);
                SetBrush(app,      "Theme.Search.Caret",       search["caret"]?["color"]);
                SetBrush(app,      "Theme.Search.Selection",   search["selection"]?["color"]);
                var hint = search["hint"];
                SetHintStyle(app, "Error", hint?["error"]);
                SetHintStyle(app, "Info",  hint?["info"]);
            }

            // ── Groups (mode chips: All / Files / Clipboard) ──
            var groups = json["groups"];
            if (groups != null) {
                SetDouble(app,     "Theme.Groups.Height",     groups["height"]);
                SetThickness(app,  "Theme.Groups.Padding",    groups["padding"]);
                SetThickness(app,  "Theme.Groups.Margin",     groups["margin"]);
                SetDouble(app,     "Theme.Groups.Size",       groups["text"]?["size"]);
                SetFontFamily(app, "Theme.Groups.FontFamily", groups["text"]?["fontFamily"]);
                var gNormal = groups["normal"];
                if (gNormal != null) {
                    SetBrush(app,        "Theme.Groups.Normal.Color",           gNormal["text"]?["color"]);
                    SetCornerRadius(app, "Theme.Groups.Normal.CornerRadius",    gNormal["cornerRadius"]);
                    SetBrush(app,        "Theme.Groups.Normal.BorderColor",     gNormal["border"]?["color"]);
                    SetThickness(app,    "Theme.Groups.Normal.BorderThickness", gNormal["border"]?["thickness"]);
                }
                var gSelected = groups["selected"];
                if (gSelected != null) {
                    SetBrush(app,        "Theme.Groups.Selected.Color",           gSelected["text"]?["color"]);
                    SetCornerRadius(app, "Theme.Groups.Selected.CornerRadius",    gSelected["cornerRadius"]);
                    SetBrush(app,        "Theme.Groups.Selected.BorderColor",     gSelected["border"]?["color"]);
                    SetThickness(app,    "Theme.Groups.Selected.BorderThickness", gSelected["border"]?["thickness"]);
                }
            }

            // ── Divider / Spinner ──
            SetBrush(app, "Theme.Divider.Color", json["divider"]?["color"]);
            SetBrush(app, "Theme.Spinner.Color", json["spinner"]?["color"]);

            // ── Results ──
            var results = json["results"];
            if (results != null) {
                SetBrush(app,   "Theme.Results.Background", results["background"]);
                SetDouble(app,  "Theme.Results.MaxHeight",  results["maxHeight"]);
                if (results["padding"] != null) {
                    var p = results["padding"]!.GetValue<double>();
                    app.Resources["Theme.Results.Padding"] = new Thickness(p);
                }
                var selBar = results["selectionBar"];
                if (selBar != null) {
                    SetBrush(app, "Theme.Results.SelectionBar.Color", selBar["color"]);
                    if (selBar["width"] != null) {
                        var barWidth = selBar["width"]!.GetValue<double>();
                        app.Resources["Theme.Results.SelectionBar.Thickness"]    = new Thickness(barWidth, 0, 0, 0);
                        app.Resources["Theme.Results.SelectionBar.ContentPadding"] = new Thickness(Math.Max(0, 10 - barWidth), 0, 10, 0);
                    }
                }
                SetCornerRadius(app, "Theme.Results.CornerRadius", results["cornerRadius"]);
                SetBrush(app,  "Theme.Results.Title.Color",     results["title"]?["color"]);
                SetDouble(app, "Theme.Results.Title.Size",      results["title"]?["size"]);
                SetBrush(app,  "Theme.Results.Subtitle.Color",  results["subtitle"]?["color"]);
                SetDouble(app, "Theme.Results.Subtitle.Size",   results["subtitle"]?["size"]);
                var clipMode = results["clipboardMode"];
                if (clipMode != null) {
                    SetDouble(app, "Theme.Results.ClipboardMode.Title.Size", clipMode["titleSize"]);
                    SetDouble(app, "Theme.Results.ClipboardMode.RowHeight",  clipMode["rowHeight"]);
                }
                SetBrush(app,  "Theme.Results.Category.Color",  results["category"]?["color"]);
                SetDouble(app, "Theme.Results.Category.Size",   results["category"]?["size"]);
                SetCornerRadius(app, "Theme.Results.Icon.CornerRadius", results["icon"]?["cornerRadius"]);
                SetBrush(app,  "Theme.Results.Shortcut.Color",      results["shortcut"]?["color"]);
                SetBrush(app,  "Theme.Results.Shortcut.Background", results["shortcut"]?["background"]);
                SetDouble(app, "Theme.Results.Shortcut.Size",       results["shortcut"]?["size"]);
                SetCornerRadius(app, "Theme.Results.Shortcut.CornerRadius", results["shortcut"]?["cornerRadius"]);

                var sel = results["selection"];
                if (sel != null) {
                    SetBrush(app, "Theme.Results.Selection.Background", sel["background"]);
                    SetBrush(app, "Theme.Results.Selection.Color",      sel["color"]);
                    // Subtitle on a selected item: falls back to the selection text color when the theme omits it.
                    SetBrush(app, "Theme.Results.Selection.SubtitleColor", sel["subtitleColor"] ?? sel["color"]);
                }

                var mh = results["matchHighlight"];
                if (mh != null) {
                    SetBrush(app, "Theme.Results.MatchHighlight.Color",      mh["color"]);
                    SetBrush(app, "Theme.Results.MatchHighlight.Background", mh["background"]);
                }

                var tags = results["tags"];
                if (tags != null) {
                    SetCornerRadius(app, "Theme.Results.Tag.CornerRadius",          tags["cornerRadius"]);
                    SetBrush(app,  "Theme.Results.Tag.Running.Color",                    tags["running"]?["color"]);
                    SetBrush(app,  "Theme.Results.Tag.Running.Background",           tags["running"]?["background"]);
                    SetBrush(app,  "Theme.Results.Tag.Running.Background.Selected",  tags["running"]?["backgroundSelected"]);
                    SetBrush(app,  "Theme.Results.Tag.Running.BorderColor",          tags["running"]?["borderColor"]);
                    SetBrush(app,  "Theme.Results.Tag.Info.Color",                   tags["info"]?["color"]);
                    SetBrush(app,  "Theme.Results.Tag.Info.Background",              tags["info"]?["background"]);
                    SetBrush(app,  "Theme.Results.Tag.Info.Background.Selected",     tags["info"]?["backgroundSelected"]);
                    SetBrush(app,  "Theme.Results.Tag.Info.BorderColor",             tags["info"]?["borderColor"]);
                    SetBrush(app,  "Theme.Results.Tag.Error.Color",                  tags["error"]?["color"]);
                    SetBrush(app,  "Theme.Results.Tag.Error.Background",             tags["error"]?["background"]);
                    SetBrush(app,  "Theme.Results.Tag.Error.Background.Selected",    tags["error"]?["backgroundSelected"]);
                    SetBrush(app,  "Theme.Results.Tag.Error.BorderColor",            tags["error"]?["borderColor"]);
                }

            }

            // ── Calculator ──
            var calc = json["calculator"];
            if (calc != null) {
                SetFontFamily(app, "Theme.Calc.FontFamily",        calc["fontFamily"]);
                SetBrush(app,       "Theme.Calc.Expression.Color",      calc["expression"]?["color"]);
                SetDouble(app,      "Theme.Calc.Expression.Size",       calc["expression"]?["size"]);
                SetFontWeight(app,  "Theme.Calc.Expression.FontWeight",  calc["expression"]?["fontWeight"]);
                SetBrush(app,       "Theme.Calc.Result.Color",           calc["result"]?["color"]);
                SetDouble(app,      "Theme.Calc.Result.Size",            calc["result"]?["size"]);
                SetFontWeight(app,  "Theme.Calc.Result.FontWeight",      calc["result"]?["fontWeight"]);
                SetBrush(app,      "Theme.Calc.Subtitle.Color",    calc["subtitle"]?["color"]);
                SetDouble(app,     "Theme.Calc.Subtitle.Size",     calc["subtitle"]?["size"]);
                SetOpacity(app,    "Theme.Calc.Subtitle.Opacity",  calc["subtitle"]?["opacity"]);
                SetBrush(app,      "Theme.Calc.Separator.Color",   calc["separator"]?["color"]);
                SetCornerRadius(app, "Theme.Calc.Cell.CornerRadius", calc["cell"]?["cornerRadius"]);
            }

            // ── Emoji ──
            var emoji = json["emoji"];
            if (emoji != null) {
                SetDouble(app, "Theme.Emoji.Cell.Size",        emoji["cell"]?["size"]);
                SetCornerRadius(app, "Theme.Emoji.Cell.CornerRadius", emoji["cell"]?["cornerRadius"]);
                SetDouble(app, "Theme.Emoji.Char.Size",        emoji["char"]?["size"]);
                SetFontFamily(app, "Theme.Emoji.Char.FontFamily", emoji["char"]?["fontFamily"]);
                SetBrush(app,  "Theme.Emoji.Keywords.Color",   emoji["keywords"]?["color"]);
                SetDouble(app, "Theme.Emoji.Keywords.Size",    emoji["keywords"]?["size"]);
                SetOpacity(app, "Theme.Emoji.Keywords.Opacity", emoji["keywords"]?["opacity"]);
                SetBrush(app,  "Theme.Emoji.SectionHeader.Color",   emoji["sectionHeader"]?["color"]);
                SetDouble(app, "Theme.Emoji.SectionHeader.Size",    emoji["sectionHeader"]?["size"]);
                SetOpacity(app, "Theme.Emoji.SectionHeader.Opacity", emoji["sectionHeader"]?["opacity"]);
                SetBrush(app,  "Theme.Emoji.Favorite.Color",   emoji["favorite"]?["color"]);
                SetDouble(app, "Theme.Emoji.Favorite.Size",    emoji["favorite"]?["size"]);
                SetOpacity(app, "Theme.Emoji.Favorite.Opacity", emoji["favorite"]?["opacity"]);
                SetBrush(app,  "Theme.Emoji.UsageCount.Color",   emoji["usageCount"]?["color"]);
                SetDouble(app, "Theme.Emoji.UsageCount.Size",    emoji["usageCount"]?["size"]);
                SetOpacity(app, "Theme.Emoji.UsageCount.Opacity", emoji["usageCount"]?["opacity"]);

                // Calculate columns/rows from theme dimensions
                var windowWidth       = window?["width"]?.GetValue<double>()               ?? AppDefaults.WindowDefaultWidth;
                var maxHeight         = results?["maxHeight"]?.GetValue<double>()           ?? 540.0;
                var resultsPadding    = results?["padding"]?.GetValue<double>()             ?? 8.0;
                var cellSize          = emoji["cell"]?["size"]?.GetValue<double>()          ?? 48.0;
                var cellMargin        = emoji["cell"]?["margin"]?.GetValue<double>()        ?? 2.0;
                var sectionHeaderSize = emoji["sectionHeader"]?["size"]?.GetValue<double>() ?? 11.0;
                CalculateEmojiLayout(app, windowWidth, maxHeight, resultsPadding, cellSize, cellMargin, sectionHeaderSize);
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
                SetBrush(app,  "Theme.Footer.Background", footer["background"]);
                SetBrush(app,  "Theme.Footer.Border",     footer["border"]);
                SetBrush(app,  "Theme.Footer.Color",      footer["text"]?["color"]);
                SetDouble(app, "Theme.Footer.Size",       footer["text"]?["size"]);
            }


            // ── Options Menu ──
            var menu = json["optionsMenu"];
            if (menu != null) {
                SetBrush(app,        "Theme.Menu.Background",               menu["background"]);
                SetBrush(app,        "Theme.Menu.Border.Color",             menu["border"]?["color"]);
                SetCornerRadius(app, "Theme.Menu.Border.Radius",            menu["border"]?["radius"]);
                SetBrush(app,        "Theme.Menu.Header.Color",             menu["header"]?["color"]);
                SetDouble(app,       "Theme.Menu.Header.Size",              menu["header"]?["size"]);
                SetBrush(app,        "Theme.Menu.Header.Background",        menu["header"]?["background"]);
                SetThickness(app,    "Theme.Menu.Header.Padding",           menu["header"]?["padding"]);
                SetThickness(app,    "Theme.Menu.Header.Margin",            menu["header"]?["margin"]);
                SetBrush(app,        "Theme.Menu.Option.Color",             menu["option"]?["color"]);
                SetDouble(app,       "Theme.Menu.Option.Size",              menu["option"]?["size"]);
                SetThickness(app,    "Theme.Menu.Option.Padding",           menu["option"]?["padding"]);
                SetCornerRadius(app, "Theme.Menu.Option.CornerRadius",      menu["option"]?["cornerRadius"]);
                SetBrush(app,        "Theme.Menu.OptionSelected.Background", menu["optionSelected"]?["background"]);
                SetBrush(app,        "Theme.Menu.OptionSelected.Color",      menu["optionSelected"]?["color"]);
            }

            // ── Update Banner ──
            var update = json["updateBanner"];
            if (update != null) {
                SetBrush(app,  "Theme.Update.Background", update["background"]);
                SetBrush(app,  "Theme.Update.Color",      update["text"]?["color"]);
                SetDouble(app, "Theme.Update.Size",       update["text"]?["size"]);
            }

            // ── Preview Panel ──
            app.Resources["Theme.Preview.Width"] = json["preview"]?["width"]?.GetValue<double>() ?? AppDefaults.EditorWidth;

            // ── Editor ──
            var editorNode = json["editor"];
            if (editorNode != null) {
                var edHeader = editorNode["header"];
                if (edHeader != null) {
                    SetBrush(app,      "Theme.Editor.Header.Background", edHeader["background"]);
                    SetBrush(app,      "Theme.Editor.Header.Color",      edHeader["color"]);
                    SetDouble(app,     "Theme.Editor.Header.Size",       edHeader["size"]);
                    SetThickness(app,  "Theme.Editor.Header.Padding",    edHeader["padding"]);
                    SetThickness(app,  "Theme.Editor.Header.Margin",     edHeader["margin"]);
                    SetFontFamily(app, "Theme.Editor.Header.FontFamily", edHeader["fontFamily"]);
                }
                var edBody = editorNode["body"];
                if (edBody != null)
                    SetBrush(app, "Theme.Editor.Body.Background", edBody["background"]);
                var edFooter = editorNode["footer"];
                if (edFooter != null) {
                    SetBrush(app,     "Theme.Editor.Footer.Background", edFooter["background"]);
                    SetBrush(app,     "Theme.Editor.Footer.Border",     edFooter["border"]);
                    SetBrush(app,     "Theme.Editor.Footer.Color",      edFooter["color"]);
                    SetDouble(app,    "Theme.Editor.Footer.Size",       edFooter["size"]);
                    SetThickness(app, "Theme.Editor.Footer.Padding",    edFooter["padding"]);
                }
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
        app.Resources["Theme.Window.Background"]         = B("#1C1C22");
        app.Resources["Theme.Window.Width"]               = AppDefaults.WindowDefaultWidth;
        app.Resources["Theme.Window.CornerRadius"]             = new CornerRadius(14);
        app.Resources["Theme.Window.CornerRadius.Bottom"]      = new CornerRadius(0, 0, 14, 14);
        app.Resources["Theme.Window.CornerRadius.BottomRight"] = new CornerRadius(0, 0, 14, 0);
        app.Resources["Theme.Window.FontFamily"]          = new FontFamily("SF Pro Text, Lucida Grande, Segoe UI, Inter");

        // ── Search ──
        app.Resources["Theme.Search.Background"]             = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Search.Input.CornerRadius"]     = new CornerRadius(0);
        app.Resources["Theme.Search.Input.Margin"]           = new Thickness(0);
        app.Resources["Theme.Search.Input.Padding"]          = new Thickness(18, 0, 18, 0);
        app.Resources["Theme.Search.Input.BorderColor"]      = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Search.Input.BorderThickness"]  = new Thickness(0);
        app.Resources["Theme.Search.Color"]       = B("#FFFFFF");
        app.Resources["Theme.Search.Size"]        = 18.0;
        app.Resources["Theme.Search.FontFamily"]  = new FontFamily("SF Pro Text, Segoe UI, Inter");
        app.Resources["Theme.Search.Placeholder"] = B("#505055");
        app.Resources["Theme.Search.Caret"]       = B("#5E8FFF");
        app.Resources["Theme.Search.Selection"]   = B("#3560EE");
        SetHintStyleDefaults(app, "Error", "#FF3B30");
        SetHintStyleDefaults(app, "Info",  "#9A9AA4");

        // ── Groups (mode chips) ──
        app.Resources["Theme.Groups.Height"]                  = 24.0;
        app.Resources["Theme.Groups.Padding"]                 = new Thickness(10, 4, 10, 4);
        app.Resources["Theme.Groups.Margin"]                  = new Thickness(0, 0, 6, 0);
        app.Resources["Theme.Groups.Size"]                    = 12.0;
        app.Resources["Theme.Groups.FontFamily"]              = new FontFamily("SF Pro Text, Segoe UI, Inter");
        app.Resources["Theme.Groups.Normal.Color"]            = B("#70707A");
        app.Resources["Theme.Groups.Normal.CornerRadius"]     = new CornerRadius(12);
        app.Resources["Theme.Groups.Normal.BorderColor"]      = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Groups.Normal.BorderThickness"]  = new Thickness(0);
        app.Resources["Theme.Groups.Selected.Color"]          = B("#FFFFFF");
        app.Resources["Theme.Groups.Selected.CornerRadius"]   = new CornerRadius(12);
        app.Resources["Theme.Groups.Selected.BorderColor"]    = B("#FFFFFF");
        app.Resources["Theme.Groups.Selected.BorderThickness"] = new Thickness(1);

        // ── Divider / Spinner ──
        app.Resources["Theme.Divider.Color"] = B("#2A2A30");
        app.Resources["Theme.Spinner.Color"] = B("#505055");

        // ── Results ──
        app.Resources["Theme.Results.MaxHeight"]                 = 540.0;
        app.Resources["Theme.Results.Background"]                = B("#0D0D12");
        app.Resources["Theme.Results.Padding"]                   = new Thickness(8);
        app.Resources["Theme.Results.SelectionBar.Color"]          = B("#2C5AF0");
        app.Resources["Theme.Results.SelectionBar.Thickness"]      = new Thickness(4, 0, 0, 0);
        app.Resources["Theme.Results.SelectionBar.ContentPadding"] = new Thickness(6, 0, 10, 0);
        app.Resources["Theme.Results.CornerRadius"]              = new CornerRadius(8);
        app.Resources["Theme.Results.Title.Color"]               = B("#EAEAEE");
        app.Resources["Theme.Results.Title.Size"]                = 14.0;
        app.Resources["Theme.Results.Subtitle.Color"]            = B("#9A9AA4");
        app.Resources["Theme.Results.Subtitle.Size"]             = 12.0;
        // Clipboard-only mode: compact single-line rows (title font size + row height).
        app.Resources["Theme.Results.ClipboardMode.Title.Size"]  = 13.0;
        app.Resources["Theme.Results.ClipboardMode.RowHeight"]   = 26.0;
        app.Resources["Theme.Results.Category.Color"]            = B("#606068");
        app.Resources["Theme.Results.Category.Size"]             = 12.0;
        app.Resources["Theme.Results.Icon.CornerRadius"]         = new CornerRadius(8);
        app.Resources["Theme.Results.Shortcut.Color"]            = B("#505055");
        app.Resources["Theme.Results.Shortcut.Background"]       = B("#252529");
        app.Resources["Theme.Results.Shortcut.Size"]             = 12.0;
        app.Resources["Theme.Results.Shortcut.CornerRadius"]     = new CornerRadius(5);
        app.Resources["Theme.Results.Selection.Background"] = B("#2C5AF0");
        app.Resources["Theme.Results.Selection.Color"]     = B("#FFFFFF");
        app.Resources["Theme.Results.Selection.SubtitleColor"] = B("#CCFFFFFF");
        app.Resources["Theme.Results.MatchHighlight.Color"]      = B("#FFFFFF");
        app.Resources["Theme.Results.MatchHighlight.Background"] = B("#662C5AF0");

        // ── Result Tags (pills) ──
        app.Resources["Theme.Results.Tag.CornerRadius"]          = new CornerRadius(4);
        app.Resources["Theme.Results.Tag.Running.Color"]                    = B("#30D158");
        app.Resources["Theme.Results.Tag.Running.Background"]           = B("#2430D158");  // green ~14% opacity (ARGB)
        app.Resources["Theme.Results.Tag.Running.Background.Selected"]  = B("#8030D158");  // green ~50% opacity
        app.Resources["Theme.Results.Tag.Running.BorderColor"]          = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Results.Tag.Info.Color"]                   = B("#5AC8FA");
        app.Resources["Theme.Results.Tag.Info.Background"]              = B("#1A0A84FF");  // blue ~10% opacity (ARGB)
        app.Resources["Theme.Results.Tag.Info.Background.Selected"]     = B("#805AC8FA");  // info blue ~50% opacity
        app.Resources["Theme.Results.Tag.Info.BorderColor"]             = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Results.Tag.Error.Color"]                  = B("#FF453A");
        app.Resources["Theme.Results.Tag.Error.Background"]             = B("#24FF453A");  // red ~14% opacity (ARGB)
        app.Resources["Theme.Results.Tag.Error.Background.Selected"]    = B("#80FF453A");  // red ~50% opacity
        app.Resources["Theme.Results.Tag.Error.BorderColor"]            = new SolidColorBrush(Colors.Transparent);

        // ── Calculator ──
        app.Resources["Theme.Calc.FontFamily"]        = new FontFamily("avares://Yottacast/Assets/Fonts#Geist Mono, SF Mono, Menlo, Consolas, monospace");
        app.Resources["Theme.Calc.Expression.Color"]       = B("#EAEAEE");
        app.Resources["Theme.Calc.Expression.Size"]        = 20.0;
        app.Resources["Theme.Calc.Expression.FontWeight"]  = FontWeight.Medium;
        app.Resources["Theme.Calc.Result.Color"]           = B("#EAEAEE");
        app.Resources["Theme.Calc.Result.Size"]            = 20.0;
        app.Resources["Theme.Calc.Result.FontWeight"]      = FontWeight.Medium;
        app.Resources["Theme.Calc.Subtitle.Color"]    = B("#EAEAEE");
        app.Resources["Theme.Calc.Subtitle.Size"]     = 13.0;
        app.Resources["Theme.Calc.Subtitle.Opacity"]  = 0.55;
        app.Resources["Theme.Calc.Separator.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Calc.Cell.CornerRadius"]  = new CornerRadius(6);

        // ── Emoji ──
        app.Resources["Theme.Emoji.Columns"]          = AppDefaults.EmojiColumns;
        app.Resources["Theme.Emoji.Cell.Size"]        = 48.0;
        app.Resources["Theme.Emoji.Cell.CornerRadius"] = new CornerRadius(8);
        app.Resources["Theme.Emoji.Char.Size"]        = 28.0;
        app.Resources["Theme.Emoji.Char.FontFamily"]  = new FontFamily("Apple Color Emoji, Segoe UI Emoji, Noto Color Emoji");
        app.Resources["Theme.Emoji.Keywords.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Emoji.Keywords.Size"]    = 12.0;
        app.Resources["Theme.Emoji.Keywords.Opacity"] = 0.55;
        app.Resources["Theme.Emoji.SectionHeader.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Emoji.SectionHeader.Size"]    = 11.0;
        app.Resources["Theme.Emoji.SectionHeader.Opacity"] = 0.5;
        app.Resources["Theme.Emoji.Favorite.Color"]   = B("#FFD60A");
        app.Resources["Theme.Emoji.Favorite.Size"]    = 8.0;
        app.Resources["Theme.Emoji.Favorite.Opacity"] = 0.7;
        app.Resources["Theme.Emoji.UsageCount.Color"]   = B("#EAEAEE");
        app.Resources["Theme.Emoji.UsageCount.Size"]    = 9.0;
        app.Resources["Theme.Emoji.UsageCount.Opacity"] = 0.4;
        app.Resources["Theme.Emoji.Cell.Margin"] = new Thickness(2);
        CalculateEmojiLayout(app, windowWidth: AppDefaults.WindowDefaultWidth, maxHeight: 540.0,
            resultsPadding: 8.0, cellSize: 48.0, cellMargin: 2.0, sectionHeaderSize: 11.0);

        // ── No Results ──
        app.Resources["Theme.NoResults.Title.Color"]    = B("#505055");
        app.Resources["Theme.NoResults.Title.Size"]     = 16.0;
        app.Resources["Theme.NoResults.Subtitle.Color"] = B("#36363C");
        app.Resources["Theme.NoResults.Subtitle.Size"]  = 14.0;

        // ── Footer ──
        app.Resources["Theme.Footer.Background"] = B("#1C1C22");
        app.Resources["Theme.Footer.Border"]     = B("#2A2A30");
        app.Resources["Theme.Footer.Color"]      = B("#9A9AA4");
        app.Resources["Theme.Footer.Size"]       = 13.0;


        // ── Options Menu ──
        app.Resources["Theme.Menu.Background"]                = B("#1C1C22");
        app.Resources["Theme.Menu.Border.Color"]              = B("#3A3A50");
        app.Resources["Theme.Menu.Border.Radius"]             = new CornerRadius(10);
        app.Resources["Theme.Menu.Header.Color"]              = B("#606068");
        app.Resources["Theme.Menu.Header.Size"]               = 11.0;
        app.Resources["Theme.Menu.Header.Background"]         = new SolidColorBrush(Colors.Transparent);
        app.Resources["Theme.Menu.Header.Padding"]            = new Thickness(12, 6, 12, 4);
        app.Resources["Theme.Menu.Header.Margin"]             = new Thickness(0, 0, 0, 2);
        app.Resources["Theme.Menu.Option.Color"]              = B("#EAEAEE");
        app.Resources["Theme.Menu.Option.Size"]               = 13.0;
        app.Resources["Theme.Menu.Option.Padding"]            = new Thickness(12, 7);
        app.Resources["Theme.Menu.Option.CornerRadius"]       = new CornerRadius(6);
        app.Resources["Theme.Menu.OptionSelected.Background"] = B("#2C5AF0");
        app.Resources["Theme.Menu.OptionSelected.Color"]      = B("#FFFFFF");

        // ── Update Banner ──
        app.Resources["Theme.Update.Background"] = B("#2C5AF0");
        app.Resources["Theme.Update.Color"]      = B("#FFFFFF");
        app.Resources["Theme.Update.Size"]       = 12.0;

        // ── Preview Panel ──
        app.Resources["Theme.Preview.Width"] = AppDefaults.EditorWidth;

        // ── Editor ──
        app.Resources["Theme.Editor.Header.Background"] = B("#0D0D12");
        app.Resources["Theme.Editor.Header.Color"]      = B("#606068");
        app.Resources["Theme.Editor.Header.Size"]       = 12.0;
        app.Resources["Theme.Editor.Header.Padding"]    = new Thickness(12, 8);
        app.Resources["Theme.Editor.Header.Margin"]     = new Thickness(0);
        app.Resources["Theme.Editor.Header.FontFamily"] = new FontFamily("SF Pro Text, Lucida Grande, Segoe UI, Inter");
        app.Resources["Theme.Editor.Body.Background"]   = B("#0D0D12");
        app.Resources["Theme.Editor.Footer.Background"] = B("#1C1C22");
        app.Resources["Theme.Editor.Footer.Border"]     = B("#2A2A30");
        app.Resources["Theme.Editor.Footer.Color"]      = B("#606068");
        app.Resources["Theme.Editor.Footer.Size"]       = 11.0;
        app.Resources["Theme.Editor.Footer.Padding"]    = new Thickness(12, 6);
    }

    private static void SetHintStyle(Application app, string kind, JsonNode? node) {
        if (node == null) return;
        var p = $"Theme.Search.Hint.{kind}";
        SetBrush(app,        $"{p}.Color",        node["color"]);
        SetDouble(app,       $"{p}.Size",         node["size"]);
        SetFontFamily(app,   $"{p}.FontFamily",   node["fontFamily"]);
        SetThickness(app,    $"{p}.Padding",      node["padding"]);
        SetBrush(app,        $"{p}.Background",   node["background"]);
        SetCornerRadius(app, $"{p}.CornerRadius", node["cornerRadius"]);
        SetThickness(app,    $"{p}.Margin",       node["margin"]);
        if (node["horizontalAlignment"] != null)
            app.Resources[$"{p}.HorizontalAlignment"] = node["horizontalAlignment"]!.GetValue<string>().ToLowerInvariant() switch {
                "left"   => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "right"  => HorizontalAlignment.Right,
                _        => HorizontalAlignment.Stretch
            };
        if (node["textAlignment"] != null)
            app.Resources[$"{p}.TextAlignment"] = node["textAlignment"]!.GetValue<string>().ToLowerInvariant() switch {
                "center" => TextAlignment.Center,
                "right"  => TextAlignment.Right,
                _        => TextAlignment.Left
            };
    }

    private static void SetHintStyleDefaults(Application app, string kind, string defaultColor) {
        var p = $"Theme.Search.Hint.{kind}";
        app.Resources[$"{p}.Color"]               = new SolidColorBrush(Color.Parse(defaultColor));
        app.Resources[$"{p}.Size"]                = 12.0;
        app.Resources[$"{p}.FontFamily"]          = new FontFamily("SF Pro Text, Segoe UI, Inter");
        app.Resources[$"{p}.Padding"]             = new Thickness(0);
        app.Resources[$"{p}.Background"]          = new SolidColorBrush(Colors.Transparent);
        app.Resources[$"{p}.CornerRadius"]        = new CornerRadius(0);
        app.Resources[$"{p}.Margin"]              = new Thickness(20, 0, 20, 8);
        app.Resources[$"{p}.HorizontalAlignment"] = HorizontalAlignment.Stretch;
        app.Resources[$"{p}.TextAlignment"]       = TextAlignment.Left;
    }

    private void CalculateEmojiLayout(
        Application app,
        double windowWidth, double maxHeight,
        double resultsPadding, double cellSize, double cellMargin,
        double sectionHeaderSize)
    {
        var cellH = cellSize + 2 * cellMargin;

        // Horizontal overhead: outer window border margin (28×2=56)
        //   + ListBox padding (resultsPadding×2) + ListBoxItem padding (10×2=20)
        //   + EmojiGridResultView StackPanel margin (4×2=8)
        var horizontalOverhead = 56 + 2 * resultsPadding + 28;
        var columns = Math.Max(1, (int)Math.Floor((windowWidth - horizontalOverhead) / cellH));

        // Vertical overhead: ListBox padding (resultsPadding×2) + ListBoxItem margin (1×2=2)
        //   + StackPanel outer margin (8+8=16) + info panel TextBlock (~12+6=18)
        //   + 3 section headers each (sectionHeaderSize + margin 6+2=8)
        var sectionHeaderH = (int)(sectionHeaderSize + 8);
        var verticalOverhead = (int)(2 * resultsPadding) + 2 + 16 + 18 + 3 * sectionHeaderH;
        var rows = Math.Max(2, (int)Math.Floor((maxHeight - verticalOverhead) / cellH));

        emojiLayoutConfig.Columns      = columns;
        emojiLayoutConfig.ViewportRows = rows;
        app.Resources["Theme.Emoji.Columns"]     = columns;
        app.Resources["Theme.Emoji.Cell.Margin"] = new Thickness(cellMargin);
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

    private static void SetString(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = node.GetValue<string>();
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

    private static void SetFontWeight(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = node.GetValue<string>() switch {
            "Thin"       => FontWeight.Thin,
            "ExtraLight" => FontWeight.ExtraLight,
            "Light"      => FontWeight.Light,
            "Regular"    => FontWeight.Regular,
            "Medium"     => FontWeight.Medium,
            "SemiBold"   => FontWeight.SemiBold,
            "Bold"       => FontWeight.Bold,
            "ExtraBold"  => FontWeight.ExtraBold,
            "Black"      => FontWeight.Black,
            _            => FontWeight.Regular,
        };
    }

    // Parses "l,t,r,b" | "h,v" | "uniform" strings or a bare number
    private static void SetThickness(Application app, string key, JsonNode? node) {
        if (node == null) return;
        try {
            var s = node.GetValue<string>();
            var parts = s.Split(',')
                .Select(p => double.Parse(p.Trim(), System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            app.Resources[key] = parts.Length switch {
                1 => new Thickness(parts[0]),
                2 => new Thickness(parts[0], parts[1], parts[0], parts[1]),
                4 => new Thickness(parts[0], parts[1], parts[2], parts[3]),
                _ => new Thickness(0)
            };
        } catch {
            try { app.Resources[key] = new Thickness(node.GetValue<double>()); } catch { }
        }
    }

    private static void SetCornerRadius(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = new CornerRadius(node.GetValue<double>());
    }

    public void Dispose() {
        if (_pluginService != null) {
            _pluginService.PluginsChanged -= OnPluginsChanged;
            _pluginService = null;
        }
        _activeThemeWatcher?.Dispose();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
