using System.Globalization;
using System.Text.Json;
using WinModes.Core.Localization;

namespace WinModes.Core.Usage;

public enum CheckState
{
    Ok,
    Warning,
    Failed,
}

/// <summary>One thing that must be true for a tool's usage to show up, with what to do when it is not.</summary>
public sealed record CheckItem(string Label, CheckState State, string Detail);

/// <summary>The sign-in file of a tool, as far as the online reading is concerned. The token itself is never exposed.</summary>
public enum SignInState
{
    Missing,
    Unusable,
    Expired,
    Valid,
}

public sealed record SignInCheck(SignInState State, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Checks, one by one, what the widget needs to show the plan usage of Claude Code or Codex, and says in words
/// what is missing. Read-only: it opens the same files the widget reads and sends nothing anywhere.
/// </summary>
public static class ProviderCheck
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    public static IReadOnlyList<CheckItem> Claude(string settingsPath, string statusLineSettingsPath, string limitsRecordPath,
        string credentialsPath, string statusCallPath, bool readOnline, DateTimeOffset now, bool? ownSession = null)
    {
        var plan = Subscriptions.ReadClaude(settingsPath);
        return
        [
            plan is null
                ? new(Loc.T("Plan"), CheckState.Failed, Loc.T("No Pro or Max plan found in Claude Code's settings. Sign in to Claude Code with your claude.ai account."))
                : new(Loc.T("Plan"), CheckState.Ok, Loc.F("{0} plan found.", plan.Plan)),
            StatusLine(statusLineSettingsPath),
            Call(statusCallPath, now),
            Record(limitsRecordPath, now),
            .. OwnSession(ownSession),
            Online(OnlineUsage.InspectClaudeSignIn(credentialsPath, now), readOnline),
        ];
    }

    public static IReadOnlyList<CheckItem> Codex(string codexHome, string authPath, bool readOnline, DateTimeOffset now, bool? ownSession = null)
    {
        var items = new List<CheckItem>();
        if (!Directory.Exists(codexHome))
        {
            items.Add(new(Loc.T("Codex folder"), CheckState.Failed, Loc.T("The Codex folder was not found on this PC. Install Codex and run it once.")));
        }
        else
        {
            items.Add(new(Loc.T("Codex folder"), CheckState.Ok, Loc.T("Found.")));
            var status = Subscriptions.ReadCodex(codexHome);
            items.Add(status is null
                ? new(Loc.T("Usage record"), CheckState.Warning, Loc.T("No usage found in the Codex session logs yet. Codex writes it during a session."))
                : Age(Loc.T("Usage record"), status.SeenAt, now, Loc.T("Codex writes it during a session.")));
            items.Add(status is null
                ? new(Loc.T("Plan"), CheckState.Warning, Loc.T("Not known until Codex has recorded its usage."))
                : new(Loc.T("Plan"), CheckState.Ok, Loc.F("{0} plan found.", status.Plan)));
        }

        items.AddRange(OwnSession(ownSession));
        items.Add(Online(OnlineUsage.InspectCodexSignIn(authPath), readOnline));
        return items;
    }

    private static CheckItem StatusLine(string settingsPath)
    {
        var label = Loc.T("Status line");
        try
        {
            return ClaudeStatusLineSetup.Read(settingsPath) switch
            {
                ClaudeStatusLineSetup.State.Ours => new(label, CheckState.Ok, Loc.T("The WinModes status line is set in Claude Code.")),
                ClaudeStatusLineSetup.State.Other => new(label, CheckState.Warning, Loc.T("Claude Code uses another status line, so WinModes cannot record the usage.")),
                _ => new(label, CheckState.Warning, Loc.T("Not set. Turn on 'Record Claude usage' to get the usage from Claude Code.")),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(label, CheckState.Failed, Loc.T("Claude Code's settings could not be read."));
        }
    }

    private static CheckItem Call(string callPath, DateTimeOffset now)
    {
        var label = Loc.T("Status line calls");
        if (ClaudeStatusLine.LoadCall(callPath) is not { } call)
        {
            return new(label, CheckState.Warning, Loc.T("Claude Code has not run the WinModes status line yet. Send a message in a Claude Code session. If this stays empty, the installed status line is older than this version, or Claude Code does not run it in that session."));
        }

        var when = Age(label, call.At, now, "");
        return call.HasRateLimits
            ? new(label, when.State == CheckState.Ok ? CheckState.Ok : CheckState.Warning, Loc.F("Last call {0}: Claude Code sent the usage limits.", Local(call.At)))
            : new(label, CheckState.Warning, Loc.F("Last call {0}: Claude Code sent no usage limits. They exist for Pro and Max plans, after the first answer of a session.", Local(call.At)));
    }

    private static CheckItem Record(string recordPath, DateTimeOffset now)
    {
        var label = Loc.T("Usage record");
        var limits = ClaudeStatusLine.Load(recordPath);
        return limits is null
            ? new(label, CheckState.Warning, Loc.T("Nothing recorded yet. Claude Code sends the limits to its status line after the first answer of a session."))
            : Age(label, limits.SeenAt, now, Loc.T("It is refreshed while a Claude Code session is open."));
    }

    private static CheckItem Age(string label, DateTimeOffset? seenAt, DateTimeOffset now, string hint)
    {
        if (seenAt is not { } seen)
        {
            return new(label, CheckState.Warning, Loc.T("Found, but without a date."));
        }

        var age = now - seen;
        return age <= Fresh
            ? new(label, CheckState.Ok, Loc.F("Updated {0} min ago.", Math.Max((int)age.TotalMinutes, 1)))
            : new(label, CheckState.Warning, Loc.F("Last updated {0}.", Local(seen)) + " " + hint);
    }

    /// <summary>WinModes' own sign-in, when the caller knows it: the most reliable way to get the usage, so it comes before the other sign-in.</summary>
    private static IEnumerable<CheckItem> OwnSession(bool? signedIn)
    {
        if (signedIn is { } known)
        {
            yield return known
                ? new(Loc.T("WinModes account"), CheckState.Ok, Loc.T("Signed in: the usage is read online with a session of its own, renewed by WinModes."))
                : new(Loc.T("WinModes account"), CheckState.Warning, Loc.T("Not signed in. Use the WinModes account row of this section to sign in."));
        }
    }

    private static CheckItem Online(SignInCheck signIn, bool readOnline)
    {
        var label = Loc.T("Online reading");
        var suffix = readOnline ? "" : " " + Loc.T("The online reading is off.");
        return signIn.State switch
        {
            SignInState.Valid => new(label, CheckState.Ok, Loc.T("A sign-in is available.") + suffix),
            SignInState.Expired => new(label, CheckState.Warning,
                Loc.F("The sign-in expired on {0}. The tool renews it when it runs.", Local(signIn.ExpiresAt!.Value)) + suffix),
            SignInState.Unusable => new(label, CheckState.Warning, Loc.T("The sign-in file has no usable sign-in.") + suffix),
            _ => new(label, CheckState.Warning, Loc.T("No sign-in file found. Sign in to the tool first.") + suffix),
        };
    }

    private static string Local(DateTimeOffset moment) => moment.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
}
