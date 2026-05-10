using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public record EmojiGridSection(string Header, IReadOnlyList<EmojiCellViewModel> Cells);

public class EmojiGridResultViewModel : ResultItemViewModel, INotifyPropertyChanged {
    public IReadOnlyList<EmojiCellViewModel> Cells { get; init; } = [];
    public bool HasPinnedSection { get; init; }
    public string PinnedSectionHeader { get; init; } = "";
    public int Columns      { get; init; } = AppDefaults.EmojiColumns;
    public int ViewportRows { get; init; } = AppDefaults.EmojiViewportRows;

    // Absolute cell offset from pinnedCount for the Default viewport start.
    // Always row-aligned (multiple of Columns).
    // Pinned (Favorites + MostUsed) is always rendered in full; Default scrolls independently.
    private int _viewportStartCell = 0;

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
            var pinnedRows = (pinnedCount + Columns - 1) / Columns;
            var defaultVisibleRows = Math.Max(0, ViewportRows - pinnedRows);

            // Always show all pinned cells.
            // For Default, take only as many real cells as fit in defaultVisibleRows rendered rows.
            // Simply taking defaultVisibleRows*Columns real cells is wrong: padding at section
            // boundaries consumes extra rendered rows, causing subsequent sections to overflow.
            var takeCount = ComputeVisibleDefaultCount(pinnedCount, _viewportStartCell, defaultVisibleRows);
            var visible = Cells
                .Take(pinnedCount)
                .Concat(Cells.Skip(pinnedCount + _viewportStartCell).Take(takeCount))
                .ToList();

