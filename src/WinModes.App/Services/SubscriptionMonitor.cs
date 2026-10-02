using System.Net.Http;
using WinModes.Core.Accounts;
using WinModes.Core.Usage;

namespace WinModes.App.Services;

/// <summary>
/// Keeps the last known plan and usage limits of Codex and Claude for the widget. Local files are read in the
/// background, at most once per <see cref="RefreshInterval"/>, and only while something asks for them. When the
/// user turned the online reading on, the providers are also asked, at most once per <see cref="OnlineInterval"/>.
/// </summary>
internal static class SubscriptionMonitor
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OnlineInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // No redirect is followed, so the sign-in header can never be sent to another host.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = RequestTimeout };

    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static DateTime _lastOnlineUtc = DateTime.MinValue;
    private static SubscriptionStatus? _onlineClaude;
    private static SubscriptionStatus? _onlineCodex;
    private static int _refreshing;
    private static (bool ClaudeOnline, bool CodexOnline, bool Claude, bool Codex) _choice = (false, false, true, true);

    /// <summary>Last statuses read; empty until the first read ends or when no plan is recorded on this PC.</summary>
    public static IReadOnlyList<SubscriptionStatus> Current { get; private set; } = [];

    /// <summary>False until the first read has ended, so "nothing found" is not shown too early.</summary>
    public static bool HasRead { get; private set; }

    /// <summary>Reads again at once, for example right after signing in or out.</summary>
    public static void RefreshNow() => (_lastRefreshUtc, _lastOnlineUtc) = (DateTime.MinValue, DateTime.MinValue);

    /// <summary>Returns what is known now and starts a background read when it is getting old.</summary>
    public static IReadOnlyList<SubscriptionStatus> Get(bool claudeOnline, bool codexOnline, bool claudeWanted = true, bool codexWanted = true)
    {
        // A changed choice is applied at once instead of waiting for the next read.
        var choice = (claudeOnline, codexOnline, claudeWanted, codexWanted);
        if (choice != _choice)
        {
            (_choice, _lastRefreshUtc, _lastOnlineUtc) = (choice, DateTime.MinValue, DateTime.MinValue);
        }

        if (DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    var claude = claudeWanted ? Subscriptions.ReadClaude(Subscriptions.DefaultClaudeSettings, ClaudeStatusLine.DefaultRecordPath) : null;
                    var codex = codexWanted ? Subscriptions.ReadCodex(Subscriptions.DefaultCodexHome) : null;
                    // Signing in to WinModes is the consent to ask: such a tool is read online with its own session whatever the
                    // option says. A tool whose online reading is off, or that is not shown, is not asked and keeps no online figure.
                    var claudeSigned = claudeWanted && AccountSession.IsSignedIn(AccountProvider.Claude);
                    var codexSigned = codexWanted && AccountSession.IsSignedIn(AccountProvider.ChatGpt);
                    var askClaude = (claudeOnline && claudeWanted) || claudeSigned;
                    var askCodex = (codexOnline && codexWanted) || codexSigned;
                    if (!askClaude)
                    {
                        _onlineClaude = null;
                    }

                    if (!askCodex)
                    {
                        _onlineCodex = null;
                    }

                    var anyOnline = askClaude || askCodex;
                    if (anyOnline && DateTime.UtcNow - _lastOnlineUtc >= OnlineInterval)
                    {
                        _lastOnlineUtc = DateTime.UtcNow;
                        var now = DateTimeOffset.UtcNow;
                        // A failed request keeps the previous online figures; they are dated in the widget.
                        if (askCodex)
                        {
                            var request = codexSigned
                                ? await AccountSession.TokensAsync(AccountProvider.ChatGpt) is { } own ? OnlineUsage.CodexRequestFor(own.AccessToken, own.AccountId) : null
                                : OnlineUsage.CodexRequest(OnlineUsage.DefaultCodexAuth);
                            _onlineCodex = await AskAsync(request, json => OnlineUsage.ParseCodex(json, now)) ?? _onlineCodex;
                        }

                        if (askClaude)
                        {
                            var request = claudeSigned
                                ? await AccountSession.TokensAsync(AccountProvider.Claude) is { } own ? OnlineUsage.ClaudeRequestFor(own.AccessToken) : null
                                : OnlineUsage.ClaudeRequest(OnlineUsage.DefaultClaudeCredentials, now);
                            _onlineClaude = await AskAsync(request, json => OnlineUsage.ParseClaude(json, claude?.Plan ?? Loc.T("Plan unknown"), now)) ?? _onlineClaude;
                        }
                    }

                    Current = [.. new[] { claudeWanted ? Newest(_onlineClaude, claude) : null, codexWanted ? Newest(_onlineCodex, codex) : null }.OfType<SubscriptionStatus>()];
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

    /// <summary>The online figure unless a local record is more recent (a session wrote one since the last request).</summary>
    private static SubscriptionStatus? Newest(SubscriptionStatus? online, SubscriptionStatus? local) =>
        online is null ? local
        : local?.Primary is not null && local.SeenAt > online.SeenAt ? local with { ResetCredits = online.ResetCredits, ModelLimits = online.ModelLimits, Extra = online.Extra }
        : online;

    private static async Task<SubscriptionStatus?> AskAsync(UsageRequest? request, Func<string, SubscriptionStatus?> parse)
    {
        if (request is null)
        {
            return null;
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Address);
            foreach (var (name, value) in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await Http.SendAsync(message);
            return response.IsSuccessStatusCode ? parse(await response.Content.ReadAsStringAsync()) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
