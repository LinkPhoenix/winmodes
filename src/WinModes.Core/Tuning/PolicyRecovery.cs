using WinModes.Core.Planning;

namespace WinModes.Core.Tuning;

public sealed record PolicyEnvironment(string Edition, string Build, bool HasPolicyFiles, bool HasManagementIndicators, string? Error = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> LocalPolicyValues { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> DocumentedPolicyValues { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static PolicyEnvironment Unknown { get; } = new("Unknown", "Unknown", false, false, "Policy sources could not be checked.");
    public bool MayReapply(TweakValue value) => LocalPolicyValues.Contains(PolicyRecovery.Target(value));
    public bool CanReleaseValue(TweakValue value) => Error is null && !HasManagementIndicators && !MayReapply(value);
}

/// <summary>Only documented, non-security catalog values can be returned to an unconfigured state.</summary>
public static class PolicyRecovery
{
    public static string Target(TweakValue value) => $"{value.Hive}\\{value.Path}\\{value.Name}";
    public static bool IsPolicy(TweakValue value) => value.Path.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase);

    public static bool Supports(TweakValue value, PolicyEnvironment environment) => Supports(value)
        || value.Kind == TweakValueKind.Number
            && value.Path.StartsWith(@"SOFTWARE\Policies\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)
            && environment.DocumentedPolicyValues.Contains(Target(value));

    public static bool Supports(TweakValue value) => value.Hive == TweakHive.Machine && value.Kind == TweakValueKind.Number
        && (value.Path.Equals(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", StringComparison.OrdinalIgnoreCase)
            && value.Name.Equals("AllowTelemetry", StringComparison.OrdinalIgnoreCase)
        || value.Path.Equals(@"SOFTWARE\Policies\Microsoft\Windows\System", StringComparison.OrdinalIgnoreCase)
            && value.Name.Equals("AllowCrossDeviceClipboard", StringComparison.OrdinalIgnoreCase));

    public static string Explain(PolicyEnvironment environment) => environment.Error is not null
        ? "Policy sources could not be checked. Recovery is unavailable."
        : environment.HasManagementIndicators ? "Management indicators found. Resolve the policy with its owner before changing it."
        : environment.HasPolicyFiles ? "Local policy files found. Matching values may be reapplied; each source is checked separately."
        : "No local policy files or management indicators found. This does not prove which script set the value.";
}
