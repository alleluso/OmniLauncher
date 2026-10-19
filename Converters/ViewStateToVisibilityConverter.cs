using System;
using System.Globalization;
using Avalonia.Data.Converters;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Converters;

/// <summary>
/// Returns true when the bound <see cref="ViewState"/> matches the <see cref="ViewState"/>
/// named by the converter parameter, false otherwise.
/// </summary>
public class ViewStateToVisibilityConverter : IValueConverter
{
    public static readonly ViewStateToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ViewState viewState || parameter is not string parameterText)
            return false;

        return Enum.TryParse<ViewState>(parameterText, out var target) && viewState == target;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
