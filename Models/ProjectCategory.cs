using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniLauncher.Models;

public sealed record ProjectCategory(int Id, string Name, string Color)
{
    public static ProjectCategory None { get; } = new(0, "No category", "Transparent");
    public bool IsNone => Id == None.Id;

    // IDs are persisted on projects; names and colors can change independently.
    public static IReadOnlyList<ProjectCategory> CreateDefaults() => Array.AsReadOnly(new[]
    {
        new ProjectCategory(1, "Red", "#E36A76"),
        new ProjectCategory(2, "Orange", "#E99A58"),
        new ProjectCategory(3, "Yellow", "#E6CA66"),
        new ProjectCategory(4, "Green", "#70BA8D"),
        new ProjectCategory(5, "Blue", "#6A9FE3"),
        new ProjectCategory(6, "Purple", "#AA88DE")
    });

}
