using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OmniLauncher.Views;

public partial class EditProjectDialogView : UserControl
{
    public EditProjectDialogView()
    {
        InitializeComponent();
        ShortcutRecorder.AddHandler(KeyDownEvent, OnRecorderKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnRecordShortcut(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.EditProjectDialogViewModel vm)
            return;

        if (vm.IsRecordingShortcut)
        {
            vm.IsRecordingShortcut = false;
            vm.ShortcutError = "";
            return;
        }

        if (!string.IsNullOrEmpty(vm.LaunchShortcut))
        {
            vm.ClearShortcutCommand.Execute(null);
            return;
        }

        ShortcutRecorder.Focus();
        vm.ShortcutError = "";
        vm.IsRecordingShortcut = true;
    }

    private void OnRecorderLostFocus(object? sender, RoutedEventArgs e)
    {
        // Wait until focus has moved; clicking Cancel must not turn it into Record
        // before its Click handler runs.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ShortcutRecorder.IsKeyboardFocusWithin && !ShortcutAction.IsKeyboardFocusWithin && DataContext is ViewModels.EditProjectDialogViewModel vm)
                vm.IsRecordingShortcut = false;
        });
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.EditProjectDialogViewModel { IsRecordingShortcut: true } vm)
            return;

        if (e.Key == Key.Tab)
        {
            vm.IsRecordingShortcut = false;
            return; // Preserve normal keyboard navigation.
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            vm.IsRecordingShortcut = false;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var key = e.Key switch
        {
            >= Key.A and <= Key.Z => e.Key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(),
            >= Key.F1 and <= Key.F11 => e.Key.ToString(),
            _ => null
        };
        if (key is null)
        {
            vm.ShortcutError = "Choose a letter, number, or F1–F11.";
            return;
        }

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Win");
        parts.Add(key);

        try
        {
            vm.LaunchShortcut = Services.LaunchShortcut.Normalize(string.Join("+", parts));
            vm.ShortcutError = "";
            vm.IsRecordingShortcut = false;
        }
        catch (FormatException ex)
        {
            vm.ShortcutError = ex.Message;
        }
    }

    // Resolve the owning window here: flyout items live in a separate visual tree.
    private void OnBrowseFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.EditProjectDialogViewModel vm)
            vm.BrowsePathCommand.Execute(TopLevel.GetTopLevel(this));
    }

    private void OnBrowseFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.EditProjectDialogViewModel vm)
            vm.BrowseProjectFileCommand.Execute(TopLevel.GetTopLevel(this));
    }
}
