using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using OmniLauncher.Services;

namespace OmniLauncher.Converters;

public sealed class ApplicationNameToIconConverter : IValueConverter
{
    public static readonly ApplicationNameToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name
            ? ApplicationRegistry.All.FirstOrDefault(application => application.Name == name)?.IconPath
                ?? "avares://OmniLauncher/Assets/all-applications.svg"
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
