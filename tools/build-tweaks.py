"""Builds data/tweaks.json, the catalog read by the Optimize page.

Each tweak is a list of registry values and scheduled tasks. The list of tools that ship the same
setting comes from research/oss-optimizers/tweaks-consensus.json (code-verified survey); a tweak
without a research id has no tool list and is never marked as recommended.

A second survey (research/oss-optimizers/round2-tweaks.json, from reading about forty more optimizers) adds
settings of its own, with the projects that ship each one and where in their code it was seen.

Run: python tools/build-tweaks.py
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
RESEARCH = {item["id"]: item for item in json.loads((ROOT / "research/oss-optimizers/tweaks-consensus.json").read_text(encoding="utf-8"))}

HKCU, HKLM = "User", "Machine"
CDM = r"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
ADVANCED = r"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"


def dword(hive, path, name, value):
    return {"hive": hive, "path": path, "name": name, "kind": "Number", "value": str(value)}


def text(hive, path, name, value):
    return {"hive": hive, "path": path, "name": name, "kind": "Text", "value": value}


def tweak(id, title, description, category, values=(), tasks=(), research=None, risk="low", recommended=None,
          restart="none", warning=None):
    tools = sorted(RESEARCH[research]["projects"]) if research else []
    return {
        "id": id,
        "title": title,
        "description": description,
        "category": category,
        "risk": risk,
        # Recommended needs evidence: a research entry rated low risk, unless stated otherwise.
        "recommended": (research is not None and risk == "low") if recommended is None else recommended,
        "restart": restart,
        "warning": warning,
        "tools": tools,
        "values": list(values),
        "tasks": list(tasks),
    }


PRIVACY, ADS, SEARCH, GAMING, SYSTEM, EXPLORER = (
    "Privacy and telemetry", "Ads and suggestions", "Search and AI", "Gaming", "System and background", "Explorer and developer")

TWEAKS = [
    # ---- Privacy and telemetry
    tweak("telemetry-required-only", "Send required diagnostic data only",
          "Limits what Windows sends to Microsoft to the required level. Windows Pro and Home do not accept a lower level.",
          PRIVACY, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1)],
          research="reg-allowtelemetry",
          warning="Settings then shows \"Some settings are managed by your organization\" on the diagnostics page."),
    tweak("advertising-id", "Turn off the advertising ID",
          "Apps can no longer use a per-user identifier to personalise ads.",
          PRIVACY, [dword(HKCU, r"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)],
          research="reg-advertisingid"),
    tweak("tailored-experiences", "Turn off tailored experiences",
          "Windows stops using diagnostic data to personalise tips, ads and recommendations.",
          PRIVACY, [dword(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)],
          research="reg-tailoredexperiences"),
    tweak("activity-history", "Turn off activity history",
          "Windows stops recording the apps and files you open and stops sending that history to Microsoft.",
          PRIVACY, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\System", name, 0)
                    for name in ("EnableActivityFeed", "PublishUserActivities", "UploadUserActivities")],
          research="reg-activityhistory"),
    tweak("feedback-prompts", "Stop feedback prompts",
          "Windows no longer asks for feedback.",
          PRIVACY, [dword(HKCU, r"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)]),
    tweak("task-ceip", "Disable the Customer Experience Improvement tasks",
          "Two scheduled tasks that collect and upload usage data.",
          PRIVACY, tasks=[r"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                          r"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"],
          research="task-ceip"),
    tweak("task-appcompat", "Disable the compatibility appraiser tasks",
          "Scheduled tasks that scan installed programs for upgrade telemetry; the appraiser is known for CPU spikes.",
          PRIVACY, tasks=[r"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                          r"\Microsoft\Windows\Application Experience\ProgramDataUpdater"],
          research="task-appcompat"),
    tweak("task-diskdiag", "Disable the disk diagnostic data collector",
          "Scheduled task that sends disk and system information to Microsoft.",
          PRIVACY, tasks=[r"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"],
          research="task-diskdiag"),
    tweak("task-feedback", "Disable the feedback tasks",
          "Scheduled tasks of the Windows feedback client.",
          PRIVACY, tasks=[r"\Microsoft\Windows\Feedback\Siuf\DmClient", r"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"],
          research="task-feedback"),

    # ---- Ads and suggestions
    tweak("consumer-features", "Stop promoted apps from being installed",
          "Windows no longer installs or pins suggested apps and games by itself.",
          ADS, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
                dword(HKCU, CDM, "SilentInstalledAppsEnabled", 0)],
          research="reg-consumerfeatures"),
    tweak("start-suggestions", "Hide suggestions in Start and Settings",
          "Removes suggested apps in Start, tips about Windows and suggested content in Settings.",
          ADS, [dword(HKCU, CDM, name, 0) for name in (
              "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "SubscribedContent-338388Enabled",
              "SubscribedContent-338389Enabled", "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled",
              "SubscribedContent-353696Enabled")] + [dword(HKCU, ADVANCED, "Start_IrisRecommendations", 0)]),
    tweak("lockscreen-tips", "Hide tips and ads on the lock screen",
          "The lock screen stops showing fun facts, tips and promotions.",
          ADS, [dword(HKCU, CDM, "RotatingLockScreenOverlayEnabled", 0), dword(HKCU, CDM, "SubscribedContent-338387Enabled", 0)]),
    tweak("explorer-ads", "Hide OneDrive and Microsoft 365 ads in File Explorer",
          "File Explorer stops showing sync-provider notifications.",
          ADS, [dword(HKCU, ADVANCED, "ShowSyncProviderNotifications", 0)]),

    # ---- Search and AI
    tweak("bing-search", "Remove web results from Start search",
          "Start search only looks on this PC, which makes it faster and keeps what you type local.",
          SEARCH, [dword(HKCU, r"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)],
          research="reg-bingsearch", restart="sign-out"),
    tweak("search-highlights", "Hide search highlights",
          "Removes the daily illustrations and suggestions from the search box.",
          SEARCH, [dword(HKCU, r"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0)]),
    tweak("widgets", "Turn off Widgets",
          "Removes the Widgets board and its background processes, which hold memory even when the board is closed.",
          SEARCH, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)],
          research="reg-widgets", restart="sign-out"),
    tweak("recall", "Turn off Recall snapshots",
          "Windows does not save snapshots of the screen for Recall. Only matters on Copilot+ PCs.",
          SEARCH, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)],
          research="reg-recall",
          warning="Snapshots that Recall already saved are removed when the policy takes effect."),
    tweak("windows-copilot", "Turn off Windows Copilot",
          "Policy that hides the Windows Copilot side panel. It does not affect Claude, Codex or any other AI tool.",
          SEARCH, [dword(HKCU, r"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)],
          research="reg-copilot", recommended=False,
          warning="Microsoft has partly retired this policy: on recent builds Copilot is an app and is not affected."),

    # ---- Gaming
    tweak("game-mode", "Keep Game Mode on",
          "Windows gives the running game priority and holds back updates and notifications while you play.",
          GAMING, [dword(HKCU, r"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1)],
          research="reg-gamemode"),
    tweak("game-dvr", "Turn off background game recording",
          "Stops the Xbox Game Bar from recording in the background, which costs CPU and GPU time in games.",
          GAMING, [dword(HKCU, r"System\GameConfigStore", "GameDVR_Enabled", 0),
                   dword(HKCU, r"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)],
          research="reg-gamedvr",
          warning="Do not apply it if you record clips with the Game Bar."),
    tweak("mouse-acceleration", "Turn off mouse acceleration",
          "Disables \"Enhance pointer precision\" so the pointer moves the same distance whatever the speed of the hand.",
          GAMING, [text(HKCU, r"Control Panel\Mouse", name, "0") for name in ("MouseSpeed", "MouseThreshold1", "MouseThreshold2")],
          research="reg-mouseaccel", recommended=False, restart="sign-out",
          warning="A matter of taste: the pointer feels different on the desktop too."),

    # ---- System and background
    tweak("edge-background", "Stop Edge from running in the background",
          "Edge no longer preloads at sign-in and no longer keeps running after its last window is closed. Edge and WebView2 keep working.",
          SYSTEM, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0),
                   dword(HKLM, r"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0)],
          research="reg-edge-startupboost",
          warning="Edge then shows \"Managed by your organization\" in its settings."),
    tweak("delivery-optimization", "Do not upload updates to other PCs",
          "Windows Update stops sharing downloaded updates with other computers. Updates still install normally.",
          SYSTEM, [dword(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)],
          research="reg-deliveryopt"),
    tweak("fast-startup", "Turn off Fast Startup",
          "Shut down then really shuts down. This avoids stale driver state and helps with WSL and Docker networking problems after a boot.",
          SYSTEM, [dword(HKLM, r"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0)],
          research="reg-fastboot", recommended=False,
          warning="Starting the PC after a shutdown takes a few seconds longer."),
    tweak("transparency", "Turn off transparency effects",
          "Windows draws the taskbar and menus without the translucent effect. A small saving on the GPU.",
          SYSTEM, [dword(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)],
          research="reg-transparency", recommended=False,
          warning="Cosmetic: Windows looks flatter."),

    # ---- Explorer and developer
    tweak("file-extensions", "Show file extensions",
          "File Explorer shows .exe, .ps1, .txt and other extensions, which makes disguised files easier to spot.",
          EXPLORER, [dword(HKCU, ADVANCED, "HideFileExt", 0)],
          research="reg-showfileext", restart="explorer"),
    tweak("taskbar-end-task", "Add \"End task\" to the taskbar menu",
          "A right-click on a taskbar button offers to end the app without opening Task Manager.",
          EXPLORER, [dword(HKCU, ADVANCED + r"\TaskbarDeveloperSettings", "TaskbarEndTask", 1)],
          research="reg-endtask"),
    tweak("long-paths", "Allow paths longer than 260 characters",
          "Programs that support it can use long paths, which avoids errors in deep node_modules folders.",
          EXPLORER, [dword(HKLM, r"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1)],
          research="reg-longpaths"),
]

# Second survey: curated by hand from the findings, with the texts shown in the app. The evidence stays in the research file.
for item in json.loads((ROOT / "research/oss-optimizers/round2-tweaks.json").read_text(encoding="utf-8")):
    TWEAKS.append({key: item[key] for key in (
        "id", "title", "description", "category", "risk", "recommended", "restart", "warning", "tools", "values", "tasks")})

ids = [item["id"] for item in TWEAKS]
assert len(ids) == len(set(ids)), "duplicate tweak id"
target = ROOT / "data" / "tweaks.json"
target.write_text(json.dumps(TWEAKS, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
print(f"{len(TWEAKS)} tweaks written to {target}")
