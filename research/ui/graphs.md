# CPU and memory graphs: reference product, data sources, rendering

Research date: 2026-10-01. No source code or system setting was changed.

Claim markers used throughout:

- **[V]** verified in a page fetched during this research (URL given).
- **[S]** seen only in a web-search result snippet, page itself not read in full.
- **[M]** from memory / general knowledge, not re-verified. Check before relying on it.

---

## 1. The reference product: Task Manager TMOG

### Identification (high confidence)

| Item | Value | Marker |
| --- | --- | --- |
| Name | Task Manager TMOG ("Task Manager OG") | [V] https://tmog.org/ |
| Author | Dave Plummer, author of the original Windows NT 4.0 Task Manager | [V] https://tmog.org/ |
| Publisher | Plummers' Software LLC, (c) 2026 | [V] https://tmog.org/ |
| Site | https://tmog.org/ | [V] |
| Licence | Closed-source commercial. Free edition plus paid Pro edition (sold through Lemon Squeezy) | [V] https://tmog.org/ |
| Platforms | Windows 11 x64/ARM64 ("C++ + Win32"), macOS 14+ ("Swift + AppKit"), Linux ("C++ + Qt 6") | [V] https://tmog.org/ |
| Version | 1.0.0 RTM, Windows hotfix RTM.1 (1.0.1) | [V] https://tmog.org/rtm/release-notes.html |
| Windows renderer | Direct2D, on a shared C++ core | [S] search snippet, not found on the fetched tmog.org pages |
| Meters | "animated at 60 Hz" instead of one tick per second | [S] search snippet (vladan.fr / it-connect.tech coverage) |
| Built with | A 107-page spec fed to Claude Code | [S] Tom's Hardware headline |

The screenshot description (cyberpunk look, phosphor themes, memory page with
segmented bar and DDR details) matches press coverage that describes a
"cyberpunk vibe" [S] and the site's theme list (light, dark, green/amber/blue
phosphor, monochrome) [V] https://tmog.org/.

### What the author has published about the graphs

Nothing usable. The fetched home page and release notes contain **no**
statement on sampling rate, history length, smoothing, glow technique or fonts
[V]. The release notes only mention "selectable visual frame rates",
"performance history" and a system-load screensaver with shared drawing code
across platforms [V] https://tmog.org/rtm/release-notes.html.

Not fetched / could not be read: the X posts by @davepl1968, the Tom's Hardware
article body (paywall stub returned), any YouTube dev video. A dev video may
exist on the "Dave's Garage" channel [M]; it was not checked.

Consequence: the look must be reproduced from the screenshot, not from
documented internals. Do not copy TMOG assets or its font; reproduce the idea
only.

### Visual elements to reproduce (from the screenshot description)

1. Wide "futuristic" title font. Candidate free fonts: Orbitron, Michroma,
   Audiowide (all SIL OFL) [M]. Bundling a font is an asset decision, not a
   package dependency.
2. Segmented bar: about 100 small rounded blocks, lit up to the current
   percentage, value at the right.
3. Line chart: fine square grid, thin bright line, soft glow, accent-coloured
   1 px border, title top-left.
4. Stat grid below: label above value, two or three columns.

---

## 2. Data sources on Windows (.NET 10, P/Invoke, no extra package)

### 2.1 CPU, total: `GetSystemTimes` (kernel32)

[V] https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes

```
BOOL GetSystemTimes(PFILETIME lpIdleTime, PFILETIME lpKernelTime, PFILETIME lpUserTime);
```

- Values are 100 ns ticks, summed across all processors [V].
- **Kernel time includes idle time** [V].
- On systems with more than 64 logical processors it only covers the calling
  thread's primary processor group [V].

Formula (two samples, deltas `dIdle`, `dKernel`, `dUser`):

```
total = dKernel + dUser
busy  = total - dIdle
cpu%  = 100 * busy / total        (guard total == 0)
```

This yields **% Processor Time** (time-based), not what Task Manager shows.

### 2.2 CPU, per core: `NtQuerySystemInformation(SystemProcessorPerformanceInformation)`

[V] https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation

