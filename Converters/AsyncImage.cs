using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniLauncher.Services;

namespace OmniLauncher.Converters;

/// <summary>
/// Attached property that loads an Image's bitmap in the background:
/// <c>&lt;Image conv:AsyncImage.Path="{Binding DisplayImagePath}"/&gt;</c>.
/// The UI thread never touches the file, so a cover on an offline drive cannot freeze rendering;
/// the image simply stays empty and the tile's fallback shows.
/// </summary>
public static class AsyncImage
{
    public static readonly AttachedProperty<string?> PathProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Path", typeof(AsyncImage));

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    static AsyncImage()
    {
        PathProperty.Changed.AddClassHandler<Image>(OnPathChanged);
    }

    public static string? GetPath(Image image) => image.GetValue(PathProperty);
    public static void SetPath(Image image, string? value) => image.SetValue(PathProperty, value);

    /// <summary>Drops cached bitmaps so edited covers are read again on the next library load.</summary>
    public static void ClearCache() => Cache.Clear();

    private static async void OnPathChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var path = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(path)) return;
        var bitmap = await LoadAsync(path);
        // Ignore results for a path the control no longer shows (recycled items, quick edits).
        if (string.Equals(GetPath(image), path, StringComparison.OrdinalIgnoreCase)) image.Source = bitmap;
    }

    public static Task<Bitmap?> LoadAsync(string path) => Cache.GetOrAdd(path, LoadUncachedAsync);

    private static async Task<Bitmap?> LoadUncachedAsync(string path)
    {
        Bitmap? bitmap;
        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            try { using var stream = AssetLoader.Open(new Uri(path)); bitmap = new Bitmap(stream); }
            catch (Exception) { bitmap = null; }
        }
        else
        {
            bitmap = await PathProbe.RunAsync<Bitmap?>(path, p =>
            {
                if (!File.Exists(p)) return null;
                using var stream = new MemoryStream(File.ReadAllBytes(p));
                return new Bitmap(stream);
            }, null);
        }
        // Do not cache failures: a reconnected drive should show the cover on the next attempt.
        if (bitmap is null) Cache.TryRemove(path, out _);
        return bitmap;
    }
}
