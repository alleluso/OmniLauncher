using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public partial class MainWindowViewModel
{
    private SourceControlSettings _sourceControl = new();
    private CancellationTokenSource? _gitRefreshCts;
    public string SourceControlOpenLabel => _sourceControl.IsTerminal ? "Open in terminal" : "Open in Git client";

    public async Task RefreshGitStatusAsync()
    {
        if (_disposed || _librarySnapshot is null) return;
        _gitRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _gitRefreshCts = cts;
        var projects = _masterGroups.SelectMany(group => group.Projects).ToArray();
        try
        {
            _sourceControl = SourceControlSettings.Read(_librarySnapshot);
            var settings = _sourceControl;
            // Share results between projects in one repository; bound subprocess concurrency.
            var cache = new Dictionary<string, GitStatus?>();
            foreach (var project in projects)
            {
                cts.Token.ThrowIfCancellationRequested();
                GitStatus? status = null;
                if (settings.Enabled)
                {
                    try
                    {
                        // An offline drive must not stall the UI or the other projects' status.
                        var key = await Task.Run(() => GitService.FindRepository(project.ProjectPath) ?? project.ProjectPath)
                            .WaitAsync(TimeSpan.FromSeconds(3), cts.Token);
                        if (!cache.TryGetValue(key, out status))
                        {
                            status = await Task.Run(() => GitService.ReadAsync(project.ProjectPath, settings, cts.Token), cts.Token);
                            cache[key] = status;
                        }
                    }
                    catch (TimeoutException) { status = null; }
                }
                if (!_disposed && !cts.IsCancellationRequested) project.GitStatus = status;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportActionError("Could not refresh source control", ex); }
        finally
        {
            if (ReferenceEquals(_gitRefreshCts, cts)) _gitRefreshCts = null;
            cts.Dispose();
        }
    }

    public void OpenSourceControl(ProjectItem project)
    {
        try
        {
            var repository = GitService.FindRepository(project.ProjectPath)
                ?? throw new InvalidOperationException("This project is no longer in a Git repository.");
            using var process = Process.Start(GitService.CreateLaunchInfo(_sourceControl, repository));
        }
        catch (Exception ex) { ReportActionError("Could not open source control", ex); }
    }
}
