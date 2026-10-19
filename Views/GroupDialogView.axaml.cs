using Avalonia.Controls;

namespace OmniLauncher.Views;

public partial class GroupDialogView : UserControl
{
    public GroupDialogView()
    {
        InitializeComponent();
        CategorySelector.SelectionChanged += (_, _) =>
        {
            // Clearing/replacing the selection must resolve to the explicit "No category" entry.
            if (CategorySelector.SelectedIndex < 0 && CategorySelector.ItemCount > 0)
                CategorySelector.SelectedIndex = 0;
        };
    }
}
