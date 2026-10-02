using System.Text;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Services;

/// <summary>
/// What the Optimize page reads from this PC: services, the state of every setting, and what was changed and can be put back.
/// The last one is kept for the run, so the page can show it at once while a new one is being read.
/// </summary>
internal sealed class OptimizeSnapshot
{
    private string? _signature;

    private OptimizeSnapshot(
        IReadOnlyList<ServiceInfo> services, IReadOnlyList<ServiceTweak> changedServices, Dictionary<string, IReadOnlyList<bool?>> states,
        Dictionary<string, HashSet<int>> journaledParts, HashSet<string> undoable)
    {
        Services = services;
        ChangedServices = changedServices;
        States = states;
        JournaledParts = journaledParts;
        Undoable = undoable;
    }

    public static OptimizeSnapshot? Last { get; private set; }

    public IReadOnlyList<ServiceInfo> Services { get; }
    public IReadOnlyList<ServiceTweak> ChangedServices { get; }

    /// <summary>For each catalog tweak, the state of each of its parts: applied, not applied, or null when this PC does not have it.</summary>
    public Dictionary<string, IReadOnlyList<bool?>> States { get; }

    public Dictionary<string, HashSet<int>> JournaledParts { get; }
    public HashSet<string> Undoable { get; }

    /// <summary>Reads everything, the independent parts side by side, and keeps the result as <see cref="Last"/>.</summary>
    public static async Task<OptimizeSnapshot> TakeAsync()
    {
        // The program of each service is not needed here, and finding it costs a registry lookup per service.
        var services = Task.Run(() => SystemMonitor.GetServices(withExecutablePaths: false));
        var changed = Task.Run(ServiceTuning.Store.Load);
        var states = Task.Run(() => ServiceTuning.Catalog.Tweaks.ToDictionary(tweak => tweak.Id, ServiceTuning.UserTweaks.GetPartStates));
        var journaled = Task.Run(ServiceTuning.LoadJournaledParts);
        var undoable = Task.Run(ServiceTuning.LoadUndoableTweaks);
        await Task.WhenAll(services, changed, states, journaled, undoable);

        var snapshot = new OptimizeSnapshot(services.Result, changed.Result, states.Result, journaled.Result, undoable.Result);
        Last = snapshot;
        return snapshot;
    }

    /// <summary>True when both read the same things, so a page that shows one has nothing to redraw for the other.</summary>
    public bool SameAs(OptimizeSnapshot other) => Signature == other.Signature;

    private string Signature => _signature ??= BuildSignature();

    private string BuildSignature()
    {
        var text = new StringBuilder();
        foreach (var (id, parts) in States.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append(id).Append(':');
            foreach (var state in parts)
            {
                text.Append(state is null ? '-' : state.Value ? '1' : '0');
            }

            text.Append(';');
        }

        text.Append('|');
        foreach (var service in Services)
        {
            text.Append(service.Name).Append(':').Append((int)service.StartMode).Append(service.IsRunning ? 'r' : 's').Append(';');
        }

        text.Append('|');
        foreach (var tweak in ChangedServices)
        {
            text.Append(tweak.Service).Append(':').Append(tweak.SetTo).Append(';');
        }

        text.Append('|');
        foreach (var (id, parts) in JournaledParts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append(id).Append(':').Append(string.Join(',', parts.Order())).Append(';');
        }

        text.Append('|').Append(string.Join(';', Undoable.Order(StringComparer.Ordinal)));
        return text.ToString();
    }
}
