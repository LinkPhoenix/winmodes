namespace WinModes.Core.Monitoring;

/// <summary>The readings of this PC that WinModes can take in the background, one by one.</summary>
[Flags]
public enum StatsSources
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Network = 4,

    /// <summary>Finding the AI and coding tools among the running processes, with the memory and the CPU each one uses.</summary>
    AiTools = 8,
}

/// <summary>
/// What the features that are switched on would like to read, as plain facts the app fills from its settings. Together with what the
/// user allows at all (Settings, Monitoring) it decides what is read: a source nobody shows, or one the user turned off, is never read.
/// </summary>
public sealed record SamplingDemand
{
    /// <summary>The widget is on the desktop or on the taskbar.</summary>
    public bool Widget { get; init; }

    /// <summary>The blocks of the widget that are drawn, once the options of the Widget page and the Monitoring choices agree.</summary>
    public bool WidgetCpu { get; init; }

    public bool WidgetMemory { get; init; }

    public bool WidgetNetwork { get; init; }

    public bool WidgetAiTools { get; init; }

    /// <summary>The tray icon draws the memory of the AI tools.</summary>
    public bool TrayMeter { get; init; }

    /// <summary>A memory alert is set, for all the AI tools or for one of them.</summary>
    public bool AiMemoryAlerts { get; init; }

    /// <summary>Idle AI sessions are ended automatically.</summary>
    public bool EndIdleSessions { get; init; }

    /// <summary>The daily usage of each AI tool is recorded.</summary>
    public bool RecordUsage { get; init; }

    /// <summary>What the features ask for, whatever the user allows.</summary>
    public StatsSources Wanted
    {
        get
        {
            var sources = StatsSources.None;
            if (Widget && WidgetCpu)
            {
                sources |= StatsSources.Cpu;
            }

            if (Widget && WidgetMemory)
            {
                sources |= StatsSources.Memory;
            }

            if (Widget && WidgetNetwork)
            {
                sources |= StatsSources.Network;
            }

            if ((Widget && WidgetAiTools) || TrayMeter || AiMemoryAlerts || EndIdleSessions || RecordUsage)
            {
                sources |= StatsSources.AiTools;
            }

            return sources;
        }
    }

    /// <summary>What is read: what the features ask for, among what the user allows.</summary>
    public StatsSources Resolve(StatsSources allowed) => Wanted & allowed;

    /// <summary>
    /// Whether a timer has to run at all. The widget needs one even when it shows no PC figure, since it also refreshes its plans and its
    /// mode; the features that live on the AI tools are inert, and cost nothing, while the user keeps them from being read.
    /// </summary>
    public bool NeedsTimer(StatsSources allowed) =>
        Widget || (allowed.HasFlag(StatsSources.AiTools) && (TrayMeter || AiMemoryAlerts || EndIdleSessions || RecordUsage));
}
