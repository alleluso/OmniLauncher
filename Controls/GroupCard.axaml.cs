using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using OmniLauncher.Models;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Controls;

public partial class GroupCard : UserControl
{
    public static readonly StyledProperty<int> TileSizeProperty = AvaloniaProperty.Register<GroupCard, int>(nameof(TileSize), 2);
    public static readonly StyledProperty<bool> IsListViewProperty = AvaloniaProperty.Register<GroupCard, bool>(nameof(IsListView));
    public int TileSize { get => GetValue(TileSizeProperty); set => SetValue(TileSizeProperty, value); }
    public bool IsListView { get => GetValue(IsListViewProperty); set => SetValue(IsListViewProperty, value); }

    private LibraryGroup? Group => DataContext as LibraryGroup;
    private MainWindowViewModel? Library => (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel;

    public GroupCard()
    {
        InitializeComponent();
        var open = new MenuItem { Header = "Open group" };
        open.Click += OnOpen;
        var edit = new MenuItem { Header = "Edit group" };
        edit.Click += (_, _) => Library?.EditGroupCommand.Execute(Group);
        var delete = new MenuItem { Header = "Delete group", Foreground = Brush.Parse("#FF909B") };
        delete.Click += (_, _) => Library?.DeleteGroupCommand.Execute(Group);
        ContextMenu = new ContextMenu { Items = { open, edit, new Separator(), delete } };
        ContextMenu.Closed += (_, _) =>
        {
            ContextMenu.ClearValue(ContextMenu.PlacementTargetProperty);
            ContextMenu.ClearValue(ContextMenu.PlacementProperty);
        };
        UpdateSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TileSizeProperty || change.Property == IsListViewProperty) UpdateSize();
    }

    private void UpdateSize()
    {
        if (TileContent is null) return;
        var size = Math.Clamp(TileSize, 0, 2);
        Width = IsListView ? double.NaN : size switch { 0 => 168, 1 => 210, _ => 260 };
        Height = IsListView ? double.NaN : size switch { 0 => 142, 1 => 224, _ => 258 };
        Margin = new Thickness(0, 0, IsListView && size < 2 ? 12 : 0, 8);
        TileContent.RowDefinitions[0].Height = size == 0 ? GridLength.Star : new GridLength(size == 1 ? 140 : 174);
        TileContent.RowDefinitions[1].Height = size == 0 ? new GridLength(0) : GridLength.Star;
        TileFooter.IsVisible = size != 0;
        CompactCount.IsVisible = size == 0;
        ListCover.IsVisible = size != 0;
        ListTitle.Margin = new Thickness(size == 0 ? 12 : 248, 0, size < 2 ? 8 : 16, 0);
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => Library?.OpenGroupCommand.Execute(Group);
    private void OnMenu(object? sender, RoutedEventArgs e)
    {
        ContextMenu!.PlacementTarget = sender as Control ?? this;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.Open(this);
    }
}
