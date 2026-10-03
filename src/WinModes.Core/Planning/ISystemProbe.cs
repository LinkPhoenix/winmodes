namespace WinModes.Core.Planning;

public enum ServiceStartMode { Unknown, Automatic, Manual, Disabled }

/// <summary>Live state of one Windows service; null from the probe means it is not installed.</summary>
public sealed record ServiceState(ServiceStartMode StartMode, bool IsRunning);

/// <summary>Read-only view of the machine, abstracted so the planner can be tested without Windows.</summary>
public interface ISystemProbe
{
    WinModes.Core.Tuning.PolicyEnvironment GetPolicyEnvironment() => WinModes.Core.Tuning.PolicyEnvironment.Unknown;
    /// <summary>Technical setting observations. Probes without Windows access report an explicit unavailable observation.</summary>
    IReadOnlyList<WinModes.Core.Tuning.TweakPartObservation> GetTweakObservations(WinModes.Core.Tuning.Tweak tweak) =>
        [.. tweak.Parts.Select(part => new WinModes.Core.Tuning.TweakPartObservation(part.Index, null, "Unreadable", part.Setting ?? "Disabled", true, "This probe does not support setting observations."))];

    ServiceState? GetService(string name);
    bool IsProcessRunning(string processFileName);
    bool IsWslRunning();
}
