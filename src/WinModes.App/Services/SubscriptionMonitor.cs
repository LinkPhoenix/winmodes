using System.Net.Http;
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

    /// <summary>Last statuses read; empty until the first read ends or when no plan is recorded on this PC.</summary>
    public static IReadOnlyList<SubscriptionStatus> Current { get; private set; } = [];

    /// <summary>False until the first read has ended, so "nothing found" is not shown too early.</summary>
    public static bool HasRead { get; private set; }

    /// <summary>Returns what is known now and starts a background read when it is getting old.</summary>
    public static IReadOnlyList<SubscriptionStatus> Get(bool online)
    {
        if (DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    var claude = Subscriptions.ReadClaude(Subscriptions.DefaultClaudeSettings, ClaudeStatusLine.DefaultRecordPath);
                    var codex = Subscriptions.ReadCodex(Subscriptions.DefaultCodexHome);
                    if (!online)
                    {
                        (_onlineClaude, _onlineCodex) = (null, null);
                    }
                    else if (DateTime.UtcNow - _lastOnlineUtc >= OnlineInterval)
                    {
                        _lastOnlineUtc = DateTime.UtcNow;
                        var now = DateTimeOffset.UtcNow;
                        // A failed request keeps the previous online figures; they are dated in the widget.
                        _onlineCodex = await AskAsync(OnlineUsage.CodexRequest(OnlineUsage.DefaultCodexAuth), json => OnlineUsage.ParseCodex(json, now)) ?? _onlineCodex;
                        _onlineClaude = await AskAsync(OnlineUsage.ClaudeRequest(OnlineUsage.DefaultClaudeCredentials, now),
                            json => OnlineUsage.ParseClaude(json, claude?.Plan ?? "Plan unknown", now)) ?? _onlineClaude;
                    }

                    Current = [.. new[] { Newest(_onlineClaude, claude), Newest(_onlineCodex, codex) }.OfType<SubscriptionStatus>()];
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
        : local?.Primary is not null && local.SeenAt > online.SeenAt ? local with { ResetCredits = online.ResetCredits }
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
