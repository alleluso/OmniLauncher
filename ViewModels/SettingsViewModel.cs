using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public sealed record SettingsResult(IReadOnlyList<RegisteredApplication> Applications,
    IReadOnlyList<ProjectCategory> Categories, IReadOnlyList<ProjectCategory> OriginalCategories, SourceControlSettings SourceControl);

public partial class CategorySettingsViewModel : ViewModelBase
{
    private readonly SettingsViewModel _owner;
    public int Id { get; }
    public string UsageLabel { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _colorHex;
    [ObservableProperty] private double _hue;
    [ObservableProperty] private double _saturation;
    [ObservableProperty] private double _value;
    public IBrush ColorBrush => Brush.Parse(CategoryLibrary.IsColor(ColorHex) ? ColorHex : "#808080");
    public bool HasColorError => !CategoryLibrary.IsColor(ColorHex);
    private bool _updating;

    public CategorySettingsViewModel(SettingsViewModel owner, ProjectCategory category, int projectCount)
    {
        _owner = owner;
        Id = category.Id;
        _name = category.Name;
        _colorHex = category.Color;
        UsageLabel = $"{projectCount} project{(projectCount == 1 ? "" : "s")}";
        OnColorHexChanged(_colorHex);
    }

    partial void OnColorHexChanged(string value)
    {
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(HasColorError));
        if (!CategoryLibrary.IsColor(value) || _updating) return;
        _updating = true;
        var color = Color.Parse(value).ToHsv();
        // Keep the selected hue for grays and hue/saturation for black.
        if (color.S > 0) Hue = color.H;
        if (color.V > 0) Saturation = color.S * 100;
        Value = color.V * 100;
        _updating = false;
    }
    private void UpdateColor()
    {
        if (_updating) return;
        var color = new HsvColor(1, Hue, Saturation / 100, Value / 100).ToRgb();
        // Avoid converting back from rounded RGB while adjusting HSV controls.
        _updating = true;
        try { ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; }
        finally { _updating = false; }
    }
    partial void OnHueChanged(double value) => UpdateColor();
    partial void OnSaturationChanged(double value) => UpdateColor();
    partial void OnValueChanged(double value) => UpdateColor();
    [RelayCommand] private void Remove() => _owner.Categories.Remove(this);
}

