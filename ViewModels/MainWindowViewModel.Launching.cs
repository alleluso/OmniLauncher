using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public partial class MainWindowViewModel
{
    // Snapshot before launching: a history save can refresh and replace the library.
    public async Task LaunchProjectsAsync(IEnumerable<ProjectItem> projects)
    {
        if (IsWorking || _disposed) return;
        var selected = projects.DistinctBy(p => p.Id).ToArray();
        if (selected.Length == 0) return;
        IsBusy = true;
        BusyMessage = "Opening projects…";
        ErrorMessage = "";
        try
        {
            var result = await ProjectBatchLauncher.RunAsync(selected, ProjectLoaderService.RecordLaunchAsync);
            if (result.Opened > 0) await LoadProjectsAsync();
            Notice = $"Opened {result.Opened} of {result.Requested} projects";
            if (result.Errors.Count > 0) ErrorMessage = string.Join(Environment.NewLine, result.Errors);
        }
        finally { IsBusy = false; }
    }
}
