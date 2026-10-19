using OmniLauncher.Services.ProjectLoaders;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class EditProjectDialogViewModel : ViewModelBase
{
    private ProjectItem? _originalItem;
    public IProjectTypeLoader PathLoader =>
        OmniLauncher.Services.ApplicationRegistry.Get(_originalItem?.ApplicationType ?? ProjectType.UnrealEngine).Loader;
    public bool HasPathChoices => PathLoader.FolderPickerTitle is not null && PathLoader.FilePickerTitle is not null;
    [ObservableProperty] private string _executableOverride = "";
    [ObservableProperty] private string _applicationError = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShortcutButtonLabel)),
     NotifyPropertyChangedFor(nameof(ShortcutDisplayText)), NotifyPropertyChangedFor(nameof(IsShortcutActionDanger))]
    private string _launchShortcut = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShortcutButtonLabel)),
     NotifyPropertyChangedFor(nameof(ShortcutDisplayText)), NotifyPropertyChangedFor(nameof(IsShortcutActionDanger))]
    private bool _isRecordingShortcut;
    public string ShortcutDisplayText => IsRecordingShortcut ? "Press a Key Combination" : LaunchShortcut;
    public string ShortcutButtonLabel => IsRecordingShortcut ? "Cancel"
        : string.IsNullOrEmpty(LaunchShortcut) ? "Record" : "Clear";
    public bool IsShortcutActionDanger => IsRecordingShortcut || !string.IsNullOrEmpty(LaunchShortcut);
    [ObservableProperty] private string _shortcutError = "";
    [RelayCommand] private void ClearShortcut()
    {
        IsRecordingShortcut = false;
        LaunchShortcut = "";
        ShortcutError = "";
    }
    [RelayCommand]
    private async Task BrowseExecutable(TopLevel? window)
    {
        if (window is null) return;
        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select application override", AllowMultiple = false,
                FileTypeFilter = OperatingSystem.IsWindows()
                    ? [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }] : [FilePickerFileTypes.All]
            });
            if (files.Count > 0)
                ExecutableOverride = OmniLauncher.Services.ApplicationPaths.Validate(
                    _originalItem!.ApplicationType, files[0].Path.LocalPath);
            ApplicationError = "";
        }
        catch (Exception ex) { ApplicationError = ex.Message; }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty] private string _pathError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _projectPath = string.Empty;

    public event Action<EditProjectResult>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        try { LaunchShortcut = OmniLauncher.Services.LaunchShortcut.Normalize(LaunchShortcut); ShortcutError = ""; }
        catch (FormatException ex) { ShortcutError = ex.Message; return; }
        Confirmed?.Invoke(new EditProjectResult(
            _originalItem!,
            ProjectName.Trim(),
            ProjectPath.Trim(),
            ImagePath.Trim(), ExecutableOverride.Trim(), LaunchShortcut));
    }

    private bool CanConfirm() =>
        !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(ProjectPath);

    [RelayCommand]
    private async Task BrowseImage(TopLevel? topLevel)
    {
        if (topLevel is null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Cover Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }
            ]
        });
        if (files.Count > 0)
            ImagePath = files[0].Path.LocalPath;
    }

    [RelayCommand]
    private Task BrowsePath(TopLevel? topLevel) => BrowseProjectPath(topLevel, false);

    [RelayCommand]
    private Task BrowseProjectFile(TopLevel? topLevel) => BrowseProjectPath(topLevel, true);

    private async Task BrowseProjectPath(TopLevel? topLevel, bool selectFile)
    {
        if (topLevel is null) return;
        try
        {
            var path = await OmniLauncher.Services.ProjectPathPicker.PickAsync(topLevel, _originalItem!.ApplicationType, selectFile);
            if (path is not null) ProjectPath = path;
            PathError = string.Empty;
        }
        catch (Exception ex) { PathError = ex.Message; }
    }

    public void LoadProject(ProjectItem project)
    {
        PathError = string.Empty;
        _originalItem = project;
        OnPropertyChanged(nameof(PathLoader));
        OnPropertyChanged(nameof(HasPathChoices));
        ExecutableOverride = project.ExecutableOverride;
        LaunchShortcut = project.LaunchShortcut;
        IsRecordingShortcut = false;
        ShortcutError = "";
        ApplicationError = "";
        ProjectName   = project.Name;
        ImagePath     = project.CustomImagePath;
        ProjectPath   = project.ProjectPath;
    }
}

public record EditProjectResult(ProjectItem OriginalItem, string Name, string Path, string Image, string ExecutableOverride, string LaunchShortcut = "");






