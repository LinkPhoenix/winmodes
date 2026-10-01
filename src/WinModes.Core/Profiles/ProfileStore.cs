using System.Text.Json;

namespace WinModes.Core.Profiles;

/// <summary>Loads generated mode profiles from the profiles directory.</summary>
public sealed class ProfileStore(string profilesDirectory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly string[] NonModeFiles = ["baseline", "modes.manual"];

    public IReadOnlyList<string> ListModes() =>
        [.. Directory.EnumerateFiles(profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !NonModeFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];

    public ModeProfile Load(string mode)
    {
        // The mode name comes from user input; reject anything that is not a known profile file.
        var known = ListModes().FirstOrDefault(name => name.Equals(mode, StringComparison.OrdinalIgnoreCase))
            ?? throw new ProfileException($"Unknown mode '{mode}'. Available: {string.Join(", ", ListModes())}.");

        var path = Path.Combine(profilesDirectory, known + ".json");
        try
        {
            return JsonSerializer.Deserialize<ModeProfile>(File.ReadAllText(path), Options)
                ?? throw new ProfileException($"Profile '{path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileException($"Profile '{path}' is not valid: {ex.Message}", ex);
        }
    }
}

public sealed class ProfileException : Exception
{
    public ProfileException(string message) : base(message) { }
    public ProfileException(string message, Exception inner) : base(message, inner) { }
    public ProfileException() { }
}
