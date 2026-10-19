using OmniLauncher.Models;

namespace OmniLauncher.ViewModels;

public sealed record LibraryViewOption(string Label, string Glyph, ProjectCategory? Category = null)
{
    public static LibraryViewOption AllProjects { get; } = new("All projects", "🔥");
    public static LibraryViewOption Recent { get; } = new("Recent", "🕐");
    public static LibraryViewOption Favorites { get; } = new("Favorites", "⭐");
    public static LibraryViewOption NoCategory { get; } = new("No Category", "○");

    public string Name => Category?.Name ?? Label;
    public bool IsCategory => Category is not null;
}
