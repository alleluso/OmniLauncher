using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniLauncher.Models;
using OmniLauncher.Services;
using OmniLauncher.ViewModels;

namespace OmniLauncher.Views;

public partial class MainWindow : Window
{
    public bool CloseToTray { get; set; }
    public void ShowLauncher()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private MainWindowViewModel? _viewModel;
    private Vector _libraryOffset;
    private readonly Dictionary<ProjectType, Vector> _rowOffsets = new();
    private int _restoreVersion;
    private Vector _beforeGroupOffset;
    private readonly Dictionary<ProjectType, Vector> _beforeGroupRowOffsets = new();
    private IInputElement? _previousFocus;
    private bool _focusPending;
    private IInputElement? _beforeBusyFocus;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindViewModel();
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, BlockBusyInput, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, BlockBusyInput, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, BlockBusyInput, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, BlockBusyInput, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, BlockBusyInput, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnProjectRowWheelChanged, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Activated += async (_, _) => { if (_viewModel is not null) await _viewModel.RefreshGitStatusAsync(); };
        Closed += (_, _) => { DetachViewModel(); (DataContext as IDisposable)?.Dispose(); };
    }

    private async void OnApplicationHeadingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectGroup group } button || _viewModel is not { IsWorking: false } vm)
            return;

        vm.BusyMessage = "Opening application…";
        vm.IsBusy = true;

        try
        {
            var applications = (await ProjectLoaderService.LoadApplicationsAsync())
                .Where(application => application.Type == group.ProjectType)
                .DistinctBy(application => application.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (applications.Count == 0)
                throw new InvalidOperationException("Register this application in Settings.");

            // Single app, launch that one
            if (applications.Count == 1)
            {
                LaunchApplication(applications[0]);
                return;
            }

            // Multiple app versions
            var menu = new MenuFlyout();
            foreach (var application in applications)
            {
                var version = string.IsNullOrWhiteSpace(application.Version) ? "Default" : application.Version;
                var item = new MenuItem { Header = $"{group.GroupName} {version}" };
                ToolTip.SetTip(item, application.Path);
                item.Click += (_, _) => LaunchApplication(application);
                menu.Items.Add(item);
            }
            vm.IsBusy = false;
            menu.ShowAt(button);
        }
        catch (Exception ex)
        {
            vm.ReportActionError("Could not open application", ex);
        }
        finally
        {
            vm.IsBusy = false;
        }
    }

    private void LaunchApplication(RegisteredApplication application)
    {
        try
        {
            var executable = ApplicationRegistry.Get(application.Type).ValidateExecutable(application.Path);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };

            // Custom handling
            // TODO: Remove from here
            if (application.Type == ProjectType.VSCode)
                start.ArgumentList.Add("--new-window");

            using var process = Process.Start(start);
        }
        catch (Exception ex)
        {
            _viewModel?.ReportActionError("Could not open application", ex);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (CloseToTray && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (e.Cancel || _viewModel is null)
            return;

        var pendingSave = _viewModel.FlushDisplayPreferencesAsync();
        if (pendingSave.IsCompleted)
            return;

        // A selection made immediately before closing must reach disk first.
        e.Cancel = true;
        _viewModel.BusyMessage = "Saving preferences…";
        _viewModel.IsBusy = true;
        await pendingSave;
        _viewModel.IsBusy = false;
        Close();
    }

    private void BindViewModel()
    {
        DetachViewModel();

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null)
            return;

        _viewModel.ProjectsUpdating += CaptureScroll;
        _viewModel.ProjectsUpdated += RestoreScroll;
        _viewModel.GroupEntering += CaptureBeforeGroup;
        _viewModel.GroupExited += RestoreBeforeGroup;
        _viewModel.PropertyChanged += OnViewModelChanged;
        UpdateBusyFocus();
    }
    
    private void DetachViewModel()
    {
        if (_viewModel is null)
            return;

        _viewModel.ProjectsUpdating -= CaptureScroll;
        _viewModel.ProjectsUpdated -= RestoreScroll;
        _viewModel.GroupEntering -= CaptureBeforeGroup;
        _viewModel.GroupExited -= RestoreBeforeGroup;
        _viewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SearchText)
        or nameof(MainWindowViewModel.SelectedLibraryView)
            or nameof(MainWindowViewModel.SelectedApplication)
            or nameof(MainWindowViewModel.SelectedSort))
            _restoreVersion++;

        if (_viewModel is null)
            return;

        if (e.PropertyName == nameof(MainWindowViewModel.IsWorking))
            UpdateBusyFocus();

        if (e.PropertyName == nameof(MainWindowViewModel.ViewState))
        {
            _focusPending = true;
            if (_viewModel.ViewState != ViewState.ProjectsList)
                _previousFocus = FocusManager?.GetFocusedElement();
        }
        else if (e.PropertyName != nameof(MainWindowViewModel.IsWorking) || !_focusPending)
            return;

        if (_viewModel.IsWorking)
            return;

        var state = _viewModel.ViewState;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.ViewState != state || _viewModel.IsWorking)
                return;

            _focusPending = false;

            if (state == ViewState.ProjectsList)
            {
                if (_previousFocus is Control control && TopLevel.GetTopLevel(control) == this && control.IsEffectivelyEnabled)
                    control.Focus();
                else
                    LibrarySearch.Focus();

                return;
            }

            Control? dialog = state switch
            {
                ViewState.AddProjectDialog => AddDialog,
                ViewState.EditProjectDialog => EditDialog,
                ViewState.ProjectCommandsDialog => CommandsDialog,
                ViewState.ProjectNotesDialog => NotesDialog,
                ViewState.Settings => SettingsDialog,
                ViewState.DeleteProjectDialog => RemoveDialog,
                ViewState.GroupDialog => GroupDialog,
                _ => null
            };
            dialog?.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => control is TextBox or ComboBox or Button && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)?
                .Focus();
        }, DispatcherPriority.Loaded);
    }

    private IEnumerable<ScrollViewer> ProjectRows => LibraryScroll.GetVisualDescendants()
        .OfType<ScrollViewer>()
        .Where(scroll => scroll.Classes.Contains("project-row"));

    private void CaptureScroll()
    {
        _libraryOffset = LibraryScroll.Offset;
        _rowOffsets.Clear();
        foreach (var row in ProjectRows)
        {
            if (row.DataContext is ProjectGroup group)
                _rowOffsets[group.ProjectType] = row.Offset;
        }
    }
    private void CaptureBeforeGroup()
    {
        _beforeGroupOffset = LibraryScroll.Offset;
        _beforeGroupRowOffsets.Clear();
        foreach (var row in ProjectRows)
        {
            if (row.DataContext is ProjectGroup group)
                _beforeGroupRowOffsets[group.ProjectType] = row.Offset;
        }

        _restoreVersion++;
        LibraryScroll.Offset = default;
        foreach (var row in ProjectRows)
        {
            row.Offset = default;
        }
    }
    private void RestoreBeforeGroup()
    {
        _libraryOffset = _beforeGroupOffset;
        _rowOffsets.Clear();
        foreach (var entry in _beforeGroupRowOffsets)
        {
            _rowOffsets[entry.Key] = entry.Value;
        }
        RestoreScroll();
    }
    private void RestoreScroll()
    {
        var version = ++_restoreVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _restoreVersion) return;
            LibraryScroll.UpdateLayout();
            LibraryScroll.Offset = _libraryOffset;
            foreach (var row in ProjectRows)
            {
                if (row.DataContext is ProjectGroup group && _rowOffsets.TryGetValue(group.ProjectType, out var offset))
                    row.Offset = offset;
            }
        }, DispatcherPriority.Loaded);
    }
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.IsWorking == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Handled || _viewModel is not { IsWorking: false } vm)
            return;

        if (e.Key == Key.Escape && vm.ViewState != ViewState.ProjectsList)
        {
            if (vm.ViewState == ViewState.EditProjectDialog && vm.EditDialogViewModel.IsRecordingShortcut)
            {
                vm.EditDialogViewModel.IsRecordingShortcut = false;
                vm.EditDialogViewModel.ShortcutError = "";
                e.Handled = true;
                return;
            }

            // Let an open picker or menu consume Escape first.
            if (this.GetVisualDescendants().OfType<ComboBox>().Any(combo => combo.IsDropDownOpen))
                return;

            vm.CloseDialogCommand.Execute(null);
            e.Handled = true;
        }
        else if (vm.ViewState == ViewState.ProjectsList && e.Key == Key.F5)
        {
            vm.RefreshLibraryCommand.Execute(null);
            e.Handled = true;
        }
        else if (vm.ViewState == ViewState.ProjectsList && e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            LibrarySearch.Focus();
            LibrarySearch.SelectAll();
            e.Handled = true;
        }
    }

    private void BlockBusyInput(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.IsWorking == true)
            e.Handled = true;
    }

    private void UpdateBusyFocus()
    {
        if (_viewModel?.IsWorking == true)
        {
            _beforeBusyFocus = FocusManager?.GetFocusedElement();
            BusyOverlay.Focus();
        }
        else
        {
            if (!_focusPending && _beforeBusyFocus is Control control && TopLevel.GetTopLevel(control) == this && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
                control.Focus();

            _beforeBusyFocus = null;
        }
    }
    private void OnProjectRowWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || _viewModel?.IsWorking == true || e.Source is not Visual visual)
            return;

        var scroll = visual.GetVisualAncestors().Prepend(visual).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || !scroll.Classes.Contains("project-row") || scroll.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
            return;

        var maxOffset = scroll.Extent.Width - scroll.Viewport.Width;
        if (maxOffset <= 0)
            return;

        // Intercept before the nested presenters scroll; retain native horizontal wheel input.
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X - delta * 50, 0, maxOffset), scroll.Offset.Y);

        // Keep the page still even when the row has reached either end.
        e.Handled = true;
    }
}
