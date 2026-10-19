using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class ProjectCommandViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty]
    private string _executablePath = "";
    [ObservableProperty] private string _arguments = "";
    [ObservableProperty] private string _error = "";

    public ProjectCommandViewModel(ProjectCommand? command = null)
    {
        if (command is null) return;
        Name = command.Name;
        ExecutablePath = command.ExecutablePath;
        Arguments = command.Arguments;
    }

    public ProjectCommand ToCommand() => new(Name.Trim(), ExecutablePath.Trim(), Arguments);

    [RelayCommand]
    private async Task BrowseApplication(TopLevel? window)
    {
        if (window is null) return;
        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select command application",
                AllowMultiple = false,
                FileTypeFilter = OperatingSystem.IsWindows()
                    ? [new FilePickerFileType("Applications") { Patterns = ["*.exe"] }]
                    : [FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null || !File.Exists(path))
                throw new InvalidOperationException("Select an application on this computer.");
            ExecutablePath = path;
            Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
