using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class CategoryLibrary
{
    public const int MaximumCategories = 20;

    public static JsonArray Serialize(IEnumerable<ProjectCategory> categories) => new(categories.Select(category =>
        (JsonNode)new JsonObject { ["id"] = category.Id, ["name"] = category.Name.Trim(), ["color"] = category.Color }).ToArray());

    public static IReadOnlyList<ProjectCategory> Read(JsonObject root)
    {
        if (root["categories"] is not JsonArray entries)
            throw new InvalidDataException("Categories must be an array.");
        var result = new List<ProjectCategory>();
        var ids = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item || item["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<int>(out var id) || id <= 0 || !ids.Add(id) ||
                item["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name) ||
                item["color"] is not JsonValue colorValue || !colorValue.TryGetValue<string>(out var color) || !IsColor(color))
                throw new InvalidDataException("Each category needs a unique positive ID, a name, and a #RRGGBB color.");
            result.Add(new(id, name, color));
        }
        return result;
    }

    public static bool IsColor(string? color) => color is not null && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");

    public static void Replace(JsonObject root, IReadOnlyList<ProjectCategory> categories)
    {
        if (categories.Count > MaximumCategories)
            throw new InvalidDataException($"You can have up to {MaximumCategories} categories.");
        var entries = Serialize(categories);
        // Validate before modifying the library or its project assignments.
        _ = Read(new JsonObject { ["categories"] = entries.DeepClone() });
        var ids = categories.Select(category => category.Id).ToHashSet();
        // Category removal clears memberships before both projects and groups become uncategorized.
        if (root["groups"] is JsonArray groups)
        {
            var removedGroups = new HashSet<string>();
            foreach (var group in groups.OfType<JsonObject>())
            {
                var categoryId = group["category"]!.GetValue<int>();
                if (categoryId != 0 && !ids.Contains(categoryId))
                {
                    group["category"] = 0;
                    removedGroups.Add(group["id"]!.GetValue<string>());
                }
            }
            foreach (var application in root["projects"]!.AsObject())
                foreach (var project in application.Value!.AsArray().OfType<JsonObject>())
                    if (project["groupId"] is JsonValue groupId && removedGroups.Contains(groupId.GetValue<string>()))
                        project.Remove("groupId");
        }
        foreach (var group in root["projects"]!.AsObject())
            foreach (var project in group.Value!.AsArray())
                if (project!["category"] is JsonValue value && value.TryGetValue<int>(out var id) && id != 0 && !ids.Contains(id))
                    project["category"] = 0;
        root["categories"] = entries;
    }
}
