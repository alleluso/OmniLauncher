namespace OmniLauncher.Models;

public class UnityProject : ProjectItem
{
    public UnityProject() => ApplicationType = ProjectType.Unity;
    public string UnityVersion { get; set; } = string.Empty;
    public override string RequiredVersion { get => UnityVersion; set => UnityVersion = value; }
    public override string ApplicationName => $"Unity {UnityVersion}";
}
