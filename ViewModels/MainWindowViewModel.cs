using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;


public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private bool _disposed;
    private bool _hasLoaded;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly DispatcherTimer _relativeTimeTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadCts;
    private List<ProjectGroup> _masterGroups = new();
    private JsonObject? _librarySnapshot;
    private bool _savingProjectMetadata;
    private Task _displayPreferencesSave = Task.CompletedTask;

    public SettingsViewModel SettingsViewModel { get; } = new();
    public AddProjectDialogViewModel DialogViewModel { get; } = new();
    public EditProjectDialogViewModel EditDialogViewModel { get; } = new();
    public ProjectCommandsDialogViewModel CommandsDialogViewModel { get; } = new();
    public ProjectNotesDialogViewModel NotesDialogViewModel { get; } = new();
    public DeleteConfirmationDialogViewModel DeleteDialogViewModel { get; } = new();
    public ObservableCollection<ProjectGroup> ProjectGroups { get; } = new();
    public IReadOnlyList<LibraryViewOption> LibraryViews { get; private set; } = new[]
    {
        LibraryViewOption.AllProjects, LibraryViewOption.Recent, LibraryViewOption.Favorites, LibraryViewOption.NoCategory
    };
    public IReadOnlyList<string> ApplicationFilters { get; } = new[] { "All applications" }
        .Concat(ApplicationRegistry.All.Select(p => p.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Name", "Category", "Last opened", "Date added" };

    // The view owns scroll positions; persistence refreshes keep them while filters can start fresh.
    public event Action? ProjectsUpdating;
    public event Action? ProjectsUpdated;
    public event Action? LibraryChanged;
    public IReadOnlyList<ProjectItem> AllProjects => _masterGroups.SelectMany(group => group.Projects).ToArray();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError)), NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string _errorMessage = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking))]
    private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking)), NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;
    [ObservableProperty] private string _busyMessage = "Loading library…";
    [ObservableProperty] private string _notice = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLibraryVisible))]
    private ViewState _viewState = ViewState.ProjectsList;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private LibraryViewOption _selectedLibraryView = LibraryViewOption.AllProjects;
    [ObservableProperty] private string _selectedApplication = "All applications";
    [ObservableProperty] private string _selectedSort = "Name";
    [ObservableProperty] private bool _isListView;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TileSizeLabel)), NotifyPropertyChangedFor(nameof(ListColumns))]
    private int _tileSize = 2;
    public string TileSizeLabel => TileSize switch { 0 => "Small", 1 => "Medium", _ => "Large" };
    public int ListColumns => TileSize switch { 0 => 3, 1 => 2, _ => 1 };
    [RelayCommand] private void ShowTiles() => IsListView = false;
    [RelayCommand] private void ShowList() => IsListView = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyState)), NotifyPropertyChangedFor(nameof(ResultCountLabel))]
    private int _visibleProjectCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLibraryEmpty)), NotifyPropertyChangedFor(nameof(ResultCountLabel))]
    private int _totalProjectCount;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsLibraryVisible => ViewState == ViewState.ProjectsList;
    public bool IsWorking => IsBusy || IsLoading;
    public bool IsLibraryEmpty => TotalProjectCount == 0;
    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchText) || SelectedApplication != "All applications";
    public bool ShowEmptyState => _hasLoaded && !IsLoading && !HasError && !ProjectGroups.Any(group => group.Entries.Count > 0);
    public string ResultCountLabel => $"{VisibleProjectCount} of {TotalProjectCount} projects";
    public string EmptyTitle => IsGroupOpen && IsLibraryEmpty ? "This group is empty" : IsLibraryEmpty ? "Your library starts here" : HasFilters ? "No matching projects"
        : SelectedLibraryView == LibraryViewOption.NoCategory ? "No uncategorized projects"
        : SelectedLibraryView.Category is { } category ? $"No projects in {category.Name}"
        : SelectedLibraryView == LibraryViewOption.Favorites ? "No favorites yet" : "No recent projects yet";
    public string EmptyDescription => IsGroupOpen && IsLibraryEmpty ? "Add a project here, or add compatible projects from the library using their actions menu." : IsLibraryEmpty ? "Add a project and choose the application to open it."
        : HasFilters ? "Try another search or clear the filters."
        : SelectedLibraryView == LibraryViewOption.NoCategory ? "Projects without a category will appear here."
        : SelectedLibraryView.IsCategory ? "Assign this category from a project's actions menu."
        : SelectedLibraryView == LibraryViewOption.Favorites ? "Star a project to keep it close at hand."
        : "Projects you open from this hub will appear here.";
    public string EmptyActionLabel => IsLibraryEmpty ? "Add project" : HasFilters ? "Clear filters" : "Show all projects";

    public MainWindowViewModel()
    {
        SettingsViewModel.Confirmed += OnSettingsConfirmed;
        SettingsViewModel.Cancelled += CloseDialog;
        DialogViewModel.Confirmed += OnDialogConfirmed;
        DialogViewModel.Cancelled += CloseDialog;
        EditDialogViewModel.Confirmed += OnEditDialogConfirmed;
        EditDialogViewModel.Cancelled += CloseDialog;
        CommandsDialogViewModel.Confirmed += OnCommandsDialogConfirmed;
        CommandsDialogViewModel.Cancelled += CloseDialog;
        NotesDialogViewModel.Confirmed += OnNotesDialogConfirmed;
        NotesDialogViewModel.Cancelled += CloseDialog;
        DeleteDialogViewModel.Confirmed += OnDeleteDialogConfirmed;
        DeleteDialogViewModel.Cancelled += CloseDialog;
        DeleteDialogViewModel.GroupConfirmed += async group => await SaveChangeAsync(() => ProjectLoaderService.DeleteGroupAsync(group.Id));
        GroupDialogViewModel.Confirmed += OnGroupConfirmed;
        GroupDialogViewModel.Cancelled += CloseDialog;
        if (Design.IsDesignMode) return;
        _relativeTimeTimer.Tick += (_, _) =>
        {
            foreach (var project in _masterGroups.SelectMany(group => group.Projects))
                project.RefreshLastOpenedLabel();
        };
        _relativeTimeTimer.Start();
        _ = LoadProjectsAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnIsListViewChanged(bool value) => SaveDisplayPreferences();
    partial void OnTileSizeChanged(int value) => SaveDisplayPreferences();

    private void SaveDisplayPreferences()
    {
        if (!_hasLoaded || _disposed || Design.IsDesignMode) return;
        _displayPreferencesSave = SaveDisplayPreferencesAsync(_displayPreferencesSave, IsListView, TileSize);
    }

    public Task FlushDisplayPreferencesAsync() => _displayPreferencesSave;

    private void RestoreDisplayPreferences(JsonObject root)
    {
        var preferences = root["displayPreferences"] as JsonObject;
        IsListView = preferences?["view"] is JsonValue view && view.TryGetValue<string>(out var mode) && mode == "list";
        TileSize = preferences?["tileSize"] is JsonValue size && size.TryGetValue<int>(out var value) && value is >= 0 and <= 2
            ? value : 2;
    }

    private async Task SaveDisplayPreferencesAsync(Task previousSave, bool isListView, int tileSize)
    {
        // Keep rapid changes in order and serialize with library refreshes.
        await previousSave;
        await _loadGate.WaitAsync();
        try
        {
            await ProjectLoaderService.SaveDisplayPreferencesAsync(isListView, tileSize);
            if (_librarySnapshot is not null)
                _librarySnapshot["displayPreferences"] = new JsonObject
                {
                    ["view"] = isListView ? "list" : "tiles",
                    ["tileSize"] = Math.Clamp(tileSize, 0, 2)
                };
        }
        catch (Exception ex) { ReportActionError("Could not save display preferences", ex); }
        finally { _loadGate.Release(); }
    }
    partial void OnSelectedApplicationChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();
    partial void OnSelectedLibraryViewChanged(LibraryViewOption value)
    {
        OnPropertyChanged(nameof(LibraryTitle));
        // Recent opens in recency order, with the other sort choices still available.
        if (value == LibraryViewOption.Recent) SelectedSort = "Last opened";
        ApplyFilter();
    }

    private void ApplyFilter(bool preserveScroll = false)
    {
        if (_changingGroup) return;
        if (preserveScroll) ProjectsUpdating?.Invoke();
        var search = SearchText.Trim();
        var desiredGroups = new List<ProjectGroup>();
        foreach (var group in _masterGroups.OrderBy(group => group.GroupName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (SelectedApplication != "All applications" && SelectedApplication != group.GroupName) continue;
            if (ActiveGroup is { } active && group.ProjectType != active.ApplicationType) continue;
            var projects = group.Projects.Where(p =>
                (ActiveGroup is null || p.GroupId == ActiveGroup.Id) &&
                (SelectedLibraryView != LibraryViewOption.Favorites || p.IsFavorite) &&
                (SelectedLibraryView != LibraryViewOption.Recent || p.LastOpenedAt.HasValue) &&
                (SelectedLibraryView != LibraryViewOption.NoCategory || p.CategoryId == ProjectCategory.None.Id) &&
                (SelectedLibraryView.Category is not { } category || p.CategoryId == category.Id) &&
                (search.Length == 0 || new[] { p.Name, group.GroupName, p.ApplicationName, p.RequiredVersion, p.ProjectPath }
                    .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase))));
            var sorted = SelectedSort switch
            {
                "Category" => projects.OrderBy(p => p.Category.Id == ProjectCategory.None.Id).ThenBy(p => p.Category.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                "Last opened" => projects.OrderByDescending(p => p.LastOpenedAt).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                "Date added" => projects.OrderByDescending(p => p.AddedAt).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            var items = sorted.ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            var entries = BuildEntries(group, items);
            if (entries.Count == 0) continue;
            // A matching group card represents all of its members in the row/footer counts.
            var representedGroups = entries.OfType<LibraryGroup>().Select(entry => entry.Id).ToHashSet();
            items = items.Concat(group.Projects.Where(project => project.GroupId is { } id && representedGroups.Contains(id)))
                .DistinctBy(project => project.Id).ToList();
            var visible = ProjectGroups.FirstOrDefault(g => g.ProjectType == group.ProjectType)
                ?? new ProjectGroup { GroupName = group.GroupName, ProjectType = group.ProjectType };
            // Keep the group and its scroll container alive; replace only changed tile data.
            for (var i = 0; i < items.Count; i++)
            {
                var existing = visible.Projects.FirstOrDefault(p => p.Id == items[i].Id);
                if (existing is null) visible.Projects.Insert(i, items[i]);
                else
                {
                    var oldIndex = visible.Projects.IndexOf(existing);
                    if (oldIndex != i) visible.Projects.Move(oldIndex, i);
                    if (!ReferenceEquals(visible.Projects[i], items[i])) visible.Projects[i] = items[i];
                }
            }
            while (visible.Projects.Count > items.Count) visible.Projects.RemoveAt(visible.Projects.Count - 1);
            for (var i = 0; i < entries.Count; i++)
            {
                if (i >= visible.Entries.Count) visible.Entries.Add(entries[i]);
                else if (!Equals(visible.Entries[i], entries[i])) visible.Entries[i] = entries[i];
            }
            while (visible.Entries.Count > entries.Count) visible.Entries.RemoveAt(visible.Entries.Count - 1);
            desiredGroups.Add(visible);
        }
        for (var i = 0; i < desiredGroups.Count; i++)
        {
            var oldIndex = ProjectGroups.IndexOf(desiredGroups[i]);
            if (oldIndex < 0) ProjectGroups.Insert(i, desiredGroups[i]);
            else if (oldIndex != i) ProjectGroups.Move(oldIndex, i);
        }
        while (ProjectGroups.Count > desiredGroups.Count) ProjectGroups.RemoveAt(ProjectGroups.Count - 1);
        TotalProjectCount = AllProjects.Count(project => ActiveGroup is null || project.GroupId == ActiveGroup.Id);
        VisibleProjectCount = desiredGroups.Sum(g => g.Projects.Count);
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(EmptyActionLabel));
        if (preserveScroll) ProjectsUpdated?.Invoke();
    }

    [RelayCommand] private void ClearFilters() { SearchText = ""; SelectedApplication = "All applications"; }
    [RelayCommand] private void EmptyAction()
    {
        if (IsLibraryEmpty) OpenAddDialog();
        else if (HasFilters) ClearFilters();
        else SelectedLibraryView = LibraryViewOption.AllProjects;
    }
    [RelayCommand] private void DismissError() => ErrorMessage = "";
    [RelayCommand] private void CloseDialog()
    {
        if (!IsWorking) ViewState = ViewState.ProjectsList;
    }
    [RelayCommand] private void OpenAddDialog()
    {
        if (IsWorking) return;
        DialogViewModel.Reset();
        if (ActiveGroup is { } group)
        {
            DialogViewModel.SelectedProjectType = group.ApplicationType;
            DialogViewModel.CanChooseApplication = false;
        }
        ViewState = ViewState.AddProjectDialog;
    }
    [RelayCommand] private async Task RefreshLibrary()
    {
        if (IsWorking) return;
        ErrorMessage = "";
        Notice = "";
        await LoadProjectsAsync();
    }
    [RelayCommand] private async Task OpenSettings()
    {
        if (IsWorking) return;
        IsBusy = true;
        BusyMessage = "Loading settings…";
        ErrorMessage = "";
        try
        {
            SettingsViewModel.Load(await ProjectLoaderService.ReadLibraryAsync());
            ViewState = ViewState.Settings;
        }
        catch (Exception ex) { ReportActionError("Could not open Settings", ex); }
        finally { IsBusy = false; }
    }

    private async void OnSettingsConfirmed(SettingsResult settings) =>
        await SaveChangeAsync(() => ProjectLoaderService.SaveSettingsAsync(settings.Applications, settings.Categories, settings.OriginalCategories, settings.SourceControl));
    private async void OnDialogConfirmed(AddProjectResult item) =>
        await SaveChangeAsync(() => ProjectLoaderService.AddProjectAsync(item.Type, item.Name, item.Path, item.Image,
            ActiveGroup?.CategoryId ?? SelectedLibraryView.Category?.Id ?? ProjectCategory.None.Id, ActiveGroup?.Id));
    private async void OnEditDialogConfirmed(EditProjectResult item) =>
        await SaveChangeAsync(() => ProjectLoaderService.EditProjectAsync(item.OriginalItem, item.Name, item.Path, item.Image, item.ExecutableOverride, item.LaunchShortcut));
    private async void OnCommandsDialogConfirmed(ProjectCommandsResult result) =>
        await SaveChangeAsync(() => ProjectLoaderService.SaveProjectCommandsAsync(result.ProjectId, result.Commands));
    private async void OnNotesDialogConfirmed(ProjectNotesResult result) =>
        await SaveChangeAsync(() => ProjectLoaderService.SaveProjectNotesAndLinksAsync(result.ProjectId, result.Notes, result.Links));
    private async void OnDeleteDialogConfirmed(ProjectItem item) =>
        await SaveChangeAsync(() => ProjectLoaderService.DeleteProjectAsync(item));

    private async Task SaveChangeAsync(Func<Task> save)
    {
        if (IsWorking || _disposed) return;
        IsBusy = true;
        BusyMessage = "Saving library…";
        ErrorMessage = "";
        Notice = "";
        try
        {
            await save();
            if (_disposed) return;
            ViewState = ViewState.ProjectsList;
            _reloadCts?.Cancel();
            await LoadProjectsAsync();
        }
        catch (Exception ex) { ReportActionError("Could not save the library", ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task ToggleFavorite(ProjectItem? project)
    {
        if (project is null || IsWorking || _savingProjectMetadata || _disposed) return;
        _savingProjectMetadata = true;
        await _loadGate.WaitAsync();
        try
        {
            if (_disposed) return;
            var favorite = !project.IsFavorite;
            await ProjectLoaderService.SetFavoriteAsync(project, favorite);
            if (_disposed) return;
            // Keep the snapshot in sync so our file-watcher notification is a no-op.
            // Other disk changes still differ and trigger a normal refresh.
            if (_librarySnapshot is not null)
                ProjectLibraryOperations.SetFavorite(_librarySnapshot, project.Id, favorite);
            var current = _masterGroups.SelectMany(group => group.Projects).FirstOrDefault(item => item.Id == project.Id);
            if (current is not null) current.IsFavorite = favorite;
            if (SelectedLibraryView == LibraryViewOption.Favorites) ApplyFilter(preserveScroll: true);
        }
        catch (Exception ex) { ReportActionError("Could not save the favorite", ex); }
        finally { _loadGate.Release(); _savingProjectMetadata = false; }
    }

    public async Task AssignCategoryAsync(ProjectItem project, int categoryId)
    {
        if (IsWorking || _savingProjectMetadata || _disposed || project.CategoryId == categoryId) return;
        _savingProjectMetadata = true;
        await _loadGate.WaitAsync();
        try
        {
            if (_disposed) return;
            await ProjectLoaderService.SetCategoryAsync(project, categoryId);
            if (_disposed) return;
            // Updating the snapshot keeps our own file-watcher notification from reloading the library.
            if (_librarySnapshot is not null)
                ProjectLibraryOperations.SetCategory(_librarySnapshot, project.Id, categoryId);
            var current = _masterGroups.SelectMany(group => group.Projects).FirstOrDefault(item => item.Id == project.Id);
            if (current is not null)
            {
                current.CategoryId = categoryId;
                if (current.GroupId is { } groupId && !_libraryGroups.Any(group => group.Id == groupId && group.IsCompatible(current)))
                    current.GroupId = null;
            }
            _libraryGroups = _libraryGroups.Select(group => group with { ProjectCount = AllProjects.Count(project => project.GroupId == group.Id) }).ToArray();
            LibraryChanged?.Invoke();
            ApplyFilter(preserveScroll: true);
        }
        catch (Exception ex) { ReportActionError("Could not save the category", ex); }
        finally { _loadGate.Release(); _savingProjectMetadata = false; }
    }
    [RelayCommand] private async Task LaunchProject(ProjectItem? project)
    {
        if (project is null || IsWorking) return;
        if (!project.IsProjectAvailable) { EditProject(project); return; }
        if (!project.HasExecutable) { await OpenSettings(); return; }
        IsBusy = true;
        BusyMessage = "Opening project…";
        ErrorMessage = "";
        try
        {
            // Launching validates and starts files that may sit on a slow drive; keep it off the UI thread.
            await Task.Run(project.Launch);
            var openedAt = DateTimeOffset.UtcNow;
            try
            {
                await ProjectLoaderService.RecordLaunchAsync(project, openedAt);
                _reloadCts?.Cancel();
                await LoadProjectsAsync();
                Notice = $"Opened {project.Name}";
            }
            catch (Exception ex) { ReportActionError("The application started, but launch history could not be saved", ex); }
        }
        catch (Exception ex) { ReportActionError($"Could not open {project.Name}", ex); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void EditProject(ProjectItem? project)
    {
        if (project is null || IsWorking) return;
        EditDialogViewModel.LoadProject(project);
        ViewState = ViewState.EditProjectDialog;
    }

    public void ManageProjectCommands(ProjectItem project)
    {
        if (IsWorking) return;
        CommandsDialogViewModel.LoadProject(project);
        ViewState = ViewState.ProjectCommandsDialog;
    }

    public void ManageProjectNotes(ProjectItem project)
    {
        if (IsWorking) return;
        NotesDialogViewModel.LoadProject(project);
        ViewState = ViewState.ProjectNotesDialog;
    }

    public void OpenProjectLink(ProjectLink link)
    {
        try { ProjectLinkLauncher.Open(link.Url); }
        catch (Exception ex) { ReportActionError($"Could not open {link.Name}", ex); }
    }

    public void RunProjectCommand(ProjectItem project, ProjectCommand command)
    {
        if (IsWorking) return;
        ErrorMessage = "";
        Notice = "";
        try
        {
            ProjectCommandRunner.Run(project, command);
            Notice = $"Started {command.Name}";
        }
        catch (Exception ex) { ReportActionError($"Could not run {command.Name}", ex); }
    }
    [RelayCommand] private void DeleteProject(ProjectItem? project)
    {
        if (project is null || IsWorking) return;
        DeleteDialogViewModel.LoadProject(project);
        ViewState = ViewState.DeleteProjectDialog;
    }
    /// <summary>Re-checks every project and application path (e.g. after a drive is reconnected).</summary>
    public async Task RefreshAvailabilityAsync()
    {
        if (_disposed || !_hasLoaded) return;
        try { await Task.WhenAll(AllProjects.Select(project => project.RefreshAvailabilityAsync())); }
        catch (Exception ex) { ReportActionError("Could not check project availability", ex); }
    }

    [RelayCommand] private void ShowInExplorer(ProjectItem? project)
    {
        if (project is null) return;
        try
        {
            var path = Path.GetFullPath(project.ProjectPath);
            if (!project.IsProjectAvailable)
                throw new IOException("This project path is missing or its drive is offline. Use Edit Project to locate it.");
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                if (File.Exists(path)) start.ArgumentList.Add("/select,");
                start.ArgumentList.Add(path);
            }
            else
            {
                start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
            }
            using var process = Process.Start(start);
        }
        catch (Exception ex) { ReportActionError("Could not show the project folder", ex); }
    }

    private async Task LoadProjectsAsync(bool onlyIfChanged = false)
    {
        await _loadGate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (!onlyIfChanged)
            {
                IsLoading = true;
                if (!IsBusy) BusyMessage = "Loading library…";
            }
            var root = await ProjectLoaderService.ReadLibraryAsync();
            if (_disposed || (onlyIfChanged && JsonNode.DeepEquals(root, _librarySnapshot))) return;
            if (!_hasLoaded) RestoreDisplayPreferences(root);
            if (!onlyIfChanged) OmniLauncher.Converters.AsyncImage.ClearCache();
            IsLoading = true;
            if (!IsBusy) BusyMessage = "Loading library…";
            var groups = await ProjectLoaderService.LoadProjectGroupsAsync(root);
            if (_disposed) return;
            await Task.Run(() => ProjectLoaderService.CleanupUnusedThumbnails(root, groups));
            if (_disposed) return;
            _librarySnapshot = root;
            _masterGroups = groups;
            _libraryGroups = GroupLibrary.Read(root).Select(group => group with
            {
                ProjectCount = AllProjects.Count(project => project.GroupId == group.Id)
            }).ToArray();
            // Empty groups still need their application row.
            foreach (var group in _libraryGroups.Where(group => ApplicationRegistry.All.Any(application => application.Key == group.ApplicationKey)))
                if (!_masterGroups.Any(application => application.ProjectType == group.ApplicationType))
                    _masterGroups.Add(new ProjectGroup { GroupName = group.ApplicationName, ProjectType = group.ApplicationType });
            LibraryChanged?.Invoke();
            var selected = SelectedLibraryView;
            _globalLibraryViews = new[] { LibraryViewOption.AllProjects, LibraryViewOption.Recent, LibraryViewOption.Favorites, LibraryViewOption.NoCategory }
                .Concat(CategoryLibrary.Read(root).Select(category => new LibraryViewOption(category.Name, "", category))).ToArray();
            var exitedGroup = false;
            if (ActiveGroup is { } activeGroup)
            {
                var refreshedGroup = _libraryGroups.FirstOrDefault(group => group.Id == activeGroup.Id &&
                    ApplicationRegistry.All.Any(application => application.Key == group.ApplicationKey));
                if (refreshedGroup is null) { ExitGroup(); selected = SelectedLibraryView; exitedGroup = true; }
                else ActiveGroup = refreshedGroup;
            }
            LibraryViews = IsGroupOpen
                ? new[] { LibraryViewOption.AllProjects, LibraryViewOption.Recent, LibraryViewOption.Favorites }
                : _globalLibraryViews;
            OnPropertyChanged(nameof(LibraryViews));
            SelectedLibraryView = selected.Category is { } selectedCategory
                ? LibraryViews.FirstOrDefault(view => view.Category?.Id == selectedCategory.Id) ?? LibraryViewOption.AllProjects
                : selected;
            _hasLoaded = true;
            ApplyFilter(preserveScroll: !exitedGroup);
            SetupFileWatcher();
            _ = RefreshGitStatusAsync();
        }
        catch (Exception ex) { ReportActionError("Could not load the library", ex); }
        finally { IsLoading = false; _loadGate.Release(); }
    }

    public void ReportActionError(string action, Exception ex)
    {
        if (!_disposed) ErrorMessage = $"{action}. {ex.Message}";
    }
    private async Task ReloadProjectsAsync()
    {
        if (_disposed) return;
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = new CancellationTokenSource();
        var token = _reloadCts.Token;
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested && !IsBusy) await LoadProjectsAsync(onlyIfChanged: true);
        }
        catch (OperationCanceledException) { }
    }
    private void SetupFileWatcher()
    {
        if (_disposed || _watcher is not null) return;
        var directory = Path.GetDirectoryName(ProjectLoaderService.ConfigPath);
        if (directory is null || !Directory.Exists(directory)) return;
        _watcher = new FileSystemWatcher(directory, "projects.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        void Changed(object _, FileSystemEventArgs __) => Dispatcher.UIThread.Post(() => _ = ReloadProjectsAsync());
        _watcher.Changed += Changed;
        _watcher.Created += Changed;
        _watcher.Renamed += Changed;
        _watcher.Deleted += Changed;
        _watcher.Error += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) ErrorMessage = "Automatic refresh stopped. Use Refresh to reload the library.";
            _watcher?.Dispose();
            _watcher = null;
        });
    }
    public void Dispose()
    {
        _disposed = true;
        _relativeTimeTimer.Stop();
        _gitRefreshCts?.Cancel();
        _watcher?.Dispose();
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
    }
}
