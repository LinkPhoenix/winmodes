# Lessons for win-modes

## Reuse
- **Declarative tweak catalog**: follow winutil's `tweaks.json`. Each action is data (registry/service/task/appx/script) and carries its own undo, as privacy.sexy does with `code`/`revertCode` and Sophia with `-Enable/-Disable`. Our catalog should hold facts and our modes should be *compositions* of catalog items.
- **Snapshot real state, not defaults**: winutil's `OriginalValue` is the Microsoft default. Our app should read the live value, including when a key is absent, before every change and keep it in a journal. That makes an exact revert possible, which the project rules already requires.
- **Safety tiers per item**: add fields like Win11Debloat's `Recommendation` and privacy.sexy's standard/strict. Our `risk` must include `never`, and the engine must refuse `never` items.
- **State detection UI**: show the current real value next to the target, as Winhance does.
- **Default vs recommended vs live**: Winhance stores both `DefaultValue` and `RecommendedValue` per registry entry. We need three values: Microsoft default, mode target, and the live snapshot from the journal.
- **Explicit dependencies between items**: Winhance declares `ParentSettingId` / `RequiredSettingId` (Memory Integrity needs VBS, Prefetch needs SysMain). Our catalog should declare dependencies and conflicts so a mode cannot apply an item whose prerequisite it turns off.
- **OS-build guards**: check the build before applying anything, as Sophia does.
- **Restore point** before the first apply (winutil). This is only a fallback; the journal is the main undo.

## Gaps none of them fill
1. True multi-mode switching (A → B → baseline) with a diff between modes.
2. Orchestrating WSL/Docker per mode: `.wslconfig` limits, `wsl --shutdown`, starting and stopping Docker Desktop and `com.docker.service`, Node/dev servers.
3. Live-only changes: most tools write persistent start types and need a reboot. We should prefer `Stop-Service` + Manual, powercfg `/setactive`, and process priority, all of which take effect instantly.
4. Dev-safe performance: Dev Drive + Defender performance mode, search-index exclusions for repos. Code check: none of the 12 read projects creates a Dev Drive, sets Defender performance mode or writes `.wslconfig`. They only *enable* WSL/Hyper-V/Sandbox as features (winutil, Sophia, Win11Debloat).
6. Per-plan power settings: core parking (CPMINCORES) and USB selective suspend are stored per power plan (Winhance, optimizer). A dedicated Game plan lets us apply them without touching the Balanced plan, so revert is just `powercfg /setactive`.
5. Drift detection when Windows Update resets settings, and triggers such as a game exe starting.

## Recommended feature list
- Catalog (existing `data/db`) + mode files (`modes/code.json` etc.) that reference catalog ids.
- Transactional apply engine: snapshot → apply → verify → journal. Revert = replay the journal in reverse. The journal must survive a crash.
- Hard blocklist, which cannot be overridden: Defender/Tamper, Firewall, SmartScreen, VBS/HVCI, Windows Update services, CPU mitigations, memory compression, hypervisorlaunchtype, IPv6 disable, plus anything that breaks WSL2/Hyper-V (vmcompute, vmms, LxssManager, HvHost, hns). Code check shows these ship as defaults, not only as options, in some tools: ReviOS disables memory compression in `final.yml`; tiny11Coremaker removes Defender and disables `wuauserv`. Treat any imported preset from those tools as untrusted.
- Per-mode power plan (Balanced / High / Ultimate) via GUID.
- Code mode: WSL memory cap, Docker running, Dev Drive advice, long paths, Search index exclusions, background apps kept.
- Game mode: `wsl --shutdown` + stop Docker (prompt first), Game Mode on, Game DVR off, Spooler/SysMain optional Manual, notifications off, Windows Update active hours/pause (never disable).
- Work mode: baseline plus telemetry and ads reduction; Office, Teams and OneDrive left intact.
- One-shot "cleanup" page kept apart from modes: appx removal is not reversible.
- Dry-run/diff view and an elevation boundary: a small admin helper and an unprivileged UI.