- Returns one `SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION` per processor:
  `LARGE_INTEGER IdleTime, KernelTime, UserTime, Reserved1[2]; ULONG Reserved2;`
  (100 ns units) [V].
- Class value is 8 and the struct is 48 bytes on x64 [M].
- KernelTime includes idle time here too, same formula as 2.1 per core [M]
  (the doc page does not say so explicitly).
- Microsoft warns the function "may be altered or unavailable in future
  versions of Windows" and advises run-time dynamic linking [V]. It is
  undocumented-ish but used by virtually every third-party monitor [M].
- Like `GetSystemTimes`, it reports the current processor group only; for
  >64 logical processors use `NtQuerySystemInformationEx` with a group
  number [M].

### 2.3 CPU as Task Manager shows it: PDH `% Processor Utility`

[V] https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/cpu-usage-exceeds-100

- Since Windows 8, Task Manager values "correspond to the Processor
  Information % Processor Utility ... counters, not to ... % Processor Time"
  [V].
- Utility counters account for processor performance state and Turbo Boost,
  so they can exceed 100 % and read lower than time-based values when the CPU
  is clocked down [V]. Task Manager clamps the display to 100 % [M].

Counter paths [M]:

- Total: `\Processor Information(_Total)\% Processor Utility`
- Per core: `\Processor Information(*)\% Processor Utility`, instances named
  `group,index` (e.g. `0,3`) plus `0,_Total` and `_Total`.

Access without a package: P/Invoke `pdh.dll` [M]:
`PdhOpenQueryW`, `PdhAddEnglishCounterW` (locale-independent names; the plain
`PdhAddCounterW` needs localized names, relevant on a French Windows),
`PdhCollectQueryData`, `PdhGetFormattedCounterValue` (`PDH_FMT_DOUBLE`),
`PdhGetFormattedCounterArrayW` for the wildcard, `PdhCloseQuery`. A rate
counter needs two collections before it returns a value [M].

`System.Diagnostics.PerformanceCounter` is **not** in the .NET 10 shared
framework; it is the `System.Diagnostics.PerformanceCounter` NuGet package
[M]. It also uses localized-name lookups and is slow to initialise [M].

**Recommendation:** PDH `% Processor Utility` (clamped to 0-100) as the
displayed CPU figure so it matches Task Manager; fall back to
`GetSystemTimes` if PDH fails to open (corrupt counter registry is a real
field failure [M]).

### 2.4 Memory: `GlobalMemoryStatusEx` and `GetPerformanceInfo`

`MEMORYSTATUSEX` [V] https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex

| Field | Meaning |
| --- | --- |
| `dwLength` | must be set before the call |
| `dwMemoryLoad` | 0-100, approximate % of physical memory in use (integer) |
| `ullTotalPhys` | physical memory, bytes |
| `ullAvailPhys` | available bytes = standby + free + zero lists |
| `ullTotalPageFile` | commit limit for system **or current process, whichever is smaller** |
| `ullAvailPageFile` | max the current process can commit |
| `ullTotalVirtual`, `ullAvailVirtual` | this process's address space, irrelevant here |

Despite their names, the `PageFile` fields are commit figures, and are
process-capped; the page says to use `GetPerformanceInfo` for system-wide
commit [V].

`PERFORMANCE_INFORMATION` (`GetPerformanceInfo`, psapi / `K32GetPerformanceInfo` in kernel32) [V]
https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information

All memory fields are in **pages**; multiply by `PageSize` [V].

| Field | Meaning |
| --- | --- |
| `CommitTotal` | pages currently committed |
| `CommitLimit` | max committable without extending the pagefile(s) |
| `CommitPeak` | peak since boot |
| `PhysicalTotal`, `PhysicalAvailable` | available = standby + free + zero |
| `SystemCache` | **standby list + system working set** |
| `KernelTotal`, `KernelPaged`, `KernelNonpaged` | kernel pools |
| `PageSize` | bytes |
| `HandleCount`, `ProcessCount`, `ThreadCount` | counts |

Fields are `SIZE_T` (use `nuint`), `cb` is `DWORD` [V].

Formulas:

