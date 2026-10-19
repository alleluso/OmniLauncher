using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniLauncher.Controls;

public partial class ProjectListItem : ProjectCard
{
    public static readonly StyledProperty<int> ListSizeProperty =
        AvaloniaProperty.Register<ProjectListItem, int>(nameof(ListSize), 2);

    public int ListSize { get => GetValue(ListSizeProperty); set => SetValue(ListSizeProperty, value); }

    public ProjectListItem()
    {
        InitializeComponent();
        UpdateSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ListSizeProperty) UpdateSize();
    }

    private void UpdateSize()
    {
        if (ListLayout is null) return;
        var compact = ListSize < 2;
        var small = ListSize == 0;
        Metadata.IsVisible = !small;
        CompactGitStatus.IsVisible = small;
        Margin = new Thickness(0, 0, compact ? 12 : 0, 8);
        Cover.IsVisible = ListSize != 0;
        Cover.Width = 220;
        TitlePanel.Margin = new Thickness(small ? 12 : 248, 0, compact ? 8 : 16, 0);
        // Preserve the large row's positions; constrain text instead of moving it below the title.
        ListLayout.ColumnDefinitions[1].Width = ListSize == 1
            ? new GridLength(0.65, GridUnitType.Star) : GridLength.Auto;
        LastOpened.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        LastOpened.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        GitLabel.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        GitLabel.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
    }
}
