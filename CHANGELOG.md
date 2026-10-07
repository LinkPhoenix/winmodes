# Changelog

All notable changes to WinModes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

Each change is one line under **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed** or **Security**, written for the person using the app: what they can now do or what behaves differently, starting with the page or area it concerns.

To publish a release, list the changes under **Unreleased**, then run `pwsh -NoProfile -File tools/release.ps1 -Bump minor` (or `-Version X.Y.Z`). The script runs the tests, moves **Unreleased** under the new version, commits, tags and pushes; the release workflow then builds and publishes the package with that section as the release notes.

## [Unreleased]

### Added

- Widget, Settings and Notifications: a list of the page's sections stays on the left; click one to jump to it, and the section you are reading is marked by a bar that slides along the list, and the page glides to the section, which fades in (skipped when Windows animations are off).
- Widget: the taskbar can draw the limits of each plan as small rings with the percentage left inside, which take about half the width of the bars; choose Bars or Rings under Plan usage.
- Widget: sign in to your xAI account to see the weekly usage of your SuperGrok plan next to Claude and Codex, with the Grok logo, in the widget on the desktop and on the taskbar; Grok has its own section on the Widget page to sign in or out and to show or hide it.
- Widget: while you sign in to Claude, ChatGPT or Grok, a field accepts the code the provider shows (or the address the browser ended on) when your browser cannot come back to WinModes by itself, for example when it blocks the local address.

### Changed

- Widget: the plan and the usage of Claude, Codex and Grok now come only from your accounts, through the sign-in of WinModes; nothing is read any more from the files of Claude Code or Codex, and a plan whose account is not signed in is not listed.
- Widget: the plan of a Claude account is read from the account itself, and a Grok account shows the tier of its plan.
- Widget: Grok shows a second bar for the usage billed on demand when the account has set a cap.

### Removed

- Widget: the options that read the usage online with the sign-in of Claude Code or Codex, the Check buttons and the Claude usage recording, now that every figure comes from your accounts; the recording row stays only while an older version left it set, to switch it off.

### Fixed

- Window: while WinModes is minimized, the pages (Dashboard, Modes, Processes, Services, AI tools, Usage, History) and the charts no longer go on reading the PC and redrawing for a window nobody can see; the Dashboard used about ten times more processor time minimized than it needed.
- Window: opening WinModes again from the tray keeps it maximized if it was maximized when it was sent to the tray, instead of shrinking it to its normal size.
- Debloat: the line under the title lines up with the title like on the other pages, and the description of an app that does not fit its card now ends with an ellipsis instead of being cut in the middle of a line.

## [0.9.3] - 2026-10-03

### Added

- Optimize: policy badges, blocked-setting filters, and a local policy diagnostic with reviewed, journaled recovery for supported registry policy values and an undo of each recovery.
- Optimize: refresh with check time and read errors, live technical values, and a detailed review before applying or restoring changes.
- Optimize: per-setting outcomes and a summary of saved changes that require sign-out, Explorer refresh or a Windows restart.
- Configuration: prepare, import and export catalog settings without applying them; unsupported entries are shown and choices are staged for review in Optimize.
- History: search and filter mode journals, optimization recovery records and removed apps together, with refresh status and recovery-page links.
- Operations: keep the latest operation visible across pages, refuse overlapping actions and wait for completion before quitting.
- Support: a title-bar heart and About button open a centered donation window for Emilio LECERF, with the official blue PayPal logo and his PayPal.me link.
- Debloat: persistent badges for installation, removal guidance, recovery options and pending removal; a scrollable review lists consequences and recovery for every selected app.
- Debloat: refresh with detection source and check time, selection of visible results, hidden-selection counts, and an expanded four-column layout on large windows.

- Optimize: filter recommendations, precautions, applied settings, administrator requirements and pending changes, with a visible result count.
- Debloat: separate installation and removal guidance in every view, status filters, result counts, and sorting by name or category.
- Navigation: Ctrl+1 through Ctrl+9 open the main pages; view controls use accessible icons with tooltips and a keyboard focus indicator.

