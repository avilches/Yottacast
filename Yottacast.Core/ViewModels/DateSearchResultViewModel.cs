using System.Collections.Generic;
using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public class DateSearchResultViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "📅";
    public string Category { get; init; } = "";

    // ── Copyable cells (←→ navigation) ──────────────────────────────────────
    public IReadOnlyList<string> Cells { get; init; } = [];

    // ── Named cell accessors (for compiled bindings in AXAML) ────────────────
    public string? Cell0 => Cells.Count > 0 ? Cells[0] : null;
    public string? Cell1 => Cells.Count > 1 ? Cells[1] : null;
    public string? Cell2 => Cells.Count > 2 ? Cells[2] : null;
    public string? Cell3 => Cells.Count > 3 ? Cells[3] : null;

    /// <summary>Full content of the currently selected cell — shown in the subtitle row.</summary>
    public string SelectedCellValue => Cells.Count > _selectedCell ? Cells[_selectedCell] : "";

    private int _selectedCell;
    public int SelectedCell {
        get => _selectedCell;
        set {
            if (_selectedCell == value) return;
            _selectedCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCell)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCell0Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCell1Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCell2Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCell3Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCellValue)));
        }
    }

    public bool IsCell0Selected => SelectedCell == 0;
    public bool IsCell1Selected => SelectedCell == 1;
    public bool IsCell2Selected => SelectedCell == 2;
    public bool IsCell3Selected => SelectedCell == 3;

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
