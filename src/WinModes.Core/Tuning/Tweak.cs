using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinModes.Core.Tuning;

public enum TweakHive { User, Machine }

public enum TweakValueKind { Number, Text }

/// <summary>One registry value a tweak sets. Number values (DWORD) are written as a decimal number.</summary>
public sealed record TweakValue(TweakHive Hive, string Path, string Name, TweakValueKind Kind, string Value);

public enum TweakPartKind { Value, Task }

/// <summary>
/// One change inside a tweak: a registry value or a scheduled task. A tweak applies all its parts, or only those the user picks.
/// <see cref="Index"/> counts the values first, then the tasks, and is what the elevated helper is given.
/// </summary>
/// <param name="Label">What this part does, in plain words (the value name when the catalog has no text for it).</param>
/// <param name="Target">Where it acts: the registry key and value, or the task path.</param>
/// <param name="Setting">The value that is written ("0", or a quoted text); null for a task, which is disabled.</param>
public sealed record TweakPart(int Index, TweakPartKind Kind, string Label, string Target, string? Setting, bool MachineWide);

/// <summary>
/// One entry of data/tweaks.json: registry values and scheduled tasks that together turn one Windows behaviour off or on.
/// A tweak is data only; it never carries a command.
/// </summary>
public sealed class Tweak
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "Other";
    public string Risk { get; init; } = "medium";
    public bool Recommended { get; init; }

    /// <summary>"none", "explorer", "sign-out" or "restart": what the change needs before it shows.</summary>
    public string Restart { get; init; } = "none";
    public string? Warning { get; init; }

    /// <summary>Open-source optimizers that ship the same setting (from the code-verified survey).</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];
    public IReadOnlyList<TweakValue> Values { get; init; } = [];

    /// <summary>Full paths of scheduled tasks to disable.</summary>
    public IReadOnlyList<string> Tasks { get; init; } = [];

    /// <summary>What each part does, one text per value and then one per task. Optional: a part without a text shows its technical name.</summary>
    public IReadOnlyList<string> PartLabels { get; init; } = [];

    private IReadOnlyList<TweakPart>? _parts;

    /// <summary>Every value and task of the tweak, in the order the helper numbers them.</summary>
    [JsonIgnore]
    public IReadOnlyList<TweakPart> Parts => _parts ??= BuildParts();

    private List<TweakPart> BuildParts()
    {
        string Label(int index, string fallback) => index < PartLabels.Count && !string.IsNullOrWhiteSpace(PartLabels[index]) ? PartLabels[index] : fallback;

        var parts = new List<TweakPart>(Values.Count + Tasks.Count);
        foreach (var value in Values)
        {
            var hive = value.Hive == TweakHive.Machine ? "HKLM" : "HKCU";
            parts.Add(new TweakPart(
                parts.Count,
                TweakPartKind.Value,
                Label(parts.Count, value.Name),
                $@"{hive}\{value.Path}\{value.Name}",
                value.Kind == TweakValueKind.Text ? $"\"{value.Value}\"" : value.Value,
                value.Hive == TweakHive.Machine));
        }

        foreach (var task in Tasks)
        {
            parts.Add(new TweakPart(parts.Count, TweakPartKind.Task, Label(parts.Count, task[(task.LastIndexOf('\\') + 1)..]), task, null, MachineWide: true));
        }

        return parts;
    }

    /// <summary>The part a journaled value belongs to, or null when the tweak no longer lists it.</summary>
    public int? PartOf(TweakValueRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        for (var i = 0; i < Values.Count; i++)
        {
            var value = Values[i];
            if (value.Hive == record.Hive && value.Path.Equals(record.Path, StringComparison.OrdinalIgnoreCase)
                && value.Name.Equals(record.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The part a journaled task belongs to, or null.</summary>
    public int? PartOfTask(string taskPath)
    {
        for (var i = 0; i < Tasks.Count; i++)
        {
            if (Tasks[i].Equals(taskPath, StringComparison.OrdinalIgnoreCase))
            {
                return Values.Count + i;
            }
        }

        return null;
    }

    /// <summary>True when one of the chosen parts (all of them when null) is machine-wide and must go through the elevated helper.</summary>
    public bool NeedsElevationFor(IReadOnlySet<int>? parts) =>
        parts is null ? NeedsElevation : Parts.Any(part => part.MachineWide && parts.Contains(part.Index));

    /// <summary>True when one of the chosen parts (all of them when null) is a value of the user's own hive.</summary>
    public bool HasUserPartFor(IReadOnlySet<int>? parts) =>
        parts is null ? HasUserPart : Parts.Any(part => !part.MachineWide && parts.Contains(part.Index));

    /// <summary>True when part of the tweak is machine-wide and must go through the elevated helper.</summary>
    [JsonIgnore]
    public bool NeedsElevation => Tasks.Count > 0 || Values.Any(value => value.Hive == TweakHive.Machine);

    [JsonIgnore]
    public bool HasUserPart => Values.Any(value => value.Hive == TweakHive.User);
}

/// <summary>
/// Refuses any tweak that reaches a security, update, start-up or service key, whatever the catalog says.
/// The elevated helper applies this check again on its own copy of the catalog.
/// </summary>
public static class TweakGuard
{
    private const string TaskRoot = @"\Microsoft\Windows\";

    private static readonly string[] DeniedPathParts =
    [
        @"\Services\", @"\Services", "Image File Execution Options", @"\CurrentVersion\Run", "Winlogon", "Windows Defender",
        "Microsoft Defender", "Microsoft Antimalware", "WindowsFirewall", "FirewallPolicy", "WindowsUpdate", "DeviceGuard",
        "Memory Management", "SmartScreen", @"\Lsa", "Tcpip6", @"CurrentVersion\Policies\System", "Bitdefender", "AdGuard",
        "SecurityHealth", @"\Control\CI", "Safer",
    ];

    private static readonly string[] DeniedNameParts = ["SmartScreen", "AntiSpyware", "EnableLUA", "FeatureSettings", "Hypervisor", "NoAutoUpdate"];

    private static readonly string[] DeniedTaskParts =
        ["WindowsUpdate", "UpdateOrchestrator", "WaaSMedic", "Windows Defender", "InstallService", "Servicing", "SystemRestore", "Subscription"];

    /// <summary>Returns every rule the tweak breaks; empty means it may be applied.</summary>
    public static IReadOnlyList<string> Validate(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        var violations = new List<string>();

        foreach (var value in tweak.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Path) || string.IsNullOrWhiteSpace(value.Name))
            {
                violations.Add($"'{tweak.Id}' has a registry value without a key or a name.");
            }
            else if (DeniedPathParts.Any(part => value.Path.Contains(part, StringComparison.OrdinalIgnoreCase))
                || DeniedNameParts.Any(part => value.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add($"'{tweak.Id}' touches a protected registry area ({value.Path}\\{value.Name}).");
            }
            else if (value.Kind == TweakValueKind.Number && !int.TryParse(value.Value, out _))
            {
                violations.Add($"'{tweak.Id}': '{value.Value}' is not a number.");
            }
        }

        violations.AddRange(tweak.Tasks
            .Where(task => !task.StartsWith(TaskRoot, StringComparison.OrdinalIgnoreCase)
                || task.Contains("..", StringComparison.Ordinal)
                || DeniedTaskParts.Any(part => task.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Select(task => $"'{tweak.Id}' touches a protected scheduled task ({task})."));

        if (tweak.Values.Count == 0 && tweak.Tasks.Count == 0)
        {
            violations.Add($"'{tweak.Id}' does nothing.");
        }

        return violations;
    }
}

/// <summary>Reads data/tweaks.json. A tweak that breaks <see cref="TweakGuard"/> is dropped, never loaded.</summary>
public sealed class TweakCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, Tweak> _byId;

    private TweakCatalog(IReadOnlyList<Tweak> tweaks)
    {
        Tweaks = tweaks;
        _byId = tweaks.ToDictionary(tweak => tweak.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Tweak> Tweaks { get; }

    public Tweak? Find(string id) => _byId.GetValueOrDefault(id);

    public static TweakCatalog Load(string path)
    {
        try
        {
            var tweaks = File.Exists(path) ? JsonSerializer.Deserialize<List<Tweak>>(File.ReadAllText(path), Options) ?? [] : [];
            return new TweakCatalog([.. tweaks
                .Where(tweak => !string.IsNullOrWhiteSpace(tweak.Id) && TweakGuard.Validate(tweak).Count == 0)
                .DistinctBy(tweak => tweak.Id, StringComparer.OrdinalIgnoreCase)]);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fail closed: an unreadable catalog offers nothing.
            return new TweakCatalog([]);
        }
    }
}
