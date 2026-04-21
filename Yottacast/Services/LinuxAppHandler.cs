using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;

namespace Yottacast.Services;

internal sealed class LinuxAppHandler : AppHandler {
    public override void OnFrameworkInitializationCompleted() { }
    public override void OnHide() { }
    public override (KeyModifiers Modifiers, Key Key) CloseWindowShortcut => (KeyModifiers.Control, Key.W);

    public override IReadOnlyList<(KeyModifiers, Key)> ForbiddenHotkeys =>
        [(KeyModifiers.Control, Key.W)];

    // Linux Settings theme — neutral GTK-inspired palette (placeholder, matches macOS for now).
    // Font: Ubuntu or system default.
    public override void ApplySettingsTheme(Window window) {
        window.FontFamily = new Avalonia.Media.FontFamily("Ubuntu, Cantarell, sans-serif");
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = MakeThemeDict(
            ("Theme.WindowBackground",   Brush("#F6F5F4")),
            ("Theme.ItemIconBackground", Brush("#EAEAEA")),
            ("Theme.Divider",            Brush("#CFCFCF")),
            ("Theme.ItemTitle",          Brush("#1C1C1C")),
            ("Theme.ItemSubtitle",       Brush("#636363")),
            ("Theme.ItemCategory",       Brush("#8A8A8A")),
            ("Theme.ItemSelection",      Brush("#3584E4")),
            ("Theme.ItemSelectionText",  Brush("#FFFFFF")),
            ("Theme.ItemHover",          Brush("#12000000")),
            ("Theme.FooterText",         Brush("#8A8A8A")),
            ("Theme.SearchCaret",        Brush("#3584E4")),
            ("Theme.FontSizeTitle",      13d),
            ("Theme.FontSizeSmall",      11d),
            ("Theme.FontSizeNoResults",  14d)
        );
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = MakeThemeDict(
            ("Theme.WindowBackground",   Brush("#242424")),
            ("Theme.ItemIconBackground", Brush("#303030")),
            ("Theme.Divider",            Brush("#3A3A3A")),
            ("Theme.ItemTitle",          Brush("#FFFFFF")),
            ("Theme.ItemSubtitle",       Brush("#ABABAB")),
            ("Theme.ItemCategory",       Brush("#6B6B6B")),
            ("Theme.ItemSelection",      Brush("#3584E4")),
            ("Theme.ItemSelectionText",  Brush("#FFFFFF")),
            ("Theme.ItemHover",          Brush("#18FFFFFF")),
            ("Theme.FooterText",         Brush("#6B6B6B")),
            ("Theme.SearchCaret",        Brush("#78AEED")),
            ("Theme.FontSizeTitle",      13d),
            ("Theme.FontSizeSmall",      11d),
            ("Theme.FontSizeNoResults",  14d)
        );
    }
}