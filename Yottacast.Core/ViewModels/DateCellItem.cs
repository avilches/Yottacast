using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

/// <summary>
/// Represents a single copyable cell inside a DateSearchResultView.
/// Holds its value and tracks whether it is currently selected (highlighted).
/// </summary>
public sealed class DateCellItem : INotifyPropertyChanged {
    public string Value { get; }

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

    public DateCellItem(string value, bool isSelected = false) {
        Value       = value;
        _isSelected = isSelected;
    }
}
