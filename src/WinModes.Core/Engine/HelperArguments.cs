using WinModes.Core.Tuning;

namespace WinModes.Core.Engine;

public enum HelperCommandKind
{
    Apply,
    Revert,
    Change,
    TaskInstall,
    TaskRemove,
}

/// <summary>One ":action name name" group of the <c>change</c> verb.</summary>
public sealed record ChangeGroup(TuneAction Action, IReadOnlyList<string> Names);

/// <param name="FromTask">Started by the silent-switch task: no prompt was shown, so only a mode switch is accepted.</param>
public sealed record HelperCommand(HelperCommandKind Kind, bool FromTask = false, string? Mode = null, IReadOnlyList<ChangeGroup>? Groups = null);

/// <summary>
/// Reads the command line of the elevated helper. The arguments come from a process that is not elevated, so
/// nothing is trusted: only the known verbs are accepted, action names must be names (a number would also parse
/// as an enum value), and every service name, tweak id and mode name must be short and made of a few characters.
/// </summary>
public static class HelperArguments
{
    public const int MaxNameLength = 256;
    public const int MaxNames = 500;

    private const string ChangeVerb = "change";
    private const string TaskVerb = "task";
    private const char ActionPrefix = ':';

    /// <returns>The command, or null when the arguments are not an accepted command.</returns>
    public static HelperCommand? Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var fromTask = arguments.Count > 0 && arguments[0] == SilentSwitchTask.HelperVerb;
        // The task passes an empty mode for "revert".
        var args = fromTask ? arguments.Skip(1).Where(argument => argument.Length > 0).ToList() : [.. arguments];

        if (args is ["revert"])
        {
            return new HelperCommand(HelperCommandKind.Revert, fromTask);
        }

        if (args is ["apply", var mode] && IsValidName(mode))
        {
            return new HelperCommand(HelperCommandKind.Apply, fromTask, Mode: mode);
        }

        if (fromTask)
        {
            return null;
        }

        if (args is [TaskVerb, "install"])
        {
            return new HelperCommand(HelperCommandKind.TaskInstall);
        }

        if (args is [TaskVerb, "remove"])
        {
            return new HelperCommand(HelperCommandKind.TaskRemove);
        }

        return args.Count > 1 && args[0] == ChangeVerb && ParseGroups(args.Skip(1)) is { } groups
            ? new HelperCommand(HelperCommandKind.Change, Groups: groups)
            : null;
    }

    /// <summary>Letters, digits, space and <c>_ . $ @ - ( ) + , #</c>: what service names (e.g. "Intel(R) ... Service"), tweak ids and mode names use.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= MaxNameLength
        && name == name.Trim()
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '.' or '$' or '@' or '-' or '(' or ')' or '+' or ',' or '#');

    // ":manual A B :stop A :tweak widgets". ":restore" and ":untweak" without a name undo every recorded change.
    private static List<ChangeGroup>? ParseGroups(IEnumerable<string> arguments)
    {
        var groups = new List<(TuneAction Action, List<string> Names)>();
        var nameCount = 0;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(ActionPrefix))
            {
                if (!Enum.GetNames<TuneAction>().Contains(argument[1..], StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }

                groups.Add((Enum.Parse<TuneAction>(argument[1..], ignoreCase: true), []));
            }
            else if (groups.Count == 0 || !IsValidName(argument) || ++nameCount > MaxNames)
            {
                return null;
            }
            else
            {
                groups[^1].Names.Add(argument);
            }
        }

        return [.. groups.Select(group => new ChangeGroup(group.Action, group.Names))];
    }
}
