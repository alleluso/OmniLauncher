using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

/// <summary>Owns library persistence. Never replaces an unreadable library with an empty one.</summary>
public sealed class ProjectLibraryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _hasLoaded;
    public string ConfigPath { get; }
    public string BackupPath => ConfigPath + ".bak";

    public ProjectLibraryStore(string configPath)
    {
        ConfigPath = Path.GetFullPath(configPath);
    }

    public Task<JsonObject> ReadAsync() => AccessAsync(null);

    public async Task UpdateAsync(Action<JsonObject> update) => await AccessAsync(update);

    private async Task<JsonObject> AccessAsync(Action<JsonObject>? update)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            // Also serialize read/modify/write across running application instances.
            using var fileLock = await AcquireLockAsync();
            var exists = File.Exists(ConfigPath);
            if (!exists && (_hasLoaded || File.Exists(BackupPath)))
                throw new InvalidDataException("The library file is missing. Restore it from a backup, then refresh. The current library has not been replaced.");
            var root = exists ? Parse(await File.ReadAllTextAsync(ConfigPath)) : CreateLibrary();
            var seedCategories = !root.ContainsKey("categories");
            if (seedCategories) root["categories"] = CategoryLibrary.Serialize(ProjectCategory.CreateDefaults());
            Validate(root);
            var membershipChanged = GroupLibrary.ReconcileMembership(root);
            if (update is not null)
            {
                update(root);
                Validate(root);
                GroupLibrary.ReconcileMembership(root);
            }
            if (!exists || seedCategories || membershipChanged || update is not null)
                await SaveAsync(root);
            _hasLoaded = true;
            return root;
        }
        finally { _gate.Release(); }
    }

    private async Task<FileStream> AcquireLockAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(ConfigPath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 40) { await Task.Delay(50); }
        }
    }

    private static JsonObject Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidDataException("The library must contain a JSON object. The original file has been kept.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The library contains invalid JSON. The original file has been kept; repair it or restore a backup.", ex);
        }
    }

    private static JsonObject CreateLibrary() => new()
    {
        ["schemaVersion"] = 1,
        ["projects"] = new JsonObject(),
        ["applications"] = new JsonArray()
    };

    private static void Validate(JsonObject root)
    {
        var categoryIds = new HashSet<int>();
        foreach (var category in CategoryLibrary.Read(root)) categoryIds.Add(category.Id);
        _ = GroupLibrary.Read(root);
        if (root["projects"] is not JsonObject)
            throw new InvalidDataException("The library 'projects' section must be an object. The original file has been kept.");
        if (root["schemaVersion"] is not JsonValue version ||
            !version.TryGetValue<int>(out var number) || number != 1)
            throw new InvalidDataException("The library must use the current schema version (1). Older formats are not supported.");
        if (root.ContainsKey("applications"))
        {
            if (root["applications"] is not JsonArray applications)
                throw new InvalidDataException("Applications must be an array.");
            var indexes = new HashSet<int>();
            foreach (var entry in applications)
            {
                if (entry is not JsonObject item ||
                    !IsString(item["application"]) || string.IsNullOrWhiteSpace(item["application"]!.GetValue<string>()) ||
                    !IsString(item["path"]) || string.IsNullOrWhiteSpace(item["path"]!.GetValue<string>()) ||
                    !IsString(item["version"]))
                    throw new InvalidDataException("Each application entry must contain an application key, executable path, and version string.");
                if (item["index"] is not JsonValue indexValue || !indexValue.TryGetValue<int>(out var index) ||
                    index < 0 || !indexes.Add(index))
                    throw new InvalidDataException("Each application must have a unique, non-negative integer index.");
            }
        }
        var ids = new HashSet<Guid>();
        foreach (var group in root["projects"]!.AsObject())
        {
            if (group.Value is not JsonArray entries)
                throw new InvalidDataException($"Projects for '{group.Key}' must be an array.");
            foreach (var entry in entries)
            {
                if (entry is not JsonObject item || !IsString(item["name"]) || !IsString(item["path"]) ||
                    (item.ContainsKey("image") && !IsString(item["image"])) ||
                    (item.ContainsKey("executableOverride") && !IsString(item["executableOverride"])))
                    throw new InvalidDataException($"A '{group.Key}' project has invalid name, path, or image data.");
                if (!IsString(item["id"]) || !Guid.TryParse(item["id"]!.GetValue<string>(), out var id) ||
                    id == Guid.Empty || !ids.Add(id))
                    throw new InvalidDataException("Each project must have a valid, unique ID. Automatic ID repair is not supported.");
                ProjectLibraryOperations.ReadCommands(item);
                ProjectLibraryOperations.ReadNotes(item);
                ProjectLibraryOperations.ReadLinks(item);
                if (item.ContainsKey("isFavorite") &&
                    (item["isFavorite"] is not JsonValue favorite || !favorite.TryGetValue<bool>(out _)))
                    throw new InvalidDataException("A project's favorite flag must be true or false.");
                if (item.ContainsKey("category") &&
                    (item["category"] is not JsonValue category || !category.TryGetValue<int>(out var categoryId) ||
                     (categoryId != 0 && !categoryIds.Contains(categoryId))))
                    throw new InvalidDataException("A project's category must be 0 or the ID of an existing category.");
                foreach (var key in new[] { "addedAt", "lastOpenedAt" })
                    if (item[key] is { } timestamp && (!IsString(timestamp) ||
                        !DateTimeOffset.TryParseExact(timestamp.GetValue<string>(), "O", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out _)))
                        throw new InvalidDataException($"A project's {key} must be an ISO timestamp or null.");
            }
        }

    }

    private static bool IsString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out _);

    private async Task SaveAsync(JsonObject root)
    {
        var temporaryPath = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(ConfigPath))
                File.Replace(temporaryPath, ConfigPath, BackupPath);
            else
                File.Move(temporaryPath, ConfigPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}




