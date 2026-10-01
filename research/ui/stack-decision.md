# UI stack decision for WinModes

Date: 2026-10-01. Evidence legend: **[code]** read in a repository file, **[api]** NuGet/GitHub API on 2026-10-01, **[docs]** official documentation read, **[memory]** recalled and not re-checked.

## Recommendation

**Stay on WPF / .NET 10 and add `WPF-UI` 4.3.0 (lepoco/wpfui, MIT).** Keep the existing WinForms `NotifyIcon` for the tray at first. Adding the package needs the user's approval.

Fallback if the dependency is declined: stay on the built-in Fluent theme and hand-build four small pieces (navigation rail, card style, info bar, toggle style). The design proposal is written so that both paths produce the same layout.

Reasons:

1. **It closes exactly the gaps of the built-in theme.** The .NET Fluent theme has no `NavigationView`, no `ToggleSwitch`, no `InfoBar`, no card or settings-card control and no title-bar extension API (see "What the built-in theme has" below). WPF-UI ships all of them.
2. **Lowest migration cost.** The app is one window with code-behind. `Window` becomes `ui:FluentWindow`; `WinModes.Core`, the tray code and the tests are untouched. WinUI 3 would mean a new project type, a different XAML dialect and a new TFM.
3. **.NET 10 is a declared target.** `Wpf.Ui.csproj` lists `net10.0-windows` first and the 4.3.0 package contains a `net10.0-windows7.0` group **[code][api]**. FluentFlyout ships a .NET 10 WPF tray app on it **[code]**.
4. **One light dependency.** `WPF-UI` 4.3.0 on net10 depends only on `WPF-UI.Abstractions` **[api]**. No Windows App SDK runtime, no MSIX, no self-contained bundle.
5. **Admin and tray stay simple.** WinModes will need elevation to stop services, and it lives in the tray. Both are ordinary in WPF. For WinUI 3 neither is built in (tray needs a third-party package or Win32 interop) **[memory]**.

Known risks, accepted:

- **Maintenance pace.** Last commit on `main` is 2026-06-27 and the repository has 454 open issues and PRs **[api]**. FluentFlyout uses a personal fork of the package **[code]**. Mitigation: pin the version, use only mainstream controls (FluentWindow, NavigationView, Card*, InfoBar, ToggleSwitch, ContentDialog, Snackbar), keep view-models free of `Wpf.Ui` types so the fallback stays cheap.
- **Theme ownership changes.** WPF-UI brings its own theme dictionaries; `ThemeMode="System"` in `App.xaml` and the `WPF0001` suppression would be removed and system theme following handed to WPF-UI **[memory]**. This must be verified in a spike (light, dark, high contrast, accent change at runtime).
- **Icons.** `ui:SymbolIcon` uses the Fluent System Icons font shipped in the package **[memory]**, not Segoe Fluent Icons. The design uses Segoe Fluent Icons glyphs in a plain `TextBlock` for content so it works on both paths; `SymbolIcon` is only used where WPF-UI requires an icon element.

## Comparison

| Criterion | .NET 10 WPF built-in Fluent (today) | WPF + WPF-UI 4.3.0 | WinUI 3 (Windows App SDK) |
|---|---|---|---|
| Look | Windows 11 control styles, light/dark/high contrast, accent **[docs]** | Windows 11 look with a wider control set **[code]** | Native Windows 11 controls |
| Backdrop | Mica applied automatically on Windows 11 22H2+; no API to choose another type; opt-out switch only **[docs][code]** | `WindowBackdropType="Mica"` on `FluentWindow` **[code]**; other values (Acrylic, Tabbed) **[memory]** | `MicaBackdrop`, `DesktopAcrylicBackdrop` **[code: WinUI Gallery]** |
| NavigationView | none (0 hits for `class NavigationView` in dotnet/wpf) **[api]** | yes, Left/Top/compact templates **[code]** | yes |
| Cards / settings rows | brushes only (`CardBackgroundFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`) **[code]** | `Card`, `CardControl`, `CardAction`, `CardExpander` **[code]** | Toolkit `SettingsCard`/`SettingsExpander` (extra package) **[code]** |
| InfoBar, ToggleSwitch, ContentDialog, Snackbar | none **[code/api]** | all present **[code]** | InfoBar, ToggleSwitch, ContentDialog built in |
| Custom title bar | manual `WindowChrome` work **[memory]** | `ui:TitleBar` + `ExtendsContentIntoTitleBar` **[code]** | `TitleBar` control **[code]** |
| Tray icon | none; WinForms `NotifyIcon` (current approach) | `WPF-UI.Tray` 4.3.0 optional **[code][api]** | none; third party or Win32 **[memory]** |
| API stability | `ThemeMode` still experimental (`WPF0001`); the official guide warns of breaking changes **[docs]** | stable 4.x line | stable 2.x line |
| .NET 10 | in-box | `net10.0-windows7.0` group in the package **[api]** | works: Winhance builds `net10.0-windows10.0.19041.0` with WASDK 1.8 **[code]** |
| Current version | .NET 10 | 4.3.0 (2026-05-04) **[api]** | 2.5.1 stable (2026-09-16) **[api]** |
| Dependency weight | zero | 2 small managed packages **[api]**; size on disk not measured | WASDK runtime; unpackaged apps either need the runtime installed or ship self-contained **[memory]**; size not measured |
| Packaging | plain `dotnet publish` | same | MSIX, or unpackaged (`WindowsPackageType=None`) + self-contained as Winhance does **[code]** |
| Memory | not measured | not measured | not measured |
| Maintenance | Microsoft, in-box; described as work in progress **[docs]** | community; 3 months since last commit, 454 open items **[api]** | Microsoft, active (pushed 2026-09-30) **[api]** |
| Migration cost from current app | none | small: window base class, `App.xaml` dictionaries, new pages | high: new project, XAML rewrite, new tray and elevation plumbing |

