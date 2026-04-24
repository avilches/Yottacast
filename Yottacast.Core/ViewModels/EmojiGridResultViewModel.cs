using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public record EmojiGridSection(string Header, IReadOnlyList<EmojiCellViewModel> Cells);

public class EmojiGridResultViewModel : ResultItemViewModel, INotifyPropertyChanged {
    public IReadOnlyList<EmojiCellViewModel> Cells { get; init; } = [];
    public bool HasPinnedSection { get; init; }
    public string PinnedSectionHeader { get; init; } = "";

    private int _viewportStartRow = 0;

    public IReadOnlyList<EmojiCellViewModel> VisibleCells =>
        Cells
            .Skip(_viewportStartRow * AppDefaults.EmojiColumns)
            .Take(AppDefaults.EmojiViewportRows * AppDefaults.EmojiColumns)
            .ToList();

    public IReadOnlyList<EmojiGridSection> VisibleSections {
        get {
            var visible = Cells
                .Skip(_viewportStartRow * AppDefaults.EmojiColumns)
                .Take(AppDefaults.EmojiViewportRows * AppDefaults.EmojiColumns)
                .ToList();

            return GroupIntoSections(visible);
        }
    }

    private static IReadOnlyList<EmojiGridSection> GroupIntoSections(List<EmojiCellViewModel> cells) {
        var sections = new List<EmojiGridSection>();
        if (cells.Count == 0) return sections;

        string currentKey = SectionKey(cells[0]);
        var currentCells = new List<EmojiCellViewModel>();

        foreach (var cell in cells) {
            var key = SectionKey(cell);
            if (key != currentKey) {
                sections.Add(new EmojiGridSection(currentKey, currentCells.ToList()));
                currentKey = key;
                currentCells.Clear();
            }
            currentCells.Add(cell);
        }
        if (currentCells.Count > 0)
            sections.Add(new EmojiGridSection(currentKey, currentCells.ToList()));

        return sections;
    }

    private static string SectionKey(EmojiCellViewModel cell) => cell.Section switch {
        EmojiSection.Favorite => "\u2605 Favorites",
        EmojiSection.MostUsed => "Frequently Used",
        _ => cell.Category
    };

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
            EnsureVisible();
        }
    }

    private void EnsureVisible() {
        var row = _selectedEmojiIndex / AppDefaults.EmojiColumns;
        if (row < _viewportStartRow) {
            _viewportStartRow = row;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSections)));
        } else if (row >= _viewportStartRow + AppDefaults.EmojiViewportRows) {
            _viewportStartRow = row - AppDefaults.EmojiViewportRows + 1;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSections)));
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
