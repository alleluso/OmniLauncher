using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using OmniLauncher.Models;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Controls;

// Shared project actions; tile and list controls own their layouts independently.
public class ProjectCard : UserControl
{
    private readonly MenuItem _categoryMenu = new() { Header = "Category" };
    private readonly List<Control> _commandItems = new();
    private readonly List<Control> _groupItems = new();

    public ProjectCard()
    {
        
        var open = new MenuItem { Header = "Open project" };
        open.Bind(IsEnabledProperty, new Binding(nameof(ProjectItem.CanLaunch)));
        open.Click += OnOpen;
        var edit = new MenuItem { Header = "Edit project" };
        edit.Click += OnEdit;
        var favorite = new MenuItem();
        favorite.Bind(MenuItem.HeaderProperty, new Binding(nameof(ProjectItem.FavoriteLabel)));
        favorite.Click += OnFavorite;
        var folder = new MenuItem { Header = "Show in Explorer" };
        folder.Click += OnShowFolder;
        var copy = new MenuItem { Header = "Copy path" };
        copy.Click += OnCopyPath;
        var remove = new MenuItem { Header = "Remove project", Foreground = Brush.Parse("#FF909B") };
        remove.Click += OnRemove;

        ContextMenu = new ContextMenu
        {
            Items = { open, edit, favorite, _categoryMenu, new Separator(), folder, copy, new Separator(), remove }
        };
        ContextMenu.Opened += (_, _) => { RebuildCategories(); UpdateCategorySelection(); RebuildCommands(); RebuildGroups(); };
        ContextMenu.Closed += (_, _) =>
        {
            ContextMenu.ClearValue(ContextMenu.PlacementTargetProperty);
            ContextMenu.ClearValue(ContextMenu.PlacementProperty);
        };
    }

    private void RebuildGroups()
    {
        foreach (var item in _groupItems) ContextMenu!.Items.Remove(item);
        _groupItems.Clear();
        if (Project is not { } project || Library is not { } library) return;
        foreach (var group in library.CompatibleGroups(project))
        {
            var remove = project.GroupId == group.Id;
            var item = new MenuItem { Header = $"{(remove ? "Remove from" : "Add to")} Group \"{group.Name}\"" };
            item.Click += async (_, _) => await library.AssignGroupAsync(project, remove ? null : group);
            _groupItems.Add(item);
        }
        var index = 4;
        foreach (var item in _groupItems) ContextMenu!.Items.Insert(index++, item);
    }

    private void RebuildCommands()
    {
        foreach (var item in _commandItems) ContextMenu!.Items.Remove(item);
        _commandItems.Clear();
        if (Project is not { } project) return;

        _commandItems.Add(new Separator());
        foreach (var command in project.Commands)
        {
            var item = new MenuItem { Header = command.Name, Icon = new TextBlock { Text = "▷" } };
            ToolTip.SetTip(item, command.ExecutablePath + Environment.NewLine + command.Arguments);
            item.Click += (_, _) => Library?.RunProjectCommand(project, command);
            _commandItems.Add(item);
        }
        var manage = new MenuItem { Header = "Manage commands" };
        manage.Click += (_, _) => Library?.ManageProjectCommands(project);
        _commandItems.Add(manage);
        _commandItems.Add(new Separator());
        foreach (var link in project.Links)
        {
            var item = new MenuItem { Header = link.Name, Icon = new TextBlock { Text = "↗" } };
            ToolTip.SetTip(item, link.Url);
            item.Click += (_, _) => Library?.OpenProjectLink(link);
            _commandItems.Add(item);
        }
        var notes = new MenuItem { Header = "Manage Notes and Links" };
        notes.Click += (_, _) => Library?.ManageProjectNotes(project);
        _commandItems.Add(notes);
        var index = 4;
        foreach (var item in _commandItems) ContextMenu!.Items.Insert(index++, item);
    }

    private void RebuildCategories()
    {
        _categoryMenu.Items.Clear();
        var categoryGroup = Guid.NewGuid().ToString("N");
        foreach (var category in new[] { ProjectCategory.None }.Concat(Project?.Categories ?? Array.Empty<ProjectCategory>()))
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var icon = new Grid { Width = 20, VerticalAlignment = VerticalAlignment.Center };
            if (category.IsNone)
                icon.Children.Add(new TextBlock
                {
                    Text = "○", FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            else
                icon.Children.Add(new Ellipse
            {
                Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Fill = Brush.Parse(category.Color)
            });
            header.Children.Add(icon);
            header.Children.Add(new TextBlock { Text = category.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem
            {
                Header = header, Tag = category.Id,
                ToggleType = MenuItemToggleType.Radio, GroupName = categoryGroup
            };
            Avalonia.Automation.AutomationProperties.SetName(item, category.Name);
            item.Click += OnCategory;
            _categoryMenu.Items.Add(item);
        }
    }
    private ProjectItem? Project => DataContext as ProjectItem;
    private MainWindowViewModel? Library => (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel;

    protected void OnOpen(object? sender, RoutedEventArgs e) => Library?.LaunchProjectCommand.Execute(Project);
    protected void OnEdit(object? sender, RoutedEventArgs e) => Library?.EditProjectCommand.Execute(Project);
    protected void OnRemove(object? sender, RoutedEventArgs e) => Library?.DeleteProjectCommand.Execute(Project);
    protected void OnFavorite(object? sender, RoutedEventArgs e) => Library?.ToggleFavoriteCommand.Execute(Project);
    protected void OnNotes(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Project is { } project) Library?.ManageProjectNotes(project);
    }
    protected void OnShowFolder(object? sender, RoutedEventArgs e) => Library?.ShowInExplorerCommand.Execute(Project);
    private void UpdateCategorySelection()
    {
        foreach (var item in _categoryMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is int id && id == Project?.CategoryId;
    }
    protected async void OnCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int categoryId } || Project is not { } project || Library is not { } library) return;
        await library.AssignCategoryAsync(project, categoryId);
        UpdateCategorySelection();
    }
    protected void OnMenu(object? sender, RoutedEventArgs e)
    {
        ContextMenu!.PlacementTarget = sender as Control ?? this;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.Open(this);
    }
    protected void OnSourceControl(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control target || Project is not { } project || Library is not { } library) return;
        new Flyout
        {
            Content = new SourceControlPopover(project, library),
            FlyoutPresenterClasses = { "source-control-flyout" },
            Placement = PlacementMode.Bottom
        }.ShowAt(target);
    }

    protected async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (Project is not { } project || Library is not { } library) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("The clipboard is unavailable.");
            await clipboard.SetTextAsync(project.ProjectPath);
            library.Notice = "Project path copied";
        }
        catch (Exception ex) { library.ReportActionError("Could not copy the project path", ex); }
    }
}
