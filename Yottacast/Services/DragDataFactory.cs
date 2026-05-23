using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Yottacast.Core.ViewModels;

namespace Yottacast.Services;

/// <summary>
/// Translates a Core <see cref="DragPayload"/> into the Avalonia <see cref="IDataObject"/> that
/// the OS expects. Lives in the UI project because it depends on Avalonia.
/// </summary>
public static class DragDataFactory {
    public static async Task<IDataObject?> BuildAsync(Visual visual, DragPayload payload) {
        return payload switch {
            DragPayload.Text t => Text(t.Value),
            DragPayload.File f => await FileAsync(visual, f.AbsolutePath),
            _                  => null,
        };
    }

    private static IDataObject Text(string text) {
        var data = new DataObject();
        data.Set(DataFormats.Text, text);
        return data;
    }

    private static async Task<IDataObject?> FileAsync(Visual visual, string absolutePath) {
        var topLevel = TopLevel.GetTopLevel(visual);
        var storage = topLevel?.StorageProvider;
        if (storage is null) return null;
        IStorageItem? file;
        try {
            file = await storage.TryGetFileFromPathAsync(new Uri(absolutePath));
        } catch (Exception) {
            return null;
        }
        if (file is null) return null;
        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { file });
        return data;
    }
}
