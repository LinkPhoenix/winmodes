namespace WinModes.Core.Planning;

/// <summary>The priority class of a process, as Task Manager names it.</summary>
public enum ProcessPriority
{
    Unknown,
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    Realtime,
}

/// <summary>Plain facts about a sampled process, worked out from what the sampler already read: nothing here touches the system.</summary>
public static class ProcessFacts
{
    /// <summary>
    /// The priority class from the base priority the toolhelp snapshot gives (4 idle, 6 below normal, 8 normal, 10 above normal, 13 high,
    /// 24 realtime). A value between two classes belongs to the lower one; zero or less means Windows gave none.
    /// </summary>
    public static ProcessPriority PriorityOf(int basePriority) => basePriority switch
    {
        <= 0 => ProcessPriority.Unknown,
        <= 4 => ProcessPriority.Idle,
        <= 7 => ProcessPriority.BelowNormal,
        <= 9 => ProcessPriority.Normal,
        <= 12 => ProcessPriority.AboveNormal,
        <= 23 => ProcessPriority.High,
        _ => ProcessPriority.Realtime,
    };

    /// <summary>How long a process has been running; null when its start time is unknown or lies in the future (a clock change).</summary>
    public static TimeSpan? RunningFor(DateTime? start, DateTime now) => start is { } started && now >= started ? now - started : null;
}
