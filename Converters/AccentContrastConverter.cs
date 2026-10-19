using System;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OmniLauncher.Converters;

public static class AccentContrastConverter
{
    // Choose readable text/icons for both bright (yellow) and dark system accents.
    public static readonly FuncValueConverter<Color, IBrush> Instance = new(color =>
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
    });
}
