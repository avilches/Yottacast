using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public class EmojiGridResultViewModel : ResultItemViewModel, INotifyPropertyChanged {
    public IReadOnlyList<EmojiCellViewModel> Cells { get; init; } = [];

    private int _selectedEmojiIndex;
    public int SelectedEmojiIndex {
        get => _selectedEmojiIndex;
        set {
            if (_selectedEmojiIndex == value) return;
            if (Cells.Count > 0) {
                Cells[_selectedEmojiIndex].IsSelected = false;
                Cells[value].IsSelected = true;
            }
            _selectedEmojiIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedEmojiIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedEmoji)));
        }
    }

    public EmojiCellViewModel? SelectedEmoji =>
        Cells.Count > 0 ? Cells[SelectedEmojiIndex] : null;

    public const int Columns = AppDefaults.EmojiColumns;

    public void SelectNext()     => SelectedEmojiIndex = (SelectedEmojiIndex + 1)       % Cells.Count;
    public void SelectPrevious() => SelectedEmojiIndex = (SelectedEmojiIndex - 1 + Cells.Count) % Cells.Count;
    public bool SelectDown() {
        if (SelectedEmojiIndex + Columns >= Cells.Count) return false;
        SelectedEmojiIndex += Columns;
        return true;
    }

    public bool SelectUp() {
        if (SelectedEmojiIndex < Columns) return false;
        SelectedEmojiIndex -= Columns;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
