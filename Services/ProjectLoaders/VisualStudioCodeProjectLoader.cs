using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services.ProjectLoaders;

/// <summary>Opens local folders and saved Visual Studio Code workspaces.</summary>
public sealed class VisualStudioCodeProjectLoader : IProjectTypeLoader
{
    public string JsonKey => "vscode";
    public string GroupName => "Visual Studio Code";
    public ProjectType ProjectType => ProjectType.VSCode;
    public string ProjectPathLabel => "Workspace file or folder";
    public string ProjectPathPlaceholder => "Select the workspace file or folder";
    public string? FolderPickerTitle => "Select folder…";
    public string? FilePickerTitle => "Select workspace file…";
    public IReadOnlyList<string> ProjectFilePatterns => ["*.code-workspace"];

    // A folder needs no markers. Leave workspace contents (including JSON comments,
    // relative folders and remote URIs) to VS Code rather than rewriting them.
    public static bool IsProjectValid(string path) => Directory.Exists(path) ||
        (string.Equals(Path.GetExtension(path), ".code-workspace", StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    public Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage) =>
        Task.FromResult(new ProjectItem
        {
            Name = name,
            ProjectPath = path,
            ApplicationType = ProjectType.VSCode
        });
}