public partial class RegisteredApplicationViewModel : ViewModelBase
{
    private readonly SettingsViewModel _owner;
    public ProjectType Type { get; }
    public string Name => ApplicationRegistry.Get(Type).Name;
    public string IconPath => ApplicationRegistry.Get(Type).IconPath;
    [ObservableProperty] private string _path;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionLabel))]
    private string _version;
    [ObservableProperty] private string _error = "";
    // Checked in the background: the executable may be on an offline drive.
    [ObservableProperty] private bool _isMissing;
    private int _checkVersion;

    partial void OnPathChanged(string value) => _ = CheckAsync(inferVersion: false);

    /// <summary>Checks the executable off the UI thread; optionally re-reads its installed version.</summary>
    public async Task CheckAsync(bool inferVersion)
    {
        var check = ++_checkVersion;
        var path = Path;
        var type = Type;
        var (exists, version) = await PathProbe.RunAsync(path,
            p => File.Exists(p) ? (true, inferVersion ? ApplicationPaths.InferVersion(type, p) : "") : (false, ""),
            (false, ""));
        if (check != _checkVersion) return;
        IsMissing = !exists;
        if (inferVersion && exists) Version = version;
    }
    public string VersionLabel => string.IsNullOrWhiteSpace(Version) ? "Version unavailable" : Version;

    public RegisteredApplicationViewModel(SettingsViewModel owner, RegisteredApplication application)
    {
        _owner = owner;
        Type = application.Type;
        _path = application.Path;
        _version = application.Version;
        _ = CheckAsync(inferVersion: true);
    }

    [RelayCommand] private void Remove() => _owner.Applications.Remove(this);
    [RelayCommand]
    private async Task Browse(TopLevel? window)
    {
        if (window is null) return;
        Error = "";
        try
        {
            var application = await SettingsViewModel.PickApplicationAsync(window, Type);
            if (application is null) return;
            _owner.EnsureUnique(application, this);
            Path = application.Path;
            Version = application.Version;
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _sourceControlApplication = "";
    [ObservableProperty] private string _sourceControlArguments = "";
    [ObservableProperty] private string _gitExecutable = "git";

    [RelayCommand] private async Task BrowseSourceControl(TopLevel? window)
    {
        if (window is null) return;
        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a Git client or terminal", AllowMultiple = false,
                FileTypeFilter = OperatingSystem.IsWindows()
                    ? [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }] : [FilePickerFileTypes.All]
            });
            if (files.Count > 0) SourceControlApplication = files[0].TryGetLocalPath() ?? "";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand] private void ClearSourceControl()
    {
        SourceControlApplication = "";
        SourceControlArguments = "";
    }
    public ObservableCollection<CategorySettingsViewModel> Categories { get; } = new();
    private IReadOnlyList<ProjectCategory> _originalCategories = Array.Empty<ProjectCategory>();
    private int _nextCategoryId;
    public bool HasNoCategories => Categories.Count == 0;
    public string CategoryCountLabel => $"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}";
    public ObservableCollection<RegisteredApplicationViewModel> Applications { get; } = new();
    public string CountLabel => $"{Applications.Count} application{(Applications.Count == 1 ? "" : "s")}";
    public bool IsEmpty => Applications.Count == 0;
    [ObservableProperty] private string _error = "";
    public event Action<SettingsResult>? Confirmed;
    public event Action? Cancelled;

    public SettingsViewModel()
    {
        Applications.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(IsEmpty));
        };
        Categories.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CategoryCountLabel));
            OnPropertyChanged(nameof(HasNoCategories));
            AddCategoryCommand.NotifyCanExecuteChanged();
        };
    }

    private bool CanAddCategory() => Categories.Count < CategoryLibrary.MaximumCategories;

    [RelayCommand(CanExecute = nameof(CanAddCategory))] private void AddCategory()
    {
        if (!CanAddCategory()) return;
        var name = "New category";
        var suffix = 2;
        while (Categories.Any(category => category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"New category {suffix++}";
        Categories.Add(new(this, new ProjectCategory(_nextCategoryId++, name, "#AA88DE"), 0));
    }

    public void MoveCategory(CategorySettingsViewModel row, int target)
    {
        var source = Categories.IndexOf(row);
        if (source >= 0 && target >= 0 && target < Categories.Count && source != target)
            Categories.Move(source, target);
    }

    [RelayCommand] private void Cancel() => Cancelled?.Invoke();
    [RelayCommand]
    private async Task Add(TopLevel? window)
    {
        if (window is null) return;
        Error = "";
        try
        {
            var application = await PickApplicationAsync(window);
            if (application is null) return;
            EnsureUnique(application);
            Applications.Add(new(this, application));
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    internal void EnsureUnique(RegisteredApplication application, RegisteredApplicationViewModel? except = null)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Applications.Any(row => row != except && row.Type == application.Type && string.Equals(row.Path, application.Path, comparison)))
            throw new InvalidOperationException("This executable is already in the list.");
    }

    internal static async Task<RegisteredApplication?> PickApplicationAsync(TopLevel window, ProjectType? type = null)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an executable", AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }] : [FilePickerFileTypes.All]
        });
        if (files.Count == 0) return null;
        var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Select an application on this computer.");
        return await Task.Run(() => ApplicationPaths.DetectApplication(path, type));
    }

    public void Move(RegisteredApplicationViewModel row, int target)
    {
        var source = Applications.IndexOf(row);
        if (source >= 0 && target >= 0 && target < Applications.Count && source != target)
            Applications.Move(source, target);
    }

    [RelayCommand] private void Save()
    {
        Error = "";
        if (Categories.Count > CategoryLibrary.MaximumCategories)
        { Error = $"You can have up to {CategoryLibrary.MaximumCategories} categories."; return; }
        if (Categories.Any(category => string.IsNullOrWhiteSpace(category.Name)))
        { Error = "Enter a name for every category."; return; }
        if (Categories.Any(category => !CategoryLibrary.IsColor(category.ColorHex)))
        { Error = "Enter a valid #RRGGBB color for every category."; return; }
        Confirmed?.Invoke(new(Applications.Select(i => new RegisteredApplication(i.Type, i.Path, i.Version)).ToList(),
            Categories.Select(category => new ProjectCategory(category.Id, category.Name.Trim(), category.ColorHex)).ToList(),
            _originalCategories, new SourceControlSettings(SourceControlApplication.Trim(),
                SourceControlArguments, string.IsNullOrWhiteSpace(GitExecutable) ? "git" : GitExecutable.Trim())));
    }

    public void Load(JsonObject root)
    {
        Error = "";
        _originalCategories = CategoryLibrary.Read(root);
        var sourceControl = SourceControlSettings.Read(root);
        SourceControlApplication = sourceControl.Application;
        SourceControlArguments = sourceControl.Arguments;
        GitExecutable = sourceControl.GitExecutable;
        _nextCategoryId = _originalCategories.Select(category => category.Id).DefaultIfEmpty(0).Max() + 1;
        var counts = root["projects"]!.AsObject().SelectMany(group => group.Value!.AsArray())
            .GroupBy(project => project!["category"]?.GetValue<int>() ?? 0).ToDictionary(group => group.Key, group => group.Count());
        Categories.Clear();
        foreach (var category in _originalCategories)
            Categories.Add(new(this, category, counts.GetValueOrDefault(category.Id)));
        Applications.Clear();
        foreach (var application in ProjectLoaderService.ReadApplications(root))
        {
            // Installed metadata is re-read in the background by the row (never executes the application).
            Applications.Add(new(this, application));
        }
    }
}
