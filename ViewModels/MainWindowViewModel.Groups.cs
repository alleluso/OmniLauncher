using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyList<LibraryGroup> _libraryGroups = Array.Empty<LibraryGroup>();
    private IReadOnlyList<LibraryViewOption> _globalLibraryViews = Array.Empty<LibraryViewOption>();
    private (LibraryViewOption View, string Search, string Application, string Sort)? _previousLibraryView;
    private bool _changingGroup;

    public GroupDialogViewModel GroupDialogViewModel { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGroupOpen)), NotifyPropertyChangedFor(nameof(LibraryTitle)),
        NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    private LibraryGroup? _activeGroup;
    public bool IsGroupOpen => ActiveGroup is not null;
    public string LibraryTitle => ActiveGroup is { } group ? $"{group.ApplicationName} · {group.Name}" : SelectedLibraryView.Name;
    public string SearchPlaceholder => IsGroupOpen ? "Search projects in this group…" : "Search projects, applications, versions, or paths…";
    public event Action? GroupEntering;
    public event Action? GroupExited;

    public IEnumerable<LibraryGroup> CompatibleGroups(ProjectItem project) =>
        _libraryGroups.Where(group => group.IsCompatible(project)).OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase);

    [RelayCommand]
    private void OpenGroup(LibraryGroup? group)
    {
        if (group is null || IsWorking || IsGroupOpen) return;
        GroupEntering?.Invoke();
        _previousLibraryView = (SelectedLibraryView, SearchText, SelectedApplication, SelectedSort);
        _changingGroup = true;
        ActiveGroup = group;
        LibraryViews = new[] { LibraryViewOption.AllProjects, LibraryViewOption.Recent, LibraryViewOption.Favorites };
        OnPropertyChanged(nameof(LibraryViews));
        SelectedLibraryView = LibraryViewOption.AllProjects;
        SearchText = "";
        SelectedApplication = "All applications";
        _changingGroup = false;
        ApplyFilter();
    }

    [RelayCommand]
    private void ExitGroup()
    {
        if (!IsGroupOpen) return;
        _changingGroup = true;
        ActiveGroup = null;
        LibraryViews = _globalLibraryViews;
        OnPropertyChanged(nameof(LibraryViews));
        if (_previousLibraryView is { } previous)
        {
            SelectedLibraryView = LibraryViews.FirstOrDefault(view => view == previous.View ||
                (previous.View.Category is { } category && view.Category?.Id == category.Id)) ?? LibraryViewOption.AllProjects;
            SearchText = previous.Search;
            SelectedApplication = previous.Application;
            SelectedSort = previous.Sort;
        }
        _previousLibraryView = null;
        _changingGroup = false;
        ApplyFilter();
        GroupExited?.Invoke();
    }

    [RelayCommand]
    private void OpenAddGroupDialog()
    {
        if (IsWorking || IsGroupOpen) return;
        GroupDialogViewModel.Load(_librarySnapshot is null ? ProjectCategory.CreateDefaults() : CategoryLibrary.Read(_librarySnapshot),
            application: ApplicationRegistry.All.FirstOrDefault(application => application.Name == SelectedApplication)?.Type,
            categoryId: SelectedLibraryView.Category?.Id ?? 0);
        ViewState = ViewState.GroupDialog;
    }

    [RelayCommand]
    private void EditGroup(LibraryGroup? group)
    {
        if (group is null || IsWorking || _librarySnapshot is null) return;
        GroupDialogViewModel.Load(CategoryLibrary.Read(_librarySnapshot), group);
        ViewState = ViewState.GroupDialog;
    }

    [RelayCommand]
    private void DeleteGroup(LibraryGroup? group)
    {
        if (group is null || IsWorking) return;
        DeleteDialogViewModel.LoadGroup(group);
        ViewState = ViewState.DeleteProjectDialog;
    }

    private async void OnGroupConfirmed(GroupResult result) =>
        await SaveChangeAsync(() => result.Id is null
            ? ProjectLoaderService.AddGroupAsync(result.Name, result.Application, result.CategoryId, result.ImagePath)
            : ProjectLoaderService.EditGroupAsync(result.Id, result.Name, result.ImagePath, result.Application, result.CategoryId));

    public Task AssignGroupAsync(ProjectItem project, LibraryGroup? group) =>
        SaveChangeAsync(() => ProjectLoaderService.SetProjectGroupAsync(project.Id, group?.Id));

    private IReadOnlyList<object> BuildEntries(ProjectGroup application, IReadOnlyList<ProjectItem> projects)
    {
        if (IsGroupOpen || SelectedLibraryView == LibraryViewOption.Recent || SelectedLibraryView == LibraryViewOption.Favorites)
            return projects.Cast<object>().ToArray();

        var search = SearchText.Trim();
        var groups = _libraryGroups.Where(group => group.ApplicationType == application.ProjectType &&
            (SelectedLibraryView != LibraryViewOption.NoCategory || group.CategoryId == 0) &&
            (SelectedLibraryView.Category is not { } category || group.CategoryId == category.Id) &&
            (search.Length == 0 || group.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                group.ApplicationName.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();

        // Search exposes matching projects even when they belong to a group.
        var entries = projects.Where(project => project.GroupId is null || search.Length > 0).Cast<object>().Concat(groups);
        string Name(object entry) => entry is LibraryGroup group ? group.Name : ((ProjectItem)entry).Name;
        ProjectCategory Category(object entry) => entry is LibraryGroup group ? group.Category : ((ProjectItem)entry).Category;
        DateTimeOffset? Timestamp(object entry, bool opened) => entry is ProjectItem project
            ? (opened ? project.LastOpenedAt : project.AddedAt)
            : application.Projects.Where(project => project.GroupId == ((LibraryGroup)entry).Id)
                .Select(project => opened ? project.LastOpenedAt : project.AddedAt).Max();
        return (SelectedSort switch
        {
            "Category" => entries.OrderBy(entry => Category(entry).Id == 0).ThenBy(entry => Category(entry).Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase),
            "Last opened" => entries.OrderByDescending(entry => Timestamp(entry, true)).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase),
            "Date added" => entries.OrderByDescending(entry => Timestamp(entry, false)).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase),
            _ => entries.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)
        }).ToArray();
    }
}
