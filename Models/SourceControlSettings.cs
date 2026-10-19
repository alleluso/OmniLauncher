using System;
using System.IO;
using System.Text.Json.Nodes;

namespace OmniLauncher.Models;

public sealed record SourceControlSettings(string Application = "",
    string Arguments = "", string GitExecutable = "git")
{
    public bool Enabled => !string.IsNullOrWhiteSpace(Application);
    public bool IsTerminal => Path.GetFileNameWithoutExtension(Application).ToLowerInvariant()
        is "wt" or "windowsterminal" or "powershell" or "pwsh" or "cmd" or "git-bash";
    public static SourceControlSettings Read(JsonObject root)
    {
        var node = root["sourceControl"] as JsonObject;
        return node is null ? new() : new(
            node["application"]?.GetValue<string>() ?? "",
            node["arguments"]?.GetValue<string>() ?? "",
            node["gitExecutable"]?.GetValue<string>() ?? "git");
    }
    public JsonObject ToJson() => new()
    {
        ["application"] = Application,
        ["arguments"] = Arguments, ["gitExecutable"] = GitExecutable
    };
}

public sealed record GitStatus(string Repository, string Branch, int Changes = 0, int Conflicts = 0,
    string? Upstream = null, int Ahead = 0, int Behind = 0, string? Error = null)
{
    public string BranchLabel => Error is not null ? "Git" : Branch;
    public string StatusLabel => Error is not null ? "Unavailable" : Conflicts > 0 ? "Conflict" : Changes > 0 ? $"{Changes} changes" : "Clean";
    public string Badge => Error is not null ? "Git · unavailable" : $"{Branch} · " +
        (Conflicts > 0 ? "Conflict" : Changes > 0 ? $"{Changes} changes" : "Clean");
    public string Color => Error is not null || Conflicts > 0 ? "#FF909B" : Changes > 0 ? "#E9B95F" : "#C4C4D0";
    public bool HasError => Error is not null;
    public bool HasStatus => Error is null;
    public bool HasUpstream => Upstream is not null && HasStatus;
    public string Summary => HasError ? "Status unavailable" : Conflicts > 0 ? "Conflicts need attention" : Changes > 0 ? "Uncommitted changes" : "Working tree clean";
    public string UpstreamLabel => Upstream ?? "No upstream branch";
    public string Details => Error ?? $"Branch: {Branch}\nLocal changes: {Changes} files\nConflicts: {Conflicts}\n" +
        (Upstream is null ? "No upstream branch" : $"Upstream: {Upstream}\nOutgoing: {Ahead} · Incoming: {Behind}\nBased on last fetch; no automatic network access.");
}
