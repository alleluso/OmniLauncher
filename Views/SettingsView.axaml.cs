using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Views;

public partial class SettingsView : UserControl
{
    private object? _dragged;
    private object? _target;
    private IPointer? _pointer;
    private Point _start;
    private bool _moving;

    public SettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => EndDrag();
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border grip || grip.DataContext is not (RegisteredApplicationViewModel or CategorySettingsViewModel) || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;

        _dragged = grip.DataContext;
        _pointer = e.Pointer;
        _start = e.GetPosition(this);

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

        var scrollPoint = e.GetPosition(ApplicationScroll);
        if (scrollPoint.Y < 24)
            ApplicationScroll.Offset = new Vector(0, Math.Max(0, ApplicationScroll.Offset.Y - 18));
        else if (scrollPoint.Y > ApplicationScroll.Bounds.Height - 24)
            ApplicationScroll.Offset = new Vector(0, Math.Min(Math.Max(0, ApplicationScroll.Extent.Height - ApplicationScroll.Viewport.Height), ApplicationScroll.Offset.Y + 18));

        _target = null;

        var list = _dragged is CategorySettingsViewModel ? CategoryList : ApplicationList;
        foreach (var border in list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("install-row")))
        {
            var position = e.GetPosition(border);
            var row = border.DataContext;
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

        if (DataContext is SettingsViewModel vm)
        {
            if (dragged is RegisteredApplicationViewModel application && target is RegisteredApplicationViewModel applicationTarget)
                vm.Move(application, vm.Applications.IndexOf(applicationTarget));
            else if (dragged is CategorySettingsViewModel category && target is CategorySettingsViewModel categoryTarget)
                vm.MoveCategory(category, vm.Categories.IndexOf(categoryTarget));
        }

        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        var pointer = _pointer;
        _pointer = null;
        _dragged = null;
        _target = null;
        _moving = false;

        foreach (var border in ApplicationList.GetVisualDescendants().Concat(CategoryList.GetVisualDescendants()).OfType<Border>())
        {
            border.Classes.Remove("drop-target");
            border.Classes.Remove("drag-source");
        }

        pointer?.Capture(null);
    }
}
