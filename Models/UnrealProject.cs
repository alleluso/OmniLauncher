namespace OmniLauncher.Models;

public class UnrealProject : ProjectItem
{
    public UnrealProject() => ApplicationType = ProjectType.UnrealEngine;
    public string EngineVersion { get; set; } = string.Empty;
    public string UprojectFilePath { get; set; } = string.Empty;
    public override string RequiredVersion { get => EngineVersion; set => EngineVersion = value; }
    public override string ApplicationName => $"UE {EngineVersion}";
}
