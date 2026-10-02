using WinModes.Core.Localization;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Planning;

public enum ChangeKind { StopService, StartService, CloseApp, LaunchApp, ShutdownWsl, StartDocker, SetPowerPlan, ApplyTweak }

/// <summary>One change the engine would make, with the live value it would replace.</summary>
public sealed record PlannedChange(ChangeKind Kind, string Target, string From, string To, string Reason);

public sealed record ModePlan(string Mode, IReadOnlyList<PlannedChange> Changes, IReadOnlyList<string> Skipped);

/// <summary>
/// Compares a profile with the live machine and lists only the changes that would have an effect.
/// Planning is read-only; applying a plan is a separate, journaled step.
/// </summary>
public sealed class ModePlanner(ISystemProbe probe, ProtectionPolicy policy, ITweakProbe? tweaks = null)
{
    public ModePlan Plan(ModeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var violations = policy.Validate(profile);
        if (violations.Count > 0)
        {
            throw new ProfileException(
                $"Profile '{profile.Mode}' breaks the protection policy:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        var changes = new List<PlannedChange>();
        var skipped = new List<string>();

        PlanServiceStops(profile, changes, skipped);
        PlanServiceStarts(profile, changes, skipped);
        PlanApps(profile, changes);
        PlanWsl(profile, changes);
        PlanTweaks(profile, changes, skipped);

        if (!string.IsNullOrEmpty(profile.Power.Plan))
        {
            changes.Add(new PlannedChange(ChangeKind.SetPowerPlan, Loc.T("power plan"), Loc.T("current"), profile.Power.Plan, Loc.T("Mode power plan")));
        }

        foreach (var value in profile.Power.Values)
        {
            if (PowerCatalog.Find(value.Setting) is { } info)
            {
                var target = string.Join(" / ", new[] { value.Ac, value.Dc }.OfType<int>().Select(number => info.Seconds ? PowerCatalog.DescribeSeconds(number) : number.ToString(System.Globalization.CultureInfo.CurrentCulture)));
                changes.Add(new PlannedChange(ChangeKind.SetPowerPlan, Loc.T(info.Title), Loc.T("current"), target, Loc.T("On a copy of the power plan")));
            }
        }

        return new ModePlan(profile.Mode, changes, skipped);
    }

    private void PlanServiceStops(ModeProfile profile, List<PlannedChange> changes, List<string> skipped)
    {
        foreach (var stop in profile.Services.Stop)
        {
            var state = probe.GetService(stop.Id);
            if (state is null)
            {
                skipped.Add($"{stop.Id}: not installed");
                continue;
            }

            if (state.StartMode == ServiceStartMode.Disabled)
            {
                // The user or another tool disabled it on purpose; re-enabling it as Manual would be a regression.
                skipped.Add($"{stop.Id}: already Disabled");
                continue;
            }

            if (!state.IsRunning && state.StartMode == ServiceStartMode.Manual)
            {
                skipped.Add($"{stop.Id}: already stopped and Manual");
                continue;
            }

            changes.Add(new PlannedChange(ChangeKind.StopService, stop.Id, Describe(state), $"{Loc.T("Manual")}/{Loc.T("Stopped")}", Loc.T(stop.Why)));
        }
    }

    private void PlanServiceStarts(ModeProfile profile, List<PlannedChange> changes, List<string> skipped)
    {
        foreach (var start in profile.Services.EnsureRunning)
        {
            var state = probe.GetService(start.Id);
            if (state is null)
            {
                skipped.Add($"{start.Id}: not installed");
            }
            else if (state.IsRunning)
            {
                skipped.Add($"{start.Id}: already running");
            }
            else if (state.StartMode == ServiceStartMode.Disabled)
            {
                skipped.Add($"{start.Id}: Disabled, left untouched");
            }
            else
            {
                changes.Add(new PlannedChange(ChangeKind.StartService, start.Id, Describe(state), $"{Loc.T(state.StartMode.ToString())}/{Loc.T("Running")}", Loc.T("Required by this mode")));
            }
        }
    }

    private void PlanApps(ModeProfile profile, List<PlannedChange> changes)
    {
        changes.AddRange(profile.Apps.Close
            .Where(app => probe.IsProcessRunning(app.Process))
            .Select(app => new PlannedChange(ChangeKind.CloseApp, app.Id, Loc.T("running"), Loc.T("closed"), Loc.T(app.Why))));

        changes.AddRange(profile.Apps.Launch
            .Where(app => !probe.IsProcessRunning(Path.GetFileName(app.Path)))
            .Select(app => new PlannedChange(ChangeKind.LaunchApp, app.Id, Loc.T("not running"), Loc.T("running"), Loc.T("Launched by this mode"))));
    }

    private void PlanTweaks(ModeProfile profile, List<PlannedChange> changes, List<string> skipped)
    {
        foreach (var id in profile.TweakIds)
        {
            var info = tweaks?.Find(id);
            if (info is null || !ModeTweaks.CanBeApplied(info))
            {
                skipped.Add($"{id}: not available in a mode");
            }
            else if (info.IsApplied)
            {
                skipped.Add($"{id}: already applied");
            }
            else
            {
                changes.Add(new PlannedChange(ChangeKind.ApplyTweak, Loc.T(info.Title), Loc.T("not set"), Loc.T("set"), Loc.T("Windows setting of this mode")));
            }
        }
    }

    private void PlanWsl(ModeProfile profile, List<PlannedChange> changes)
    {
        var wslRunning = probe.IsWslRunning();
        if (!profile.Wsl.Running && wslRunning)
        {
            changes.Add(new PlannedChange(ChangeKind.ShutdownWsl, "WSL + Docker Desktop", Loc.T("running"), Loc.T("stopped"), Loc.T("Frees the WSL2 VM memory")));
        }
        else if (profile.Wsl.Running && profile.Wsl.Docker == "start" && !probe.IsProcessRunning("Docker Desktop.exe"))
        {
            changes.Add(new PlannedChange(ChangeKind.StartDocker, "Docker Desktop", Loc.T("not running"), Loc.T("running"), Loc.T("Dev stack for this mode")));
        }
    }

    private static string Describe(ServiceState state) =>
        $"{Loc.T(state.StartMode.ToString())}/{Loc.T(state.IsRunning ? "Running" : "Stopped")}";
}