- About: an "Update channel" choice in the Updates section, Stable or Beta. On Beta, WinModes also offers the test versions (and the stable version when it comes out); on Stable it only offers finished versions, even if you run a beta build. Until you choose, a beta build follows the betas and a stable build the stable releases, as before.
- Debloat: the list also covers the old Feedback and Reading List apps, Connect (wireless display), the promoted travel and note apps (Booking.com, Expedia, Priceline, Evernote) and the Acer, ASUS and Samsung tools, plus a few names that other optimizers list (older games, Instagram Beta, LinkedIn, XING, Yandex, Fresh Paint, Drawboard PDF, Cortana, the old Wallet). Nothing new is offered that the protected list covers.
- Debloat: a "Your other apps" view lists every other app of your Start menu with its real name and logo, so everything on the PC can be seen. WinModes never removes them: the protected ones say so (the Store, winget, Windows, your AI tools), and a button opens Windows Settings to uninstall one the usual way.
- Debloat: every app opens to show the packages it is made of (with their logos and versions) and, when there are several, to remove only some of them; it also says whether the app is safe to remove, and where to get it back from the Store or winget.
- Debloat: a bar of categories with the number of apps in each, and a search that also reads the descriptions and the package names. A tick box shows what the list covers although this PC does not have it (the apps are then faded and cannot be selected).
- Debloat: each app shows its own logo, and OneDrive its official icon, read from the app and from OneDrive itself. "Remove selected" moves to a bar at the bottom of the page that stays in view while the list scrolls.
- Optimize: every setting can be opened to see what it changes (the registry values or scheduled tasks, in plain words and with their technical names) and, when it changes several things, to pick only some of them. Unticking a change that WinModes made puts back that change alone. Searching also looks in these descriptions.
- Optimize: a bar of categories stays at the top of the page, with the number of settings in each. A click limits the list to one category instead of scrolling for it, and the numbers follow the search, so they show where the matches are.
- About and title bar: a beta build shows a "Beta" tag next to its version (and the full version, such as 0.9.3-beta.20261002, on the About page). A beta build looks for newer betas as well as stable versions, unless you choose the Stable update channel on the About page; a stable build only hears about betas if you choose the Beta channel.
- Optimize: two settings for OneDrive that remove nothing: hide it in File Explorer, or keep it from syncing and starting by policy.
- Startup: a new page lists what starts with Windows (the Run entries of your account and of all users, and the Startup folders) and turns the items of your account on or off exactly the way Task Manager does, so nothing is deleted and each item can be reset to what it was. Security software, audio drivers, WinModes itself and the tools you rely on are shown as protected, and the items for all users, which need administrator rights, are shown but left to Task Manager.
- Debloat: a new page lists the preinstalled apps of your account (Clipchamp, News, Solitaire, Teams, Copilot, the promoted games and apps, Xbox pieces, Phone Link, and about sixty more, found by comparing what the well-known optimizers remove) and removes the ones you tick, for your account only. Each app is "Safe" or "Check first", and the ones to check say what stops working without them. Everything removed is listed on the page and Restore registers it again from the files that stay on the PC. The Microsoft Store, App Installer (winget), Edge, WebView2, the framework packages, Windows Security, the shell, Windows Terminal, the image and video codecs, the Xbox identity pieces and the apps of your daily tools are never offered, whatever the list says.
- Debloat: OneDrive can be uninstalled from the page. WinModes refuses while Desktop, Documents, Pictures, Music, Videos or Downloads live inside OneDrive, warns when an account is signed in or files exist only online, keeps every file that is on the PC and never touches the OneDrive setup program of Windows, so "Install OneDrive again" is always there.
- Modes: a mode can change power values (when the display turns off, sleep and hibernation, disk, processor speed, USB suspend) without ever editing your power plans: it makes a copy of the plan, changes the copy and uses it, then deletes the copy and goes back to your plan when it ends. A copy left by a crash is removed at the next start, and a plan you pick yourself meanwhile is kept.
- Modes and tray: automatic switching can be paused for an hour (and resumed) from the Modes page or the tray menu. While it is paused nothing is started or ended, and the mode that is on stays on.
- Modes: two new modes. Focus silences the notification pop-ups (Do not disturb), keeps the screen and the PC awake on mains power, and leaves the services and the dev stack alone; add the chat and social apps to close in the mode editor. Eco is for the battery: Power saver plan, WSL and Docker stopped, background services calmed and transparency off.
- Modes: a mode can now turn on settings from the Optimize list while it is active, and puts them back when it ends. Only per-user settings that take effect at once are used, so there is no extra permission prompt, and a setting that was already on before the mode is left alone. The preview lists them under "Windows settings". Game mode now turns on Do not disturb.
- Optimize: 51 more settings, found by reading the code of about forty more open-source Windows optimizers. They cover telemetry and error-reporting policies, promotions in Windows and Edge, Recall, Click to Do, Copilot and other AI features, Search, AutoPlay, the Sticky Keys pop-ups, the startup-app delay, File Explorer and taskbar options, and Game Bar, audio ducking and notification sounds. Each one shows how many projects ship it, can be undone exactly, and those that need care carry a note. The Recall setting now says that snapshots already saved are removed.
- Modes: a switch above the mode cards turns automatic switching on or off, and says what it is doing ("Code mode is on: Claude Code is running", or the time left before it ends). A mode started automatically is labelled "ACTIVE (AUTO)", and the tray menu has the same switch. The page now shows a switch made by the tray, a hotkey or automatic switching at once instead of at its next refresh.
- Automation: a "Coding tools" section starts a mode (Code by default) while Claude Code, Codex, Cursor, T3 Code, OpenCode, Windsurf or VS Code is open, and ends it after the last one closes. Each tool is a switch; "Open now" shows which ones are running. Claude Code is told apart from the Claude chat app, so chatting does not start Code mode. Turning automatic switching on for the first time switches on the usual coding tools.
- Automation: a "Wait before returning to normal" setting (30 seconds to 10 minutes, one minute by default) and a line that says what automatic switching is doing now, with a countdown.
- Automation: each mode (Code, Work, Game, Focus, Eco) has its own list of the programs that start it. Add a program by typing its name, by picking it among the programs that are open (with its icon), or by browsing to its file; switch one off without losing it, and see which ones are open now. This is how a game starts Game mode, or Outlook or Teams start Work mode.