            return GroupIntoSections(visible);
        }
    }

    // Returns how many real Default cells fit in 'maxRows' rendered rows starting at 'start'.
    // Each section boundary may add padding cells to fill a partial row, so the rendered row
    // count can exceed (real cells / Columns). This method accounts for that correctly.
    private int ComputeVisibleDefaultCount(int pinnedCount, int start, int maxRows) {
        int remainingRows = maxRows;
        int i = start;
        int totalDefault = Cells.Count - pinnedCount;
        int count = 0;

        while (remainingRows > 0 && i < totalDefault) {
            // Find end of current section
            var key = SectionKey(Cells[pinnedCount + i]);
            int j = i;
            while (j < totalDefault && SectionKey(Cells[pinnedCount + j]) == key) j++;

            int sectionCells = j - i;
            int sectionRows  = (sectionCells + Columns - 1) / Columns;

            if (sectionRows <= remainingRows) {
                // Take the entire section (it will be padded but fits in remaining rows)
                count += sectionCells;
                remainingRows -= sectionRows;
                i = j;
            } else {
                // Take only as many cells as fill the remaining rows (last section in slice)
                count += remainingRows * Columns;
                remainingRows = 0;
            }
        }

        return count;
    }

    private IReadOnlyList<EmojiGridSection> GroupIntoSections(List<EmojiCellViewModel> cells) {
        var sections = new List<EmojiGridSection>();
        if (cells.Count == 0) return sections;

        string currentKey = SectionKey(cells[0]);
        var currentCells = new List<EmojiCellViewModel>();

        foreach (var cell in cells) {
            var key = SectionKey(cell);
            if (key != currentKey) {
                // Pad current section to a full row before starting the next section,
                // so each section's UniformGrid always starts at column 0 (no orphan partial rows).
                int remainder = currentCells.Count % Columns;
                if (remainder != 0) {
                    int pad = Columns - remainder;
                    for (int i = 0; i < pad; i++)
                        currentCells.Add(EmojiCellViewModel.Placeholder);
                }
                sections.Add(new EmojiGridSection(currentKey, currentCells.ToList()));
                currentKey = key;
                currentCells.Clear();
            }
            currentCells.Add(cell);
        }
        // Last section: no padding — a partial row at the bottom of the viewport is fine.
        if (currentCells.Count > 0)
            sections.Add(new EmojiGridSection(currentKey, currentCells.ToList()));

        return sections;
    }

    private static string SectionKey(EmojiCellViewModel cell) => cell.Section switch {
        EmojiSection.Favorite or EmojiSection.MostUsed => "Favorites & recently used",
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

    // Returns the Default-relative index where the section containing 'defaultIndex' starts.
    private int SectionStartOf(int pinnedCount, int defaultIndex) {
        var key = SectionKey(Cells[pinnedCount + defaultIndex]);
        int s = defaultIndex;
        while (s > 0 && SectionKey(Cells[pinnedCount + s - 1]) == key) s--;
        return s;
    }

    // Returns the Default-relative index where the section containing 'defaultIndex' ends
    // (exclusive: first index of the next section, or totalDefault if it's the last section).
    private int SectionEndOf(int pinnedCount, int defaultIndex) {
        int totalDefault = Cells.Count - pinnedCount;
        var key = SectionKey(Cells[pinnedCount + defaultIndex]);
        int e = defaultIndex;
        while (e < totalDefault && SectionKey(Cells[pinnedCount + e]) == key) e++;
        return e;
    }

    // Snaps 'pos' to the start of the row within its section (section-row-aligned).
    // Invariant: (result - sectionStart) % Columns == 0
    private int AlignToSectionRow(int pinnedCount, int pos) {
        int sectionStart = SectionStartOf(pinnedCount, pos);
        int offset = pos - sectionStart;
        return sectionStart + (offset / Columns) * Columns;
    }

    private void EnsureVisible() {
        var pinnedCount = PinnedCount();
        var pinnedRows  = (pinnedCount + Columns - 1) / Columns;
        var defaultVisibleRows = Math.Max(1, ViewportRows - pinnedRows);

        if (_selectedEmojiIndex < pinnedCount) {
            // Pinned cell selected: always fully visible; reset Default viewport to top.
            if (_viewportStartCell == 0) return;
            _viewportStartCell = 0;
        } else {
            var defaultIndex = _selectedEmojiIndex - pinnedCount;

            // Use ComputeVisibleDefaultCount (not defaultVisibleRows*Columns) because padding at
            // section boundaries reduces the number of real cells that fit in the viewport.
            var visibleCount = ComputeVisibleDefaultCount(pinnedCount, _viewportStartCell, defaultVisibleRows);
            if (defaultIndex >= _viewportStartCell && defaultIndex < _viewportStartCell + visibleCount)
                return; // already visible

            int newStart;
            if (defaultIndex < _viewportStartCell) {
                // Scroll UP: section-row-aligned start for the row containing the selected cell.
                // (defaultIndex / Columns) * Columns would be flat-aligned — wrong for sections
                // that don't start at a Columns multiple. Use sectionStart as the base instead.
                newStart = AlignToSectionRow(pinnedCount, defaultIndex);
            } else {
                // Scroll DOWN: estimate a start that puts the selected cell near the bottom,
                // then align it to the section row boundary.
                var rawStart = defaultIndex - defaultVisibleRows * Columns + 1;
                if (rawStart <= 0) {
                    newStart = 0;
                } else {
                    // Ceiling-align within the section: snap to the next section-row boundary
                    // at or after rawStart.
                    int sectionStart = SectionStartOf(pinnedCount, rawStart);
                    int offset = rawStart - sectionStart;
                    int alignedOffset = ((offset + Columns - 1) / Columns) * Columns;
                    newStart = sectionStart + alignedOffset;

                    // If that lands in or past the section's partial tail, jump to the next section.
                    int sectionEnd = SectionEndOf(pinnedCount, sectionStart);
                    bool isPartialTail = (sectionEnd - sectionStart) % Columns != 0;
                    if (isPartialTail && newStart >= sectionStart +
                            ((sectionEnd - sectionStart - 1) / Columns) * Columns
                        && newStart > sectionStart) {
                        newStart = sectionEnd;
                    }
                }

                // Advance row by row (section-row-aligned) if the selected cell is still outside
                // the rendered viewport (section padding may have reduced visible real cells).
                while (newStart < defaultIndex) {
                    var vc = ComputeVisibleDefaultCount(pinnedCount, newStart, defaultVisibleRows);
                    if (defaultIndex < newStart + vc) break;
                    // Step to the next section-row-aligned position.
                    int stepSectionEnd = SectionEndOf(pinnedCount, newStart);
                    int nextInSection  = newStart + Columns;
                    newStart = nextInSection < stepSectionEnd ? nextInSection : stepSectionEnd;
                }
            }

            _viewportStartCell = Math.Max(0, newStart);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSections)));
    }

    public EmojiCellViewModel? SelectedEmoji =>
        Cells.Count > 0 ? Cells[SelectedEmojiIndex] : null;

    public void SelectNext()     => SelectedEmojiIndex = (SelectedEmojiIndex + 1)       % Cells.Count;
    public void SelectPrevious() => SelectedEmojiIndex = (SelectedEmojiIndex - 1 + Cells.Count) % Cells.Count;

    public void SelectByIndex(int index) {
        if (index >= 0 && index < Cells.Count)
            SelectedEmojiIndex = index;
    }

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

    public void SetShowUsageCount(bool show) {
        foreach (var cell in Cells)
            cell.ShowUsage = show && cell.HasUsageCount;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
