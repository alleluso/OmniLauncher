using OmniLauncher.Services.ProjectLoaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class ProjectLoaderService
{
    private static readonly IReadOnlyList<IProjectTypeLoader> Loaders =
    ApplicationRegistry.All.Select(application => application.Loader).ToArray();

    public static IReadOnlyList<ProjectType> SupportedTypes { get; } =
        Array.AsReadOnly(Loaders.OrderBy(loader => loader.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .Select(loader => loader.ProjectType).ToArray());

    private static readonly ProjectLibraryStore Store = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniLauncher", "projects.json"));

    public static string ConfigPath => Store.ConfigPath;

    public static Task<JsonObject> ReadLibraryAsync() => Store.ReadAsync();

    public static Task SaveDisplayPreferencesAsync(bool isListView, int tileSize) => Store.UpdateAsync(root =>
    {
        var preferences = root["displayPreferences"] as JsonObject ?? new JsonObject();
        preferences["view"] = isListView ? "list" : "tiles";
        preferences["tileSize"] = Math.Clamp(tileSize, 0, 2);
        if (preferences.Parent is null) root["displayPreferences"] = preferences;
    });

    public static async Task<List<ProjectGroup>> LoadProjectGroupsAsync(JsonObject? root = null)
    {
        root ??= await Store.ReadAsync();
        var applications = ReadApplications(root);
        var categories = CategoryLibrary.Read(root);
        var groups = new List<ProjectGroup>();
        foreach (var loader in Loaders)
        {
            var items = new List<ProjectItem>();
            if (root["projects"]![loader.JsonKey] is not JsonArray entries) continue;
            // Read every project concurrently on the thread pool. Windows file calls on an offline
            // network/removable drive block for a long time and ignore cancellation tokens, so each
            // read gets a hard deadline; unreachable projects still appear and can be repaired.
            var loads = entries.Select(entry =>
            {
                var name = entry!["name"]!.GetValue<string>();
                var path = entry["path"]!.GetValue<string>();
                var noImage = string.IsNullOrWhiteSpace(entry["image"]?.GetValue<string>());
                return LoadWithDeadlineAsync(loader, name, path, noImage);
            }).ToArray();
            var loaded = await Task.WhenAll(loads);
            var candidates = applications.Where(i => i.Type == loader.ProjectType).ToArray();
            var found = await Task.WhenAll(candidates.Select(i => PathProbe.FileExistsAsync(i.Path)));
            var executables = candidates.Select((application, i) => (Application: application, Exists: found[i])).ToArray();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var customImagePath = entry!["image"]?.GetValue<string>() ?? "";
                var item = loaded[index];
                item.CustomImagePath = customImagePath;
                item.Id = entry["id"]!.GetValue<string>();
                item.GroupId = entry["groupId"]?.GetValue<string>();
                item.IsFavorite = entry["isFavorite"]?.GetValue<bool>() ?? false;
                item.Categories = categories;
                item.CategoryId = entry["category"]?.GetValue<int>() ?? 0;
                item.AddedAt = ReadTimestamp(entry["addedAt"]);
                item.LastOpenedAt = ReadTimestamp(entry["lastOpenedAt"]);
                item.ExecutableOverride = entry["executableOverride"]?.GetValue<string>() ?? "";
                item.LaunchShortcut = entry["launchShortcut"]?.GetValue<string>() ?? "";
                item.Commands = ProjectLibraryOperations.ReadCommands(entry.AsObject());
                item.Notes = ProjectLibraryOperations.ReadNotes(entry.AsObject());
                item.Links = ProjectLibraryOperations.ReadLinks(entry.AsObject());
                item.ApplicationType = loader.ProjectType;
                var definition = ApplicationRegistry.Get(loader.ProjectType);
                item.ExecutablePath = !string.IsNullOrWhiteSpace(item.ExecutableOverride)
                    ? item.ExecutableOverride
                    : executables.FirstOrDefault(i => i.Exists &&
                        definition.MatchesVersion(i.Application.Version, item.RequiredVersion)).Application?.Path ?? "";
                items.Add(item);
            }
            // Every project and executable path is checked in the background before the items reach the UI.
            await Task.WhenAll(items.Select(item => item.RefreshAvailabilityAsync()));
            if (items.Count > 0)
                groups.Add(new ProjectGroup { GroupName = loader.GroupName, ProjectType = loader.ProjectType, Projects = new(items) });
        }
        return groups;
    }

    private static readonly TimeSpan ProjectTimeout = TimeSpan.FromSeconds(6);

    private static async Task<ProjectItem> LoadWithDeadlineAsync(IProjectTypeLoader loader, string name, string path, bool loadFallbackImage)
    {
        try
        {
            return await Task.Run(() => loader.LoadProjectAsync(name, path, loadFallbackImage)).WaitAsync(ProjectTimeout);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Project '{name}' unavailable: {ex.Message}");
            return loader.CreateUnavailable(name, path);
        }
    }


    public static Task AddProjectAsync(ProjectType type, string name, string path, string image, int categoryId = 0, string? groupId = null)
    {
        var key = Loaders.FirstOrDefault(loader => loader.ProjectType == type)?.JsonKey
            ?? throw new InvalidOperationException("This project type is not supported yet.");
        return Store.UpdateAsync(root =>
        {
            ProjectLibraryOperations.Add(root, key, name, path, image, categoryId);
            if (groupId is not null)
            {
                var added = root["projects"]![key]!.AsArray().Last()!;
                GroupLibrary.SetMembership(root, added["id"]!.GetValue<string>(), groupId);
            }
        });
    }

    public static Task AddGroupAsync(string name, ProjectType type, int categoryId, string image) =>
        Store.UpdateAsync(root => GroupLibrary.Add(root, name, type, categoryId, image));
    public static Task EditGroupAsync(string id, string name, string image, ProjectType? type = null, int? categoryId = null) =>
        Store.UpdateAsync(root => GroupLibrary.Edit(root, id, name, image, type, categoryId));
    public static Task DeleteGroupAsync(string id) => Store.UpdateAsync(root => GroupLibrary.Delete(root, id));
    public static Task SetProjectGroupAsync(string projectId, string? groupId) =>
        Store.UpdateAsync(root => GroupLibrary.SetMembership(root, projectId, groupId));

    internal static void CleanupUnusedThumbnails(JsonObject root, IEnumerable<ProjectGroup> groups, string? cacheDirectory = null)
    {
        var directory = Path.GetFullPath(cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniLauncher", "Thumbnails"));
        var retained = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        void Retain(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { retained.Add(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
        }
        foreach (var project in groups.SelectMany(group => group.Projects))
        {
            Retain(project.FallbackImagePath);
            Retain(project.CustomImagePath);
        }
        // Protect saved custom covers, including integrations not currently registered.
        foreach (var group in GroupLibrary.Read(root)) Retain(group.ImagePath);
        foreach (var group in root["projects"]!.AsObject())
            foreach (var entry in group.Value!.AsArray())
                Retain(entry!["image"]?.GetValue<string>());

        try
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                // Only the SHA-256-named PNG/BMP files produced by our loaders belong to this cache.
                // Do not touch unrelated files, subdirectories, links or in-progress temporary writes.
                var name = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                if (name.Length != 64 || !name.All(Uri.IsHexDigit) ||
                    (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                     !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)) || retained.Contains(path)) continue;
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A locked cache file can be retried on the next successful library load.
                    System.Diagnostics.Debug.WriteLine($"Could not remove thumbnail: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache maintenance must never interrupt library refresh.
            System.Diagnostics.Debug.WriteLine($"Could not clean thumbnail cache: {ex.Message}");
        }
    }

    public static Task EditProjectAsync(ProjectItem original, string name, string path, string image, string executableOverride, string launchShortcut = "") =>
                Store.UpdateAsync(root =>
        {
            var type = original.ApplicationType;
            executableOverride = executableOverride.Trim();
            if (executableOverride.Length > 0 && executableOverride != original.ExecutableOverride)
                executableOverride = ApplicationPaths.Validate(type, executableOverride);
            ProjectLibraryOperations.Edit(root, original.Id, name, path, image);
            ProjectLibraryOperations.SetLaunchShortcut(root, original.Id, launchShortcut);
            foreach (var group in root["projects"]!.AsObject())
                foreach (var entry in group.Value!.AsArray())
                    if (entry!["id"]!.GetValue<string>() == original.Id)
                    {
                        entry["executableOverride"] = executableOverride;
                    }
        });

    public static Task SaveProjectCommandsAsync(string projectId, IReadOnlyList<ProjectCommand> commands) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.SetCommands(root, projectId, commands));

    public static Task SaveProjectNotesAndLinksAsync(string projectId, string notes, IReadOnlyList<ProjectLink> links) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.SetNotesAndLinks(root, projectId, notes, links));

    public static Task DeleteProjectAsync(ProjectItem project) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.Delete(root, project.Id));
    public static Task SetFavoriteAsync(ProjectItem project, bool favorite) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.SetFavorite(root, project.Id, favorite));
    public static Task SetCategoryAsync(ProjectItem project, int categoryId) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.SetCategory(root, project.Id, categoryId));
    public static Task RecordLaunchAsync(ProjectItem project, DateTimeOffset openedAt) =>
        Store.UpdateAsync(root => ProjectLibraryOperations.RecordLaunch(root, project.Id, openedAt));

    private static DateTimeOffset? ReadTimestamp(JsonNode? node) => node is null ? null
        : DateTimeOffset.ParseExact(node.GetValue<string>(), "O", CultureInfo.InvariantCulture);

    public static async Task<List<RegisteredApplication>> LoadApplicationsAsync() => ReadApplications(await Store.ReadAsync());

    public static List<RegisteredApplication> ReadApplications(JsonObject root)
    {
        var result = new List<RegisteredApplication>();
        if (root["applications"] is not JsonArray applications) return result;
        foreach (var entry in applications.OrderBy(entry => entry!["index"]!.GetValue<int>()))
        {
            var loader = Loaders.FirstOrDefault(loader => loader.JsonKey == entry!["application"]!.GetValue<string>());
            if (loader is not null)
                result.Add(new RegisteredApplication(loader.ProjectType,
                    entry!["path"]!.GetValue<string>(), entry["version"]!.GetValue<string>()));
        }
        return result;
    }

    public static Task SaveSettingsAsync(IReadOnlyList<RegisteredApplication> applications,
        IReadOnlyList<ProjectCategory> categories, IReadOnlyList<ProjectCategory> originalCategories, SourceControlSettings? sourceControl = null) => Store.UpdateAsync(root =>
    {
        if (!CategoryLibrary.Read(root).SequenceEqual(originalCategories))
            throw new InvalidOperationException("Categories changed while Settings was open. Reopen Settings and try again.");
        CategoryLibrary.Replace(root, categories);
        if (sourceControl is not null) root["sourceControl"] = sourceControl.ToJson();
        var existing = ReadApplications(root);
        foreach (var item in applications)
        {
            if (!SupportedTypes.Contains(item.Type)) throw new InvalidOperationException("Unsupported application type.");
            if (string.IsNullOrWhiteSpace(item.Path)) throw new InvalidOperationException("Choose an executable for each application, or remove the empty row.");
            if (!existing.Any(old => old.Type == item.Type && old.Path == item.Path)) ApplicationPaths.Validate(item.Type, item.Path);
        }
        if (applications.GroupBy(i => (i.Type, OperatingSystem.IsWindows() ? i.Path.ToUpperInvariant() : i.Path)).Any(g => g.Count() > 1))
            throw new InvalidOperationException("The same application executable is listed more than once.");
        var entries = new JsonArray();
        foreach (var application in applications)
            entries.Add(new JsonObject
            {
                ["application"] = ApplicationRegistry.Get(application.Type).Key,
                ["path"] = application.Path,
                ["version"] = application.Version,
                ["index"] = entries.Count
            });
        // Preserve applications belonging to integrations not currently registered.
        if (root["applications"] is JsonArray previous)
            foreach (var entry in previous.OrderBy(entry => entry!["index"]!.GetValue<int>()))
                if (!Loaders.Any(loader => loader.JsonKey == entry!["application"]!.GetValue<string>()))
                {
                    var retained = entry!.DeepClone();
                    retained["index"] = entries.Count;
                    entries.Add(retained);
                }
        root["applications"] = entries;
    });
}








