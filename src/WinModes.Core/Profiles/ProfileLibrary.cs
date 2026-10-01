using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WinModes.Core.Protection;

namespace WinModes.Core.Profiles;

/// <summary>
/// Adds, copies, edits and removes profile files. Every profile is checked against the protection
/// policy before it is written, so a file from someone else cannot bring in a forbidden change.
/// </summary>
public sealed partial class ProfileLibrary(string profilesDirectory, ProtectionPolicy policy)
{
    private static readonly string[] BuiltInModes = ["code", "work", "game"];
    private static readonly string[] ReservedNames = ["baseline", "modes.manual"];
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool IsBuiltIn(string mode) => BuiltInModes.Contains(mode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies a profile file into the library. Returns the mode name it was stored under.</summary>
    public string Import(string sourcePath)
    {
        JsonNode node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(sourcePath)) ?? throw new ProfileException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileException($"The file is not valid JSON: {ex.Message}", ex);
        }

        var mode = node["mode"]?.GetValue<string>() ?? throw new ProfileException("The file has no 'mode' name.");
        Save(mode, node, allowOverwriteBuiltIn: false);
        return mode;
    }

    public void Export(string mode, string destinationPath) => File.Copy(PathOf(mode), destinationPath, overwrite: true);

    /// <summary>Creates a copy of a mode under a new name and label.</summary>
    public void Duplicate(string sourceMode, string newMode, string newLabel)
    {
        var node = Load(sourceMode);
        node["mode"] = newMode;
        node["label"] = newLabel;
        Save(newMode, node, allowOverwriteBuiltIn: false);
    }

    public void Delete(string mode)
    {
        if (IsBuiltIn(mode))
        {
            throw new ProfileException("Built-in modes cannot be deleted.");
        }

        File.Delete(PathOf(mode));
    }

    public JsonNode Load(string mode) =>
        JsonNode.Parse(File.ReadAllText(PathOf(mode))) ?? throw new ProfileException($"Profile '{mode}' is empty.");

    /// <summary>Validates and writes a profile. Fields WinModes does not know are kept as they are.</summary>
    public void Save(string mode, JsonNode node, bool allowOverwriteBuiltIn = true)
    {
        ArgumentNullException.ThrowIfNull(node);
        ValidateName(mode);
        if (!allowOverwriteBuiltIn && File.Exists(PathOf(mode)))
        {
            throw new ProfileException($"A mode named '{mode}' already exists.");
        }

        ModeProfile profile;
        try
        {
            profile = node.Deserialize<ModeProfile>(ReadOptions) ?? throw new ProfileException("The profile is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileException($"The profile is not valid: {ex.Message}", ex);
        }

        var violations = policy.Validate(profile);
        if (violations.Count > 0)
        {
            throw new ProfileException("The profile breaks the protection policy:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        var path = PathOf(mode);
        AtomicFile.WriteAllText(path, node.ToJsonString(WriteOptions));
    }

    private string PathOf(string mode)
    {
        ValidateName(mode);
        return Path.Combine(profilesDirectory, mode + ".json");
    }

    private static void ValidateName(string mode)
    {
        // The name becomes a file name read by the elevated helper: letters, digits and dashes only.
        if (!ModeName().IsMatch(mode) || ReservedNames.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            throw new ProfileException("A mode name must be 1 to 32 lowercase letters, digits or dashes.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex ModeName();
}
