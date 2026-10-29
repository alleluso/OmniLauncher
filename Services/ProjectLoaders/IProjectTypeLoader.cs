using System.Collections.Generic;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services.ProjectLoaders;

/// <summary>Reads project metadata. ApplicationRegistry owns application selection and launch policy.</summary>
public interface IProjectTypeLoader
{
    string JsonKey { get; }
    string GroupName { get; }
    ProjectType ProjectType { get; }
    string ProjectPathLabel { get; }
    string ProjectPathPlaceholder { get; }
    // A null title disables that picker. Supplying both titles enables the Browse menu.
    string? FolderPickerTitle { get; }
    string? FilePickerTitle { get; }
    IReadOnlyList<string> ProjectFilePatterns { get; }
    /// <summary>
    /// Reads metadata and, when requested, optionally supplies ProjectItem.FallbackImagePath.
    /// Leave the fallback empty if this application has none. Custom covers and persistence
    /// are owned by the library service, not the loader.
    /// </summary>
    Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage);

    /// <summary>Item shown when metadata could not be read in time (e.g. an offline network drive).</summary>
    ProjectItem CreateUnavailable(string name, string path) =>
        new() { Name = name, ProjectPath = path, ApplicationType = ProjectType };
}

