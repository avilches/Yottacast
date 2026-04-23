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
            ("Theme.Window.Background",            Brush("#F6F5F4")),
            ("Theme.Results.Icon.Background",      Brush("#EAEAEA")),
            ("Theme.Divider.Color",                Brush("#CFCFCF")),
            ("Theme.Results.Title.Color",          Brush("#1C1C1C")),
            ("Theme.Results.Subtitle.Color",       Brush("#636363")),
            ("Theme.Results.Category.Color",       Brush("#8A8A8A")),
            ("Theme.Results.Selection.Background", Brush("#3584E4")),
            ("Theme.Results.Selection.Color",      Brush("#FFFFFF")),
            ("Theme.Results.Hover.Background",     Brush("#12000000")),
            ("Theme.Footer.Color",                 Brush("#8A8A8A")),
            ("Theme.Search.Caret",                 Brush("#3584E4")),
            ("Theme.Results.Title.Size",           13d),
            ("Theme.Results.Category.Size",        11d),
            ("Theme.NoResults.Title.Size",         14d)
        );
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = MakeThemeDict(
            ("Theme.Window.Background",            Brush("#242424")),
            ("Theme.Results.Icon.Background",      Brush("#303030")),
            ("Theme.Divider.Color",                Brush("#3A3A3A")),
            ("Theme.Results.Title.Color",          Brush("#FFFFFF")),
            ("Theme.Results.Subtitle.Color",       Brush("#ABABAB")),
            ("Theme.Results.Category.Color",       Brush("#6B6B6B")),
            ("Theme.Results.Selection.Background", Brush("#3584E4")),
            ("Theme.Results.Selection.Color",      Brush("#FFFFFF")),
            ("Theme.Results.Hover.Background",     Brush("#18FFFFFF")),
            ("Theme.Footer.Color",                 Brush("#6B6B6B")),
            ("Theme.Search.Caret",                 Brush("#78AEED")),
            ("Theme.Results.Title.Size",           13d),
            ("Theme.Results.Category.Size",        11d),
            ("Theme.NoResults.Title.Size",         14d)
        );
    }
}