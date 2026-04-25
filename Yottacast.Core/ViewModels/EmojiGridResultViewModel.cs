using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public record EmojiGridSection(string Header, IReadOnlyList<EmojiCellViewModel> Cells);

public class EmojiGridResultViewModel : ResultItemViewModel, INotifyPropertyChanged {
    public IReadOnlyList<EmojiCellViewModel> Cells { get; init; } = [];
    public bool HasPinnedSection { get; init; }
    public string PinnedSectionHeader { get; init; } = "";

    // Row offset within the Default section only. Pinned (Favorites + MostUsed) is
    // always rendered in full; Default has its own independent viewport scroll.
    private int _viewportStartRow = 0;

    // Number of cells in the pinned section (Favorite + MostUsed), which always
    // precede Default in the flat Cells list. O(pinned count) ≤ O(20).
    private int PinnedCount() {
        int i = 0;
        while (i < Cells.Count && Cells[i].Section != EmojiSection.Default) i++;
        return i;
    }

    public IReadOnlyList<EmojiGridSection> VisibleSections {
        get {
            var pinnedCount = PinnedCount();
            var pinnedRows = (pinnedCount + AppDefaults.EmojiColumns - 1) / AppDefaults.EmojiColumns;
            var defaultVisibleRows = Math.Max(0, AppDefaults.EmojiViewportRows - pinnedRows);

            // Always show all pinned cells; Default scrolls independently.
            var visible = Cells
                .Take(pinnedCount)
                .Concat(Cells
                    .Skip(pinnedCount + _viewportStartRow * AppDefaults.EmojiColumns)
                    .Take(defaultVisibleRows * AppDefaults.EmojiColumns))
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
        EmojiSection.Favorite or EmojiSection.MostUsed => "\u2605 Favorites",
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
        var pinnedCount = PinnedCount();
        var pinnedRows  = (pinnedCount + AppDefaults.EmojiColumns - 1) / AppDefaults.EmojiColumns;
        var defaultVisibleRows = Math.Max(1, AppDefaults.EmojiViewportRows - pinnedRows);

        if (_selectedEmojiIndex < pinnedCount) {
            // Pinned cell selected: always fully visible; reset Default viewport to top.
            if (_viewportStartRow == 0) return;
            _viewportStartRow = 0;
        } else {
            // Default cell: scroll the Default viewport to keep it visible.
            var defaultIndex = _selectedEmojiIndex - pinnedCount;
            var row = defaultIndex / AppDefaults.EmojiColumns;
            if (row < _viewportStartRow) {
                _viewportStartRow = row;
            } else if (row >= _viewportStartRow + defaultVisibleRows) {
                _viewportStartRow = row - defaultVisibleRows + 1;
            } else {
                return;
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSections)));
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
