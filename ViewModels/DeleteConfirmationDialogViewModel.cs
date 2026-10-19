using System;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class DeleteConfirmationDialogViewModel : ViewModelBase
{
    private ProjectItem? _item;
    private LibraryGroup? _group;

    public string ProjectName => _group?.Name ?? _item?.Name ?? string.Empty;
    public string Title => _group is null ? "Remove project" : "Delete group";
    public string Description => _group is null ? "Remove this project from your library?" : "Delete this group from your library?";
    public string PreservationMessage => _group is null ? "Your project files will stay on disk." : "Projects in this group will remain in your library.";

    public event Action<ProjectItem>? Confirmed;
    public event Action<LibraryGroup>? GroupConfirmed;
    public event Action? Cancelled;

    [RelayCommand]
    private void Confirm()
    {
        if (_group is not null) GroupConfirmed?.Invoke(_group);
        else if (_item is not null)
            Confirmed?.Invoke(_item);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    public void LoadProject(ProjectItem project)
    {
        _item = project;
        _group = null;
        NotifyContentChanged();
    }

    public void LoadGroup(LibraryGroup group)
    {
        _item = null;
        _group = group;
        NotifyContentChanged();
    }

    private void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(PreservationMessage));
    }
}
