using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public enum EmojiSection { Default, Favorite, MostUsed }

public class EmojiCellViewModel : INotifyPropertyChanged {
    public string   Char     { get; init; } = "";
    public string   Name     { get; init; } = "";
    public string   Category { get; init; } = "";
    public string[] Keywords { get; init; } = [];
    public EmojiSection Section { get; init; } = EmojiSection.Default;

    public string KeywordsText => Keywords.Length > 0 ? string.Join(", ", Keywords) : "";
    public string SectionLabel => Section switch {
        EmojiSection.Favorite => "★ Favorite",
        EmojiSection.MostUsed => "Frequently Used",
        _ => "",
    };

    private bool _isFavorite;
    public bool IsFavorite {
        get => _isFavorite;
        set {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    private bool _isSelected;
    public bool IsSelected {
        get => _isSelected;
        set {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
