using System.Linq;
using OmniLauncher.Services;

namespace OmniLauncher.Models;

// User-created groups are distinct from ProjectGroup, which represents an application row.
public sealed record LibraryGroup(string Id, string Name, string ApplicationKey, int CategoryId, string ImagePath)
{
    public ProjectCategory Category { get; init; } = ProjectCategory.None;
    public int ProjectCount { get; init; }
    public string CountLabel => $"{ProjectCount} project{(ProjectCount == 1 ? "" : "s")}";
    public bool HasCategory => CategoryId != 0;
    public ProjectType ApplicationType => ApplicationRegistry.All.FirstOrDefault(application => application.Key == ApplicationKey)?.Type ?? ProjectType.Other;
    public string ApplicationName => ApplicationRegistry.All.FirstOrDefault(application => application.Key == ApplicationKey)?.Name ?? ApplicationKey;
    public bool IsCompatible(ProjectItem project) => project.ApplicationType == ApplicationType && project.CategoryId == CategoryId;
}
