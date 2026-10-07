using System.Net.Http;
using WinModes.Core.Accounts;
using WinModes.Core.Usage;

namespace WinModes.App.Services;

/// <summary>
/// Keeps the last known plan and usage limits of Claude, Codex and Grok for the widget. Every figure comes from the provider itself,
/// asked with the sign-in of WinModes (never from the files of Claude Code, Codex or Grok), at most once per
/// <see cref="OnlineInterval"/> for each provider and only while something asks for them: a provider that fails or refuses is
/// asked less and less often, and for as long as it says when it sends a Retry-After. An account that is not signed in is not shown.
/// </summary>
internal static class SubscriptionMonitor
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OnlineInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromHours(1);

    // The plan of a Claude account changes rarely and costs one more request: it is asked again after this long.
    private static readonly TimeSpan ClaudePlanLifetime = TimeSpan.FromHours(6);

    // No redirect is followed, so the sign-in header can never be sent to another host.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = RequestTimeout };

    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static readonly PollBackoff ClaudePoll = new(OnlineInterval, LongestBackoff);
    private static readonly PollBackoff CodexPoll = new(OnlineInterval, LongestBackoff);
    private static readonly PollBackoff GrokPoll = new(OnlineInterval, LongestBackoff);
    private static SubscriptionStatus? _claude;
    private static SubscriptionStatus? _codex;
    private static SubscriptionStatus? _grok;
    private static (string Plan, DateTimeOffset At)? _claudePlan;
    private static int _refreshing;
    private static (bool Claude, bool Codex, bool Grok) _choice = (true, true, true);

    /// <summary>Last statuses read; empty until the first read ends or while no wanted account is signed in.</summary>
    public static IReadOnlyList<SubscriptionStatus> Current { get; private set; } = [];

    /// <summary>False until the first read has ended, so "nothing found" is not shown too early.</summary>
    public static bool HasRead { get; private set; }

    /// <summary>Reads again at once, for example right after signing in or out.</summary>
    public static void RefreshNow()
    {
        _lastRefreshUtc = DateTime.MinValue;
        ClaudePoll.Reset();
        CodexPoll.Reset();
        GrokPoll.Reset();
    }

    /// <summary>Returns what is known now and starts a background read when it is getting old.</summary>
    public static IReadOnlyList<SubscriptionStatus> Get(bool claudeWanted = true, bool codexWanted = true, bool grokWanted = true)
    {
        // A changed choice is applied at once instead of waiting for the next read.
        var choice = (claudeWanted, codexWanted, grokWanted);
        if (choice != _choice)
        {
            _choice = choice;
            RefreshNow();
        }

        if (DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var askClaude = claudeWanted && AccountSession.IsSignedIn(AccountProvider.Claude);
                    var askCodex = codexWanted && AccountSession.IsSignedIn(AccountProvider.ChatGpt);
                    var askGrok = grokWanted && AccountSession.IsSignedIn(AccountProvider.Grok);

                    // Signing out, or hiding a plan, drops its figures at once.
                    if (!askClaude)
                    {
                        (_claude, _claudePlan) = (null, null);
                    }

                    if (!askCodex)
                    {
                        _codex = null;
                    }

                    if (!askGrok)
                    {
                        _grok = null;
                    }

                    // A failed request keeps the previous figures; they are dated in the widget.
                    if (askCodex && CodexPoll.IsDue(now))
                    {
                        var request = await AccountSession.TokensAsync(AccountProvider.ChatGpt) is { } own ? OnlineUsage.CodexRequestFor(own.AccessToken, own.AccountId) : null;
                        var answer = await AskAsync(request, json => OnlineUsage.ParseCodex(json, now));
                        answer.Settle(CodexPoll, now);
                        _codex = answer.Value ?? _codex;
                    }

                    if (askClaude && ClaudePoll.IsDue(now))
                    {
                        var tokens = await AccountSession.TokensAsync(AccountProvider.Claude);
                        if (tokens is not null && (_claudePlan is not { } known || now - known.At > ClaudePlanLifetime)
                            && await AskTextAsync(OnlineUsage.ClaudeProfileRequestFor(tokens.AccessToken)) is { } profile
                            && Subscriptions.ClaudePlanFromProfile(profile) is { } plan)
                        {
                            _claudePlan = (plan, now);
                        }

                        var request = tokens is null ? null : OnlineUsage.ClaudeRequestFor(tokens.AccessToken);
                        var answer = await AskAsync(request, json => OnlineUsage.ParseClaude(json, _claudePlan?.Plan ?? Loc.T("Plan unknown"), now));
                        answer.Settle(ClaudePoll, now);
                        _claude = answer.Value ?? _claude;
                    }

                    if (askGrok && GrokPoll.IsDue(now))
                    {
                        var tokens = await AccountSession.TokensAsync(AccountProvider.Grok);
                        var request = tokens is { AccountId: { Length: > 0 } userId } ? OnlineUsage.GrokRequestFor(tokens.AccessToken, userId) : null;
                        var plan = tokens is null ? null : AccountProvider.GrokPlanName(tokens.AccessToken);
                        var answer = await AskAsync(request, json => OnlineUsage.ParseGrok(json, plan ?? Loc.T("Plan unknown"), now));
                        answer.Settle(GrokPoll, now);
                        _grok = answer.Value ?? _grok;
                    }

                    Current = [.. new[] { askClaude ? _claude : null, askCodex ? _codex : null, askGrok ? _grok : null }.OfType<SubscriptionStatus>()];
                    HasRead = true;
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }

        return Current;
    }

    /// <summary>What a request to a provider gave: the figures, or a failure with the wait the provider asked for.</summary>
    private sealed record Answer(SubscriptionStatus? Value, bool Failed, TimeSpan? RetryAfter)
    {
        /// <summary>No request was possible (no sign-in): nothing failed, so the regular pace stands.</summary>
        public static Answer Nothing { get; } = new(null, false, null);

        public void Settle(PollBackoff poll, DateTimeOffset now)
        {
            if (Failed)
            {
                poll.Failed(now, RetryAfter);
            }
            else
            {
                poll.Succeeded(now);
            }
        }
    }

    private static async Task<Answer> AskAsync(UsageRequest? request, Func<string, SubscriptionStatus?> parse)
    {
        if (request is null)
        {
            return Answer.Nothing;
        }

        try
        {
            using var response = await SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var asked = response.Headers.TryGetValues("Retry-After", out var values) ? PollBackoff.ParseRetryAfter(values.FirstOrDefault(), DateTimeOffset.UtcNow) : null;
                return new Answer(null, true, asked);
            }

            // An answer that cannot be read (the provider changed its format) is a failure too: asking faster would not help.
            return parse(await response.Content.ReadAsStringAsync()) is { } status ? new Answer(status, false, null) : new Answer(null, true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new Answer(null, true, null);
        }
    }

    /// <summary>The body of a successful answer, or null; a failure here only means the plan name stays unknown for now.</summary>
    private static async Task<string?> AskTextAsync(UsageRequest request)
    {
        try
        {
            using var response = await SendAsync(request);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(UsageRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Address);
        foreach (var (name, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        return await Http.SendAsync(message);
    }
}
