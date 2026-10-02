using System.IO.Enumeration;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;

namespace WinModes.Core.Protection;

/// <summary>
/// Hard blocklist from data/protected.json. The engine refuses any profile that would stop a
/// protected service or close a protected app, whatever the profile generator produced.
/// </summary>
public sealed partial class ProtectionPolicy
{
    private readonly HashSet<string> _services;
    private readonly IReadOnlyList<string> _startupPatterns;
    private readonly HashSet<string> _processes;

    private ProtectionPolicy(HashSet<string> services, IReadOnlyList<string> startupPatterns, HashSet<string> processes)
    {
        _services = services;
        _startupPatterns = startupPatterns;
        _processes = processes;
    }

    public static ProtectionPolicy Load(string protectedJsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(protectedJsonPath));
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startupPatterns = new List<string>();
        var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in document.RootElement.GetProperty("requiredCapabilities").EnumerateObject())
        {
            if (capability.Value.TryGetProperty("services", out var list))
            {
                foreach (var name in list.EnumerateArray())
                {
                    services.Add(name.GetString()!);
                }
            }

            if (capability.Value.TryGetProperty("processes", out var names))
            {
                processes.UnionWith(names.EnumerateArray().Select(name => name.GetString()!));
            }

            if (capability.Value.TryGetProperty("startupIds", out var ids))
            {
                startupPatterns.AddRange(ids.EnumerateArray().Select(id => id.GetString()!));
            }
        }

        if (services.Count == 0)
        {
            // Fail closed: an empty or renamed list must never be read as "nothing is protected".
            throw new ProfileException($"'{protectedJsonPath}' lists no protected service; refusing to continue.");
        }

        return new ProtectionPolicy(services, startupPatterns, processes);
    }

    public bool IsProtectedService(string serviceName)
    {
        ArgumentNullException.ThrowIfNull(serviceName);

        // Per-user services run as "<template>_<luid>" (e.g. NPSMSvc_1a2b3c); the policy lists the template name.
        return _services.Contains(serviceName) || _services.Contains(PerUserSuffix().Replace(serviceName, ""));
    }

    public bool IsProtectedApp(string appId) =>
        _startupPatterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, appId, ignoreCase: true));

    /// <summary>True when the process belongs to a protected app, whatever id the profile gives the entry.</summary>
    public bool IsProtectedProcess(string processFileName)
    {
        var name = processFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processFileName[..^4]
            : processFileName;
        return _processes.Contains(name) || IsProtectedApp(name);
    }

    [GeneratedRegex("_[0-9a-f]{4,}$", RegexOptions.IgnoreCase)]
    private static partial Regex PerUserSuffix();

    /// <summary>Returns every rule the profile breaks; an empty list means the profile is safe to plan.</summary>
    public IReadOnlyList<string> Validate(ModeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var violations = new List<string>();

        violations.AddRange(profile.Services.Stop
            .Where(stop => IsProtectedService(stop.Id))
            .Select(stop => $"Service '{stop.Id}' is protected and cannot be stopped."));

        violations.AddRange(profile.Services.Stop
            .Where(stop => stop.SetStartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            .Select(stop => $"Service '{stop.Id}': a mode may set Manual, never Disabled."));

        violations.AddRange(profile.Apps.Close
            .Where(app => IsProtectedApp(app.Id) || IsProtectedProcess(app.Process) || profile.Apps.KeepOpen.Contains(app.Id, StringComparer.OrdinalIgnoreCase))
            .Select(app => $"App '{app.Id}' is protected and cannot be closed."));

        violations.AddRange(PowerCatalog.Validate(profile.Power));

        return violations;
    }
}
