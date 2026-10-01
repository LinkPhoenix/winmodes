using System.Globalization;
using System.Text;
using WinModes.Core.Planning;

namespace WinModes.Core.Reports;

/// <summary>Everything a report shows. It holds names and figures only: no path, command line, project or account name.</summary>
public sealed record SystemReportData(
    string AppVersion,
    DateTime CreatedAt,
    string WindowsVersion,
    int LogicalProcessors,
    MemorySample Memory,
    string? ActiveMode,
    int ProcessCount,
    int RunningServiceCount,
    IReadOnlyList<ProcessGroup> TopProcesses,
    IReadOnlyList<AiSession> AiSessions);

/// <summary>
/// Builds a Markdown snapshot of the PC that is safe to paste in a public issue: it is written from
/// process names and totals only, whatever the privacy setting of the app.
/// </summary>
public static class SystemReport
{
    private const double MbPerGb = 1024;

    public static string Build(SystemReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        text.AppendLine("# WinModes report");
        text.AppendLine();
        text.AppendLine(culture, $"- WinModes: {data.AppVersion}");
        text.AppendLine(culture, $"- Created: {data.CreatedAt:yyyy-MM-dd HH:mm}");
        text.AppendLine(culture, $"- Windows: {data.WindowsVersion}");
        text.AppendLine(culture, $"- Logical processors: {data.LogicalProcessors}");
        text.AppendLine(culture, $"- Active mode: {data.ActiveMode ?? "none"}");
        text.AppendLine();

        text.AppendLine("## Memory");
        text.AppendLine();
        text.AppendLine(culture, $"- In use: {data.Memory.UsedGb:0.0} of {data.Memory.TotalGb:0.0} GB ({data.Memory.UsedPercent:0} %)");
        text.AppendLine(culture, $"- Available: {data.Memory.AvailableGb:0.0} GB");
        text.AppendLine(culture, $"- Committed: {data.Memory.CommittedGb:0.0} of {data.Memory.CommitLimitGb:0.0} GB");
        text.AppendLine(culture, $"- Processes: {data.ProcessCount}, services running: {data.RunningServiceCount}");
        text.AppendLine();

        text.AppendLine("## AI tools");
        text.AppendLine();
        if (data.AiSessions.Count == 0)
        {
            text.AppendLine("None running.");
        }
        else
        {
            text.AppendLine("| Tool | Sessions | Processes | MCP processes | Memory |");
            text.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var tool in data.AiSessions.GroupBy(session => session.Tool.Name).OrderByDescending(group => group.Sum(session => session.TotalMemoryMb)))
            {
                var processes = tool.Sum(session => session.Descendants.Count + 1);
                var servers = tool.Sum(session => session.Descendants.Count(McpServers.IsServer));
                text.AppendLine(culture, $"| {tool.Key} | {tool.Count()} | {processes} | {servers} | {Gb(tool.Sum(session => session.TotalMemoryMb))} |");
            }

            var duplicates = McpServers.FindDuplicates(data.AiSessions);
            if (duplicates.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("MCP servers running in several sessions:");
                text.AppendLine();
                foreach (var duplicate in duplicates)
                {
                    text.AppendLine(culture, $"- {duplicate.Name}: {duplicate.Sessions} sessions, {duplicate.Processes} processes, {Gb(duplicate.MemoryMb)}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("## Top memory consumers");
        text.AppendLine();
        text.AppendLine("| Program | Processes | Private memory |");
        text.AppendLine("|---|---:|---:|");
        foreach (var group in data.TopProcesses)
        {
            text.AppendLine(culture, $"| {group.Name} | {group.Count} | {Gb(group.PrivateMemoryMb)} |");
        }

        text.AppendLine();
        text.AppendLine("This report contains program names and totals only: no folder, command line, project name or account name.");
        return text.ToString();
    }

    private static string Gb(double megabytes) =>
        megabytes >= MbPerGb
            ? string.Create(CultureInfo.InvariantCulture, $"{megabytes / MbPerGb:0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{megabytes:0} MB");
}
