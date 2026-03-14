using System;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Yottacast.Services;

public static class ThemeService {
    private static string ThemesFolder =>
        Path.Combine(AppContext.BaseDirectory, "Themes");

    public static void Apply(string themeName) {
        try {
            var themePath = Path.Combine(ThemesFolder, $"{themeName}.json");
            if (!File.Exists(themePath)) {
                Console.WriteLine($"[Theme] File not found: {themePath}, using built-in default");
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

            var colors = json["colors"];
            if (colors != null) {
                SetBrush(app, "Theme.WindowBackground",            colors["windowBackground"]);
                SetBrush(app, "Theme.SearchText",                  colors["searchText"]);
                SetBrush(app, "Theme.SearchPlaceholder",           colors["searchPlaceholder"]);
                SetBrush(app, "Theme.SearchCaret",                 colors["searchCaret"]);
                SetBrush(app, "Theme.SearchSelection",             colors["searchSelection"]);
                SetBrush(app, "Theme.Icon",                        colors["icon"]);
                SetBrush(app, "Theme.Divider",                     colors["divider"]);
                SetBrush(app, "Theme.ItemIconBackground",          colors["itemIconBackground"]);
                SetBrush(app, "Theme.ItemTitle",                   colors["itemTitle"]);
                SetBrush(app, "Theme.ItemSubtitle",                colors["itemSubtitle"]);
                SetBrush(app, "Theme.ItemCategory",                colors["itemCategory"]);
                SetBrush(app, "Theme.ItemShortcutText",            colors["itemShortcutText"]);
                SetBrush(app, "Theme.ItemShortcutBackground",      colors["itemShortcutBackground"]);
                SetBrush(app, "Theme.ItemSelection",               colors["itemSelection"]);
                SetBrush(app, "Theme.ItemSelectionHover",          colors["itemSelectionHover"]);
                SetBrush(app, "Theme.ItemHover",                   colors["itemHover"]);
                SetBrush(app, "Theme.ItemSelectionText",           colors["itemSelectionText"]);
                SetBrush(app, "Theme.ItemSelectionIconBackground", colors["itemSelectionIconBackground"]);
                SetBrush(app, "Theme.EscBadgeBackground",          colors["escBadgeBackground"]);
                SetBrush(app, "Theme.EscBadgeText",                colors["escBadgeText"]);
                SetBrush(app, "Theme.FooterBorder",                colors["footerBorder"]);
                SetBrush(app, "Theme.FooterText",                  colors["footerText"]);
                SetBrush(app, "Theme.NoResultsTitle",              colors["noResultsTitle"]);
                SetBrush(app, "Theme.NoResultsSubtitle",           colors["noResultsSubtitle"]);
            }

            var fonts = json["fonts"];
            if (fonts != null) {
                SetDouble(app, "Theme.FontSizeSearch",    fonts["search"]);
                SetDouble(app, "Theme.FontSizeTitle",     fonts["title"]);
                SetDouble(app, "Theme.FontSizeSubtitle",  fonts["subtitle"]);
                SetDouble(app, "Theme.FontSizeSmall",     fonts["small"]);
                SetDouble(app, "Theme.FontSizeNoResults", fonts["noResults"]);
            }

            var layout = json["layout"];
            if (layout != null) {
                SetCornerRadius(app, "Theme.CornerRadiusWindow",   layout["windowCornerRadius"]);
                SetCornerRadius(app, "Theme.CornerRadiusItem",     layout["itemCornerRadius"]);
                SetCornerRadius(app, "Theme.CornerRadiusIcon",     layout["iconCornerRadius"]);
                SetCornerRadius(app, "Theme.CornerRadiusEsc",      layout["escCornerRadius"]);
                SetCornerRadius(app, "Theme.CornerRadiusShortcut", layout["shortcutCornerRadius"]);
                SetDouble(app, "Theme.WindowWidth",                layout["windowWidth"]);
            }

            Console.WriteLine($"[Theme] Applied: {json["name"]?.GetValue<string>() ?? themeName}");
        } catch (Exception ex) {
            Console.WriteLine($"[Theme] Error applying '{themeName}': {ex.Message}");
            ApplyBuiltinDefault();
        }
    }

    // Hardcoded fallback — mirrors dark-default.json so the app never fails to start.
    public static void ApplyBuiltinDefault() {
        if (Application.Current is not { } app) return;
        Console.WriteLine("[Theme] Applying built-in default theme");

        app.RequestedThemeVariant = ThemeVariant.Dark;

        static SolidColorBrush B(string hex) => new(Color.Parse(hex));

        app.Resources["Theme.WindowBackground"]            = B("#F21C1C22");
        app.Resources["Theme.SearchText"]                  = B("#FFFFFF");
        app.Resources["Theme.SearchPlaceholder"]           = B("#505055");
        app.Resources["Theme.SearchCaret"]                 = B("#5E8FFF");
        app.Resources["Theme.SearchSelection"]             = B("#3560EE");
        app.Resources["Theme.Icon"]                        = B("#505055");
        app.Resources["Theme.Divider"]                     = B("#2A2A30");
        app.Resources["Theme.ItemIconBackground"]          = B("#252529");
        app.Resources["Theme.ItemTitle"]                   = B("#EAEAEE");
        app.Resources["Theme.ItemSubtitle"]                = B("#505055");
        app.Resources["Theme.ItemCategory"]                = B("#36363C");
        app.Resources["Theme.ItemShortcutText"]            = B("#505055");
        app.Resources["Theme.ItemShortcutBackground"]      = B("#252529");
        app.Resources["Theme.ItemSelection"]               = B("#2C5AF0");
        app.Resources["Theme.ItemSelectionHover"]          = B("#3564FF");
        app.Resources["Theme.ItemHover"]                   = B("#20FFFFFF");
        app.Resources["Theme.ItemSelectionText"]           = B("#FFFFFF");
        app.Resources["Theme.ItemSelectionIconBackground"] = B("#30FFFFFF");
        app.Resources["Theme.EscBadgeBackground"]          = B("#252529");
        app.Resources["Theme.EscBadgeText"]                = B("#444448");
        app.Resources["Theme.FooterBorder"]                = B("#1E1E24");
        app.Resources["Theme.FooterText"]                  = B("#36363C");
        app.Resources["Theme.NoResultsTitle"]              = B("#505055");
        app.Resources["Theme.NoResultsSubtitle"]           = B("#36363C");

        app.Resources["Theme.FontSizeSearch"]    = 18.0;
        app.Resources["Theme.FontSizeTitle"]     = 14.0;
        app.Resources["Theme.FontSizeSubtitle"]  = 12.0;
        app.Resources["Theme.FontSizeSmall"]     = 11.0;
        app.Resources["Theme.FontSizeNoResults"] = 16.0;

        app.Resources["Theme.CornerRadiusWindow"]   = new CornerRadius(14);
        app.Resources["Theme.CornerRadiusItem"]     = new CornerRadius(8);
        app.Resources["Theme.CornerRadiusIcon"]     = new CornerRadius(8);
        app.Resources["Theme.CornerRadiusEsc"]      = new CornerRadius(6);
        app.Resources["Theme.CornerRadiusShortcut"] = new CornerRadius(5);
        app.Resources["Theme.WindowWidth"]          = 700.0;
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

    private static void SetCornerRadius(Application app, string key, JsonNode? node) {
        if (node == null) return;
        app.Resources[key] = new CornerRadius(node.GetValue<double>());
    }
}
