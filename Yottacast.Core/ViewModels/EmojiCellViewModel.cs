using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public class EmojiCellViewModel : INotifyPropertyChanged {
    public string Char { get; init; } = "";
    public string Name { get; init; } = "";

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
