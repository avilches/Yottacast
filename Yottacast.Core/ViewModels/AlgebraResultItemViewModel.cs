using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.ViewModels;

/// <summary>
/// Result item for algebraic expressions (simplify / expand / factor / derivatives / integral).
/// Cells are navigated left/right; Enter copies the selected cell's Result.
/// Follows the same pattern as DateSearchResultViewModel.
/// </summary>
public class AlgebraResultItemViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "🧮";
    public string Category { get; init; } = "Calculator";

    // ── Cells ─────────────────────────────────────────────────────────────────
    private IReadOnlyList<AlgebraCell> _cells = [];

    public IReadOnlyList<AlgebraCell> Cells {
        get => _cells;
        init {
            _cells    = value;
            CellItems = value.Select((c, i) => new AlgebraCellItem(c.Label, c.Result, isSelected: i == 0))
                             .ToList();
        }
    }

    /// <summary>Per-cell items exposed to the view for UniformGrid rendering.</summary>
    public IReadOnlyList<AlgebraCellItem> CellItems { get; private set; } = [];

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCellLabel)));
        }
    }

    /// <summary>Label of the currently selected cell (e.g. "factor", "d/dx").</summary>
    public string SelectedCellLabel =>
        CellItems.Count > _selectedCell ? CellItems[_selectedCell].Label : "";

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

    public event PropertyChangedEventHandler? PropertyChanged;
}
