using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace OmniLauncher.Converters;

/// <summary>
/// Converts an enum value into a human-friendly string by inserting spaces
/// before each capital letter, e.g. "UnrealEngine" becomes "Unreal Engine".
/// </summary>
public class EnumToFriendlyStringConverter : IValueConverter
{
    public static readonly EnumToFriendlyStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        if (value is OmniLauncher.Models.ProjectType type)
        {
            var definition = System.Linq.Enumerable.FirstOrDefault(OmniLauncher.Services.ApplicationRegistry.All, p => p.Type == type);
            if (definition is not null) return definition.Name;
        }

        var text = value.ToString() ?? string.Empty;
        var builder = new StringBuilder(text.Length + 4);

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(text[i - 1]))
                builder.Append(' ');

            builder.Append(current);
        }

        return builder.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

