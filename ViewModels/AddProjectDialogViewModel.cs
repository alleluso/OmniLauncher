using OmniLauncher.Services.ProjectLoaders;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class AddProjectDialogViewModel : ViewModelBase
{
    public IReadOnlyList<ProjectType> ProjectTypes { get; } = OmniLauncher.Services.ProjectLoaderService.SupportedTypes;
    [ObservableProperty] private bool _canChooseApplication = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathLoader))]
    [NotifyPropertyChangedFor(nameof(HasPathChoices))]
    private ProjectType _selectedProjectType = OmniLauncher.Services.ProjectLoaderService.SupportedTypes[0];

    public IProjectTypeLoader PathLoader => OmniLauncher.Services.ApplicationRegistry.Get(SelectedProjectType).Loader;
    public bool HasPathChoices => PathLoader.FolderPickerTitle is not null && PathLoader.FilePickerTitle is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty] private string _pathError = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _projectPath = string.Empty;

    public event Action<AddProjectResult>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() =>
        Confirmed?.Invoke(new AddProjectResult(
            SelectedProjectType,
            ProjectName.Trim(),
            ProjectPath.Trim(),
            ImagePath.Trim()));

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
            var path = await OmniLauncher.Services.ProjectPathPicker.PickAsync(topLevel, SelectedProjectType, selectFile);
            if (path is not null) ProjectPath = path;
            PathError = string.Empty;
        }
        catch (Exception ex) { PathError = ex.Message; }
    }

    public void Reset()
    {
        CanChooseApplication = true;
        PathError = string.Empty;
        ProjectName = string.Empty;
        ImagePath   = string.Empty;
        ProjectPath = string.Empty;
        SelectedProjectType = OmniLauncher.Services.ProjectLoaderService.SupportedTypes[0];
    }
}

public record AddProjectResult(ProjectType Type, string Name, string Path, string Image);




