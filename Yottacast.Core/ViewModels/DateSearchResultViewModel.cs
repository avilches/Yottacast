using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Yottacast.Core.ViewModels;

public class DateSearchResultViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "📅";
    public string Category { get; init; } = "";

    // ── Copyable cells (←→ navigation) ──────────────────────────────────────
    private IReadOnlyList<string> _cells = [];
    public IReadOnlyList<string> Cells {
        get => _cells;
        init {
            _cells    = value;
            CellItems = value.Select((v, i) => new DateCellItem(v, isSelected: i == 0))
                             .ToList();
        }
    }

    /// <summary>Per-cell items exposed to the view for uniform-grid rendering.</summary>
    public IReadOnlyList<DateCellItem> CellItems { get; private set; } = [];

    // ── Per-cell subtitles ────────────────────────────────────────────────────
    /// <summary>
    /// One subtitle string per cell. The subtitle for the currently selected cell
    /// is shown below the cells row to give contextual information.
    /// </summary>
    public IReadOnlyList<string> CellSubtitles { get; init; } = [];

    /// <summary>Subtitle for the currently selected cell.</summary>
    public string SelectedCellSubtitle =>
        CellSubtitles.Count > _selectedCell ? CellSubtitles[_selectedCell] : "";

    // ── Selection ─────────────────────────────────────────────────────────────
    private int _selectedCell;
    public int SelectedCell {
        get => _selectedCell;
        set {
            if (_selectedCell == value) return;
            foreach (var (item, i) in CellItems.Select((c, i) => (c, i)))
                item.IsSelected = i == value;
            _selectedCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCell)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCellSubtitle)));
        }
    }

    /// <summary>Move selection one cell to the left (circular). Returns false if Cells.Count ≤ 1.</summary>
    public bool MoveCellLeft() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell - 1 + Cells.Count) % Cells.Count;
        return true;
    }

    /// <summary>Move selection one cell to the right (circular). Returns false if Cells.Count ≤ 1.</summary>
    public bool MoveCellRight() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell + 1) % Cells.Count;
        return true;
    }

    public DragPayload BuildDragPayload() {
        if (Cells.Count == 0) return new DragPayload.Text("");
        var idx = System.Math.Clamp(SelectedCell, 0, Cells.Count - 1);
        return new DragPayload.Text(Cells[idx]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