No memory or startup measurements were taken. The usual claim that a WinUI 3 app idles higher than a comparable WPF app is **[memory]** and should not drive the decision without a measurement.

## What the built-in theme has (verified)

From `PresentationFramework.Fluent` in dotnet/wpf `main` **[code]**:

- Styles for standard WPF controls only: Button (`DefaultButtonStyle`, `AccentButtonStyle`), CheckBox, ComboBox, Expander, ListBox, ListView, GridView, ProgressBar, RadioButton, Slider, TabControl, TextBox, ToggleButton, ToolTip, TreeView, Menu, ScrollBar and others. Directory: <https://github.com/dotnet/wpf/tree/main/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles>
- Text styles: `CaptionTextBlockStyle`, `BodyTextBlockStyle`, `BodyStrongTextBlockStyle`, `SubtitleTextBlockStyle`, `TitleTextBlockStyle`, `TitleLargeTextBlockStyle`, `DisplayTextBlockStyle`.
- Brush keys present in `Fluent.Light.xaml`: `CardBackgroundFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`, `TextFillColorSecondaryBrush`, `AccentFillColorDefaultBrush`, `AccentTextFillColorPrimaryBrush`, `SystemFillColorSuccessBrush`, `SystemFillColorCautionBrush`, `SystemFillColorCriticalBrush`, `SubtleFillColorSecondaryBrush`, `ControlFillColorDefaultBrush`, `LayerFillColorDefaultBrush`, `SolidBackgroundFillColorBaseBrush`; also `ControlCornerRadius`, `OverlayCornerRadius` and the font key `SymbolThemeFontFamily`.
- Backdrop: `WindowBackdropManager` is `internal`; Mica (`DWMSBT_MAINWINDOW`) is set when the Fluent theme is on, gated on Windows 11 22H2 or newer; the only control is the `Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop` switch. Guide: <https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md> (the guide is titled for .NET 9 and was not updated for .NET 10; whether .NET 10 changed any of this was not verified).
- Not present: NavigationView, ToggleSwitch, InfoBar, SettingsCard, TitleBar.

## Options considered and rejected

- **WinUI 3.** Best fidelity and the stack of every Microsoft reference app studied, but it costs a rewrite and adds packaging and tray work for an app whose UI is six pages. Revisit only if WPF-UI stalls and the hand-built fallback proves insufficient.
- **iNKORE.UI.WPF.Modern.** Has the most faithful `SettingsCard`/`SettingsExpander` port for WPF **[code]**, but the 0.10.2.1 package targets only net6.0-windows and net452, the version is pre-1.0, and the license is a custom text that was not reviewed **[api]**.
- **ModernWpf.** `ModernWpfUI` 1.0.0-rc.1 does target net10.0-windows and the repository is active again **[api]**, but it is a release candidate, models WinUI 2 visuals, and its card/settings controls were not examined. Worth a second look if WPF-UI is rejected.
- **Avalonia** (UniGetUI's current stack). Cross-platform is of no value for a Windows service switcher.

## Suggested package set (pending approval)

| Package | Version | Purpose | Required |
|---|---|---|---|
| `WPF-UI` | 4.3.0 | controls, window, theme | yes |
| `CommunityToolkit.Mvvm` | 8.4.2 | observable view-models and commands via source generators | optional; the alternative is hand-written `INotifyPropertyChanged` |
| `WPF-UI.Tray` | 4.3.0 | XAML tray icon | no; keep WinForms `NotifyIcon` until there is a reason to switch |

## Spike before committing (half a day)

1. Add `WPF-UI` to a throwaway branch, switch `MainWindow` to `ui:FluentWindow` with `NavigationView` and three `CardAction` tiles.
2. Check: Mica on Windows 11 26200, runtime switch of system light/dark, high contrast theme, accent colour change, keyboard-only navigation, Narrator reading of nav items and cards, window when run elevated.
3. Check that the tray `NotifyIcon` and `ShutdownMode="OnExplicitShutdown"` still behave.
4. Record working set after idle for both variants if memory matters to the decision.
