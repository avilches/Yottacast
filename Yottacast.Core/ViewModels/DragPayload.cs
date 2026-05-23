namespace Yottacast.Core.ViewModels;

/// <summary>
/// Declarative description of what the user is dragging out of a result item.
/// The view translates this into the platform-native IDataObject. Core stays
/// independent of Avalonia.
/// </summary>
public abstract record DragPayload {
    /// <summary>A file on disk identified by its absolute path. Translates to DataFormats.Files.</summary>
    public sealed record File(string AbsolutePath) : DragPayload;

    /// <summary>Plain text payload. Translates to DataFormats.Text.</summary>
    public sealed record Text(string Value) : DragPayload;
}
