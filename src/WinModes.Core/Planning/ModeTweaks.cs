namespace WinModes.Core.Planning;

/// <summary>What the planner and the switcher need to know about one setting of the Optimize catalog.</summary>
/// <param name="Id">The id in data/tweaks.json.</param>
/// <param name="Title">Name shown to the user.</param>
/// <param name="IsApplied">The setting already holds the value the catalog asks for.</param>
/// <param name="NeedsElevation">Part of the setting is machine-wide (HKLM or a scheduled task): it needs the administrator helper.</param>
/// <param name="Restart">What the setting needs before it shows: "none", "explorer", "sign-out" or "restart".</param>
public sealed record TweakInfo(string Id, string Title, bool IsApplied, bool NeedsElevation, string Restart);

/// <summary>Read-only view of the Optimize catalog and of what is applied on this PC.</summary>
public interface ITweakProbe
{
    /// <summary>Null when the catalog has no such setting.</summary>
    TweakInfo? Find(string id);
}

/// <summary>Which catalog settings a mode may apply, and how a switch from one mode to another changes them.</summary>
public static class ModeTweaks
{
    /// <summary>
    /// A mode only applies settings that cost nothing to apply and undo: per-user (no administrator prompt) and effective at once
    /// (no restart of Explorer, sign-out or reboot). Anything else belongs on the Optimize page.
    /// </summary>
    public static bool CanBeApplied(TweakInfo tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return !tweak.NeedsElevation && string.Equals(tweak.Restart, "none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Switching from a mode to another keeps the settings both ask for, puts back the ones only the old mode set, and
    /// applies the ones only the new mode wants.
    /// </summary>
    public static (IReadOnlyList<string> Undo, IReadOnlyList<string> Apply) Diff(IReadOnlyCollection<string> ownedByPrevious, IReadOnlyCollection<string> wanted)
    {
        ArgumentNullException.ThrowIfNull(ownedByPrevious);
        ArgumentNullException.ThrowIfNull(wanted);
        var keep = ownedByPrevious.Intersect(wanted, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (
            [.. ownedByPrevious.Where(id => !keep.Contains(id))],
            [.. wanted.Where(id => !keep.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }
}
