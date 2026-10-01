using System.Collections.Concurrent;
using System.Windows.Media;

namespace WinModes.App.Services;

/// <summary>
/// The icon of each AI tool, taken from its own program. The program is seen while the tool runs and remembered
/// afterwards, so the plan rows keep their icon when the tool is closed.
/// </summary>
internal static class ToolIcons
{
    private static readonly ConcurrentDictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers the program of every running tool. Claude Code comes last so its icon wins over the desktop app's.</summary>
    public static void Remember(IEnumerable<AiToolUsage> running)
    {
        foreach (var tool in running.Where(tool => tool.ExecutablePath is not null).OrderBy(tool => tool.Name == "Claude Code"))
        {
            Paths[Key(tool.Name)] = tool.ExecutablePath!;
        }
    }

    /// <param name="tool">"Claude", "Codex" or a full tool name such as "Claude Code".</param>
    public static ImageSource? For(string tool) => Paths.TryGetValue(Key(tool), out var path) ? IconCache.Get(path) : null;

    private static string Key(string tool) => tool.Split(' ')[0];
}
