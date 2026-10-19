using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using OmniLauncher.Services;

namespace OmniLauncher.Models;

public class ProjectItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private GitStatus? _gitStatus;
    public GitStatus? GitStatus
    {
        get => _gitStatus;
        set
        {
            _gitStatus = value;
            foreach (var name in new[] { nameof(GitStatus), nameof(HasGitStatus), nameof(GitBadge), nameof(GitColor) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public bool HasGitStatus => GitStatus is not null;
    public string GitBadge => GitStatus?.Badge ?? "";
    public string GitColor => GitStatus?.Color ?? "#C4C4D0";
    private bool _isFavorite;
    private int _categoryId;
    public string Id { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public IReadOnlyList<ProjectCategory> Categories { get; set; } = Array.Empty<ProjectCategory>();
    public int CategoryId
    {
        get => _categoryId;
        set
        {
            if (_categoryId == value) return;
            if (value != 0 && !Categories.Any(category => category.Id == value))
                throw new ArgumentOutOfRangeException(nameof(value), "Unknown project category.");
            _categoryId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryId)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCategory)));
        }
    }
    public ProjectCategory Category => Categories.FirstOrDefault(category => category.Id == CategoryId) ?? ProjectCategory.None;
    public bool HasCategory => CategoryId != 0;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyph)));
        }
    }
    public DateTimeOffset? AddedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public string FavoriteLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string LastOpenedLabel
    {
        get
        {
            if (LastOpenedAt is not { } opened) return "Not opened from this hub yet";
            var elapsed = DateTimeOffset.UtcNow - opened;
            if (elapsed.TotalMinutes < 1) return "Last opened just now";
            var (count, unit) = elapsed.TotalHours < 1 ? ((int)elapsed.TotalMinutes, "minute")
                : elapsed.TotalDays < 1 ? ((int)elapsed.TotalHours, "hour")
                : elapsed.TotalDays < 30 ? ((int)elapsed.TotalDays, "day")
                : elapsed.TotalDays < 365 ? ((int)(elapsed.TotalDays / 30), "month")
                : ((int)(elapsed.TotalDays / 365), "year");
            return $"Last opened {count} {unit}{(count == 1 ? "" : "s")} ago";
        }
    }

    public void RefreshLastOpenedLabel()
    {
        if (LastOpenedAt.HasValue)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastOpenedLabel)));
    }
    public ProjectType ApplicationType { get; set; } = ProjectType.Other;
    public string Name { get; set; } = string.Empty;
    /// <summary>User-selected cover; only this image path is persisted.</summary>
    public string CustomImagePath { get; set; } = string.Empty;
    /// <summary>Optional derived image supplied by the application loader.</summary>
    public string FallbackImagePath { get; set; } = string.Empty;
    public string DisplayImagePath => string.IsNullOrWhiteSpace(CustomImagePath) ? FallbackImagePath : CustomImagePath;
    public string ProjectPath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ExecutableOverride { get; set; } = string.Empty;
    public string LaunchShortcut { get; set; } = string.Empty;
    public IReadOnlyList<ProjectCommand> Commands { get; set; } = Array.Empty<ProjectCommand>();
    public string Notes { get; set; } = string.Empty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public IReadOnlyList<ProjectLink> Links { get; set; } = Array.Empty<ProjectLink>();
    public virtual string RequiredVersion { get; set; } = string.Empty;
    public bool HasExecutable => File.Exists(ExecutablePath);
    public bool IsProjectAvailable
    {
        get
        {
            try { return ApplicationRegistry.Get(ApplicationType).IsProjectValid(ProjectPath); }
            catch (Exception) { return false; }
        }
    }
    public bool CanLaunch => IsProjectAvailable && HasExecutable;
    public bool NeedsRepair => !CanLaunch;
    public string RepairLabel => !IsProjectAvailable ? "Locate project" : "Choose application";
    public string AvailabilityLabel => !IsProjectAvailable ? "Project missing or invalid" : !HasExecutable ? "Application unavailable" : "";
    public string LaunchStatus => !IsProjectAvailable
        ? "Project missing or invalid. Use Edit Project to repair its path."
        : !HasExecutable
        ? "Application missing. Open Settings to register an application, or Edit Project to choose an override."
        : string.IsNullOrWhiteSpace(ExecutableOverride) ? "Open with " + ExecutablePath : "Open with override: " + ExecutablePath;
    public virtual string ApplicationName => ApplicationRegistry.Get(ApplicationType).Name;

    public virtual void Launch()
    {
        var definition = ApplicationRegistry.Get(ApplicationType);
        definition.ValidateExecutable(ExecutablePath);
        if (!definition.IsProjectValid(ProjectPath))
            throw new InvalidOperationException("The project is missing or invalid. Use Edit Project to repair its path.");
        var start = new ProcessStartInfo { FileName = ExecutablePath, UseShellExecute = false };
        foreach (var argument in definition.LaunchArguments(this)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The application could not be started.");
    }
}
