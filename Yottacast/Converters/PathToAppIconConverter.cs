using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Yottacast.Converters;

/// <summary>
/// Converts icon bytes (byte[]?) to an Avalonia Bitmap.
/// Uses ConditionalWeakTable so Bitmaps are GC'd alongside their source bytes.
/// </summary>
public sealed class PathToAppIconConverter : IValueConverter {
    private static readonly ConditionalWeakTable<byte[], Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not byte[] bytes) return null;
        return Cache.GetValue(bytes, static b => new Bitmap(new MemoryStream(b)));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
