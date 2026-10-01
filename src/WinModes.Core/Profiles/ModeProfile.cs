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
}

public sealed record PowerSettings
{
    public string Plan { get; init; } = "";
    public string? Fallback { get; init; }
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
