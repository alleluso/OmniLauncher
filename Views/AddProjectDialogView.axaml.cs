using Avalonia.Controls;

namespace OmniLauncher.Views;

public partial class AddProjectDialogView : UserControl
{
    public AddProjectDialogView()
    {
        InitializeComponent();
    }

    // Resolve the owning window here: flyout items live in a separate visual tree.
    private void OnBrowseFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AddProjectDialogViewModel vm)
            vm.BrowsePathCommand.Execute(TopLevel.GetTopLevel(this));
    }

    private void OnBrowseFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AddProjectDialogViewModel vm)
            vm.BrowseProjectFileCommand.Execute(TopLevel.GetTopLevel(this));
    }
}
