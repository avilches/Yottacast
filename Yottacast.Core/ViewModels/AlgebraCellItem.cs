using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

/// <summary>
/// Represents a single algebra result cell inside AlgebraResultItemViewModel.
/// Holds the operation label (e.g. "factor"), the symbolic result, and observable selection state.
/// </summary>
public sealed class AlgebraCellItem : INotifyPropertyChanged {
    public string Label  { get; }
    public string Result { get; }

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

    public AlgebraCellItem(string label, string result, bool isSelected = false) {
        Label       = label;
        Result      = result;
        _isSelected = isSelected;
    }
}
