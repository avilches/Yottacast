namespace Yottacast.Core.ViewModels;

public class ConversionResultItemViewModel : ResultItemViewModel {
    /// <summary>Forma corta del origen: "12 km"</summary>
    public string FromShort { get; init; } = "";
    /// <summary>Forma larga del origen: "12 kilometers" — null si no disponible o igual a FromShort</summary>
    public string? FromLong  { get; init; }
    /// <summary>Forma corta del destino: "12000 m"</summary>
    public string ToShort   { get; init; } = "";
    /// <summary>Forma larga del destino: "12000 meters" — null si no disponible o igual a ToShort</summary>
    public string? ToLong    { get; init; }
}
