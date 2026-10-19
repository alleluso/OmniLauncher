# User guide

[Documentation](README.md) · [Application integrations](integrations.md)

## Register applications

Open **Settings** and choose **Add application** to select an installed executable. OmniLauncher identifies the application from its registry and detects the version from its location or executable metadata without launching it.

Paths and detected versions are read-only. Use **Browse** to replace an executable while keeping the row's application type. A missing executable shows a warning. Removing a registration does not delete the application.

Drag a row's grip to change the selection order. The first compatible existing executable wins. An explicit override in **Edit Project** takes priority, even when that override is missing; repair it rather than expecting a silent fallback. If a version cannot be detected, a project override can still select the application.

Settings are drafts until saved. **Cancel** discards changes.

![Settings showing two registered applications, one category, and Git configuration](screenshots/settings.png)

## Add and manage projects

Choose **Add project**, select an application, and browse to its project folder or file. See [Application integrations](integrations.md) for accepted formats. You can also choose a category and custom cover.

Open a project from its card. The actions menu and right-click menu provide project tools, including editing, favorites, showing the project in Explorer, copying its path, and removal. Missing projects and unavailable applications have separate status messages so you can repair the appropriate path.

Removing a project unregisters it from the library; it does not delete the project files. Removing an application registration also leaves its files intact.

## Browse the library

**All projects**, **Recent**, and **Favorites** organize projects by application. Recent shows projects launched from OmniLauncher and initially sorts by last opened. The star on a project toggles its favorite status.

Search matches names, applications, required versions, and paths without case sensitivity. Filter by application or select a category in the sidebar. Sorting applies within each application row:

- **Name:** alphabetical order.
- **Category:** category name, then project name; uncategorized projects come last.
- **Last opened:** newest first.
- **Date added:** newest first.

Unknown dates sort last. Switch between tiles and lists and adjust their size with the footer controls. Search, filters, sort choices, and scroll positions survive edits and refreshes within the session.

## Categories

Create, rename, recolor, and reorder categories in Settings. Their order controls the settings rows, sidebar, and project category menu.

Deleting a category makes its projects and groups uncategorized and clears affected group memberships. It does not remove projects. Applications and categories save together. If another app instance changes categories while Settings is open, reopen Settings before saving.

## Groups

A group collects projects with the same application and category. Choose **Add group**, then assign compatible projects through their actions menu. Adding a project while inside a group uses that group's application and category automatically.

You can edit a group's name and cover. Its application and category can change only when no projects are assigned to it.

All projects and category views show group cards beside ungrouped projects. Global search, Recent, and Favorites show the projects directly. Inside a group, those views and search apply only to its members. **Exit Group** restores the previous library view, filters, sorting, and scroll position.

Deleting a group clears its memberships without deleting projects or files.

![Tile library with two distinct custom covers, default icons, and groups](screenshots/library.png)

## Custom commands

Use a project's commands dialog to name commands, select their applications, and set arguments. Commands run from the project folder. Drag entries to reorder them; the project menu uses the same order.

![Custom commands dialog with fictional examples](screenshots/custom-commands.png)

The screenshot shows example configurations, not bundled scripts.

## Notes, links, and covers

Use **Notes and Links** to keep Markdown notes, task lists, and web resources beside a project. Switch between Edit and Preview to review formatting, and save changes when finished.

A custom cover takes priority over an embedded preview. Clear the cover field in Edit Project to allow an available preview to appear. If neither is available, the application's icon is shown. See [Application integrations](integrations.md) for preview behavior.

## Desktop and keyboard controls

On Windows, closing the window hides OmniLauncher in the system tray. Choose **Exit OmniLauncher** from the tray menu to quit. Projects can also be launched from the tray or through configured global shortcuts; several projects can share a launch shortcut.

| Shortcut | Action |
| --- | --- |
| `Ctrl+F` | Focus search |
| `F5` | Refresh the library |
| `Escape` | Dismiss the active dialog when no operation is in progress |

For repository badges and terminal configuration, see [Source control](source-control.md). For backups and file locations, see [Library storage and recovery](library-storage.md).
