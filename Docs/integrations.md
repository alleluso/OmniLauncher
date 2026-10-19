# Application integrations

[Documentation](README.md) · [Registering applications](user-guide.md)

Install applications separately and register their executables in Settings. A project-specific executable override takes precedence over automatic matching.

## Supported formats

| Application | Executable | Project selection | Automatic matching |
| --- | --- | --- | --- |
| Unreal Engine | `UnrealEditor.exe` or `UE4Editor.exe` | Folder containing a `.uproject` file | Engine major/minor version |
| Unity | `Unity.exe` | Folder containing `ProjectSettings/ProjectVersion.txt` | Exact editor version |
| Blender | `blender.exe` | `.blend` file | Major/minor version |
| Affinity | `Affinity.exe` | `.af` document | No version requirement |
| Visual Studio Code | `Code.exe` | Folder or `.code-workspace` file | No version requirement |

## Blender

The loader reads the embedded project version. Blender headers do not store the patch version, so automatic matching uses the major/minor version. Explicit overrides let you choose another executable.

When no custom cover is selected, the loader reads the embedded thumbnail without modifying the file or launching Blender. Uncompressed, gzip, and Zstandard files are supported, with both 12-byte and 17-byte headers. Only early `REND`/`TEST` blocks are read, with bounds on lengths, dimensions, decompression window, and processing time.

Launching passes `--disable-autoexec` to prevent automatic execution of embedded Python scripts when opening the project.

Implementation references:

- [Blender file header](https://github.com/blender/blender/blob/main/source/blender/blenloader_core/BLO_core_blend_header.hh)
- [Blender block header](https://github.com/blender/blender/blob/main/source/blender/blenloader_core/BLO_core_bhead.hh)
- [Blender thumbnail extraction](https://github.com/blender/blender/blob/main/source/blender/blendthumb/src/blendthumb_extract.cc)

## Affinity

Support targets the unified Affinity application and `.af` documents. Legacy `.afdesign`, `.afphoto`, and `.afpub` files are not included. Validation checks file existence and the extension, case-insensitively; it does not validate proprietary document contents or parse document version metadata.

Documents open by passing their path as one argument. Missing documents remain in the library for repair.

When no custom cover is selected, the loader scans for embedded PNG previews in the background. It selects the smallest complete PNG with valid chunk checksums. This is a thumbnail heuristic, not a document-table parser.

| Preview limit | Value |
| --- | --- |
| Processing timeout | 5 seconds |
| Document scan | 256 MiB |
| Selected PNG size | 16 MiB |
| Image dimensions | 4096 pixels per dimension |

Missing, malformed, or inaccessible previews fall back to the application icon.

## Visual Studio Code

Any existing folder is accepted; a `.vscode` marker is not required. An existing `.code-workspace` file is also accepted. Add Project and Edit Project provide a Browse dropdown for these two choices.

Launching passes `--new-window` and the original path as separate arguments. VS Code interprets the workspace file, including comments, relative paths, and remote folder URIs. OmniLauncher does not parse or rewrite it, require referenced folders to exist locally, or infer a thumbnail.

Custom covers are supported. Missing project paths remain in the library for repair.

## Covers and preview caching

A custom cover always wins over a loader-generated preview. If neither is available, the registered application icon is shown. Clearing a custom cover lets the loader supply a preview again.

Generated thumbnails are cached under `%LOCALAPPDATA%\OmniLauncher\Thumbnails`, keyed by document path, size, and modification time. Reloading regenerates previews for changed files. Cached thumbnails are not saved as custom cover paths. An unreadable preview or unavailable cache leaves the project usable without a cover.

After a successful library load, unused generated thumbnails are removed. Active previews and saved custom cover paths, including those in unregistered application sections, are preserved. Cleanup only targets SHA-256-named PNG/BMP files directly inside the thumbnail directory. Unrelated files, subdirectories, links, and temporary writes remain untouched. Locked files are retried on a later load.

To implement another integration, see [Adding application support](application-support.md).
