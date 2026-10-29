using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OmniLauncher.Converters;

/// <summary>
/// Converts an image path to an already-loaded bitmap without touching the disk.
/// Prefer <see cref="AsyncImage"/>: this converter only returns images that are already cached
/// and starts a background load otherwise, so it can never block the UI thread.
/// </summary>
public class PathToImageConverter : IValueConverter
{
    public static readonly PathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        var load = AsyncImage.LoadAsync(path);
        return load.IsCompletedSuccessfully ? load.Result : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
