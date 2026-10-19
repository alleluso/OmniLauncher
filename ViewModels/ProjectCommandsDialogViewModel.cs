using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class ProjectCommandsDialogViewModel : ViewModelBase
{
    private string _projectId = "";
    public ObservableCollection<ProjectCommandViewModel> Commands { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _projectName = "";
    [ObservableProperty] private string _commandsError = "";
    public string Title => string.IsNullOrWhiteSpace(ProjectName) ? "Commands" : $"Commands · {ProjectName}";
    public string CountLabel => $"{Commands.Count} command{(Commands.Count == 1 ? "" : "s")}";

    public ProjectCommandsDialogViewModel()
    {
        Commands.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountLabel));
    }

    public event Action<ProjectCommandsResult>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand] private void AddCommand() => Commands.Add(new ProjectCommandViewModel());
    [RelayCommand] private void RemoveCommand(ProjectCommandViewModel command) => Commands.Remove(command);
    [RelayCommand] private void Cancel() => Cancelled?.Invoke();

    public void MoveCommand(ProjectCommandViewModel command, int index)
    {
        var current = Commands.IndexOf(command);
        if (current >= 0 && index >= 0 && index < Commands.Count && current != index)
            Commands.Move(current, index);
    }

    public void LoadProject(ProjectItem project)
    {
        _projectId = project.Id;
        ProjectName = project.Name;
        CommandsError = "";
        Commands.Clear();
        foreach (var command in project.Commands) Commands.Add(new ProjectCommandViewModel(command));
    }

    [RelayCommand]
    private void Confirm()
    {
        CommandsError = "";
        if (Commands.Any(command => string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.ExecutablePath)))
        {
            CommandsError = "Give each command a name and select an application, or remove the empty command.";
            return;
        }
        Confirmed?.Invoke(new ProjectCommandsResult(_projectId, Commands.Select(command => command.ToCommand()).ToArray()));
    }
}

public record ProjectCommandsResult(string ProjectId, IReadOnlyList<ProjectCommand> Commands);
