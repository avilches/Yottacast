namespace Yottacast.Core.Search.Emoji;

/// <summary>
/// Mutable singleton holding the current emoji grid dimensions.
/// ThemeService writes these values on every theme change;
/// EmojiSearch reads them when constructing EmojiGridResultViewModel.
/// </summary>
public class EmojiLayoutConfig {
    public int Columns      { get; set; } = AppDefaults.EmojiColumns;
    public int ViewportRows { get; set; } = AppDefaults.EmojiViewportRows;
}
