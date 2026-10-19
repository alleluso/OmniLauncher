using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public partial class ProjectNotesDialogViewModel : ViewModelBase
{
    private string _projectId = "";
    public ObservableCollection<ProjectLinkViewModel> Links { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _projectName = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewMarkdown)), NotifyPropertyChangedFor(nameof(HasNotes))]
    private string _notes = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewMarkdown)), NotifyPropertyChangedFor(nameof(IsEditingNotes))]
    private int _notesTabIndex;
    public bool IsEditingNotes => NotesTabIndex == 0;
    public string PreviewMarkdown => NotesTabIndex == 1 ? Notes : "";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    [ObservableProperty] private string _error = "";
    public string Title => $"Notes · {ProjectName}";
    public string CountLabel => $"{Links.Count} link{(Links.Count == 1 ? "" : "s")}";

    public ProjectNotesDialogViewModel()
    {
        Links.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountLabel));
    }
    public event Action<ProjectNotesResult>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand] private void AddLink() => Links.Add(new ProjectLinkViewModel());
    [RelayCommand] private void RemoveLink(ProjectLinkViewModel link) => Links.Remove(link);
    [RelayCommand] private void Cancel() => Cancelled?.Invoke();

    [RelayCommand] private void ToggleNoteTask(Controls.NoteTaskToggle? toggle)
    {
        // Ignore events from a preview replaced by an edit or a different project.
        if (toggle is null || toggle.Source != Notes || toggle.StateOffset < 1 ||
            toggle.StateOffset + 1 >= Notes.Length || Notes[toggle.StateOffset - 1] != '[' ||
            Notes[toggle.StateOffset + 1] != ']') return;
        Notes = Notes[..toggle.StateOffset] + (toggle.IsChecked ? "x" : " ") + Notes[(toggle.StateOffset + 1)..];
    }

    [RelayCommand] private void OpenNoteLink(string? url)
    {
        try { Services.ProjectLinkLauncher.Open(url ?? ""); Error = ""; }
        catch (Exception ex) { Error = $"Could not open the note link. {ex.Message}"; }
    }

    public void MoveLink(ProjectLinkViewModel link, int index)
    {
        var current = Links.IndexOf(link);
        if (current >= 0 && index >= 0 && index < Links.Count && current != index) Links.Move(current, index);
    }

    public void LoadProject(ProjectItem project)
    {
        _projectId = project.Id;
        NotesTabIndex = 0;
        ProjectName = project.Name;
        Notes = project.Notes;
        Error = "";
        Links.Clear();
        foreach (var link in project.Links) Links.Add(new ProjectLinkViewModel(link));
    }

    [RelayCommand] private void Confirm()
    {
        Error = "";
        var links = new List<ProjectLink>();
        foreach (var row in Links)
        {
            row.Error = "";
            try { links.Add(row.ToLink()); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                row.Error = ex.Message;
                Error = "Check the highlighted links, or delete any empty entries.";
            }
        }
        if (Error.Length == 0) Confirmed?.Invoke(new ProjectNotesResult(_projectId, Notes, links));
    }
}

public sealed record ProjectNotesResult(string ProjectId, string Notes, IReadOnlyList<ProjectLink> Links);
