using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinModes.Core.Tuning;

/// <summary>Portable desired states, never commands or executable registry payloads.</summary>
public sealed record SettingsConfiguration
{
    public required int Version { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public required IReadOnlyList<SettingChoice> Settings { get; init; }
}

public sealed record SettingChoice
{
    public required string Id { get; init; }
    public required int PartIndex { get; init; }
    public required bool Desired { get; init; }
}

public static class SettingsConfigurationFile
{
    public const int CurrentVersion = 1;
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumChoices = 2000;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    public static SettingsConfiguration Read(string path)
    {
        // Bound the actual read, including files changed after their length was checked.
        using var stream = File.OpenRead(path);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = stream.Read(bytes)) > 0)
        {
            if (buffer.Length + read > MaximumBytes) throw new InvalidDataException("The configuration exceeds the 1 MiB limit.");
            buffer.Write(bytes, 0, read);
        }
        var payload = buffer.ToArray();
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
        RejectDuplicateProperties(document.RootElement);
        var configuration = JsonSerializer.Deserialize<SettingsConfiguration>(payload, Options)
            ?? throw new InvalidDataException("The configuration is empty.");
        Validate(configuration);
        return configuration;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("The configuration contains duplicate JSON properties.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    public static void Write(string path, IReadOnlyList<SettingChoice> choices)
    {
        var configuration = new SettingsConfiguration { Version = CurrentVersion, CreatedUtc = DateTimeOffset.UtcNow, Settings = choices };
        Validate(configuration);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(configuration, Options));
    }

    private static void Validate(SettingsConfiguration configuration)
    {
        if (configuration.Version != CurrentVersion) throw new InvalidDataException("This configuration version is not supported.");
        if (configuration.Settings is null || configuration.Settings.Count > MaximumChoices)
            throw new InvalidDataException("The configuration contains too many settings.");
        var identities = new HashSet<(string, int)>();
        foreach (var choice in configuration.Settings)
        {
            if (choice is null || string.IsNullOrWhiteSpace(choice.Id) || choice.Id.Length > 128
                || choice.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
                || choice.PartIndex is < 0 or > 1024 || !identities.Add((choice.Id.ToUpperInvariant(), choice.PartIndex)))
                throw new InvalidDataException("The configuration contains invalid or duplicate setting identities.");
        }
    }
}