### Changed

- Optimize: starts with recommendations, explains choose/review/apply, separates advice, warnings, current state and scope with accessible badges, and keeps technical provenance in details.
- Optimize: quick preparation includes readable low-risk recommendations without warnings, leaves services to individual choices, and offers Clear filters and Ctrl+F search.
- Window: remembers normal size, position and maximized state and offers accessible interface zoom controls with Ctrl +/−/0.
- Window: opens at 1360 × 900 with more room for content, fitting smaller screens at their display scale.
- Services: the service knowledge base now also knows MMCSS, the Edge update services, the WSL and Hyper-V services, the Defender sensors and a few others that optimizers tend to disable, and marks them never to touch; Diagnostic Policy, IP Helper, Font Cache and Themes are now never to touch too, since other tools break Windows by switching them off. No mode stopped any of them.
- Optimize and modes: after writing a setting, WinModes reads it back. A setting that Windows refuses without an error (a policy, or the protection of default apps) is now reported as not applied instead of shown as applied, and nothing is recorded for it.
- Automation: a mode started automatically now stays on while any program or tool that triggers it is still open, and ends one minute after the last one closes, so going from Claude Code to Codex (or running both) no longer makes the PC go back and forth. A mode starts only after its trigger has held for 10 seconds, so a program that opens and closes at once changes nothing.
- Automation: automatic switching never replaces or undoes a mode you chose yourself. A mode it started is remembered, so it is still undone after WinModes was restarted.
- Automation: automatic switching waits 30 seconds after WinModes starts (Windows is still loading your programs) and stays quiet for 10 minutes after a switch that was refused or failed, instead of asking for the permission again at every check.
- Processes and Services: the pages open about four times faster and no longer freeze for more than a second at each refresh (Services rebuilt its whole list every five seconds). Only the rows in view are drawn, the column titles stay in place while the list scrolls, and the icons appear a moment after the list.
- Optimize: the page opens in about a third of the time and switching category is instant (it used to take up to two seconds on "All"): only the settings in view are drawn, the state of the PC is read in parallel and once at startup, and the page keeps what it showed when you come back, refreshing it in the background without moving the list or closing what you had opened.

- Debloat: the page shows its list in about half the time (the two PowerShell readings of the apps now run side by side, and the logos are found in parallel and remembered), shows what it knew at once when you come back, and the list is read once more shortly after WinModes starts. Nothing you ticked is lost when you leave the page and return.
- Automation: the page is laid out like the others, with the coding tools listed under the mode they start and the rules on battery or hours in a section of their own. A game outranks every other program, so a game opened next to Claude Code gets Game mode, and a program that only says "I am working" never hides another trigger.

### Fixed

- Optimize: warning filters include recommended settings with consequences, and summary/category counts match their recommendation and visible-row scope.
- Window: zoom, configuration and support buttons in the header now receive mouse clicks instead of dragging the window.
- Settings: language selection confirms the saved choice, preserves the pending choice when returning to the page and saves without changing the Windows startup entry.
- Interface: new Help and no-results messages now appear in French, Spanish and Italian, along with the operation-in-progress prompt.

- Debloat: failed or incomplete inventory reads keep the last valid reading and pause removal; details now open in Compact view and unavailable local restores are explained.

- Optimize, Processes and Services: pages open correctly with the shared search field; search uses the matching text event handler.

