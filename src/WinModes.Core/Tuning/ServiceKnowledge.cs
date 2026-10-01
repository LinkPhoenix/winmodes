using System.Text.Json;
using WinModes.Core.Planning;
using WinModes.Core.Protection;

namespace WinModes.Core.Tuning;

/// <summary>What the knowledge base says about one service.</summary>
public sealed record ServiceAdvice(
    string Id, string Description, string Category, string Usefulness, string Risk, string RamImpact,
    ServiceStartMode? Recommended, bool IsSourced);

/// <summary>A service of this PC whose start type differs from the recommended one.</summary>
public sealed record ServiceRecommendation(ServiceInfo Service, ServiceAdvice Advice)
{
    /// <summary>True when a published source backs the advice and the risk is low: safe to pre-select.</summary>
    public bool IsConfident => Advice.IsSourced && Advice.Risk is "none" or "low";
}

/// <summary>
/// Reads data/db/*.json. Only entries of kind "service" are used; an entry recommends a change only
/// when its recommended start mode is Manual or Disabled.
/// </summary>
public sealed class ServiceKnowledge
{
    private static readonly string[] ChangeableRisks = ["none", "low", "medium"];
    private const string LocalEvidenceSuffix = "-local";
    private const string Unverified = "unverified";

    private readonly Dictionary<string, ServiceAdvice> _advice;

    private ServiceKnowledge(Dictionary<string, ServiceAdvice> advice) => _advice = advice;

    public int Count => _advice.Count;

    public static ServiceKnowledge Load(string directory)
    {
        var advice = new Dictionary<string, ServiceAdvice>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return new ServiceKnowledge(advice);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (Text(entry, "kind") == "service" && Text(entry, "id") is { Length: > 0 } id)
                    {
                        advice[id] = Parse(id, entry);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A damaged file must not hide the other ones.
            }
        }

        return new ServiceKnowledge(advice);
    }

    public ServiceAdvice? Find(string serviceName) => _advice.GetValueOrDefault(serviceName);

    /// <summary>Services worth changing on this PC, the most confident first.</summary>
    public IReadOnlyList<ServiceRecommendation> Recommend(IEnumerable<ServiceInfo> services, ProtectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(policy);

        return [.. services
            .Where(service => !policy.IsProtectedService(service.Name))
            .Select(service => (Service: service, Advice: Find(service.Name)))
            .Where(pair => pair.Advice is { Recommended: not null } advice
                && ChangeableRisks.Contains(advice.Risk)
                && NeedsChange(pair.Service.StartMode, advice.Recommended.Value))
            .Select(pair => new ServiceRecommendation(pair.Service, pair.Advice!))
            .OrderByDescending(recommendation => recommendation.IsConfident)
            .ThenByDescending(recommendation => recommendation.Service.IsRunning)
            .ThenBy(recommendation => recommendation.Service.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static bool NeedsChange(ServiceStartMode current, ServiceStartMode recommended) => recommended switch
    {
        ServiceStartMode.Manual => current == ServiceStartMode.Automatic,
        ServiceStartMode.Disabled => current is ServiceStartMode.Automatic or ServiceStartMode.Manual,
        _ => false,
    };

    private static ServiceAdvice Parse(string id, JsonElement entry)
    {
        ServiceStartMode? recommended = Text(entry, "recommendedStartMode") switch
        {
            "Manual" => ServiceStartMode.Manual,
            "Disabled" => ServiceStartMode.Disabled,
            _ => null,
        };
        var verified = Text(entry, "verified") ?? Unverified;
        var sourced = verified != Unverified && !verified.EndsWith(LocalEvidenceSuffix, StringComparison.Ordinal);
        return new ServiceAdvice(
            id,
            Text(entry, "description") ?? "",
            Text(entry, "category") ?? "other",
            Text(entry, "usefulness") ?? "",
            Text(entry, "risk") ?? "high",
            Text(entry, "ramImpact") ?? "low",
            recommended,
            sourced);
    }

    private static string? Text(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
