using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public sealed record ProjectBatchResult(int Requested, int Opened, IReadOnlyList<string> Errors);

public static class ProjectBatchLauncher
{
    public static async Task<ProjectBatchResult> RunAsync(IEnumerable<ProjectItem> projects,
        Func<ProjectItem, DateTimeOffset, Task> recordLaunch)
    {
        var selected = projects.DistinctBy(p => p.Id).ToArray();
        var errors = new List<string>();
        var opened = new List<(ProjectItem Project, DateTimeOffset At)>();
        foreach (var project in selected)
        {
            try { project.Launch(); opened.Add((project, DateTimeOffset.UtcNow)); }
            catch (Exception ex) { errors.Add($"{project.Name}: {ex.Message}"); }
        }
        foreach (var (project, at) in opened)
        {
            try { await recordLaunch(project, at); }
            catch (Exception ex) { errors.Add($"{project.Name} opened, but history could not be saved: {ex.Message}"); }
        }
        return new ProjectBatchResult(selected.Length, opened.Count, errors);
    }
}
