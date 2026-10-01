# Changelog

All notable changes to WinModes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

To publish a release, list the changes under **Unreleased**, then run `pwsh -NoProfile -File tools/release.ps1 -Bump minor` (or `-Version X.Y.Z`). The script runs the tests, moves **Unreleased** under the new version, commits, tags and pushes; the release workflow then builds and publishes the package with that section as the release notes.

## [Unreleased]
- Optimize page: a catalog of 28 Windows settings (privacy and telemetry, ads, search and AI, gaming, background, Explorer) plus the services the knowledge base advises to start only when needed. Each row shows the live state, how many of the 12 surveyed open-source optimizers ship it, and whether it needs a sign-out. The current value is recorded before each change and Undo puts it back. Security, update, start-up and service keys are refused by the engine. Nothing is uninstalled or deleted.
- Services page: a right-click starts or stops a service, changes its start type or restores the original one. Protected services cannot be changed, and the original start type is recorded before the first change.
- Widget: the mode label, the border and the compact dot take the colour of the active mode.
- Dashboard: AI tool tiles share the full width and the CPU, memory and background cards are more compact, so the graphs stay in view.
- Uninstalling undoes the machine-wide changes made from the Optimize and Services pages (service start types, policy values, scheduled tasks). Per-user settings are left as they are.

## [0.4.0] - 2026-10-01

- AI tool icons in the desktop widget.
- Update check against the GitHub releases: once at startup (can be turned off) and on demand from the About page. Nothing is downloaded automatically.
- Widget: compact one-line layout, optional CPU and memory graphs, hiding during full-screen apps, and a click on the AI block opens the AI tools page.
- Usage page: opt-in history of the memory each AI tool used per project (today, 7 or 30 days), kept on this PC for 30 days.
- AI tools page lists the MCP servers running in several sessions and the memory they hold.
- Optional alert when a single AI tool passes a memory limit.
- Optional automatic end of project sessions idle for 30 minutes to 4 hours. Off by default.
- Automation rules can also trigger on battery power or during a daily time range.
- Deactivating a mode reports how long it was active and the memory it had freed.
- GPU utilization and disk activity graphs on the dashboard.
- Export a report of the PC state (names and totals only) from the About page, to ask for help.
- Installed copies can download and start the installer of a new version from the About page; the download is checked against the checksum of the release.
- Light theme (Settings, applied at the next start).

## [0.3.1] - 2026-10-01

- The page area starts right under the title bar, level with the menu.
- The widget preview stays in view while the options scroll.

## [0.3.0] - 2026-10-01

### Added

- Quick mode menu on the tray icon, and global shortcuts (Ctrl+Alt+1, 2, 3 to activate, Ctrl+Alt+0 to deactivate).
- Alert when AI tools use more memory than a limit you choose.
- Idle AI sessions are flagged, with a button to end them; MCP servers and their memory are shown per session.
- Mode editor (services to stop, apps to close, power plan, WSL), and import, export, duplicate and delete for modes.
- Memory limit for WSL and Docker Desktop in the settings.
- Memory freed is measured and reported after a mode is activated.
- Per-core load and network speed on the dashboard.
- The desktop widget remembers its position.
- Widget page: always on top, opacity, size, content shown, number of tools, refresh speed, corner placement, position lock and click-through.
- `tools/release.ps1` to cut a release in one command.
- Live preview of the widget on the Widget page.
- Title bar with the menu button, icon, name and version on one row.
- Privacy mode (Settings, or `--privacy` for one run): hides project names, folders, command-line details and the account name.
- Automation page: opt-in rules that activate a mode when a program starts and undo it when the program closes. Off by default.
- First-run welcome guide (what the app does, what it never touches, optional features), reopenable from the About page.
- Installer (Inno Setup) into Program Files with uninstaller, alongside a portable zip and SHA-256 checksums; `tools/package.ps1` builds both.

## [0.2.0] - 2026-10-01

### Added

- AI tools at a glance on the dashboard, and an AI tools page that groups every running session of Claude Code, Claude desktop, Codex, Cursor, VS Code and others by project folder.
- Process tree with PID, CPU, memory, threads and command line, child processes, sorting, search, and a right-click menu (end task, end process tree, open file location, copy).
- Optional tray meter: the notification-area icon shows the memory used by AI tools.
- Optional desktop widget: a small always-on-top panel with CPU, memory and AI tools.
- Settings page: start with Windows, start minimized, keep running in the notification area, confirmation before activating, mode to activate at startup.
- "Buy me a coffee" and source code links on the About page.
- Release workflow that publishes a self-contained build.

### Changed

- Graphs scroll continuously and gauges glide to their new value.
- The Processes page updates rows in place instead of rebuilding the list.

## [0.1.0] - 2026-10-01

### Added

- Code, Work and Game modes with preview, activation and undo.
- Journaled engine with an elevated helper; services are set to Manual, never Disabled.
- Protection blocklist for security, WSL, Docker, winget, the Microsoft Store, Edge and developer tools.
- Dashboard, Services, History, Protection and About pages.
