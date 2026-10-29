using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services.ProjectLoaders;

public class UnrealProjectLoader : IProjectTypeLoader
{
    public string JsonKey => "unreal";
    public string GroupName => "Unreal Engine";
    public ProjectType ProjectType => ProjectType.UnrealEngine;
    public string ProjectPathLabel => "Project folder";
    public string ProjectPathPlaceholder => "Select the Unreal Engine project folder…";
    public string? FolderPickerTitle => "Select the Unreal Engine project folder";
    public string? FilePickerTitle => null;
    public IReadOnlyList<string> ProjectFilePatterns => [];

    public ProjectItem CreateUnavailable(string name, string path) =>
        new UnrealProject { Name = name, ProjectPath = path, EngineVersion = "Unknown", UprojectFilePath = "Unknown" };

    public async Task<ProjectItem> LoadProjectAsync(string name, string path, bool loadFallbackImage)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (engineVersion, uprojectFilePath) = await ReadUnrealMetadataAsync(path, cts.Token);

        return new UnrealProject
        {
            Name = name,
            ProjectPath = path,
            EngineVersion = engineVersion,
            UprojectFilePath = uprojectFilePath
        };
    }

    /// <summary>
    /// Finds the first .uproject file in <paramref name="projectPath"/>, reads
    /// "EngineAssociation" from it, and returns both the version and the file path.
    /// </summary>
    private static async Task<(string engineVersion, string uprojectFilePath)> ReadUnrealMetadataAsync(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var uprojectFile = Directory
                .EnumerateFiles(projectPath, "*.uproject", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (uprojectFile is null)
                return ("Unknown", "Unknown");

            var content = await File.ReadAllTextAsync(uprojectFile, cancellationToken);
            var json = JsonNode.Parse(content);
            var version = json?["EngineAssociation"]?.GetValue<string>() ?? "Unknown";
            return (version, uprojectFile);
        }
        catch (Exception)
        {
            return ("Unknown", "Unknown");
        }
    }
}


