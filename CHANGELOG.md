# Changelog

All notable changes to WinModes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

Each change is one line under **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed** or **Security**, written for the person using the app: what they can now do or what behaves differently, starting with the page or area it concerns.

To publish a release, list the changes under **Unreleased**, then run `pwsh -NoProfile -File tools/release.ps1 -Bump minor` (or `-Version X.Y.Z`). The script runs the tests, moves **Unreleased** under the new version, commits, tags and pushes; the release workflow then builds and publishes the package with that section as the release notes.

## [Unreleased]

### Fixed

- Modes: a switch interrupted by a crash or a failed restore stays listed as active and is retried on the next undo, instead of being forgotten with services left changed.
- Services and Optimize: a damaged record of original settings is set aside as a `.corrupt-` file instead of being overwritten, so the values to restore are not lost.
- Automation: when several rules match, a lower-priority program no longer replaces the mode of a higher-priority program that is still running; it takes over when that program exits.
- Usage and widget: a plan or status-line answer holding an impossible date or number no longer stops the reading; the value is shown as unknown.
- Settings, History and Claude status line: files are saved in one step, so a crash or power loss can no longer leave a half-written settings, usage history or Claude Code settings file.
- Claude status line: installing or removing it keeps the first backup of your Claude Code settings instead of replacing it, and no longer rewrites accents or symbols elsewhere in that file.
- App: an unexpected error in a page, the tray menu or a hotkey no longer closes WinModes; it is written to `errors.log` in `%LocalAppData%\WinModes` and a notification is shown.
- Modes: asking for a mode switch while another is still running (page, tray menu, hotkey or automation) is refused with a message instead of stacking administrator prompts.

### Changed

- Modes: listing and closing the apps of a mode, and measuring memory, no longer run on the interface thread, so the window stays responsive during a switch.
- Settings: preferences are read from disk once instead of at every refresh of the tray, widget and pages.
- Dashboard and widget: the live graphs redraw at most 30 times a second instead of 60, which lowers the CPU use of the app.

### Security

- The administrator helper now refuses any command, service name, tweak id or mode name that is not in the expected form, including through the silent switch.
- Modes: the administrator helper runs one command at a time, so a switch started from the prompt and one started by the silent task can no longer overlap.

## [0.8.0] - 2026-10-01

### Added

- Settings: the interface can be shown in French, Spanish or Italian as well as English (the default). The language applies the next time WinModes starts and covers every page, the widget, the tray menu, the dialogs and the notifications.
- Widget: each plan shows a second bar for its weekly limit, next to the bar of the 5-hour limit; both bars are labelled.

### Changed

- With French, Spanish or Italian selected, dates and numbers follow that language; in English they keep the format of Windows.

## [0.7.0] - 2026-10-01

### Added

- Widget: choose whether the plan usage shows Claude, Codex or both; a plan switched off is neither read nor asked online.
- Widget: the limit resets in reserve are shown in the compact layout too, and an option hides them.
- Widget: optional notification when a plan has less than 10 % of its limit left. Off by default.
- About page: a portable copy gets a Download button when a new version is found; the zip is checked against the release checksum and saved in the Downloads folder.

### Changed

- Widget page: the plan usage options have their own section.

## [0.6.0] - 2026-10-01

### Added

- Widget: optional "Plans" block with your Claude and Codex plan, the usage left and when each limit resets. Off by default; read from files on this PC.
- Widget: the compact layout shows the usage left on each plan next to the tool's icon; the "Plans" block uses the icons instead of the names.
- Widget: "Record Claude usage" adds a WinModes status line to Claude Code, which records the 5-hour and weekly usage. It never replaces a status line you already have and keeps a backup of the settings.
- Widget: "Read the usage online" asks Anthropic and OpenAI for the live figures and the limit resets in reserve, with the sign-in Claude Code and Codex keep on this PC. Off by default.
- Automation: option to switch modes without the Windows permission prompt, through a scheduled task registered once. Off by default; installed copies only. Uninstalling removes the task.
- AI tools: MCP servers declared in the Codex configuration are recognised and shown under the name you gave them.

### Changed

- Widget: at 100 % opacity the widget is fully opaque; its background used to keep a slight transparency.

### Fixed

- AI tools: Codex and Claude desktop no longer count their own processes as MCP servers.
- AI tools: an MCP server started through `npx` is counted once instead of once per wrapper process.
- AI tools: `prisma mcp`, `shadcn mcp` and `docker mcp` are no longer merged under the name "mcp".

## [0.5.0] - 2026-10-01

### Added

