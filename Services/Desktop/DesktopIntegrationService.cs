using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using OmniLauncher.Models;
using OmniLauncher.Services;
using OmniLauncher.ViewModels;
using OmniLauncher.Views;

namespace OmniLauncher.Services.Desktop;

/// <summary>Owns tray lifetime and Windows global shortcut registrations on the UI thread.</summary>
public sealed class DesktopIntegrationService : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _vm;
    private readonly TrayIcon _tray;
    private readonly Dictionary<int, string> _hotkeys = new();
    private readonly Dictionary<NativeMenuItem, Action> _actions = new();
    private readonly nint _handle;
    private bool _disposed;
    private Window? _trayPopup;

    public DesktopIntegrationService(MainWindow window, MainWindowViewModel vm)
    {
        _window = window;
        _vm = vm;
        _handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        using var icon = AssetLoader.Open(new Uri("avares://OmniLauncher/Assets/app-logo.ico"));
        _tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "OmniLauncher", IsVisible = true };
        _tray.Clicked += OnTrayClicked;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(window, WindowProc);
            window.CloseToTray = true;
        }
        _vm.LibraryChanged += Refresh;
        _vm.PropertyChanged += OnPropertyChanged;
        Refresh();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.ViewState)) RegisterShortcuts();
    }

    private void Refresh()
    {
        if (_disposed) return;
        BuildMenu();
        RegisterShortcuts();
    }

    private void RegisterShortcuts()
    {
        if (_disposed || !OperatingSystem.IsWindows() || _handle == 0) return;
        var failures = new List<string>();
        var shortcuts = new HashSet<LaunchShortcut>();
        foreach (var project in _vm.IsLibraryVisible ? _vm.AllProjects : Array.Empty<ProjectItem>())
        {
            try { if (LaunchShortcut.Parse(project.LaunchShortcut) is { } shortcut) shortcuts.Add(shortcut); }
            catch (FormatException) { failures.Add($"{project.Name}: invalid shortcut; edit this project to fix it."); }
        }
        // Keep unchanged registrations alive, including MOD_NOREPEAT state while a key is held.
        var desired = shortcuts.Select(s => s.Text).ToHashSet();
        foreach (var registration in _hotkeys.ToArray())
        {
            if (desired.Contains(registration.Value)) continue;
            UnregisterHotKey(_handle, registration.Key);
            _hotkeys.Remove(registration.Key);
        }
        var idNumber = 0x5100;
        foreach (var shortcut in shortcuts)
        {
            if (_hotkeys.ContainsValue(shortcut.Text)) continue;
            while (_hotkeys.ContainsKey(idNumber)) idNumber++;
            var id = idNumber++;
            if (RegisterHotKey(_handle, id, shortcut.Modifiers | 0x4000, shortcut.VirtualKey))
                _hotkeys.Add(id, shortcut.Text);
            else failures.Add($"{shortcut.Text} is unavailable (reserved or already in use). Choose another launch shortcut.");
        }
        if (failures.Count > 0)
        {
            _vm.ErrorMessage = string.Join(Environment.NewLine, failures);
            ShowLauncher();
        }
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && _hotkeys.TryGetValue((int)wParam, out var shortcut))
        {
            handled = true;
            // Leave the native message handler before starting application work.
            Dispatcher.UIThread.Post(() => LaunchShortcutGroup(shortcut));
        }
        return 0;
    }

    private async void LaunchShortcutGroup(string shortcut)
    {
        if (_disposed || !_vm.IsLibraryVisible || _vm.IsWorking) return;
        var matches = _vm.AllProjects.Where(p =>
        {
            try { return LaunchShortcut.Normalize(p.LaunchShortcut) == shortcut; }
            catch (FormatException) { return false; }
        }).ToArray();
        await _vm.LaunchProjectsAsync(matches);
        if (_vm.HasError) ShowLauncher();
    }

    private NativeMenuItem ActionItem(string label, Action action)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) => action();
        _actions[item] = action;
        return item;
    }

    private void BuildMenu()
    {
        _actions.Clear();
        var menu = new NativeMenu();
        menu.Items.Add(ActionItem("Show launcher", ShowLauncher));
        menu.Items.Add(ActionItem("Hide to tray", _window.Hide));
        menu.Items.Add(new NativeMenuItemSeparator());
        var projects = _vm.AllProjects;
        var applications = new NativeMenu();
        foreach (var group in projects.GroupBy(p => p.ApplicationType).OrderBy(g => ApplicationRegistry.Get(g.Key).Name))
            applications.Items.Add(new NativeMenuItem(ApplicationRegistry.Get(group.Key).Name) { Menu = ProjectMenu(group) });
        var categories = new NativeMenu();
        foreach (var group in projects.GroupBy(p => p.CategoryId).OrderBy(g => g.Key == 0).ThenBy(g => g.First().Category.Name))
            categories.Items.Add(new NativeMenuItem(group.Key == 0 ? "Uncategorized" : group.First().Category.Name) { Menu = ProjectMenu(group) });
        menu.Items.Add(new NativeMenuItem("Applications") { Menu = applications, IsEnabled = projects.Count > 0 });
        menu.Items.Add(new NativeMenuItem("Categories") { Menu = categories, IsEnabled = projects.Count > 0 });
        if (projects.Count == 0) menu.Items.Add(new NativeMenuItem("No projects yet") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(ActionItem("Exit OmniLauncher", Exit));
        _tray.Menu = menu;
    }

    private NativeMenu ProjectMenu(IEnumerable<ProjectItem> projects)
    {
        var menu = new NativeMenu();
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var label = project.Name + (string.IsNullOrEmpty(project.LaunchShortcut) ? "" : $" ({project.LaunchShortcut})");
            menu.Items.Add(ActionItem(label, async () =>
            {
                if (_vm.IsWorking) { ShowLauncher(); return; }
                await _vm.LaunchProjectsAsync(new[] { project });
                if (_vm.HasError) ShowLauncher();
            }));
        }
        return menu;
    }

    public void ShowLauncher()
    {
        _window.ShowLauncher();
    }

    private async void Exit()
    {
        if (_vm.IsWorking) { ShowLauncher(); return; }
        await _vm.FlushDisplayPreferencesAsync();
        _window.CloseToTray = false;
        _window.Close();
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows()) { ShowLauncher(); return; }
        if (_disposed || _trayPopup is not null) return;
        if (!GetCursorPos(out var point)) return;
        BuildMenu();

        // Match Avalonia's right-click tray renderer instead of using a Win32 menu.
        var popup = new Window
        {
            WindowDecorations = WindowDecorations.None,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            CanResize = false,
            Background = null,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Position = new PixelPoint(point.X, point.Y)
        };
        var presenter = new TrayMenuPresenter(popup);
        foreach (var item in CreateMenuItems(_tray.Menu!, popup)) presenter.Items.Add(item);
        popup.Content = presenter;
        _trayPopup = popup;
        popup.Deactivated += (_, _) => popup.Close();
        popup.Closed += (_, _) => { if (ReferenceEquals(_trayPopup, popup)) _trayPopup = null; };
        // Keep the root menu inside the tray monitor's work area, including on scaled displays.
        popup.LayoutUpdated += (_, _) =>
        {
            if (popup.Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) is not { } screen) return;
            var area = screen.WorkingArea;
            var width = (int)Math.Ceiling(popup.Bounds.Width * popup.RenderScaling);
            var height = (int)Math.Ceiling(popup.Bounds.Height * popup.RenderScaling);
            var position = new PixelPoint(
                Math.Clamp(point.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(point.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
            if (popup.Position != position) popup.Position = position;
        };
        popup.Show();
    }

    private IEnumerable<Control> CreateMenuItems(NativeMenu menu, Window popup)
    {
        foreach (var entry in menu.Items)
        {
            if (entry is not NativeMenuItem native)
            {
                yield return new Separator();
                continue;
            }
            var item = new MenuItem { Header = native.Header, IsEnabled = native.IsEnabled };
            if (native.Menu is { } children)
                foreach (var child in CreateMenuItems(children, popup)) item.Items.Add(child);
            else if (_actions.TryGetValue(native, out var action))
                item.Click += (_, e) =>
                {
                    e.Handled = true;
                    popup.Close();
                    Dispatcher.UIThread.Post(action);
                };
            yield return item;
        }
    }

    private sealed class TrayMenuPresenter(Window popup) : MenuFlyoutPresenter
    {
        protected override Type StyleKeyOverride => typeof(MenuFlyoutPresenter);
        public override void Close()
        {
            SelectedIndex = -1;
            popup.Close();
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayPopup?.Close();
        _vm.LibraryChanged -= Refresh;
        _vm.PropertyChanged -= OnPropertyChanged;
        if (OperatingSystem.IsWindows())
        {
            foreach (var id in _hotkeys.Keys) UnregisterHotKey(_handle, id);
            Win32Properties.RemoveWndProcHookCallback(_window, WindowProc);
        }
        _hotkeys.Clear();
        _tray.Clicked -= OnTrayClicked;
        _tray.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}
