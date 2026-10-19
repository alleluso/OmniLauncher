using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OmniLauncher.Models;

namespace OmniLauncher.Services;

public static class ProjectPathPicker
{
    public static async Task<string?> PickAsync(TopLevel window, ProjectType type, bool selectFile = false)
    {
        var loader = ApplicationRegistry.Get(type).Loader;
        if (loader.FilePickerTitle is not null && (loader.FolderPickerTitle is null || selectFile))
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = loader.FilePickerTitle, AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(loader.ProjectPathLabel)
                { Patterns = loader.ProjectFilePatterns }]
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = loader.FolderPickerTitle, AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
