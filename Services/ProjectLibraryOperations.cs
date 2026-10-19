using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class ProjectLibraryOperations
{
    public static string ReadNotes(JsonObject project)
    {
        if (!project.ContainsKey("notes")) return "";
        if (project["notes"] is JsonValue value && value.TryGetValue<string>(out var notes)) return notes;
        throw new InvalidDataException("Project notes must be text.");
    }

    public static IReadOnlyList<ProjectLink> ReadLinks(JsonObject project)
    {
        if (!project.ContainsKey("links")) return Array.Empty<ProjectLink>();
        if (project["links"] is not JsonArray entries) throw new InvalidDataException("Project links must be an array.");
        var links = new List<ProjectLink>();
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item ||
                item["name"] is not JsonValue name || !name.TryGetValue<string>(out var label) || string.IsNullOrWhiteSpace(label) ||
                item["url"] is not JsonValue url || !url.TryGetValue<string>(out var address))
                throw new InvalidDataException("Each project link needs a name and URL.");
            try { ProjectLinkLauncher.ValidateUrl(address); }
            catch (ArgumentException ex) { throw new InvalidDataException("A project link has an invalid URL.", ex); }
            links.Add(new ProjectLink(label, address));
        }
        return links;
    }

    public static void SetNotesAndLinks(JsonObject root, string id, string notes, IReadOnlyList<ProjectLink> links)
    {
        var entries = new JsonArray();
        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link.Name)) throw new InvalidOperationException("Give each link a name.");
            entries.Add(new JsonObject { ["name"] = link.Name.Trim(), ["url"] = ProjectLinkLauncher.ValidateUrl(link.Url) });
        }
        var item = Find(root, id).Item;
        item["notes"] = notes;
        item["links"] = entries;
    }

    public static void SetLaunchShortcut(JsonObject root, string id, string shortcut) =>
        Find(root, id).Item["launchShortcut"] = LaunchShortcut.Normalize(shortcut);

    public static IReadOnlyList<ProjectCommand> ReadCommands(JsonObject project)
    {
        if (!project.ContainsKey("commands")) return Array.Empty<ProjectCommand>();
        if (project["commands"] is not JsonArray entries)
            throw new InvalidDataException("Project commands must be an array.");
        var commands = new List<ProjectCommand>();
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item ||
                item["name"] is not JsonValue name || !name.TryGetValue<string>(out var label) || string.IsNullOrWhiteSpace(label) ||
                item["executablePath"] is not JsonValue executable || !executable.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path) ||
                item["arguments"] is not JsonValue arguments || !arguments.TryGetValue<string>(out var text))
                throw new InvalidDataException("Each project command needs a name, application path, and arguments string.");
            commands.Add(new ProjectCommand(label, path, text));
        }
        return commands;
    }

    public static void SetCommands(JsonObject root, string id, IReadOnlyList<ProjectCommand> commands)
    {
        var entries = new JsonArray();
        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.ExecutablePath))
                throw new InvalidOperationException("Give each command a name and select an application.");
            entries.Add(new JsonObject
            {
                ["name"] = command.Name.Trim(), ["executablePath"] = command.ExecutablePath,
                ["arguments"] = command.Arguments
            });
        }
        Find(root, id).Item["commands"] = entries;
    }

    public static void Add(JsonObject root, string key, string name, string path, string image, int categoryId = 0)
    {
        path = Validate(key, name, path);
        CheckDuplicate(root, key, path, null);
        if (categoryId != 0 && !CategoryLibrary.Read(root).Any(category => category.Id == categoryId))
            throw new InvalidOperationException("This category no longer exists. Refresh the library.");
        var projects = root["projects"]!.AsObject();
        if (projects[key] is null) projects[key] = new JsonArray();
        projects[key]!.AsArray().Add(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"), ["name"] = name.Trim(),
            ["path"] = path, ["image"] = image.Trim(),
            ["isFavorite"] = false,
            ["category"] = categoryId,
            ["addedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["lastOpenedAt"] = null
        });
    }

    public static void Edit(JsonObject root, string id, string name, string path, string image)
    {
        var (key, _, item) = Find(root, id);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a project name.");
        // Existing missing projects and duplicate paths can still be renamed or removed.
        if (!PathsEqual(item["path"]!.GetValue<string>(), path))
        {
            path = Validate(key, name, path);
            CheckDuplicate(root, key, path, id);
        }
        item["name"] = name.Trim();
        item["path"] = path.Trim();
        item["image"] = image.Trim();
    }

    public static void Delete(JsonObject root, string id)
    {
        var (_, entries, item) = Find(root, id);
        entries.Remove(item);
    }

    public static void SetFavorite(JsonObject root, string id, bool favorite) =>
        Find(root, id).Item["isFavorite"] = favorite;

    public static void SetCategory(JsonObject root, string id, int categoryId)
    {
        if (categoryId != 0 && !CategoryLibrary.Read(root).Any(category => category.Id == categoryId))
            throw new InvalidOperationException("This category no longer exists. Refresh the library.");
        Find(root, id).Item["category"] = categoryId;
        GroupLibrary.ReconcileMembership(root);
    }

    public static void RecordLaunch(JsonObject root, string id, DateTimeOffset openedAt) =>
        Find(root, id).Item["lastOpenedAt"] = openedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static (string Key, JsonArray Entries, JsonObject Item) Find(JsonObject root, string id)
    {
        foreach (var group in root["projects"]!.AsObject())
            foreach (var entry in group.Value!.AsArray())
                if (entry!["id"]!.GetValue<string>() == id)
                    return (group.Key, group.Value.AsArray(), entry.AsObject());
        throw new InvalidOperationException("This project is no longer in the library. Close the dialog and refresh the library.");
    }

    private static string Validate(string key, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a project name.");
        var definition = ApplicationRegistry.Get(key);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw new InvalidOperationException("Select an existing project path.");
        path = NormalizePath(path);
        if (!definition.IsProjectValid(path))
            throw new InvalidOperationException("The selected path is not a valid project for this application.");
        return path;
    }

    private static void CheckDuplicate(JsonObject root, string key, string path, string? excludingId)
    {
        if (root["projects"]![key] is not JsonArray entries) return;
        if (entries.Any(entry => entry!["id"]!.GetValue<string>() != excludingId &&
            PathsEqual(entry["path"]!.GetValue<string>(), path)))
            throw new InvalidOperationException("This project folder is already in the library for this application.");
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(NormalizePath(left), NormalizePath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}


