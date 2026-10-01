namespace WinModes.Core.Planning;

public enum ServiceStartMode { Unknown, Automatic, Manual, Disabled }

/// <summary>Live state of one Windows service; null from the probe means it is not installed.</summary>
public sealed record ServiceState(ServiceStartMode StartMode, bool IsRunning);

/// <summary>Read-only view of the machine, abstracted so the planner can be tested without Windows.</summary>
public interface ISystemProbe
{
    ServiceState? GetService(string name);
    bool IsProcessRunning(string processFileName);
    bool IsWslRunning();
}
