using System.Text;
using WinModes.Core.Apps;

namespace WinModes.App.Services;

/// <summary>
/// What the Debloat page reads from this PC: the packages installed for the user and the names of the Start menu apps. Both are slow to read
/// (each starts PowerShell), so they are read side by side and the last reading is kept for the run, so the page can show it at once.
/// </summary>
internal sealed class DebloatSnapshot
{
    private string? _signature;

    private DebloatSnapshot(IReadOnlyList<InstalledPackage> installed, IReadOnlyDictionary<string, string> startNames)
    {
        Installed = installed;
        StartNames = startNames;
    }

    public static DebloatSnapshot? Last { get; private set; }

    public IReadOnlyList<InstalledPackage> Installed { get; }

    /// <summary>The name that the Start menu shows, by package family.</summary>
    public IReadOnlyDictionary<string, string> StartNames { get; }

    public static async Task<DebloatSnapshot> TakeAsync()
    {
        var installed = AppxService.ListAsync();
        var startNames = AppxService.StartAppNamesAsync();
        await Task.WhenAll(installed, startNames);

        var snapshot = new DebloatSnapshot(installed.Result, startNames.Result);
        Last = snapshot;
        return snapshot;
    }

    /// <summary>True when both read the same apps, so a page that shows one has nothing to redraw for the other.</summary>
    public bool SameAs(DebloatSnapshot other) => Signature == other.Signature;

    private string Signature => _signature ??= BuildSignature();

    private string BuildSignature()
    {
        var text = new StringBuilder();
        foreach (var package in Installed.OrderBy(package => package.FullName, StringComparer.Ordinal))
        {
            text.Append(package.FullName).Append(';');
        }

        text.Append('|');
        foreach (var (family, name) in StartNames.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append(family).Append('=').Append(name).Append(';');
        }

        return text.ToString();
    }
}
