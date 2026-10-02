using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinModes.Core.Notifications;

/// <summary>What was already announced for one usage window of a plan, in the cycle that ends at <see cref="ResetsAt"/>.</summary>
public sealed record WindowMark(DateTimeOffset? ResetsAt, bool LowSent, bool ReachedSent);

/// <summary>
/// What WinModes already told the user, kept on disk so that restarting the app does not announce the same thing
/// again: a limit that stays at 0 % for hours, a reset credit already counted, an update already announced.
/// </summary>
public sealed class NoticeLedger
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "notices.json");

    /// <summary>Marks by "Tool:window minutes"; an entry exists only while something was announced for the current cycle.</summary>
    public Dictionary<string, WindowMark> Windows { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Limit resets in reserve as last seen, by tool; a rise is what gets announced.</summary>
    public Dictionary<string, int> Credits { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Release tag of the last update announced.</summary>
    public string? UpdateTag { get; set; }

    /// <summary>Something changed since the last <see cref="Save"/>.</summary>
    [JsonIgnore]
    public bool Changed { get; set; }

    /// <summary>The saved ledger; an empty one when the file is missing or damaged, since losing it only repeats a notice.</summary>
    public static NoticeLedger Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<NoticeLedger>(File.ReadAllText(path)) ?? new NoticeLedger() : new NoticeLedger();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new NoticeLedger();
        }
    }

    /// <summary>Writes the ledger; a failure is ignored, the worst case being one repeated notice after a restart.</summary>
    public void Save(string path)
    {
        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Options));
            Changed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the next change tries again.
        }
    }
}
