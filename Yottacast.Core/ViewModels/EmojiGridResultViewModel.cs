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

    private record SectionRange(int Start, int Count);

    private IReadOnlyList<SectionRange> GetSectionRanges() {
        if (Cells.Count == 0) return [];
        var ranges = new List<SectionRange>();
        int start = 0;
        string currentKey = SectionKey(Cells[0]);
        for (int i = 1; i < Cells.Count; i++) {
            var key = SectionKey(Cells[i]);
            if (key != currentKey) {
                ranges.Add(new SectionRange(start, i - start));
                start = i;
                currentKey = key;
            }
        }
        ranges.Add(new SectionRange(start, Cells.Count - start));
        return ranges;
    }

    private (int SectionIndex, int Row, int Col) GetPosition(IReadOnlyList<SectionRange> sections) {
        for (int s = 0; s < sections.Count; s++) {
            var sec = sections[s];
            if (_selectedEmojiIndex >= sec.Start && _selectedEmojiIndex < sec.Start + sec.Count) {
                int pos = _selectedEmojiIndex - sec.Start;
                return (s, pos / Columns, pos % Columns);
            }
        }
        return (0, 0, 0);
    }

    public bool SelectDown() {
        var sections = GetSectionRanges();
        var (sIdx, row, col) = GetPosition(sections);
        var sec = sections[sIdx];
        int totalRows = (sec.Count + Columns - 1) / Columns;

        if (row + 1 < totalRows) {
            int target = sec.Start + (row + 1) * Columns + col;
            SelectedEmojiIndex = Math.Min(target, sec.Start + sec.Count - 1);
            return true;
        }
        if (sIdx + 1 < sections.Count) {
            var next = sections[sIdx + 1];
            int target = next.Start + col;
            SelectedEmojiIndex = Math.Min(target, next.Start + next.Count - 1);
            return true;
        }
        return false;
    }

    public bool SelectUp() {
        var sections = GetSectionRanges();
        var (sIdx, row, col) = GetPosition(sections);

        if (row > 0) {
            SelectedEmojiIndex = sections[sIdx].Start + (row - 1) * Columns + col;
            return true;
        }
        if (sIdx > 0) {
            var prev = sections[sIdx - 1];
            int lastRow = (prev.Count - 1) / Columns;
            int target = prev.Start + lastRow * Columns + col;
            SelectedEmojiIndex = Math.Min(target, prev.Start + prev.Count - 1);
            return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
