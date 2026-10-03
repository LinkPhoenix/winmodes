using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Protection;

namespace WinModes.Core.Tuning;

/// <summary>The first six act on a service; Tweak and Untweak apply or undo a catalog tweak.</summary>
public enum TuneAction { Automatic, Manual, Disabled, Start, Stop, Restore, Tweak, Untweak, ReleasePolicy }

public enum TuneOutcome { Done, Skipped, Failed }

/// <summary>What happened to one service or tweak (<paramref name="Target"/> is its name or id), shown to the user as is.</summary>
public sealed record TuneResult(string Target, TuneAction Action, TuneOutcome Outcome, string? Detail);

/// <summary>The start type a service had before the user first changed it.</summary>
public sealed class ServiceTweak
{
    public required string Service { get; init; }
    public ServiceStartMode OriginalStartMode { get; init; }
    public bool OriginalDelayedAutoStart { get; init; }
    public ServiceStartMode SetTo { get; set; }
    public DateTimeOffset ChangedUtc { get; set; }
}

/// <summary>
/// Remembers the original start type of every service the user changed, in one JSON file.
/// The original is written before the service is touched and kept until it is restored.
/// </summary>
public sealed class TweakStore(string directory)
{
    private const string TweaksFile = "services.json";
    private const string ResultFile = "last-result.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<ServiceTweak> Load() => Read<List<ServiceTweak>>(TweaksFile) ?? [];

    public void Save(IEnumerable<ServiceTweak> tweaks) => Write(TweaksFile, tweaks.ToList());

    /// <summary>Results of the last run of the elevated helper, read back by the app.</summary>
    public IReadOnlyList<TuneResult> LoadLastResults() => Read<List<TuneResult>>(ResultFile) ?? [];

    public void SaveLastResults(IEnumerable<TuneResult> results) => Write(ResultFile, results.ToList());

    private T? Read<T>(string file)
        where T : class => StateFile.Read<T>(Path.Combine(directory, file), Options);

    private void Write<T>(string file, T value)
    {
        AtomicFile.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(value, Options));
    }
}

/// <summary>
/// Changes one service at the user's request. Protected services are refused, the original start type is
/// recorded before the first change, and services the active mode has changed are left to the mode.
/// </summary>
public sealed class ServiceTuner(IServiceControl services, ProtectionPolicy policy, TweakStore store, IReadOnlySet<string> changedByActiveMode)
{
    public IReadOnlyList<TuneResult> Apply(TuneAction action, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (action is TuneAction.Tweak or TuneAction.Untweak)
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Not a service action.");
        }

        var tweaks = store.Load().ToDictionary(tweak => tweak.Service, StringComparer.OrdinalIgnoreCase);
        var results = new List<TuneResult>();

        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TuneResult result;
            try
            {
                result = ApplyOne(action, name, tweaks);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
            {
                result = new TuneResult(name, action, TuneOutcome.Failed, ex.InnerException?.Message ?? ex.Message);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>Every service that still has a recorded original, for "restore all".</summary>
    public IReadOnlyList<string> TweakedServices() => [.. store.Load().Select(tweak => tweak.Service)];

    private TuneResult ApplyOne(TuneAction action, string name, Dictionary<string, ServiceTweak> tweaks)
    {
        TuneResult Skip(string detail) => new(name, action, TuneOutcome.Skipped, detail);
        TuneResult Done(string? detail = null) => new(name, action, TuneOutcome.Done, detail);

        var state = services.GetState(name);
        if (state is null)
        {
            return Skip("Not installed.");
        }

        // Fail closed: starting a protected service is harmless, everything else is refused.
        if (action != TuneAction.Start && policy.IsProtectedService(name))
        {
            return Skip("Protected service.");
        }

        switch (action)
        {
            case TuneAction.Start:
                if (state.StartMode == ServiceStartMode.Disabled)
                {
                    return Skip("It is disabled; change its start type first.");
                }

                services.StartService(name);
                return Done();

            case TuneAction.Stop:
                if (!state.IsRunning)
                {
                    return Skip("Already stopped.");
                }

                var dependents = services.GetRunningDependents(name);
                if (dependents.Count > 0)
                {
                    return Skip($"Still needed by: {string.Join(", ", dependents)}.");
                }

                services.StopService(name);
                return Done();
        }

        if (changedByActiveMode.Contains(name))
        {
            return Skip("Changed by the active mode; undo the mode first.");
        }

        if (action == TuneAction.Restore)
        {
            if (!tweaks.TryGetValue(name, out var tweak))
            {
                return Skip("Nothing to restore.");
            }

            if (state.StartMode != tweak.OriginalStartMode)
            {
                services.SetStartMode(name, tweak.OriginalStartMode, tweak.OriginalDelayedAutoStart);
            }

            tweaks.Remove(name);
            store.Save(tweaks.Values);
            return Done($"Back to {tweak.OriginalStartMode}.");
        }

        var target = action switch
        {
            TuneAction.Automatic => ServiceStartMode.Automatic,
            TuneAction.Manual => ServiceStartMode.Manual,
            _ => ServiceStartMode.Disabled,
        };
        if (state.StartMode == ServiceStartMode.Unknown)
        {
            return Skip("Its start type cannot be changed here.");
        }

        if (state.StartMode == target)
        {
            return Skip($"Already {target}.");
        }

        var isFirstChange = !tweaks.TryGetValue(name, out var existing);
        if (existing is null)
        {
            tweaks[name] = existing = new ServiceTweak
            {
                Service = name,
                OriginalStartMode = state.StartMode,
                OriginalDelayedAutoStart = services.IsDelayedAutoStart(name),
            };
        }

        existing.SetTo = target;
        existing.ChangedUtc = DateTimeOffset.UtcNow;

        // The original is on disk before the service is touched.
        store.Save(tweaks.Values);
        try
        {
            services.SetStartMode(name, target, target == existing.OriginalStartMode && existing.OriginalDelayedAutoStart);
        }
        catch (Exception ex) when (isFirstChange && ex is InvalidOperationException or Win32Exception)
        {
            // Nothing changed: do not keep a restore point for a first change that failed.
            tweaks.Remove(name);
            store.Save(tweaks.Values);
            throw;
        }

        if (target == existing.OriginalStartMode)
        {
            // Back to where it started: nothing left to restore.
            tweaks.Remove(name);
            store.Save(tweaks.Values);
        }

        return Done($"{state.StartMode} to {target}.");
    }
}
