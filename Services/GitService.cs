using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class GitService
{
    // A .git file is also valid (linked worktrees and submodules).
    public static string? FindRepository(string projectPath)
    {
        var directory = File.Exists(projectPath) ? Path.GetDirectoryName(projectPath) : projectPath;
        if (!Directory.Exists(directory)) return null;
        for (var current = new DirectoryInfo(directory!); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
        return null;
    }

    public static async Task<GitStatus?> ReadAsync(string projectPath, SourceControlSettings settings, CancellationToken token = default)
    {
        if (!settings.Enabled) return null;
        string? repository = null;
        try
        {
            repository = FindRepository(projectPath);
            if (repository is null) return null;
            var start = new ProcessStartInfo(settings.GitExecutable)
            {
                WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "--no-optional-locks", "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all" })
                start.ArgumentList.Add(arg);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(start) ?? throw new IOException("Could not start Git.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await Task.WhenAll(output, error);
                token.ThrowIfCancellationRequested();
                throw new IOException("Git status timed out. Refresh to try again.");
            }
            if (process.ExitCode != 0) throw new IOException((await error).Trim());
            return Parse(repository, await output);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(repository ?? "", "", Error: $"Git status unavailable. Check the Git executable in Settings.\n{ex.Message}");
        }
    }

    public static GitStatus Parse(string repository, string output)
    {
        string branch = "Unborn branch", oid = "", upstream = "";
        int changes = 0, conflicts = 0, ahead = 0, behind = 0;
        var records = output.Split('\0');
        for (int i = 0; i < records.Length; i++)
        {
            var line = records[i];
            if (line.StartsWith("# branch.head ")) branch = line[14..];
            else if (line.StartsWith("# branch.oid ")) oid = line[13..];
            else if (line.StartsWith("# branch.upstream ")) upstream = line[18..];
            else if (line.StartsWith("# branch.ab "))
            {
                var counts = line[12..].Split(' ');
                if (counts.Length == 2) { int.TryParse(counts[0].TrimStart('+'), out ahead); int.TryParse(counts[1].TrimStart('-'), out behind); }
            }
            else if (line.Length > 1 && "12u?".Contains(line[0]) && line[1] == ' ')
            {
                changes++;
                if (line[0] == 'u') conflicts++;
                if (line[0] == '2') i++; // Rename/copy records include the original path as a second NUL field.
            }
        }
        if (branch == "(detached)") branch = "Detached " + oid[..Math.Min(7, oid.Length)];
        return new(repository, branch, changes, conflicts, upstream.Length == 0 ? null : upstream, ahead, behind);
    }

    public static ProcessStartInfo CreateLaunchInfo(SourceControlSettings settings, string repository)
    {
        if (!settings.Enabled) throw new InvalidOperationException("Choose a source-control application in Settings.");
        if (!Directory.Exists(repository)) throw new DirectoryNotFoundException("The repository directory is unavailable.");
        var start = new ProcessStartInfo(settings.Application) { WorkingDirectory = repository, UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(settings.Arguments))
        {
            foreach (var argument in settings.Arguments.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                start.ArgumentList.Add(argument.Replace("{repository}", repository, StringComparison.Ordinal));
        }
        else if (!settings.IsTerminal) start.ArgumentList.Add(repository);
        else
        {
            var name = Path.GetFileNameWithoutExtension(settings.Application).ToLowerInvariant();
            if (name is "wt" or "windowsterminal")
            {
                start.ArgumentList.Add("-d"); start.ArgumentList.Add(repository);
            }
            else if (name == "git-bash") start.ArgumentList.Add("--cd=" + repository);
            else if (name is "powershell" or "pwsh") start.ArgumentList.Add("-NoExit");
        }
        return start;
    }
}
