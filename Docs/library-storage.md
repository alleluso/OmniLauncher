# Library storage and recovery

[Documentation](README.md) · [User guide](user-guide.md)

## File locations

| File or directory | Purpose |
| --- | --- |
| `%LOCALAPPDATA%\OmniLauncher\projects.json` | Project library and settings |
| `projects.json.bak` beside the library | Previous saved library contents |
| `projects.json.lock` beside the library | Coordination between application instances |
| `%LOCALAPPDATA%\OmniLauncher\Thumbnails` | Derived preview cache |

On other platforms, the base directory comes from `Environment.SpecialFolder.LocalApplicationData`. Preview generation and cleanup are described in [Application integrations](integrations.md).

## Saving and recovery

Saves flush a temporary file and atomically replace the library. The `.bak` file holds the preceding saved contents. It is a recovery copy, not an archive of all past changes. A lock coordinates read/modify/write operations across application instances.

Invalid library files and failed writes produce an error without replacing the library. To restore a backup:

1. Exit OmniLauncher, including any instance in the system tray.
2. Preserve the damaged `projects.json` separately.
3. Copy a known-good backup in the current format to `projects.json`.
4. Restart OmniLauncher and check the library.

Backups are not imported automatically. Removing projects, groups, or application registrations from the library does not delete their underlying files.

## Current format

Only `schemaVersion: 1` is accepted. A fresh library contains a `projects` object, an `applications` array, and default categories. There is no import of other library formats or automatic schema-version upgrade.

| Field | Contents |
| --- | --- |
| `schemaVersion` | Current schema version: `1` |
| `projects` | Project arrays keyed by each loader's stable application key |
| `applications` | Registered executable paths, versions, and selection order |
| `categories` | Ordered category definitions |
| `groups` | Optional user-created groups |
| `sourceControl` | Global Git client or terminal configuration |
| `displayPreferences` | Library layout and size preferences |

Within the current schema, reading may seed missing categories or remove incompatible group memberships. Those changes use the normal backup and save mechanism. Missing IDs and duplicate IDs are not repaired automatically.

## Projects and metadata

Projects require valid, unique GUIDs. Creation assigns an ID; edits and removal target that ID. Adding a project or changing its path validates the registered project type and rejects a duplicate normalized path for the same application.

Project records can include a category, group membership, custom cover, executable override, launch shortcut, commands, notes, and links. The custom cover is stored in `image`; generated previews are not persisted as custom covers.

| Metadata | Behavior |
| --- | --- |
| `isFavorite` | Boolean; absent means not favorited |
| `addedAt` | UTC ISO timestamp assigned when a project is added |
| `lastOpenedAt` | UTC ISO timestamp updated only after a successful launch |

Absent dates remain unknown; timestamps are not invented on read. Failed launches do not update launch history. Metadata updates use the same atomic, ID-based operations as other project edits.

## Application registrations

An absent `applications` section means no registered applications. Saving Settings creates it. Entries store an application key, executable path, version string, and unique non-negative `index`. Loading sorts by index; saving rewrites indexes consecutively. The first compatible existing executable wins unless the project specifies an override.

Example fragment within a library object:

```json
{
  "applications": [
    {
      "application": "blender",
      "path": "C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe",
      "version": "5.1",
      "index": 0
    }
  ]
}
```

This fragment illustrates the field; it is not a complete library. See the [user guide](user-guide.md) for registration steps and [Adding application support](application-support.md) for extending the registry.

## Categories

`categories` is an ordered array. Each entry has a unique positive integer `id`, a `name`, and a `color` in `#RRGGBB` format. A project's `category` references that stable ID; `0` means uncategorized. Array order controls Settings, the sidebar, and the project category menu.

If `categories` is missing, the store seeds the six original colors once, retaining IDs 1–6 and existing assignments. An explicitly empty array remains empty. Invalid names, colors, duplicate IDs, and unknown project category references are rejected.

Applications and categories save together. A concurrent category change requires the user to reopen Settings rather than overwrite it. Removing a category clears project assignments and makes affected groups uncategorized, clearing their memberships.

## Groups

The optional `groups` array contains each group's `id` (GUID), `name`, `application` (registry key), `category` (category ID or `0`), and `image` (cover path, or an empty string). A project can reference one `groupId`. Libraries without these fields remain valid.

Membership requires the project and group to share an application and category. Reads and writes remove memberships whose group is missing or incompatible. A group's application and category can change only when it has no assigned projects. Deleting a group removes its memberships without deleting the projects.

For group navigation and editing, see the [user guide](user-guide.md). For the `sourceControl` settings, see [Source control](source-control.md).
