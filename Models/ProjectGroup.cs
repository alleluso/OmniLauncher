using System.Collections.ObjectModel;

namespace OmniLauncher.Models;

public class ProjectGroup
{
    public string GroupName { get; set; } = string.Empty;
    public ProjectType ProjectType { get; set; }
    public ObservableCollection<ProjectItem> Projects { get; set; } = new();
    public ObservableCollection<object> Entries { get; } = new();
}