- Optimize and Debloat: the names of the categories in the bar at the top were black on the dark theme and hard to read; they now follow the theme.
- Services and Processes: a program that has no icon of its own, such as the shared host of most Windows services (Application Information, AppX Deployment, Base Filtering Engine and about 300 others), showed a blank picture; it now shows the usual symbol instead.

## [0.9.2] - 2026-10-02

### Changed

- Build: the GitHub actions and the test packages used to build and check WinModes are updated to their latest versions; nothing changes in the app.

## [0.9.1] - 2026-10-02

### Fixed

- Taskbar widget: the card of the AI tools memory now stays open when you rest the mouse on it, instead of being closed by each refresh of the widget.
- Widget page: privacy mode now hides the e-mail address of the WinModes account of Claude Code and Codex.

## [0.9.0] - 2026-10-02

### Added

- Widget: it can sit on the taskbar, on the desktop or on both ("Where it appears"). It fills the free room next to the app icons whether they are centred or on the left, reads that room again within a fraction of a second when you change the alignment or open or close an app, and shows less (network, then mode and CPU, then AI) when there is little room. You choose the side (automatic, left or right). Windows icons in colour and the tools' own icons (kept after the tool is closed) replace the text labels, the AI total is one line, and the limit resets you have in reserve ("↻ 2") are shown next to each plan.
- Widget: each of Claude Code and Codex has a "WinModes account": sign in once in your browser and WinModes reads the usage reliably, with a session of its own that is renewed by itself and never touches the sign-in of Claude Code or Codex. Tokens are kept encrypted for your Windows account and "Sign out" deletes them. The page shown in the browser once signed in matches the app. Anthropic does not document the Claude sign-in and may restrict it.
- Widget: for Claude, the limit resets in reserve are read too when the account is eligible.
- Widget: the Claude Code and Codex sections and their account rows show the logo of each tool, and the checks list whether WinModes is signed in.
- Widget: a "Check Claude Code" button lists, one by one, what is missing for the Claude usage to appear (plan, status line, last call of the status line, usage record, sign-in) and what to do about it.
- Widget: a "Check Codex" button does the same for Codex (folder, last usage record, plan, sign-in).
- Widget: resting the mouse on Claude or Codex for under a second opens a card instead of a plain tooltip: the tool and its plan, each limit with a coloured bar and the time it resets, the limit resets in reserve, and when the figures were read. It works on the taskbar and on the desktop widget, follows you from one tool to the next without waiting, and never takes the mouse or the focus.
- Widget: on the taskbar, resting the mouse on the AI tools figure opens a card with the total, its share of your memory and what each tool uses, with a bar of its share.
- Usage: a "Tokens" section counts the tokens Claude Code and Codex used, for today, the last 7 days or the last 30 days. For each tool: the total, how it splits (new input, output, cache read, cache write), a bar per day, and the models and projects that used the most. It is off by default: it reads the usage figures from the logs the two tools keep on this PC (never the text of a conversation, and nothing leaves the PC), the first read takes a few seconds and the next ones only what was added. Turning it off deletes the figures. `winmodes tokens [days]` prints the same in the command line.
- Widget: the Claude card shows the weekly limit of each model that has its own (Opus, Sonnet, Fable) and, when the account turned it on, the extra usage spent this month against its limit.
- Notifications: a new page lists everything WinModes can notify about and switches each one on or off, with a master switch and a "Send a test" button. For Claude Code and Codex: running low (5 %, 10 %, 20 % or 30 % left, for the 5 hour and the weekly limits), limit reached, available again once a limit you had used up starts over, and a limit reset added to your reserve. Also AI tools memory (all tools, or one tool), an idle session ended, mode switches, a new release, and a sign-in that expired.
- About: "Create support file" saves a zip to attach to a bug report: the version, facts about the PC (Windows, screens, taskbar, widget, which tools and sign-ins are present), your settings and the error logs. Your user name, PC name, e-mail addresses and anything that looks like a key are taken out first, and nothing is sent.
- Notifications: "Quiet hours" hold back the optional notifications between two times you choose (the end may be the next morning). Errors and the result of a mode switch you start are still shown; the limits are judged again when the quiet hours end, so none is missed, and a memory alert that was held back is shown then if the memory is still above its limit.
- Notifications: when both limits of a tool (5 hours and weekly) are used up, or both start over, in the same read, one notification tells both instead of two. A long notification is cut with an ellipsis before Windows cuts it.
- Notifications: what was already announced is remembered across restarts, so a limit left at 0 % is announced once per cycle instead of at every start of the app, and a release is announced once.
- Modes: when the administrator helper fails, the reason is written to `helper-errors.log` in `%ProgramData%\WinModes\logs` instead of being lost.