- Optimize page: 28 Windows settings (privacy, ads, search and AI, gaming, background, Explorer) and the services advised to start only when needed, each with its live state and an Undo.
- Optimize page: each setting shows how many of the 12 surveyed open-source optimizers ship it and whether it needs a sign-out.
- Services page: a right-click starts or stops a service, changes its start type or restores the original one.
- Widget: the mode label, the border and the compact dot take the colour of the active mode.

### Changed

- Dashboard: the AI tool tiles share the full width and the CPU, memory and background cards are more compact.
- Uninstalling undoes the machine-wide changes made from the Optimize and Services pages; per-user settings are left as they are.

### Security

- Optimize and Services pages: security, update, start-up and protected service settings cannot be changed, and nothing is uninstalled or deleted.

## [0.4.0] - 2026-10-01

### Added

- Widget: AI tool icons, a compact one-line layout, optional CPU and memory graphs, and hiding during full-screen apps.
- Widget: a click on the AI block opens the AI tools page.
- Usage page: opt-in history of the memory each AI tool used per project (today, 7 or 30 days), kept on this PC for 30 days.
- AI tools: MCP servers running in several sessions are listed with the memory they hold.
- AI tools: optional alert when a single tool passes a memory limit.
- AI tools: optional automatic end of project sessions idle for 30 minutes to 4 hours. Off by default.
- Automation: rules can trigger on battery power or during a daily time range.
- Modes: deactivating a mode reports how long it was active and the memory it had freed.
- Dashboard: GPU utilization and disk activity graphs.
- About page: update check against the GitHub releases, at startup (can be turned off) and on demand.
- About page: installed copies can download and start the installer of a new version, checked against the release checksum.
- About page: export a report of the PC state (names and totals only) to ask for help.
- Settings: light theme, applied at the next start.

## [0.3.1] - 2026-10-01

### Fixed

- The page area starts right under the title bar, level with the menu.
- Widget page: the preview stays in view while the options scroll.
## [0.3.0] - 2026-10-01

### Added

- Modes: quick menu on the tray icon and global shortcuts (Ctrl+Alt+1, 2, 3 to activate, Ctrl+Alt+0 to deactivate).
- AI tools: alert when they use more memory than a limit you choose.
- AI tools: idle sessions are flagged, with a button to end them; MCP servers and their memory are shown per session.
- Modes: editor (services to stop, apps to close, power plan, WSL), and import, export, duplicate and delete.
- Settings: memory limit for WSL and Docker Desktop.
- Modes: the memory freed is measured and reported after activation.
- Dashboard: per-core load and network speed.
- Widget: the position is remembered.
- Widget page: always on top, opacity, size, content shown, number of tools, refresh speed, corner placement, position lock and click-through.
- Development: `tools/release.ps1` cuts a release in one command.
- Widget page: live preview of the widget.
- Window: title bar with the menu button, icon, name and version on one row.
- Settings: privacy mode (or `--privacy` for one run) hides project names, folders, command-line details and the account name.
- Automation page: opt-in rules that activate a mode when a program starts and undo it when the program closes. Off by default.
- First run: welcome guide (what the app does, what it never touches, optional features), reopenable from the About page.
- Installation: installer into Program Files with uninstaller, alongside a portable zip and SHA-256 checksums.

## [0.2.0] - 2026-10-01

### Added

- AI tools: summary on the dashboard and a page that groups every running session of Claude Code, Claude desktop, Codex, Cursor, VS Code and others by project folder.
- Processes page: tree with PID, CPU, memory, threads and command line, sorting, search, and a right-click menu (end task, end process tree, open file location, copy).
- Tray: optional meter showing the memory used by AI tools on the notification-area icon.
- Widget: optional always-on-top desktop panel with CPU, memory and AI tools.
- Settings page: start with Windows, start minimized, keep running in the notification area, confirmation before activating, mode to activate at startup.
- About page: "Buy me a coffee" and source code links.
- Development: release workflow that publishes a self-contained build.

### Changed

- Dashboard: graphs scroll continuously and gauges glide to their new value.
- Processes page: rows update in place instead of the list being rebuilt.

## [0.1.0] - 2026-10-01

### Added

- Modes: Code, Work and Game, with preview, activation and undo.
- Modes: every change is journaled and reversible; services are set to Manual, never Disabled.
- Protection: blocklist for security, WSL, Docker, winget, the Microsoft Store, Edge and developer tools.
- Pages: Dashboard, Services, History, Protection and About.

[Unreleased]: https://github.com/LinkPhoenix/winmodes/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/LinkPhoenix/winmodes/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/LinkPhoenix/winmodes/releases/tag/v0.3.0
