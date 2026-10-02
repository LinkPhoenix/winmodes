using System.Globalization;
using WinModes.Core.Localization;
using WinModes.Core.Profiles;

namespace WinModes.Core.Planning;

/// <summary>One power setting a mode may change, and what values it accepts.</summary>
/// <param name="Id">Name used in the profile.</param>
/// <param name="Title">Name shown to the user.</param>
/// <param name="SubGroup">Power setting subgroup (an alias or a GUID that powercfg accepts).</param>
/// <param name="Setting">The setting inside the subgroup.</param>
/// <param name="Minimum">Lowest value accepted.</param>
/// <param name="Maximum">Highest value accepted.</param>
/// <param name="Seconds">The value is a number of seconds, where 0 means never.</param>
public sealed record PowerSettingInfo(string Id, string Title, string SubGroup, string Setting, int Minimum, int Maximum, bool Seconds);

/// <summary>
/// The power settings a mode may change. A mode never edits a power plan of the user: it changes a copy of it,
/// made when the mode starts and deleted when it ends, so there is nothing to put back by hand.
/// </summary>
public static class PowerCatalog
{
    private const int OneDay = 24 * 60 * 60;

    // Aliases are those of powercfg itself (powercfg /aliases); a raw GUID is used where it has no alias.
    private static readonly PowerSettingInfo[] All =
    [
        new("display-off", "Turn the display off after", "SUB_VIDEO", "VIDEOIDLE", 0, OneDay, Seconds: true),
        new("sleep-after", "Put the PC to sleep after", "SUB_SLEEP", "STANDBYIDLE", 0, OneDay, Seconds: true),
        new("hibernate-after", "Hibernate after", "SUB_SLEEP", "HIBERNATEIDLE", 0, OneDay, Seconds: true),
        new("disk-off", "Turn the hard disk off after", "SUB_DISK", "DISKIDLE", 0, OneDay, Seconds: true),
        // Below 5 % Windows stops responding well, and the system would slow to a crawl.
        new("cpu-max", "Highest processor speed (%)", "SUB_PROCESSOR", "PROCTHROTTLEMAX", 5, 100, Seconds: false),
        new("cpu-min", "Lowest processor speed (%)", "SUB_PROCESSOR", "PROCTHROTTLEMIN", 0, 100, Seconds: false),
        new("usb-suspend", "USB selective suspend (1 on, 0 off)", "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1, Seconds: false),
    ];

    public static IReadOnlyList<PowerSettingInfo> Settings => All;

    public static PowerSettingInfo? Find(string id) => All.FirstOrDefault(setting => setting.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The rules a mode's power values break; empty means they may be applied.</summary>
    public static IReadOnlyList<string> Validate(PowerSettings power)
    {
        ArgumentNullException.ThrowIfNull(power);
        var violations = new List<string>();
        foreach (var value in power.Values)
        {
            var info = Find(value.Setting);
            if (info is null)
            {
                violations.Add($"Power setting '{value.Setting}' is not one a mode may change.");
                continue;
            }

            foreach (var (side, number) in new[] { ("on mains", value.Ac), ("on battery", value.Dc) })
            {
                if (number is { } given && (given < info.Minimum || given > info.Maximum))
                {
                    violations.Add($"Power setting '{value.Setting}' {side}: {given} is outside {info.Minimum} to {info.Maximum}.");
                }
            }

            if (value.Ac is null && value.Dc is null)
            {
                violations.Add($"Power setting '{value.Setting}' has no value.");
            }
        }

        if (power.Values.Select(value => value.Setting).Distinct(StringComparer.OrdinalIgnoreCase).Count() != power.Values.Count)
        {
            violations.Add("A power setting is listed twice.");
        }

        return violations;
    }

    /// <summary>The powercfg arguments that set one value on a plan; one list per side that has a value.</summary>
    public static IReadOnlyList<string[]> Commands(string planGuid, PowerValue value)
    {
        ArgumentNullException.ThrowIfNull(planGuid);
        ArgumentNullException.ThrowIfNull(value);
        var info = Find(value.Setting) ?? throw new ProfileException($"Unknown power setting '{value.Setting}'.");
        var commands = new List<string[]>();
        if (value.Ac is { } ac)
        {
            commands.Add(["/setacvalueindex", planGuid, info.SubGroup, info.Setting, ac.ToString(CultureInfo.InvariantCulture)]);
        }

        if (value.Dc is { } dc)
        {
            commands.Add(["/setdcvalueindex", planGuid, info.SubGroup, info.Setting, dc.ToString(CultureInfo.InvariantCulture)]);
        }

        return commands;
    }

    /// <summary>"never" for 0, otherwise "30 min" style text, for the preview.</summary>
    public static string DescribeSeconds(int seconds) =>
        seconds == 0 ? Loc.T("never")
        : seconds % 3600 == 0 ? Loc.F("{0} h", seconds / 3600)
        : seconds % 60 == 0 ? Loc.F("{0} min", seconds / 60)
        : Loc.F("{0} s", seconds);
}