### Fixed

- Settings: when "Start with Windows" is on but its startup entry points to a file that no longer exists (WinModes was moved, reinstalled or updated elsewhere), Windows skipped it silently. WinModes now says so once at start, and the Settings page offers "Use this copy of WinModes" to repair the entry.
- Widget: with an auto-hide taskbar, the widget on the taskbar now slides out of sight with it instead of staying over the desktop, and comes back when the taskbar does.
- Notifications: a plan limit that stayed low or used up no longer raised the same notification each time WinModes started; the update notification is no longer repeated at each start either.
- Notifications: the plan notifications no longer need the widget to be visible; they are watched in the background as long as the plan usage is turned on.
- Widget: on the taskbar it no longer disappears for a moment when the Start menu opens or closes; it keeps its place while Windows hides the taskbar icons from the readout, and ignores buttons of other windows that appear there for an instant.
- Modes: a switch interrupted by a crash or a failed restore stays listed as active and is retried on the next undo, instead of being forgotten with services left changed.
- Services and Optimize: a damaged record of original settings is set aside as a `.corrupt-` file instead of being overwritten, so the values to restore are not lost.
- Automation: when several rules match, a lower-priority program no longer replaces the mode of a higher-priority program that is still running; it takes over when that program exits.
- Usage and widget: a plan or status-line answer holding an impossible date or number no longer stops the reading; the value is shown as unknown.
- Settings, History and Claude status line: files are saved in one step, so a crash or power loss can no longer leave a half-written settings, usage history or Claude Code settings file.
- Claude status line: installing or removing it keeps the first backup of your Claude Code settings instead of replacing it, and no longer rewrites accents or symbols elsewhere in that file.
- App: an unexpected error in a page, the tray menu or a hotkey no longer closes WinModes; it is written to `errors.log` in `%LocalAppData%\WinModes` and a notification is shown.
- Modes: asking for a mode switch while another is still running (page, tray menu, hotkey or automation) is refused with a message instead of stacking administrator prompts.
- Modes: a switch started from the prompt and one started by the silent task can no longer overlap: the administrator helper runs one command at a time.

### Changed

- Widget: when Anthropic or OpenAI fail or refuse a request, WinModes now asks that provider less and less often (up to once an hour) and waits as long as it says in its Retry-After, instead of asking again every 5 minutes; the other provider is not slowed down, and signing in or out asks again at once.
- Settings and Widget: the AI tools memory alerts and the "warn when Claude or Codex runs low" options moved to the new Notifications page. A choice you had made for the low-usage warning is kept.
- Widget: while WinModes is signed in to Claude or Codex, the "read the usage online" option of that tool is greyed out, since the usage is then read with its own session.
- Widget page: every option and every section has a coloured icon, and the options are laid out as rows in cards, so the page is easier to scan.
- Widget page: Claude Code and Codex each have their own section with their own options (show, read online, warn when low). The settings you already had are kept for both.
- Widget: the plan usage bars and percentages change colour with what is left: green with plenty left, then amber, and redder the closer to 0 %.
- Modes: listing and closing the apps of a mode, and measuring memory, no longer run on the interface thread, so the window stays responsive during a switch.
- Settings: preferences are read from disk once instead of at every refresh of the tray, widget and pages.
- Dashboard and widget: the live graphs redraw at most 30 times a second instead of 60, which lowers the CPU use of the app.

### Security

- The shared WinModes folder in ProgramData is now restricted to administrators, and one that already exists under another owner is refused.
- The administrator helper now refuses any command, service name, tweak id or mode name that is not in the expected form, including through the silent switch.
- About and updates: a release can now be signed with a key only the maintainer holds. When the app carries the matching public key it checks the signature of the list of checksums before downloading an update and refuses a release that is unsigned or wrongly signed; a checksum published beside the package only proved that the download was not damaged. Releases are signed from this version on; earlier versions do not carry the key and keep checking by checksum only.

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

[Unreleased]: https://github.com/LinkPhoenix/winmodes/compare/v0.9.3...HEAD
[0.9.3]: https://github.com/LinkPhoenix/winmodes/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/LinkPhoenix/winmodes/compare/v0.9.1...v0.9.2
[0.9.1]: https://github.com/LinkPhoenix/winmodes/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/LinkPhoenix/winmodes/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/LinkPhoenix/winmodes/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/LinkPhoenix/winmodes/releases/tag/v0.3.0
