using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class ProjectCommandRunner
{
    public static void Run(ProjectItem project, ProjectCommand command)
    {
        if (!Path.IsPathFullyQualified(command.ExecutablePath) || !File.Exists(command.ExecutablePath))
            throw new InvalidOperationException("The command's application is missing. Choose it again in Manage commands.");

        var directory = Directory.Exists(project.ProjectPath) ? project.ProjectPath
            : File.Exists(project.ProjectPath) ? Path.GetDirectoryName(project.ProjectPath) : null;
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The project path is missing. Locate it in Edit project first.");

        var start = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = directory,
            UseShellExecute = true
        };

        // Known terminals accept command text; other applications receive raw arguments.
        var application = Path.GetFileNameWithoutExtension(command.ExecutablePath).ToLowerInvariant();
        if (OperatingSystem.IsWindows() && application is "powershell" or "pwsh" or "wt" or "windowsterminal")
        {
            if (application is "wt" or "windowsterminal")
            {
                start.ArgumentList.Add("-d");
                start.ArgumentList.Add(directory);
                start.ArgumentList.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"));
            }
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-EncodedCommand");
            var script = string.IsNullOrWhiteSpace(command.Arguments) ? "exit" : command.Arguments;
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        }
        else if (OperatingSystem.IsWindows() && application == "cmd")
            start.Arguments = "/D /S /C \"" + command.Arguments + "\"";
        else
            start.Arguments = command.Arguments;

        using var process = Process.Start(start);
    }
}
