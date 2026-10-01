using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Planning;

public enum ChangeKind { StopService, StartService, CloseApp, LaunchApp, ShutdownWsl, StartDocker, SetPowerPlan }

/// <summary>One change the engine would make, with the live value it would replace.</summary>
public sealed record PlannedChange(ChangeKind Kind, string Target, string From, string To, string Reason);

public sealed record ModePlan(string Mode, IReadOnlyList<PlannedChange> Changes, IReadOnlyList<string> Skipped);

/// <summary>
/// Compares a profile with the live machine and lists only the changes that would have an effect.
/// Planning is read-only; applying a plan is a separate, journaled step.
/// </summary>
public sealed class ModePlanner(ISystemProbe probe, ProtectionPolicy policy)
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

        if (!string.IsNullOrEmpty(profile.Power.Plan))
        {
            changes.Add(new PlannedChange(ChangeKind.SetPowerPlan, "power plan", "current", profile.Power.Plan, "Mode power plan"));
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

            changes.Add(new PlannedChange(ChangeKind.StopService, stop.Id, Describe(state), "Manual/Stopped", stop.Why));
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
                changes.Add(new PlannedChange(ChangeKind.StartService, start.Id, Describe(state), $"{state.StartMode}/Running", "Required by this mode"));
            }
        }
    }

    private void PlanApps(ModeProfile profile, List<PlannedChange> changes)
    {
        changes.AddRange(profile.Apps.Close
            .Where(app => probe.IsProcessRunning(app.Process))
            .Select(app => new PlannedChange(ChangeKind.CloseApp, app.Id, "running", "closed", app.Why)));

        changes.AddRange(profile.Apps.Launch
            .Where(app => !probe.IsProcessRunning(Path.GetFileName(app.Path)))
            .Select(app => new PlannedChange(ChangeKind.LaunchApp, app.Id, "not running", "running", "Launched by this mode")));
    }

    private void PlanWsl(ModeProfile profile, List<PlannedChange> changes)
    {
        var wslRunning = probe.IsWslRunning();
        if (!profile.Wsl.Running && wslRunning)
        {
            changes.Add(new PlannedChange(ChangeKind.ShutdownWsl, "WSL + Docker Desktop", "running", "stopped", "Frees the WSL2 VM memory"));
        }
        else if (profile.Wsl.Running && profile.Wsl.Docker == "start" && !probe.IsProcessRunning("Docker Desktop.exe"))
        {
            changes.Add(new PlannedChange(ChangeKind.StartDocker, "Docker Desktop", "not running", "running", "Dev stack for this mode"));
        }
    }

    private static string Describe(ServiceState state) =>
        $"{state.StartMode}/{(state.IsRunning ? "Running" : "Stopped")}";
}
