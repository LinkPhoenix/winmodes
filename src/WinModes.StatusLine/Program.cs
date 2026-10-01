using WinModes.Core.Usage;

// Status line command for Claude Code. Claude Code sends the session state as JSON on standard input; this
// records the plan's usage limits for the WinModes widget and prints a short line. It reads nothing else,
// sends nothing anywhere, and never fails loudly: a status line must not disturb the session.
try
{
    Console.InputEncoding = System.Text.Encoding.UTF8;
    var input = Console.In.ReadToEnd();
    // Which fields arrived (never their values), so the check on the Widget page can tell "never called" from "no limits sent".
    ClaudeStatusLine.SaveCall(ClaudeStatusLine.DefaultCallPath, ClaudeStatusLine.Describe(input, DateTimeOffset.UtcNow));
    var limits = ClaudeStatusLine.Parse(input, DateTimeOffset.UtcNow);
    if (limits is not null)
    {
        // A session without limits (before its first answer) keeps the last known ones.
        ClaudeStatusLine.Save(ClaudeStatusLine.DefaultRecordPath, limits);
    }

    Console.WriteLine(ClaudeStatusLine.Line(input, limits ?? ClaudeStatusLine.Load(ClaudeStatusLine.DefaultRecordPath)));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.WriteLine("WinModes");
}

return 0;
