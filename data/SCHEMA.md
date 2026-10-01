# Knowledge base schema

The files in `data/db/*.json` hold one JSON array of entries each. They are meant to be read by humans and AI agents.

- `windows-services.json`: built-in Windows services (binary under `C:\Windows`).
- `third-party.json`: third-party services, startup apps and scheduled tasks.
- `oem-drivers.json`: OEM and driver services installed under `C:\Windows\System32\DriverStore` (HP, NVIDIA, Intel, Realtek, DTS, ELAN, Logitech).

Each entry has `verified`: a date means at least one fetched source confirms it; `2026-10-01-local` means it was confirmed only by read-only local evidence (service config, registry, VersionInfo, scheduled tasks); `unverified` means that has not been checked yet (0 of 369 on 2026-10-01 after the second pass: 270 verified by fetched sources, 99 by local evidence). Do not act on an unverified `Manual` or `Disabled` recommendation. Local evidence confirms identity only, not usefulness: a `2026-10-01-local` entry also needs a fetched source (or explicit user approval) before its start mode is changed.

Related research: `research/oss-optimizers/` (open-source optimizers, tweak consensus, architecture lessons).

Raw machine inventory (source of truth for what exists on this PC): `data/raw/*.json`.

## Entry

```json
{
  "id": "SysMain",
  "kind": "service | startup-app | scheduled-task",
  "displayName": "SysMain",
  "vendor": "Microsoft",
  "category": "performance | security | network | audio | display | input | gaming | telemetry | update | storage | virtualization | dev | sync | communication | oem | peripheral | print-scan | telephony | ui-shell | core-system | other",
  "description": "What it does, 1–2 sentences.",
  "presentOnMachine": true,
  "currentStartMode": "Auto | Manual | Disabled | n/a",
  "ramImpact": "none | low | medium | high",
  "usefulness": "essential | useful | situational | useless",
  "why": "Reason for the usefulness verdict, including what breaks if stopped.",
  "modes": {
    "code": 0,
    "work": 0,
    "game": 0
  },
  "recommendedStartMode": "Auto | AutoDelayed | Manual | Disabled | Keep",
  "risk": "none | low | medium | high | never-touch",
  "dependsOn": ["..."],
  "sources": ["https://learn.microsoft.com/..."]
}
```

## Mode score (0–3)

- 3: required in this mode; always on.
- 2: useful; on by default.
- 1: rarely needed; stop it or start it on demand.
- 0: useless in this mode; stop or close it.

The modes:
- Code: AI coding with Claude Code, Codex, t3code, Node, WSL, Docker and PostgreSQL.
- Work: office work, communication and browsing.
- Game: gaming on an HP OMEN laptop with NVIDIA GPU, Steam, Xbox/Game Pass and Logitech peripherals.

## Hard rules

- `risk: never-touch`: security (Defender, Bitdefender, AdGuard, Firewall, BFE, wscsvc), updates and core-system services. Their mode scores stay 3.
- `ramImpact` must be based on data, not guessed. When it is unknown, use `low` and say so in `why`.
