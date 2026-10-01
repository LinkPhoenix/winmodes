using WinModes.Core.Planning;

namespace WinModes.Core.Engine;

/// <summary>Write access to Windows services, abstracted so the engine can be tested without touching the system.</summary>
public interface IServiceControl
{
    ServiceState? GetState(string name);
    bool IsDelayedAutoStart(string name);
    IReadOnlyList<string> GetRunningDependents(string name);
    void SetStartMode(string name, ServiceStartMode mode, bool delayedAutoStart);
    void StopService(string name);
    void StartService(string name);
}
