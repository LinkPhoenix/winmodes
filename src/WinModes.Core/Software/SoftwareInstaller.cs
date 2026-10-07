using System.ComponentModel;
using System.Text.Json;
using WinModes.Core.Planning;
using WinModes.Core.Protection;

namespace WinModes.Core.Software;

public enum SoftwareInstallOutcome { Installed, AlreadyInstalled, Failed, NeedsRestart, Unverified }
public sealed record SoftwareInstallResult(string EntryId, SoftwareInstallOutcome Outcome, int? ExitCode);
public interface ISoftwareInstallJournal
{
    void Record(string phase, SoftwareEntry entry, InstalledSoftware? before, SoftwareInstallResult? result);
}

public sealed class SoftwareInstallJournal(string directory) : ISoftwareInstallJournal
{
    private readonly string _path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
    public void Record(string phase, SoftwareEntry entry, InstalledSoftware? before, SoftwareInstallResult? result)
    {
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.WriteLine(JsonSerializer.Serialize(new { phase, utc = DateTimeOffset.UtcNow, entryId = entry.Id, packageId = entry.WingetId, source = entry.Source, before, result }));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

/// <summary>Fresh observation and durable intent precede each install. Cancel stops before the next package.</summary>
public sealed class SoftwareInstaller(ISystemProbe probe, ISoftwarePackageRunner runner, ISoftwareInstallJournal journal)
{
    public async Task<IReadOnlyList<SoftwareInstallResult>> InstallAsync(IReadOnlyList<string> entryIds,
        Action<SoftwareEntry, SoftwareInstallResult>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var entries = entryIds.Distinct(StringComparer.Ordinal).Select(id => SoftwareCatalog.Entries.SingleOrDefault(entry => entry.Id == id)
            ?? throw new ArgumentException("Unknown software entry.", nameof(entryIds))).ToArray();
        if (entries.Any(entry => !ProtectionPolicy.CanInstallSoftware(entry))) throw new InvalidOperationException("The installation selection is not allowed.");
        var results = new List<SoftwareInstallResult>();
        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var inventory = await Task.Run(probe.GetSoftwareInventory, CancellationToken.None).ConfigureAwait(false);
            if (!inventory.Available) throw new InvalidOperationException(inventory.Error);
            var before = inventory.Find(entry);
            if (before is not null)
            {
                var skipped = new SoftwareInstallResult(entry.Id, SoftwareInstallOutcome.AlreadyInstalled, null);
                results.Add(skipped);
                progress?.Invoke(entry, skipped);
                continue;
            }
            if (cancellationToken.IsCancellationRequested) break;
            journal.Record("intent", entry, before, null);
            SoftwareInstallResult result;
            try
            {
                var exit = await runner.InstallAsync(entry).ConfigureAwait(false);
                result = new(entry.Id, ClassifyExitCode(exit), exit);
                if (result.Outcome is SoftwareInstallOutcome.Installed or SoftwareInstallOutcome.NeedsRestart)
                {
                    var after = await Task.Run(probe.GetSoftwareInventory, CancellationToken.None).ConfigureAwait(false);
                    if (!after.Available || after.Find(entry) is null) result = result with { Outcome = SoftwareInstallOutcome.Unverified };
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                result = new(entry.Id, SoftwareInstallOutcome.Failed, null);
            }
            journal.Record("outcome", entry, before, result);
            results.Add(result);
            progress?.Invoke(entry, result);
            if (result.Outcome == SoftwareInstallOutcome.Unverified) break;
        }
        return results;
    }

    public static SoftwareInstallOutcome ClassifyExitCode(int? code) => code switch
    {
        0 => SoftwareInstallOutcome.Installed,
        3010 or 1641 or -1978334967 or -1978334965 => SoftwareInstallOutcome.NeedsRestart,
        -1978335135 or -1978335189 => SoftwareInstallOutcome.AlreadyInstalled,
        null => SoftwareInstallOutcome.Unverified,
        _ => SoftwareInstallOutcome.Failed,
    };
}
