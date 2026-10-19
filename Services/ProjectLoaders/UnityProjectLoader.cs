using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services.ProjectLoaders;

public class UnityProjectLoader : IProjectTypeLoader
{
    public string JsonKey => "unity";
    public string GroupName => "Unity";
    public ProjectType ProjectType => ProjectType.Unity;
    public string ProjectPathLabel => "Project folder";
    public string ProjectPathPlaceholder => "Select the Unity project folder…";
    public string? FolderPickerTitle => "Select the Unity project folder";
    public string? FilePickerTitle => null;
    public IReadOnlyList<string> ProjectFilePatterns => [];

    public async Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var unityVersion = await ReadEditorVersionAsync(path, cts.Token);

        return new UnityProject
        {
            Name = name,
            ProjectPath = path,
            UnityVersion = unityVersion
        };
    }

    /// <summary>
    /// Reads "ProjectSettings/ProjectVersion.txt" inside <paramref name="projectPath"/>
    /// and returns the value of the "m_EditorVersion" key.
    /// </summary>
    private static async Task<string> ReadEditorVersionAsync(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
            var lines = await File.ReadAllLinesAsync(versionFile, cancellationToken);
            foreach (var line in lines)
            {
                if (line.StartsWith("m_EditorVersion:", StringComparison.Ordinal))
                    return line["m_EditorVersion:".Length..].Trim();
            }
        }
        catch (Exception)
        {
            // Ignore I/O or parse errors for individual projects.
        }

        return "Unknown";
    }
}


