using System.Collections.Generic;
using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public class DateSearchResultViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "📅";
    public string Category { get; init; } = "";
    public string Subtitle { get; init; } = "";

    // ── Copyable cells (←→ navigation) ──────────────────────────────────────
    public IReadOnlyList<string> Cells { get; init; } = [];

    private int _selectedCell;
    public int SelectedCell {
        get => _selectedCell;
        set {
            if (_selectedCell == value) return;
            _selectedCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCell)));
        }
    }

    /// <summary>Move selection one cell to the left (circular). Returns false if Cells.Count &lt;= 1.</summary>
    public bool MoveCellLeft() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell - 1 + Cells.Count) % Cells.Count;
        return true;
    }

    /// <summary>Move selection one cell to the right (circular). Returns false if Cells.Count &lt;= 1.</summary>
    public bool MoveCellRight() {
        if (Cells.Count <= 1) return false;
        SelectedCell = (_selectedCell + 1) % Cells.Count;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
