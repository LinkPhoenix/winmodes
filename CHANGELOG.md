# Changelog

All notable changes to WinModes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

To publish a release: add a section for the new version below, commit, then push a tag such as `v0.2.0`. The release workflow builds, tests, packages and publishes it with this section as the release notes.

## [Unreleased]

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
