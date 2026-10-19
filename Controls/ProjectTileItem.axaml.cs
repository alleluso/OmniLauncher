using System;
using Avalonia;
using Avalonia.Controls;

namespace OmniLauncher.Controls;

public partial class ProjectTileItem : ProjectCard
{
    public static readonly StyledProperty<int> TileSizeProperty =
        AvaloniaProperty.Register<ProjectTileItem, int>(nameof(TileSize), 2);

    public int TileSize { get => GetValue(TileSizeProperty); set => SetValue(TileSizeProperty, value); }

    public ProjectTileItem()
    {
        InitializeComponent();
        UpdateSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TileSizeProperty) UpdateSize();
    }

    private void UpdateSize()
    {
        if (TileContent is null) return;
        var size = Math.Clamp(TileSize, 0, 2);
        // Four 32px buttons, three 4px gaps, 8px side margins, and the 1px outer inset.
        Width = size switch { 0 => 168, 1 => 210, _ => 260 };
        TileLayout.Classes.Set("compact-tile", size == 0);
        Height = size switch { 0 => 142, 1 => 224, _ => 258 };
        // Compact tiles devote their entire height to the cover; metadata stays in the larger sizes.
        TileContent.RowDefinitions[0].Height = size == 0
            ? GridLength.Star : new GridLength(size == 1 ? 140 : 174);
        TileContent.RowDefinitions[1].Height = size == 0 ? new GridLength(0) : GridLength.Star;
    }
}
