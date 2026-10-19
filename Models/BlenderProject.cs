namespace OmniLauncher.Models;

public class BlenderProject : ProjectItem
{
    public BlenderProject() => ApplicationType = ProjectType.Blender;
    public string BlenderVersion { get; set; } = string.Empty;
    public override string RequiredVersion { get => BlenderVersion; set => BlenderVersion = value; }
    public override string ApplicationName => $"Blender {BlenderVersion}";
}
