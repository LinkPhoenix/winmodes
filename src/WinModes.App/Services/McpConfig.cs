using System.IO;
using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>Keeps <see cref="McpServers.Configured"/> in step with the servers declared in Codex's configuration.</summary>
internal static class McpConfig
{
    private static DateTime _loadedStamp = DateTime.MinValue;

    /// <summary>Cheap to call often: the file is parsed again only when it changed.</summary>
    public static void Refresh()
    {
        try
        {
            var path = CodexMcpConfig.DefaultPath;
            // A missing file gives the year 1601, so a deleted configuration is picked up too.
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp != _loadedStamp)
            {
                McpServers.Configured = CodexMcpConfig.Load(path);
                _loadedStamp = stamp;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable configuration: servers are still recognised from their command lines.
        }
    }
}
