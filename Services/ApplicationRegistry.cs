using OmniLauncher.Services.ProjectLoaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

/// <summary>All integration policy for one application. Shared UI and persistence consume this contract.</summary>
public sealed record ApplicationDefinition(
    IProjectTypeLoader Loader,
    string IconPath,
    IReadOnlyList<string> ExecutableNames,
    Func<string, bool> IsProjectValid,
    Func<string, string> InferVersion,
    Func<ProjectItem, IEnumerable<string>> LaunchArguments,
    bool RequiresVersion = true,
    Func<string, string, bool>? VersionMatcher = null)
{
    public ProjectType Type => Loader.ProjectType;
    public string Key => Loader.JsonKey;
    public string Name => Loader.GroupName;

    public bool MatchesVersion(string installed, string required) => VersionMatcher?.Invoke(installed, required) ?? (!RequiresVersion ||
        (!string.IsNullOrWhiteSpace(required) && required != "Unknown" &&
         string.Equals(installed, required, StringComparison.OrdinalIgnoreCase)));

    public string ValidateExecutable(string path)
    {
        path = path.Trim();
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new InvalidOperationException("Choose an application executable that exists on this computer.");
        if (!ExecutableNames.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected executable does not match the selected application.");
        return Path.GetFullPath(path);
    }
}

public static class ApplicationRegistry
{
    // Register a loader and its application policy here. Settings, pickers, validation and launching use this list.
    public static IReadOnlyList<ApplicationDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        // Unreal Engine
        new ApplicationDefinition(new UnrealProjectLoader(), 
            "avares://OmniLauncher/Assets/unreal-engine.svg",
            ["UnrealEditor", "UE4Editor"],
            IsProjectValid: path => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.uproject").Any(),
            InferVersion: path => path.Replace('\\', '/').Split('/').FirstOrDefault(p => p.StartsWith("UE_", StringComparison.OrdinalIgnoreCase))?[3..] ?? "",
            LaunchArguments: project => new[] { Directory.EnumerateFiles(project.ProjectPath, "*.uproject").First() },
            VersionMatcher: (installed, required) => string.Equals(installed, required, StringComparison.OrdinalIgnoreCase) ||
                (Version.TryParse(installed, out var application) && Version.TryParse(required, out var project) &&
                 application.Major == project.Major && application.Minor == project.Minor)),
        
        // Unity
        new ApplicationDefinition(new UnityProjectLoader(), 
            "avares://OmniLauncher/Assets/unity.svg",
            ["Unity"],
            IsProjectValid: path => File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")),
            InferVersion: path =>
            {
                var parts = path.Replace('\\', '/').Split('/');
                var editor = Array.FindLastIndex(parts, p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase));
                return editor > 0 && char.IsDigit(parts[editor - 1].FirstOrDefault()) ? parts[editor - 1] : "";
            },
            LaunchArguments: project => new[] { "-projectPath", project.ProjectPath }),
        
        // Blender
        new ApplicationDefinition(new BlenderProjectLoader(), 
            "avares://OmniLauncher/Assets/blender.svg",
            ["blender"], 
            IsProjectValid: BlenderProjectLoader.IsProjectValid,
            InferVersion: BlenderProjectLoader.InferApplicationVersion,
            LaunchArguments: project => new[] { "--disable-autoexec", project.ProjectPath },
            VersionMatcher: BlenderProjectLoader.MatchesVersion),
        
        // Affinity
        new ApplicationDefinition(new AffinityProjectLoader(), 
            "avares://OmniLauncher/Assets/affinity.svg",
            ["Affinity"], 
            IsProjectValid: AffinityProjectLoader.IsProjectValid,
            InferVersion: _ => "",
            LaunchArguments: project => new[] { project.ProjectPath },
            RequiresVersion: false),
        
        // VScode
        new ApplicationDefinition(new VisualStudioCodeProjectLoader(), 
            "avares://OmniLauncher/Assets/vscode.svg",
            ["Code"], 
            IsProjectValid: VisualStudioCodeProjectLoader.IsProjectValid,
            InferVersion: _ => "",
            LaunchArguments: project => new[] { "--new-window", project.ProjectPath },
            RequiresVersion: false)
    });

    public static ApplicationDefinition Get(ProjectType type) => All.FirstOrDefault(p => p.Type == type)
        ?? throw new InvalidOperationException("This application is not supported yet.");
    public static ApplicationDefinition Get(string key) => All.FirstOrDefault(p => p.Key == key)
        ?? throw new InvalidOperationException("This application is not supported yet.");
}


