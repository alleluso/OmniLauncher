# Adding application support

[Documentation](README.md) · [Existing integrations](integrations.md)

[`ApplicationRegistry`](../Services/ApplicationRegistry.cs) is the single registration list for supported applications. Each `ApplicationDefinition` combines a project loader with application-specific validation and launch behavior. Settings, filtering, dialogs, icons, and launching consume that definition.

## 1. Define the project type and loader

Use an existing [`ProjectType`](../Models/ProjectType.cs) member or add one. Implement [`IProjectTypeLoader`](../Services/ProjectLoaders/IProjectTypeLoader.cs) in `Services/ProjectLoaders/`, using the `OmniLauncher.Services.ProjectLoaders` namespace.

Choose a stable `JsonKey` for storage and a `GroupName` for display. In `LoadProjectAsync`, return a `ProjectItem` with `Name`, `ProjectPath`, and `RequiredVersion`. Set `FallbackImagePath` only when a preview is available and `loadFallbackImage` is true. A custom `ProjectItem` subclass is optional and should only be needed for additional metadata or display behavior.

### Configure path selection

| Loader property | Purpose |
| --- | --- |
| `ProjectPathLabel` | Path field label in Add and Edit Project |
| `ProjectPathPlaceholder` | Path field hint |
| `FolderPickerTitle` | Enables folder selection; use `null` when unsupported |
| `FilePickerTitle` | Enables file selection; use `null` when unsupported |
| `ProjectFilePatterns` | File-picker filters |

One supported picker produces a Browse button. Supporting both produces a dropdown with the loader's titles. Shared dialogs should not need application-specific text or type switches.

## 2. Register application behavior

Add an `ApplicationDefinition` in [`Services/ApplicationRegistry.cs`](../Services/ApplicationRegistry.cs).

| Property | Contract |
| --- | --- |
| `Loader` | The project loader instance |
| `IconPath` | Asset URI for an icon under `Assets/` |
| `ExecutableNames` | Allowed executable base names, without `.exe` |
| `IsProjectValid` | Validate the project file or folder and required markers |
| `InferVersion` | Infer a version from the executable path; return empty to fall back to executable product metadata |
| `LaunchArguments` | Return individual arguments, never a prequoted command line |
| `RequiresVersion` | Default exact-version matching when true; no required version when false |
| `VersionMatcher` | Optional custom compatibility rule taking installed and required versions |

The loader reads project metadata and defines path selection. Shared services resolve the registered executable. Explicit overrides take priority, including missing overrides, which remain visible for repair. Launching validates the executable and project again and uses `ProcessStartInfo.ArgumentList`.

## 3. Reuse shared library behavior

Favorites, addition dates, launch history, and library actions belong to shared services rather than loaders. New integrations receive them automatically.

For covers, `CustomImagePath` takes precedence over `FallbackImagePath`. The library persists the custom path in the `image` field and requests a fallback only when that field is empty. Loaders must not set the custom cover. If neither path is present, the registered application icon is displayed. No application-specific image control is needed in the dialogs.

Only the current library schema is supported. Existing project sections for unregistered applications are preserved in storage. See [Library storage and recovery](library-storage.md) for schema rules, and [Application integrations](integrations.md) for preview and cache behavior.

## 4. Verify the integration

Build using the commands in the [project README](../README.md), then check:

- Registration, executable detection, and version selection.
- Add and Edit Project, including every supported picker.
- Save, reload, and duplicate-path handling.
- Missing project and executable paths, including an explicit override.
- Launch arguments, especially paths containing spaces.
- Custom covers, optional previews, and icon fallback.

Keep application-specific policy in the loader and registry so the shared UI stays reusable.
