# Changelog

All notable changes to WinModes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

To publish a release, list the changes under **Unreleased**, then run `pwsh -NoProfile -File tools/release.ps1 -Bump minor` (or `-Version X.Y.Z`). The script runs the tests, moves **Unreleased** under the new version, commits, tags and pushes; the release workflow then builds and publishes the package with that section as the release notes.

## [Unreleased]

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
