using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class GroupLibrary
{
    public static IReadOnlyList<LibraryGroup> Read(JsonObject root)
    {
        if (!root.ContainsKey("groups")) return Array.Empty<LibraryGroup>();
        if (root["groups"] is not JsonArray entries) throw new InvalidDataException("Groups must be an array.");
        var categories = CategoryLibrary.Read(root);
        var ids = new HashSet<Guid>();
        var result = new List<LibraryGroup>();
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item ||
                item["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) ||
                !Guid.TryParse(id, out var guid) || guid == Guid.Empty || !ids.Add(guid) ||
                item["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name) ||
                item["application"] is not JsonValue applicationValue || !applicationValue.TryGetValue<string>(out var application) || string.IsNullOrWhiteSpace(application) ||
                item["category"] is not JsonValue categoryValue || !categoryValue.TryGetValue<int>(out var categoryId) ||
                (categoryId != 0 && !categories.Any(category => category.Id == categoryId)) ||
                item["image"] is not JsonValue imageValue || !imageValue.TryGetValue<string>(out var image))
                throw new InvalidDataException("Each group needs a unique ID, name, application, category, and image path.");
            result.Add(new(id, name, application, categoryId, image)
            {
                Category = categories.FirstOrDefault(category => category.Id == categoryId) ?? ProjectCategory.None
            });
        }
        return result;
    }

    public static void Add(JsonObject root, string name, ProjectType type, int categoryId, string image)
    {
        ValidateName(name);
        if (categoryId != 0 && !CategoryLibrary.Read(root).Any(category => category.Id == categoryId))
            throw new InvalidOperationException("This category no longer exists. Reopen the group dialog.");
        var application = ApplicationRegistry.Get(type);
        if (root["groups"] is null) root["groups"] = new JsonArray();
        root["groups"]!.AsArray().Add(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"), ["name"] = name.Trim(),
            ["application"] = application.Key, ["category"] = categoryId, ["image"] = image.Trim()
        });
    }

    public static void Edit(JsonObject root, string id, string name, string image, ProjectType? type = null, int? categoryId = null)
    {
        ValidateName(name);
        var group = Find(root, id);
        var application = type is { } selectedType ? ApplicationRegistry.Get(selectedType).Key : group["application"]!.GetValue<string>();
        var category = categoryId ?? group["category"]!.GetValue<int>();
        if (application != group["application"]!.GetValue<string>() || category != group["category"]!.GetValue<int>())
        {
            if (root["projects"]!.AsObject().Any(applicationEntry => applicationEntry.Value!.AsArray()
                .Any(project => project!["groupId"]?.GetValue<string>() == id)))
                throw new InvalidOperationException("Application and category can only be changed when the group has no assigned projects.");
            if (category != 0 && !CategoryLibrary.Read(root).Any(item => item.Id == category))
                throw new InvalidOperationException("This category no longer exists. Reopen the group dialog.");
        }
        group["name"] = name.Trim();
        group["image"] = image.Trim();
        group["application"] = application;
        group["category"] = category;
    }

    public static void Delete(JsonObject root, string id)
    {
        var group = Find(root, id);
        root["groups"]!.AsArray().Remove(group);
        ReconcileMembership(root);
    }

    public static void SetMembership(JsonObject root, string projectId, string? groupId)
    {
        var (key, _, project) = ProjectLibraryOperations.Find(root, projectId);
        if (groupId is null)
        {
            project.Remove("groupId");
            return;
        }
        var group = Read(root).FirstOrDefault(group => group.Id == groupId)
            ?? throw new InvalidOperationException("This group no longer exists. Refresh the library.");
        if (group.ApplicationKey != key || group.CategoryId != (project["category"]?.GetValue<int>() ?? 0))
            throw new InvalidOperationException("The project and group must have the same application and category.");
        project["groupId"] = groupId;
    }

    // Run on reads and writes so external application/category edits also detach incompatible projects.
    public static bool ReconcileMembership(JsonObject root)
    {
        var groups = Read(root).ToDictionary(group => group.Id);
        var changed = false;
        foreach (var application in root["projects"]!.AsObject())
            foreach (var entry in application.Value!.AsArray())
            {
                var project = entry!.AsObject();
                if (!project.ContainsKey("groupId")) continue;
                if (project["groupId"] is not JsonValue value || !value.TryGetValue<string>(out var id))
                    throw new InvalidDataException("A project's group ID must be a string.");
                if (!groups.TryGetValue(id, out var group) || group.ApplicationKey != application.Key ||
                    group.CategoryId != (project["category"]?.GetValue<int>() ?? 0))
                    changed |= project.Remove("groupId");
            }
        return changed;
    }

    private static JsonObject Find(JsonObject root, string id) =>
        (root["groups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(group => group["id"]!.GetValue<string>() == id)
        ?? throw new InvalidOperationException("This group no longer exists. Close the dialog and refresh the library.");

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a group name.");
    }
}
