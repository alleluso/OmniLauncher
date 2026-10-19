using System;
using System.Globalization;
using Avalonia.Data.Converters;
using OmniLauncher.Models;

namespace OmniLauncher.Converters;

/// <summary>
/// Converts a <see cref="ProjectType"/> into the avares:// path of the matching engine
/// SVG icon found under Assets/.
/// </summary>
public class ProjectTypeToIconConverter : IValueConverter
{
    public static readonly ProjectTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ProjectType projectType)
            return null;

        return System.Linq.Enumerable.FirstOrDefault(OmniLauncher.Services.ApplicationRegistry.All,
            application => application.Type == projectType)?.IconPath;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

