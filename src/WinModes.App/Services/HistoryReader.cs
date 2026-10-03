using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinModes.Core.Apps;
using WinModes.Core.Engine;
using WinModes.Core.Tuning;

namespace WinModes.App.Services;

internal sealed record HistoryRead(IReadOnlyList<JournalSession> Modes, IReadOnlyList<TweakRecord> UserTweaks,
    IReadOnlyList<TweakRecord> MachineTweaks, IReadOnlyList<ServiceTweak> Services,
    IReadOnlyList<RemovedApp> Apps, IReadOnlyList<string> UnreadableSources);

/// <summary>Reads journals without quarantining or rewriting damaged files.</summary>
internal static class HistoryReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static HistoryRead Read()
    {
        var errors = new List<string>();
        var userDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes");
        var modes = new List<JournalSession>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(AppPaths.JournalDirectory, "*.json"))
            {
                if (ReadFile<JournalSession>(path, errors) is { } mode) { modes.Add(mode); }
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add("Mode journals");
        }

        return new(modes,
            ReadFile<List<TweakRecord>>(Path.Combine(userDirectory, "tweaks.json"), errors) ?? [],
            ReadFile<List<TweakRecord>>(AppPaths.MachineTweakJournal, errors) ?? [],
            ReadFile<List<ServiceTweak>>(Path.Combine(AppPaths.TweaksDirectory, "services.json"), errors) ?? [],
            ReadFile<List<RemovedApp>>(Path.Combine(userDirectory, "removed-apps.json"), errors) ?? [], errors);
    }

    private static T? ReadFile<T>(string path, List<string> errors) where T : class
    {
        try
        {
            // File.Exists suppresses permission errors; opening directly distinguishes missing from unreadable.
            using var stream = File.OpenRead(path);
            var result = JsonSerializer.Deserialize<T>(stream, Options);
            if (result is null || !ValidShape(result))
            {
                errors.Add(Path.GetFileName(path));
                return null;
            }
            return result;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            errors.Add(Path.GetFileName(path));
            return null;
        }
    }

    private static bool ValidShape(object value) => value switch
    {
        JournalSession session => !string.IsNullOrWhiteSpace(session.Id) && !string.IsNullOrWhiteSpace(session.Mode)
            && session.Entries is not null && session.Entries.All(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Target)),
        List<TweakRecord> records => records.All(record => record is not null && !string.IsNullOrWhiteSpace(record.Id)
            && record.Values is not null && record.DisabledTasks is not null
            && record.Values.All(item => item is not null && item.Path is not null && item.Name is not null && item.Written is not null)
            && record.DisabledTasks.All(task => task is not null)),
        List<ServiceTweak> records => records.All(record => record is not null && !string.IsNullOrWhiteSpace(record.Service)),
        List<RemovedApp> records => records.All(record => record is not null && !string.IsNullOrWhiteSpace(record.Name)
            && record.Title is not null && record.FullName is not null),
        _ => false,
    };
}
