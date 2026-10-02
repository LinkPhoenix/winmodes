namespace WinModes.Core.Usage.Tokens;

/// <summary>
/// Tokens of one request or a sum of requests. The four kinds do not overlap: <see cref="Input"/> is the part of the prompt that
/// was neither read from nor written to the cache, so that Claude and Codex, which report them differently, add up the same way.
/// </summary>
public readonly record struct TokenCounts(long Input, long Output, long CacheRead, long CacheWrite)
{
    public static TokenCounts Empty => default;

    /// <summary>Everything the model processed: new input, output, and what came from or went to the cache.</summary>
    public long Total => Input + Output + CacheRead + CacheWrite;

    public bool IsEmpty => Total == 0;

    /// <summary>A figure went down: a counter that should only grow was reset.</summary>
    public bool HasNegative => Input < 0 || Output < 0 || CacheRead < 0 || CacheWrite < 0;

    public static TokenCounts operator +(TokenCounts left, TokenCounts right) => left.Add(right);

    public static TokenCounts operator -(TokenCounts left, TokenCounts right) => left.Subtract(right);

    public TokenCounts Add(TokenCounts other) => new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite);

    public TokenCounts Subtract(TokenCounts other) => new(Input - other.Input, Output - other.Output, CacheRead - other.CacheRead, CacheWrite - other.CacheWrite);

    /// <summary>The larger of each kind: the same request logged again with its output grown keeps its final figures.</summary>
    public TokenCounts Max(TokenCounts other) => new(Math.Max(Input, other.Input), Math.Max(Output, other.Output), Math.Max(CacheRead, other.CacheRead), Math.Max(CacheWrite, other.CacheWrite));
}

/// <summary>Tokens of one request found in a log, with when, with which model and in which project.</summary>
/// <param name="Key">Identifies the request so a request logged several times counts once; 0 when each event is its own.</param>
public sealed record TokenEvent(string Tool, long Key, DateTimeOffset At, string Model, string Project, TokenCounts Counts);

/// <summary>A tool, a model or a project, with the tokens it used.</summary>
public sealed record TokenGroup(string Name, TokenCounts Counts);

/// <summary>The tokens used on one day.</summary>
public sealed record TokenDay(DateOnly Day, TokenCounts Counts);

/// <summary>What one tool used over a period, ranked by tokens.</summary>
public sealed record TokenSummary(string Tool, TokenCounts Total, IReadOnlyList<TokenGroup> Models, IReadOnlyList<TokenGroup> Projects, IReadOnlyList<TokenDay> Days);
