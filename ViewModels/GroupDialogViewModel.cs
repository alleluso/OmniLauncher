using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public partial class GroupDialogViewModel : ViewModelBase
{
    private string? _groupId;
    public IReadOnlyList<ProjectType> ApplicationTypes => ProjectLoaderService.SupportedTypes;
    public IReadOnlyList<ProjectCategory> Categories { get; private set; } = new[] { ProjectCategory.None };
    public bool IsCreating => _groupId is null;
    public bool CanEditClassification { get; private set; }
    public string Title => IsCreating ? "Add group" : "Edit group";
    public string ConfirmLabel => IsCreating ? "Add group" : "Save changes";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] private string _groupName = "";
    [ObservableProperty] private ProjectType _selectedApplication = ProjectLoaderService.SupportedTypes[0];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] private ProjectCategory? _selectedCategory = ProjectCategory.None;
    [ObservableProperty] private string _imagePath = "";
    [ObservableProperty] private string _error = "";

    public event Action<GroupResult>? Confirmed;
    public event Action? Cancelled;

    public void Load(IReadOnlyList<ProjectCategory> categories, LibraryGroup? group = null,
        ProjectType? application = null, int categoryId = 0)
    {
        _groupId = group?.Id;
        CanEditClassification = group is null || group.ProjectCount == 0;
        OnPropertyChanged(nameof(CanEditClassification));
        Categories = new[] { ProjectCategory.None }.Concat(categories).ToArray();
        OnPropertyChanged(nameof(Categories));
        GroupName = group?.Name ?? "";
        SelectedApplication = group?.ApplicationType ?? application ?? ApplicationTypes[0];
        SelectedCategory = Categories.FirstOrDefault(category => category.Id == (group?.CategoryId ?? categoryId)) ?? ProjectCategory.None;
        OnPropertyChanged(nameof(SelectedCategory));
        ImagePath = group?.ImagePath ?? "";
        Error = "";
        OnPropertyChanged(nameof(IsCreating));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ConfirmLabel));
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(GroupName) &&
        SelectedCategory is { } category && !string.IsNullOrWhiteSpace(category.Name) && Categories.Contains(category);
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (CanConfirm() && SelectedCategory is { } category)
            Confirmed?.Invoke(new(_groupId, GroupName.Trim(), SelectedApplication, category.Id, ImagePath.Trim()));
    }
    [RelayCommand] private void Cancel() => Cancelled?.Invoke();

    [RelayCommand]
    private async Task BrowseImage(TopLevel? topLevel)
    {
        if (topLevel is null) return;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Cover Image", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }]
            });
            if (files.Count > 0) ImagePath = files[0].Path.LocalPath;
            Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}

public sealed record GroupResult(string? Id, string Name, ProjectType Application, int CategoryId, string ImagePath);
