using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniLauncher.Models;
using OmniLauncher.Services;

namespace OmniLauncher.ViewModels;

public partial class ProjectLinkViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _error = "";

    public ProjectLinkViewModel(ProjectLink? link = null)
    {
        if (link is null) return;
        Name = link.Name;
        Url = link.Url;
    }

    public ProjectLink ToLink()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Give this link a name.");
        return new ProjectLink(Name.Trim(), ProjectLinkLauncher.ValidateUrl(Url));
    }

    [RelayCommand] private void Open()
    {
        try { ProjectLinkLauncher.Open(Url); Error = ""; }
        catch (Exception ex) { Error = ex.Message; }
    }
}
