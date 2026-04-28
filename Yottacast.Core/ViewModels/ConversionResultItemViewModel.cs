using System.ComponentModel;

namespace Yottacast.Core.ViewModels;

public enum ConversionCell { To, NormFrom, OrigFrom }

public class ConversionResultItemViewModel : BaseResultItemViewModel, INotifyPropertyChanged {
    public string Icon     { get; init; } = "";
    public string Category { get; init; } = "";

    // ── Original from (the value the user typed, well-formatted) ────────────
    /// <summary>Forma corta del origen original: "0.001 V"</summary>
    public string FromShort { get; init; } = "";
    /// <summary>Forma larga del origen original: "0.001 volts" — null si no disponible o igual a FromShort</summary>
    public string? FromLong  { get; init; }

    // ── Math.js normalized from (auto-simplified by math.js) ────────────────
    /// <summary>Forma corta del from normalizado: "1 mV" — null si no se normalizó</summary>
    public string? NormFromShort { get; init; }
    /// <summary>Forma larga del from normalizado: "1 millivolt" — null si no se normalizó o no añade información</summary>
    public string? NormFromLong  { get; init; }

    // ── To ───────────────────────────────────────────────────────────────────
    /// <summary>Forma corta del destino: "0.000225 mile"</summary>
    public string ToShort   { get; init; } = "";
    /// <summary>Forma larga del destino: "0.000225 miles" — null si no disponible o igual a ToShort</summary>
    public string? ToLong    { get; init; }

    /// <summary>True when math.js changed the from unit — enables left/right cell navigation.</summary>
    public bool FromWasNormalized { get; init; }

    // ── Cell selection ───────────────────────────────────────────────────────
    private ConversionCell _selectedCell = ConversionCell.To;
    public ConversionCell SelectedCell {
        get => _selectedCell;
        set {
            if (_selectedCell == value) return;
            _selectedCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCell)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOrigFromHighlighted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNormFromHighlighted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsToHighlighted)));
        }
    }

    public bool IsOrigFromHighlighted => SelectedCell == ConversionCell.OrigFrom;
    public bool IsNormFromHighlighted => FromWasNormalized && SelectedCell == ConversionCell.NormFrom;
    public bool IsToHighlighted       => SelectedCell == ConversionCell.To;

    /// <summary>Move selection one cell to the left (NormFrom ↔ To only). Returns true if consumed.</summary>
    public bool MoveCellLeft() {
        if (!FromWasNormalized) return false;
        if (SelectedCell == ConversionCell.To) {
            SelectedCell = ConversionCell.NormFrom;
            return true;
        }
        return false;
    }

    /// <summary>Move selection one cell to the right (NormFrom ↔ To only). Returns true if consumed.</summary>
    public bool MoveCellRight() {
        if (SelectedCell == ConversionCell.NormFrom) {
            SelectedCell = ConversionCell.To;
            return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
