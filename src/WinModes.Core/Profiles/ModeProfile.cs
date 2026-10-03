namespace WinModes.Core.Profiles;

/// <summary>One generated mode profile (profiles/code.json, work.json, game.json).</summary>
public sealed record ModeProfile
{
    public required string Mode { get; init; }
    public string Label { get; init; } = "";
    public string Intent { get; init; } = "";
    public PowerSettings Power { get; init; } = new();
    public WslSettings Wsl { get; init; } = new();
    public AppSettings Apps { get; init; } = new();
    public ServiceSettings Services { get; init; } = new();

    /// <summary>
    /// Ids of settings of the Optimize catalog (data/tweaks.json) that the mode turns on while it is active and puts back when it ends.
    /// Only per-user settings that take effect at once are applied: see <see cref="ModeTweaks"/>.
    /// </summary>
    public IReadOnlyList<string> TweakIds { get; init; } = [];
}

public sealed record PowerSettings
{
    public string Plan { get; init; } = "";
    public string? Fallback { get; init; }

    /// <summary>
    /// Values to change on a copy of the plan (see <see cref="Planning.PowerCatalog"/>), for example the display and sleep timeouts.
    /// When there are some, the mode makes a copy of its plan, changes the copy and uses it; the copy is deleted when the mode ends.
    /// </summary>
    public IReadOnlyList<PowerValue> Values { get; init; } = [];
}

/// <summary>One power setting of a mode: the value on mains (Ac) and on battery (Dc); null leaves that side as the plan has it.</summary>
public sealed record PowerValue
{
    public required string Setting { get; init; }
    public int? Ac { get; init; }
    public int? Dc { get; init; }
}

public sealed record WslSettings
{
    public bool Running { get; init; }
    public string? Docker { get; init; }
}

public sealed record AppSettings
{
    public IReadOnlyList<AppClose> Close { get; init; } = [];
    public IReadOnlyList<AppLaunch> Launch { get; init; } = [];
    public IReadOnlyList<string> KeepOpen { get; init; } = [];
}

public sealed record AppClose
{
    public required string Id { get; init; }
    public required string Process { get; init; }
    public string Why { get; init; } = "";
}

public sealed record AppLaunch
{
    public required string Id { get; init; }
    public required string Path { get; init; }
}

public sealed record ServiceSettings
{
    public IReadOnlyList<ServiceStop> Stop { get; init; } = [];
    public IReadOnlyList<ServiceStart> EnsureRunning { get; init; } = [];
}

public sealed record ServiceStop
{
    public required string Id { get; init; }
    public string SetStartMode { get; init; } = "Manual";
    public string Why { get; init; } = "";
}

public sealed record ServiceStart
{
    public required string Id { get; init; }
}