```
inUse      = PhysicalTotal - PhysicalAvailable            (x PageSize)
inUse%     = 100.0 * inUse / PhysicalTotal                (one decimal, like "55.0%")
available  = PhysicalAvailable
committed  = CommitTotal / CommitLimit                     ("x / y GB")
pagedPool  = KernelPaged ; nonPagedPool = KernelNonpaged
```

Compute the percentage yourself: `dwMemoryLoad` is an integer and cannot give
"55.0 %" [V for the integer type].

**Cached.** Task Manager defines Cached as standby + modified [M].
`SystemCache` is standby + system working set [V], so it is close but **not
identical**. Options:

- Cheap and dependency-free: show `SystemCache * PageSize` and accept a small
  difference.
- Exact: PDH counters under `\Memory\` [M]:
  `Standby Cache Normal Priority Bytes` + `Standby Cache Reserve Bytes` +
  `Standby Cache Core Bytes` + `Modified Page List Bytes`. Since PDH is
  already opened for CPU (2.3), adding these to the same query is nearly free.

"Hardware reserved" = installed DIMM capacity (sum of
`Win32_PhysicalMemory.Capacity`, or `GetPhysicallyInstalledSystemMemory` in
KB) minus `ullTotalPhys` [M].

### 2.5 Swap / pagefile

Windows has no "swap" figure distinct from the pagefile; TMOG's "Swap used /
Swap available" is cross-platform wording [M, inference].

- Exact pagefile usage: `EnumPageFilesW` (psapi / `K32EnumPageFilesW`) with a
  callback [V] https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumpagefilesw.
  Callback receives `ENUM_PAGE_FILE_INFORMATION { cb, Reserved, TotalSize,
  TotalInUse, PeakUsage }`, in pages, plus the file name [M].
  `swapUsed = sum(TotalInUse) * PageSize`,
  `swapAvailable = sum(TotalSize - TotalInUse) * PageSize`.
- Alternative: PDH `\Paging File(_Total)\% Usage` [M].
- Approximation without enumeration:
  `pagefileTotal ~= (CommitLimit - PhysicalTotal) * PageSize` [M]. It gives
  size, not usage; do not present `CommitTotal - inUse` as "swap used".

### 2.6 RAM hardware details: `Win32_PhysicalMemory`

[V] https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
(namespace `root\CIMV2`, one instance per populated module)

| Displayed | Property | Notes |
| --- | --- | --- |
| Speed | `ConfiguredClockSpeed` (uint32, MHz, 0 if unknown; Windows 10+) [V] | fall back to `Speed`. The doc labels `Speed` as "nanoseconds" [V]; in practice it carries the SMBIOS MHz/MT/s value [M] |
| Form factor | `FormFactor` (uint16): 8 = DIMM, 12 = SODIMM, 0 = Unknown ... [V] | |
| Type | `SMBIOSMemoryType` (uint32, raw SMBIOS, Windows 10+) [V] | SMBIOS values: 24 DDR3, 26 DDR4, 34 DDR5, 30 LPDDR4, 35 LPDDR5 [M]. `MemoryType` is unreliable (often 0) [M] |
| Size | `Capacity` (uint64, bytes) [V] | |
| Slot label | `DeviceLocator`, `BankLabel` [V] | |
| Slots used | instance count / `Win32_PhysicalMemoryArray.MemoryDevices` | [M] sum over arrays |

Access options:

1. `System.Management` NuGet package (Microsoft, `ManagementObjectSearcher`)
   [M]. It is a new production dependency: per the project contract, ask
   first.
2. `Microsoft.Management.Infrastructure` (CIM/MI) NuGet: also a package [M].
3. **No dependency:** `GetSystemFirmwareTable('RSMB', 0, ...)` (kernel32) and
   parse SMBIOS type 16 (Physical Memory Array: number of devices) and type
   17 (Memory Device: size, form factor, type, speed, configured speed,
   locator) [M]. This is the same source WMI reads ("comes from the Memory
   Device structure in the SMBIOS information" [V]). About 100-150 lines of
   parsing; no admin rights needed [M].
4. WMI through COM interop (`WbemScripting.SWbemLocator` via `dynamic`):
   no package but late-bound and trimming-hostile [M]. Not recommended.

These values are static: read once at startup on a background thread, cache,
never poll. WMI first-query latency is typically hundreds of ms [M].

Slots that are soldered (LPDDR laptops) report form factor "Row of chips" or
similar; show "n/a" rather than a wrong value [M].

### 2.7 Sampling design

- One sampler service on a background timer (`PeriodicTimer`), 1 s period;
  marshal a small immutable snapshot to the UI thread. Never P/Invoke PDH or
  WMI on the UI thread [M].
- First CPU sample is meaningless (needs a delta): prime the sampler once and
  discard [M].
- Pause sampling when the window is minimised or the page is not visible
  [M].

---

## 3. Rendering a real-time line chart in WPF without a library

### 3.1 What Windows Task Manager does

- Performance graphs show a **60-second** window ("60 seconds" label at the
  bottom-left, "0" bottom-right), newest sample at the right [M].
- Default update speed "Normal" is about 1 s; High 0.5 s, Low 4 s. The window
  holds a fixed number of points, so the labelled span changes with the
  speed [M].
- CPU and memory use a **fixed 0-100 % scale** (memory: 0 to installed RAM);
  disk-transfer and network graphs auto-scale to the recent peak [M].
- Grid is static in the Windows 10/11 version (the scrolling grid was the
  classic XP-era look) [M].

None of this was verified on a fetched page.

### 3.2 Data: fixed ring buffer

- `double[] samples` of capacity 60 (or 61 so that 60 intervals fill the
  width), `head` index, `count`. O(1) append, zero allocation per sample [M].
- Do not use `ObservableCollection<Point>` / `PointCollection` bound to a
  `Polyline`: each change allocates and triggers change notification plus
  layout [M].

### 3.3 Element: custom `FrameworkElement` with `OnRender`

Microsoft guidance [V]
https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging:

- `Shape` objects derive from `FrameworkElement` and cost more memory;
  `Drawing`/`DrawingVisual` are lighter because they have no layout or event
  handling [V].
- `StreamGeometry` is "a lightweight alternative to PathGeometry" [V].

Practice [M]:

- Derive from `FrameworkElement`, override `OnRender(DrawingContext dc)`.
- On a new sample call `InvalidateVisual()` only. It re-runs `OnRender`
  without measure/arrange as long as no layout-affecting property changed.
  Do **not** register sample-carrying dependency properties with
  `AffectsMeasure`/`AffectsArrange`; `AffectsRender` at most.
- Give the element a size from its parent (stretch) and never compute
  desired size from data.
- Build one `StreamGeometry` per frame: `using var ctx = g.Open();
  ctx.BeginFigure(p0, isFilled, isClosed); ctx.LineTo(...)` or
  `PolyLineTo`; then `g.Freeze()`.
- Create pens and brushes once as `static readonly` and `Freeze()` them;
  recreate only on theme change. Frozen freezables skip change tracking and
  can cross threads.
- 60 points per second is trivial for WPF; the costs to avoid are layout,
  per-frame allocations of unfrozen freezables, and bitmap effects.

Draw order inside `OnRender`:

1. Background rectangle (also makes the element hit-testable).
2. Grid.
3. Area fill under the line.
4. Glow pass(es).
5. Core line.
6. Border.

### 3.4 Grid

- Fine square grid: pick a cell size in DIPs (for example 12-16) and derive
  the line count from `ActualWidth/ActualHeight`, or fix 10 rows (10 % each)
  and compute the column pitch equal to the row pitch so cells stay square
  [M].
- Crisp 1 px lines: snap to device pixels. Either `SnapsToDevicePixels` +
  `UseLayoutRounding`, or push a `GuidelineSet` with guidelines at
  `x +/- penThickness/2`, using `VisualTreeHelper.GetDpi(this)` to round to
  physical pixels [M]. Set `RenderOptions.SetEdgeMode(this,
  EdgeMode.Aliased)` only for the grid, never for the data line (it must stay
  anti-aliased) - so draw the grid in a separate child visual or use
  guidelines instead [M].
- Cache the grid: it changes only on resize or DPI change. Options: a frozen
  `DrawingGroup` rebuilt in `OnRenderSizeChanged` and drawn with
  `dc.DrawDrawing`, or a tiled frozen `DrawingBrush` (one cell) with
  `RenderOptions.SetCachingHint(brush, CachingHint.Cache)` [V for
  CachingHint, same URL as 3.3].

### 3.5 Line, fill and glow

- Core line: frozen `Pen`, thickness 1.25-1.5, `LineJoin = Round`,
  `StartLineCap/EndLineCap = Round` [M]. Round joins avoid miter spikes on
  sharp peaks.
- Area fill: second `StreamGeometry` (or the same figure closed down to the
  baseline, `isFilled: true`) filled with a frozen vertical
  `LinearGradientBrush`, accent at about 35 % alpha at the top to 0 % at the
  bottom [M].
- **Glow without effects:** stroke the same frozen geometry several times
  before the core line with wider, translucent, round-joined pens, e.g.
  widths 9 / 6 / 3.5 at alpha about 0.05 / 0.10 / 0.20 of the accent colour
  [M]. Three extra strokes of a 60-point polyline are negligible and stay on
  the GPU path.
- Why not `DropShadowEffect` / `BlurEffect`: they are GPU pixel-shader
  effects that render the element to an intermediate surface each time it
  changes; a full-size chart re-blurred every frame is wasteful, and in
  software rendering (RDP, some VMs, `RenderMode.SoftwareOnly`) they become
  very expensive [M]. The legacy `BitmapEffect` classes are obsolete and
  software-only [M]. An effect is acceptable on small **static** elements
  (title text), optionally with `BitmapCache` [M].
- Clip to the plot rectangle (`dc.PushClip(new RectangleGeometry(plot))`,
  frozen and cached per size) so the wide glow pens do not bleed over the
  border [M].
- Border: 1 px accent pen on a pixel-snapped rectangle, optionally with one
  faint wider pass for the same glow language [M].

### 3.6 Smoothness

Two independent things are called "smooth":

1. **Curve smoothing.** Task Manager draws straight segments [M]. If curves
   are wanted, use a monotone cubic (Fritsch-Carlson) or a low-tension
   Catmull-Rom converted to `BezierTo` segments [M]. Plain Catmull-Rom/
   cardinal splines overshoot and can draw below 0 % or above 100 %; clamp
   control points or use the monotone variant [M]. A light EMA on the value
   (alpha about 0.5) is an alternative, but it misreports peaks; keep the
   numeric readout unsmoothed [M].
2. **Motion smoothing (the TMOG "60 Hz" feel).** Sample at 1 Hz but scroll
   continuously: keep one extra off-screen sample, and between samples
   translate the plot left by `pitch * elapsedFraction` [M]. Two ways:
   - Cheapest: animate a `TranslateTransform.X` on the plot visual from 0 to
     `-pitch` over the sample interval with a `DoubleAnimation`; this runs
     on the composition thread without re-running `OnRender` [M].
   - Simpler to reason about: subscribe to `CompositionTarget.Rendering`
     while visible and call `InvalidateVisual()`; unsubscribe when hidden,
     minimised, or when `SystemParameters.ClientAreaAnimation` is false or
     the session is remote [M]. `CompositionTarget.Rendering` keeps WPF
     rendering every frame, so leaving it attached costs CPU/GPU
     permanently.
   Also tween the newest value and the percentage readout over about
   200-300 ms instead of snapping [M].

Start with 1 Hz redraw and straight segments; add scroll interpolation only
if the stepping is visually objectionable. It is a polish feature with a
measurable idle-CPU cost for a monitoring tool.

### 3.7 The segmented bar (about 100 blocks)

- One custom element, one `OnRender`, a loop of `dc.DrawRoundedRectangle`
  with two frozen brushes (lit / unlit) [M]. Not 100 `Border` elements in an
  `ItemsControl`: that is 100 layout participants per bar.
- Block count from width: `n = floor((width + gap) / (blockWidth + gap))`,
  or fix 100 and derive the block width; lit count = `round(n * pct / 100)`
  [M].
- Glow: draw each lit block first as a slightly inflated rounded rectangle
  at about 0.15-0.25 alpha, then the solid block; or one translucent
  rounded rectangle behind the whole lit run [M].
- Pixel-snap block edges, otherwise gaps look uneven at 125 %/150 % scaling
  [M].

### 3.8 Accessibility and robustness

- Provide an `AutomationPeer` (or `AutomationProperties.Name` plus a bound
  text value) so the chart is not an opaque surface to screen readers [M].
- Respect high-contrast and the reduced-animation setting [M].
- Handle zero size and `count < 2` in `OnRender` [M].
- Re-create cached geometry on `OnDpiChanged` (per-monitor DPI) [M].

### 3.9 When a chart library is justified

Not for this: two or three fixed-scale, 60-point, non-interactive line charts
are roughly 150-250 lines of owned code, and a library would fight the
custom glow/grid look.

A library becomes reasonable when requirements include zoom/pan, tooltips
and crosshair, labelled axes with auto-scaling and tick formatting, several
chart types, or very large series [M]:

- **ScottPlot** (MIT, `ScottPlot.WPF`, SkiaSharp-based): strong for large or
  streaming data (`DataStreamer`) [M].
- **LiveCharts2** (MIT, `LiveChartsCore.SkiaSharpView.WPF`, SkiaSharp):
  animated, styleable; its WPF package was long in release-candidate state -
  check current status [M].
- **OxyPlot** (MIT, `OxyPlot.Wpf`): mature, static-looking, little animation
  [M].

All three add native/managed dependencies (SkiaSharp for two of them) and
need the dependency approval required by the project contract. Licences and
package states above are from memory; verify on nuget.org before adopting.

---

## 4. Recommendation summary

- **Product:** Task Manager TMOG by Dave Plummer, closed source, free + Pro;
  no published graph internals. Reproduce the look, not the code or assets.
- **CPU:** PDH `\Processor Information(_Total)\% Processor Utility` via
  `PdhAddEnglishCounterW` (clamp 0-100), per-core via the wildcard path;
  `GetSystemTimes` as fallback.
- **Memory:** `GetPerformanceInfo` for in use / available / committed /
  pools, percentage computed as a double; Cached from PDH standby + modified
  counters (or `SystemCache` as an approximation); pagefile from
  `EnumPageFilesW`.
- **RAM hardware:** read once at startup; SMBIOS via `GetSystemFirmwareTable`
  for zero dependencies, or `System.Management` (`Win32_PhysicalMemory`) if
  a package is approved.
- **Rendering:** custom `FrameworkElement`, 60-sample ring buffer at 1 s,
  fixed 0-100 % scale, `StreamGeometry` in `OnRender`, frozen pens/brushes,
  cached pixel-snapped grid, gradient area fill, glow by layered translucent
  wide pens, no `BlurEffect`/`DropShadowEffect` on the live chart, no layout
  invalidation. Optional scroll interpolation later.

## Sources

Fetched:

- https://tmog.org/
- https://tmog.org/rtm/release-notes.html
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes
- https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/cpu-usage-exceeds-100
- https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex
- https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information
- https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumpagefilesw
- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging

Search results only (not read in full):

- https://www.vladan.fr/tmog-task-manager-the-original-creator-of-windows-task-manager-is-back-with-something-better/
- https://www.it-connect.tech/tmog-1-0-the-creator-of-windows-task-manager-launches-a-cross-platform-tool-for-windows-macos-and-linux/
- https://www.thurrott.com/windows/windows-11/341878/dave-plummer-has-made-the-task-manager-of-your-dreams
- https://www.windowscentral.com/microsoft/windows/dave-plummer-task-manager-creator-new-version-cyberpunk-mac-windows
- https://www.tomshardware.com/software/windows/windows-veterans-vibe-coded-task-manager-now-also-runs-on-mac-and-linux-downloadable-app-is-the-result-of-a-107-page-spec-fed-to-claude-code (fetch returned no article body)
- https://x.com/davepl1968/status/2096972289430765834
