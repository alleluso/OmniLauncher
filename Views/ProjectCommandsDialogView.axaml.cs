using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Views;

public partial class ProjectCommandsDialogView : UserControl
{
    private ProjectCommandViewModel? _dragged;
    private ProjectCommandViewModel? _target;
    private IPointer? _pointer;
    private Point _start;
    private bool _moving;

    public ProjectCommandsDialogView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => EndDrag();
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: ProjectCommandViewModel command } grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
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

        var scrollPoint = e.GetPosition(CommandScroll);
        if (scrollPoint.Y < 24)
            CommandScroll.Offset = new Vector(0, Math.Max(0, CommandScroll.Offset.Y - 18));
        else if (scrollPoint.Y > CommandScroll.Bounds.Height - 24)
            CommandScroll.Offset = new Vector(0, Math.Min(Math.Max(0, CommandScroll.Extent.Height - CommandScroll.Viewport.Height), CommandScroll.Offset.Y + 18));

        _target = null;
        
        foreach (var border in CommandList.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("command-row")))
        {
            var position = e.GetPosition(border);
            var row = border.DataContext as ProjectCommandViewModel;
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
        if (dragged is not null && target is not null && DataContext is ProjectCommandsDialogViewModel vm)
            vm.MoveCommand(dragged, vm.Commands.IndexOf(target));

        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void OnGripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) ||
            sender is not Control { DataContext: ProjectCommandViewModel command } ||
            DataContext is not ProjectCommandsDialogViewModel vm)
            return;

        vm.MoveCommand(command, vm.Commands.IndexOf(command) + (e.Key == Key.Up ? -1 : 1));

        e.Handled = true;
    }

    private void EndDrag()
    {
        var pointer = _pointer;
        _pointer = null;
        _dragged = null;
        _target = null;
        _moving = false;

        foreach (var border in CommandList.GetVisualDescendants().OfType<Border>())
        {
            border.Classes.Remove("drop-target");
            border.Classes.Remove("drag-source");
        }

        pointer?.Capture(null);
    }

    private void OnRemoveCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.ProjectCommandViewModel command } && DataContext is ViewModels.ProjectCommandsDialogViewModel vm)
            vm.RemoveCommandCommand.Execute(command);
    }

}
