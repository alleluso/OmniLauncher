using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Views;

public partial class ProjectNotesDialogView : UserControl
{
    private ProjectLinkViewModel? _dragged;
    private ProjectLinkViewModel? _target;
    private IPointer? _pointer;
    private Point _start;
    private bool _moving;

    public ProjectNotesDialogView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => EndDrag();
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: ProjectLinkViewModel command } grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;

        _dragged = command;
        _pointer = e.Pointer;
        _start = e.GetPosition(this);

        grip.Focus();
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null)
            return;

        var point = e.GetPosition(this);
        if (!_moving && Math.Abs(point.Y - _start.Y) + Math.Abs(point.X - _start.X) < 5)
            return;

        _moving = true;

        var scrollPoint = e.GetPosition(LinkScroll);
        if (scrollPoint.Y < 24)
            LinkScroll.Offset = new Vector(0, Math.Max(0, LinkScroll.Offset.Y - 18));
        else if (scrollPoint.Y > LinkScroll.Bounds.Height - 24)
            LinkScroll.Offset = new Vector(0, Math.Min(Math.Max(0, LinkScroll.Extent.Height - LinkScroll.Viewport.Height), LinkScroll.Offset.Y + 18));

        _target = null;

        foreach (var border in LinkList.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("command-row")))
        {
            var position = e.GetPosition(border);
            var row = border.DataContext as ProjectLinkViewModel;
            var hit = position.Y >= 0 && position.Y <= border.Bounds.Height && position.X >= 0 && position.X <= border.Bounds.Width && row != _dragged;
            border.Classes.Set("drop-target", hit);
            border.Classes.Set("drag-source", row == _dragged);
            if (hit)
                _target = row;
        }

        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragged;
        var target = _target;

        EndDrag();
        if (dragged is not null && target is not null && DataContext is ProjectNotesDialogViewModel vm)
            vm.MoveLink(dragged, vm.Links.IndexOf(target));

        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void OnGripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) ||
            sender is not Control { DataContext: ProjectLinkViewModel command } ||
            DataContext is not ProjectNotesDialogViewModel vm)
            return;

        vm.MoveLink(command, vm.Links.IndexOf(command) + (e.Key == Key.Up ? -1 : 1));

        e.Handled = true;
    }

    private void EndDrag()
    {
        var pointer = _pointer;
        _pointer = null;
        _dragged = null;
        _target = null;
        _moving = false;

        foreach (var border in LinkList.GetVisualDescendants().OfType<Border>())
        {
            border.Classes.Remove("drop-target");
            border.Classes.Remove("drag-source");
        }

        pointer?.Capture(null);
    }

    private void OnRemoveLink(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.ProjectLinkViewModel command } &&
            DataContext is ViewModels.ProjectNotesDialogViewModel vm)
            vm.RemoveLinkCommand.Execute(command);
    }

}
