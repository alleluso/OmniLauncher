using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniLauncher.Models;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Controls;

public partial class SourceControlPopover : UserControl
{
    private readonly MainWindowViewModel? _library;
    public SourceControlPopover() => InitializeComponent();
    public SourceControlPopover(ProjectItem project, MainWindowViewModel library) : this()
    {
        DataContext = project;
        _library = library;
        OpenButton.Content = library.SourceControlOpenLabel;
    }
    private void OnOpenClient(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectItem project) _library?.OpenSourceControl(project);
    }
    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (_library is null) return;
        RefreshButton.IsEnabled = false;
        try { await _library.RefreshGitStatusAsync(); }
        finally { RefreshButton.IsEnabled = true; }
    }
}
