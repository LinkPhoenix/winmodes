# UI inspiration for WinModes

Research date: 2026-10-01. Read-only research; no code or system setting was changed.

Evidence legend used throughout:

- **[code]** read from the repository file linked next to it (raw file fetched on 2026-10-01).
- **[api]** GitHub REST API / NuGet API metadata fetched on 2026-10-01.
- **[memory]** recalled from general knowledge, not re-checked. Treat as a lead, not a fact.

## Projects

Stars, license, archive flag and last push come from `gh api repos/<owner>/<repo>` **[api]**. "NOASSERTION" from GitHub means the license could not be classified; the real license is noted when the file was read.

| Project | Stars | License | UI framework | Status (last push) | Relevance |
|---|---|---|---|---|---|
| [microsoft/PowerToys](https://github.com/microsoft/PowerToys) | 139k | MIT | WinUI 3 + Windows Community Toolkit **[code]** | active (2026-09-30) | Shell, dashboard with module toggles, tray quick-access flyout, lock icon for policy-locked items |
| [microsoft/devhome](https://github.com/microsoft/devhome) | 91 | MIT | WinUI 3 **[code]** | **archived** (2025-02-21) | Review -> apply -> summary flow, per-task progress with retry |
| [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery) | 3.7k | MIT | WinUI 3 **[code]** | active | Reference for TitleBar + NavigationView + Mica, card grid home page |
| [files-community/Files](https://github.com/files-community/Files) | 45.8k | MIT | WinUI 3 **[code]** | active | Status center (running operations list) |
| [lepoco/wpfui](https://github.com/lepoco/wpfui) | 9.7k | MIT | WPF library, targets net10.0-windows **[code]** | last commit 2026-06-27, 4.3.0 released 2026-05-04, 454 open issues+PRs **[api]** | NavigationView, FluentWindow (Mica), CardControl/CardAction/CardExpander, InfoBar, Snackbar, ToggleSwitch, tray icon |
| [memstechtips/Winhance](https://github.com/memstechtips/Winhance) | 13.3k | **PolyForm Shield 1.0.0** (source-available, not OSI) **[code]** | **WinUI 3** on .NET 10, Windows App SDK 1.8, unpackaged self-contained **[code]** (the brief assumed WPF) | active | Closest functional cousin: optimizer with review-before-apply bar, badges, task progress |
| [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil) | 63.4k | MIT | WPF XAML loaded from PowerShell **[code]** | active | Presets, "Run Tweaks" / "Undo Selected Tweaks", `OriginalValue` per tweak |
| [builtbybel/CrapFixer](https://github.com/builtbybel/CrapFixer) | 2.5k | MIT | WinForms, .NET Framework 4.8.1 **[code]** | active | Analyze -> Fix -> Restore verbs |
| [builtbybel/Flyoobe](https://github.com/builtbybel/Flyoobe) | 7.4k | MIT | not determined (only the legacy WinForms `Flyby11` project was located) | active | not studied further |
| [hellzerg/optimizer](https://github.com/hellzerg/optimizer) | 18.3k | GPL-3.0 | WinForms with custom-drawn controls **[code]** | **archived** (2026-01-20) | ToggleCard: one card = label + toggle with accessible names |
| [Devolutions/UniGetUI](https://github.com/Devolutions/UniGetUI) (the brief's `marticliment/UniGetUI` resolves here) | 26.3k | MIT | **Avalonia 12** in the current tree (`src/UniGetUI.Avalonia`) **[code]** | active | Operations panel docked at the bottom, live-announcement host for screen readers |
| [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout) | 4.6k | GPL-3.0 | WPF .NET 10 + WPF-UI (a **fork**, `unchihugo.WPF-UI` 4.4.2) + MicaWPF + WPF-UI.Tray **[code]** | active | Proof that WPF + WPF-UI on .NET 10 ships; settings window with NavigationView and CardAction home grid |
| [File-New-Project/EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | 11.4k | custom (license text opens with a list of excluded entities; rest not read) **[code]** | WPF, .NET Framework 4.6.2 **[code]** | active | Tray flyout window anchored to the taskbar, acrylic, entrance/exit animation |
| [xanderfrangos/twinkle-tray](https://github.com/xanderfrangos/twinkle-tray) | 9.1k | MIT | Electron 43 + React 18 **[code]** | active | Tray panel look (CSS-emulated Mica); not reusable in XAML |
| [seerge/g-helper](https://github.com/seerge/g-helper) | 15.4k | GPL-3.0 | WinForms, .NET 10, custom `RButton`/`RForm` **[code]** | active (v0.285, 2026-09-26) | Mode switching as a row of large icon buttons with a coloured "activated" border |
| [petrroll/PowerSwitcher](https://github.com/petrroll/PowerSwitcher) | 464 | MIT | WPF **[code]** | dormant (2022) | Minimal tray flyout listing power plans |
| [iNKORE-NET/UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) | 1.0k | custom `LICENSE.md` ("iNKORE.UI.WPF.Modern Software License"; terms not read) | WPF library; NuGet 0.10.2.1 targets net6.0-windows and net452 only **[api]** | last commit 2026-07-25 | WPF port of SettingsCard / SettingsExpander, NavigationView, InfoBar |
| [Kinnara/ModernWpf](https://github.com/Kinnara/ModernWpf) | 5.0k | MIT | WPF library; `ModernWpfUI` 1.0.0-rc.1 targets net462, net8.0-windows, net10.0-windows **[api]** | commits in 2026-09 ("Prepare 1.0.0-rc.1"); latest GitHub release is still v0.9.6 from 2022 **[api]** | WinUI 2-style NavigationView for WPF |
| [CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows) | 1.1k | license file not classified by GitHub; MIT **[memory]** | WinUI 3 / UWP | active | Canonical SettingsCard / SettingsExpander metrics |
| [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) | 3.8k | MIT **[memory]** | UI-agnostic | active | `CommunityToolkit.Mvvm` 8.4.2 (source generators), works with WPF |
| [dotnet/wpf](https://github.com/dotnet/wpf) | 7.7k | MIT | - | active | The built-in Fluent theme WinModes uses today |

Not covered: a dedicated open-source "game mode / profile switcher" tray app other than g-helper and PowerSwitcher. No further candidates were searched for.

## Patterns by project

### PowerToys Settings (WinUI 3) - the main structural reference

Files read **[code]**:

- Shell: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI/SettingsXAML/Views/ShellPage.xaml>
- Dashboard: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI/SettingsXAML/Views/DashboardPage.xaml>
- Module list with toggles: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI.Controls/ModuleList/ModuleList.xaml>
- Card primitive: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI.Controls/Primitives/Card.xaml>
- Page frame: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI/SettingsXAML/Controls/SettingsPageControl/SettingsPageControl.xaml>
- General page (admin section, backup/restore): <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/Settings.UI/SettingsXAML/Views/GeneralPage.xaml>
- Tray flyout window: <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/QuickAccess.UI/QuickAccessXAML/MainWindow.xaml> and <https://github.com/microsoft/PowerToys/blob/main/src/settings-ui/QuickAccess.UI/QuickAccessXAML/Flyout/LaunchPage.xaml>

Patterns:

- **Shell**: a 48 px `TitleBar` row (icon, pane toggle, centred `AutoSuggestBox` bound to Ctrl+F) above a `NavigationView` whose content background is made transparent so Mica shows through. Back button hidden; built-in settings item hidden; the first two items are "Dashboard" (glyph `E80F`, Home) and "General", then a separator, then grouped expandable items.
- **Dashboard**: page title with `AutomationProperties.HeadingLevel="1"`; content capped by a `PageMaxWidth` resource; two columns (left: "Quick access" and "Shortcuts overview" cards; right: a 400 px min-width "Utilities" card holding the module list, with a sort button in the card title). An `AdaptiveTrigger` at 840 px moves the right card under the left ones.
- **Card**: `CardBackgroundFillColorDefaultBrush` + 1 px `CardStrokeColorDefaultBrush` + `OverlayCornerRadius`, a 44 px title row (16 px SemiBold, `HeadingLevel="Level2"`), an optional 1 px divider, then content.
- **Module row**: a flat `SettingsCard` (transparent, no corner radius, only a bottom divider) with icon, label, optional "New" `InfoBadge`, and a `ToggleSwitch` with empty On/Off text whose `AutomationProperties.Name` is bound to the module label.
- **Locked items**: when a module is controlled by policy the row shows a lock glyph `E72E` with a tooltip and the toggle is disabled, rather than the row being hidden. This is the affordance WinModes needs for `data/protected.json`.
- **Empty state**: each card contains a secondary-coloured sentence ("No actions to show") swapped in by a count-to-visibility converter.
- **Admin**: an informational `InfoBar` explains the elevation state, a "Restart as administrator" button sits in a settings card, and the dependent option is disabled until elevated. Restart-required changes use an `InfoBar` with an action button.
- **Tray flyout**: a separate 400x516 non-resizable, always-on-top window without title bar, acrylic backdrop with explicit light/dark fallback colours, content on `LayerOnAcrylicFillColorDefaultBrush`, and a 48 px footer row of 32x32 subtle icon buttons (docs, bug report, settings), each with a tooltip.

### WPF UI (lepoco/wpfui) and its gallery

Files read **[code]**:

- Gallery window: <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui.Gallery/Views/Windows/MainWindow.xaml>
- Gallery `App.xaml`: <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui.Gallery/App.xaml>
- Gallery dashboard: <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui.Gallery/Views/Pages/DashboardPage.xaml>
- Gallery settings: <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui.Gallery/Views/Pages/SettingsPage.xaml>
- CardControl template: <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Controls/CardControl/CardControl.xaml>
- MVVM sample window (fetched, only skimmed): <https://github.com/lepoco/wpfui/blob/main/samples/Wpf.Ui.Demo.Mvvm/Views/MainWindow.xaml>
- Library project (target frameworks): <https://github.com/lepoco/wpfui/blob/main/src/Wpf.Ui/Wpf.Ui.csproj>
- Control inventory (directory listing): <https://github.com/lepoco/wpfui/tree/main/src/Wpf.Ui/Controls>
- Tray (directory listing): <https://github.com/lepoco/wpfui/tree/main/src/Wpf.Ui.Tray>

Patterns:

- **Window**: `ui:FluentWindow` with `ExtendsContentIntoTitleBar="True"` and `WindowBackdropType="Mica"`; a `ui:TitleBar` element overlays the top of the grid; the same grid hosts `ui:NavigationView`, a `ui:ContentDialogHost` and a `ui:SnackbarPresenter` (inside `NavigationView.ContentOverlay`).
- **Navigation**: `ui:NavigationView PaneDisplayMode="Left"` with `MenuItemsSource` / `FooterMenuItemsSource`, an optional `BreadcrumbBar` in the header, `Transition="FadeInWithSlide"`. Items (seen in FluentFlyout) are `ui:NavigationViewItem Content=... Icon="{ui:SymbolIcon Home24}" TargetPageType="{x:Type pages:HomePage}"`.
- **Theme resources**: `App.xaml` merges `ui:ThemesDictionary` and `ui:ControlsDictionary`. That these must replace, not sit beside, the built-in `ThemeMode` Fluent dictionaries is **[memory]**.
- **Home page**: a hero `Border` (image + gradient, 8 px radius) and a three-column grid of `ui:CardAction` tiles (60 px image, BodyStrong title, secondary caption, chevron hidden).
- **Settings rows**: `ui:CardControl Icon="{ui:SymbolIcon Color24}"` with a two-line header and the control on the right; `ui:CardExpander` for expandable sections.
- **Controls present in the library**: NavigationView, TitleBar, FluentWindow, Card, CardAction, CardControl, CardExpander, InfoBar, InfoBadge, Badge, Snackbar, ContentDialog, MessageBox, ToggleSwitch, ProgressRing, BreadcrumbBar, AutoSuggestBox, Flyout, DropDownButton, SplitButton, NumberBox, LoadingScreen, IconElement/IconSource.
- **Tray**: `tray:NotifyIcon` declared in XAML with `FocusOnLeftClick`, `MenuOnRightClick` and a normal WPF `ContextMenu`.

### FluentFlyout - WPF-UI in a shipping .NET 10 tray app

Files read **[code]**:

- Project file: <https://github.com/unchihugo/FluentFlyout/blob/master/FluentFlyoutWPF/FluentFlyout.csproj>
- Settings window: <https://github.com/unchihugo/FluentFlyout/blob/master/FluentFlyoutWPF/SettingsWindow.xaml>
- Home page: <https://github.com/unchihugo/FluentFlyout/blob/master/FluentFlyoutWPF/Pages/HomePage.xaml>
- Flyout window: <https://github.com/unchihugo/FluentFlyout/blob/master/FluentFlyoutWPF/MainWindow.xaml>

Patterns: `net10.0-windows10.0.22000.0`; `ui:FluentWindow` + Mica; `NavigationView PaneDisplayMode="Left"` with nine items using `TargetPageType`; "About" as the last item; home is a 2-column grid of `ui:CardAction` with a 30 px `SymbolIcon` and a chevron; the flyout itself is a `WindowStyle="None"`, `ShowInTaskbar="False"` window; tray context menu items each carry a `SymbolIcon`. Notable: the app references a personal fork of WPF-UI rather than the upstream package. The reason was not investigated.

### Winhance - closest functional cousin (WinUI 3)

License warning: PolyForm Shield. Use for ideas only; do not copy XAML.

Files read **[code]**:

- Project file: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Winhance.UI.csproj>
- Main window: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/MainWindow.xaml>
- Sidebar: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Common/Controls/NavSidebar.xaml>
- Setting row: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Common/Controls/SettingsCardItem.xaml>
- Badges: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Common/Resources/BadgeStyles.xaml> and <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Common/Controls/SettingDescriptionWithBadges.xaml>
- Progress: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Common/Controls/TaskProgressControl.xaml>
- Optimize page: <https://github.com/memstechtips/Winhance/blob/main/src/Winhance.UI/Features/Optimize/OptimizePage.xaml>

Patterns:

- **Window rows**: custom 48 px title bar; an elevation `InfoBar` (warning) row; an update `InfoBar` row; a "review mode" bar; then sidebar + `Frame`; up to three stacked `TaskProgressControl`s under the content; a full-window loading overlay with logo and tagline at startup.
- **Review-before-apply bar**: a full-width accent-coloured strip with icon, title, wrapping description, a status text, and Apply / Cancel buttons using on-accent text brushes. Apply is bound to a `CanApplyReviewedConfig` flag.
- **Sidebar**: custom `NavButton`s split into a top group (main areas) and a bottom group (advanced tools, settings, "More" menu with version, docs, bug report, update check, open logs).
- **Badges**: pill `Border`s with paired background/border/foreground brushes for Recommended (green), Default (grey), Custom (amber), Preference (blue), Danger (red), each defined separately for Light and Dark. Pills wrap under the description.
- **Per-setting quick actions**: a small icon button next to each control sets it to the recommended value, with tooltip and `AutomationProperties.Name`.
- **Page header**: breadcrumb with a section drop-down carrying `InfoBadge` counts, a "Quick actions" `DropDownButton` (apply recommended / reset defaults) and a "View" menu with a "technical details" toggle.
- **Task progress**: name, last output line, details button, cancel button, queue status, progress bar.

### g-helper - mode buttons

Files read **[code]**: <https://github.com/seerge/g-helper/blob/main/app/Settings.Designer.cs>, <https://github.com/seerge/g-helper/blob/main/app/UI/RButton.cs>, <https://github.com/seerge/g-helper/blob/main/app/UI/RForm.cs>, <https://github.com/seerge/g-helper/blob/main/app/Mode/Modes.cs>.

Patterns: each section is a top-docked panel with a header label ("Performance Mode") and a status label, followed by a one-row, four-column table of equal-size buttons (about 188x120 px, 48 px image above the text, mnemonics `&Silent`, `&Balanced`, `&Turbo`). `RButton` has an `Activated` flag and a `BorderColor`, drawn as a rounded border; `RForm` defines per-mode colours (`colorEco` green, `colorStandard` blue, `colorTurbo` red). The fourth button ("Fans + Power") is flagged `Secondary` and uses a darker fill. Custom modes are appended to the built-in three (`Modes.cs`). Footer: "Run on startup" checkbox and Quit. That the window opens from the tray icon is **[memory]**.

Takeaway for WinModes: three equal tiles, one obviously active, a secondary tile or link for details, live status on the header line. Use the per-mode colour idea sparingly: state must never be conveyed by colour alone.

### winutil - presets and undo

Files read **[code]**: <https://github.com/ChrisTitusTech/winutil/blob/main/xaml/inputXML.xaml>, <https://github.com/ChrisTitusTech/winutil/blob/main/functions/public/Invoke-WPFundoall.ps1>, <https://github.com/ChrisTitusTech/winutil/blob/main/config/tweaks.json>, <https://github.com/ChrisTitusTech/winutil/blob/main/config/preset.json>.

Patterns: top tab buttons; "Recommended Selections: Standard / Minimal" preset buttons; one "Run Tweaks" and one "Undo Selected Tweaks" button side by side; each registry tweak declares `OriginalValue`, so undo is data-driven; undo runs as a job reporting "Undoing X (i/n)" with a percentage. Weakness to avoid: undo restores a declared original rather than the value that was actually present on the machine, which is the opposite of the WinModes journaling invariant. No preview step was seen in the files read.

### DevHome setup flow - review, progress, retry

Files read **[code]**: <https://github.com/microsoft/devhome/blob/main/tools/SetupFlow/DevHome.SetupFlow/Views/ReviewView.xaml>, <https://github.com/microsoft/devhome/blob/main/tools/SetupFlow/DevHome.SetupFlow/Views/LoadingView.xaml>, <https://github.com/microsoft/devhome/blob/main/src/Views/ShellPage.xaml>, <https://github.com/microsoft/devhome/blob/main/common/Environments/Styles/HorizontalCardStyles.xaml>.

Patterns: the review page has an error `InfoBar` at the top, an `Expander` describing what will be done, and a footer with a "read and agree" checkbox next to an accent button whose tooltip is bound to an explanation text. The loading page shows one progress bar plus a task list where each row has an 18 px slot for a progress ring or a result glyph; a "some tasks failed" banner (glyph `F167`) offers a retry command. The shell has one global `InfoBar` driven by a view-model (severity, title, message, open). `SummaryView.xaml` was fetched but not analysed.

### Files - status centre

File read **[code]**: <https://github.com/files-community/Files/blob/main/src/Files.App/UserControls/StatusCenter/StatusCenter.xaml>.

Pattern: a list of operations, each with a severity-coloured icon chip, header text, a close button, a chevron that expands a detailed view, and a progress bar in the collapsed state. Good model for History rows.

### WinUI Gallery

Files read **[code]**: <https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/MainWindow.xaml>, <https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Pages/HomePage.xaml>.

Pattern: `Window.SystemBackdrop = MicaBackdrop`, the stock `TitleBar` control with a search box as content, `NavigationView` with `FontIcon` glyph icons; home is a header control plus token-style `SelectorBar` filters above card `GridView`s with 8 px item radius.

### Windows Community Toolkit SettingsCard - metrics worth copying

File read **[code]**: <https://github.com/CommunityToolkit/Windows/blob/main/components/SettingsControls/src/SettingsCard/SettingsCard.xaml>.

Values: padding 16; min height 68; border 1; header icon max 20 px with margin `2,0,20,0`; description font 12; action chevron 13 px; content min width 120; wrap threshold 476 px. A WPF port exists in iNKORE (usage page read only): <https://github.com/iNKORE-NET/UI.WPF.Modern/blob/main/source/iNKORE.UI.WPF.Modern.Gallery/Pages/Controls/Community/SettingsCardPage.xaml>.

### Tray flyouts: EarTrumpet, PowerSwitcher, Twinkle Tray

Files read **[code]**: <https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/UI/Views/FlyoutWindow.xaml>, <https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/UI/Views/FlyoutWindow.xaml.cs>, <https://github.com/petrroll/PowerSwitcher/blob/master/PowerSwitcher.TrayApp/MainWindow.xaml>, <https://github.com/xanderfrangos/twinkle-tray/blob/master/src/css/mica.scss>.

Patterns: the flyout is a borderless (`WindowStyle="None"`, `AllowsTransparency="True"`), `ShowInTaskbar="False"`, `ResizeMode="NoResize"` window. EarTrumpet positions it relative to the taskbar edge (left/right/top/bottom, auto-hide handled), enables acrylic, and plays entrance/exit animations. PowerSwitcher is the minimal version: a 360 px wide, top-most list of power plans with a highlighted selected item, wired to `Deactivated` and `PreviewKeyDown`. Twinkle Tray draws its own Mica in CSS, which confirms the visual target but offers nothing reusable.

### CrapFixer, optimizer (WinForms)

Files read **[code]**: <https://github.com/builtbybel/CrapFixer/blob/main/CFixer/MainForm.Designer.cs>, <https://github.com/hellzerg/optimizer/blob/master/Optimizer/Controls/ToggleCard.cs>.

Patterns: CrapFixer puts three verbs on one screen - Analyze, Run Fixer, Restore - plus a log box and a per-item context menu (Analyze / Fix / Restore / Help). "Analyze first" is the same idea as the WinModes preview. optimizer's `ToggleCard` sets `AccessibleName` on the card, label and toggle from the same label text.

### UniGetUI (Avalonia)

File read **[code]**: <https://github.com/Devolutions/UniGetUI/blob/main/src/UniGetUI.Avalonia/Views/MainWindow.axaml> (element names only).

Patterns: custom title bar with back button, sidebar toggle and global search; nav rail; an operations panel under the content separated by a `GridSplitter`; a `LiveAnnouncementHost` text block (presumably for screen-reader announcements); an "inactive Mica fallback" border. No WinUI 3 app project was found in the current `src` listing.

## Cross-project patterns to adopt

1. **Shell = title bar + left NavigationView + Mica**, content capped at a max width (PowerToys, WinUI Gallery, WPF-UI gallery, FluentFlyout).
2. **Large equal mode tiles with one unmistakable active state** (g-helper), built from card tiles (WPF-UI `CardAction`, FluentFlyout home).
3. **Rows inside cards / expanders: icon, title, secondary line, right-aligned control** (Toolkit SettingsCard metrics, PowerToys ModuleList, WPF-UI CardControl/CardExpander).
4. **Locked items stay visible with a lock glyph and tooltip** (PowerToys ModuleList).
5. **Review bar gating Apply, then per-item progress, then a result with Undo/Retry** (Winhance review bar, DevHome review/loading views, winutil undo job).
6. **InfoBar for elevation and errors, not a modal** (PowerToys General, Winhance, DevHome shell).
7. **Tray flyout as a small borderless window anchored to the taskbar** (PowerToys Quick Access, EarTrumpet, PowerSwitcher).
