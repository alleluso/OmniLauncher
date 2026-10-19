using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OmniLauncher.Converters;

/// <summary>
/// Converts a string image path to an <see cref="Avalonia.Media.IImage"/>.
/// Supports absolute file-system paths and avares:// URIs.
/// Returns null (transparent) when the path is empty or the file cannot be loaded.
/// </summary>
public class PathToImageConverter : IValueConverter
{
    public static readonly PathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(new Uri(path));
                return new Bitmap(stream);
            }

            if (File.Exists(path))
                return new Bitmap(path);
        }
        catch
        {
            // Fall through to return null — the tile's solid-colour fallback will show instead.
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
