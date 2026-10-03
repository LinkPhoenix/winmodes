using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using WinModes.Core.Updates;

namespace WinModes.App.Services;

/// <summary>Result of one check. <see cref="Error"/> is set when GitHub could not be reached.</summary>
internal sealed record UpdateStatus(bool IsNewer, string? LatestTag, string? Error);

/// <summary>
/// Asks GitHub for the latest published release and compares it with this build. On the beta channel (the choice of the
/// About page, or a beta build that has not chosen) it also looks at the beta releases; on the stable channel it never hears about them.
/// It only reads one public address and never downloads or runs anything: the user opens the release page.
/// </summary>
internal static class UpdateChecker
{
    public const string LatestReleasePage = "https://github.com/LinkPhoenix/winmodes/releases/latest";
    public const string AllReleasesPage = "https://github.com/LinkPhoenix/winmodes/releases";
    private const string LatestReleaseApi = "https://api.github.com/repos/LinkPhoenix/winmodes/releases/latest";
    private const string RecentReleasesApi = "https://api.github.com/repos/LinkPhoenix/winmodes/releases?per_page=15";

    /// <summary>Where the "Release page" button goes: the releases list on the beta channel, which has to show the betas.</summary>
    public static string ReleasesPage => AppSettings.Load().FollowsBetas ? AllReleasesPage : LatestReleasePage;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Client = CreateClient();

    /// <summary>Last result of this run, so the About page can show it without asking again.</summary>
    public static UpdateStatus? Last { get; private set; }

    public static async Task<UpdateStatus> CheckAsync()
    {
        var betas = AppSettings.Load().FollowsBetas;
        try
        {
            using var response = await Client.GetAsync(new Uri(betas ? RecentReleasesApi : LatestReleaseApi));
            if (!response.IsSuccessStatusCode)
            {
                return Last = new UpdateStatus(false, null, Loc.F("GitHub answered {0}.", (int)response.StatusCode));
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var tag = betas ? NewestFor(document.RootElement, current) : TagOf(document.RootElement);
            return Last = new UpdateStatus(ReleaseVersion.IsNewer(tag, current, AppInfo.BetaNumber, betas), tag, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Last = new UpdateStatus(false, null, Loc.T("GitHub could not be reached."));
        }
    }

    private static string? TagOf(JsonElement release) =>
        release.ValueKind == JsonValueKind.Object && release.TryGetProperty("tag_name", out var value) ? value.GetString() : null;

    /// <summary>The newest release this build can move to on the beta channel: the list comes newest first, drafts are not listed by the API.</summary>
    private static string? NewestFor(JsonElement releases, Version current)
    {
        if (releases.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return releases.EnumerateArray().Select(TagOf).FirstOrDefault(tag => ReleaseVersion.IsNewer(tag, current, AppInfo.BetaNumber, includeBetas: true));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout };
        // GitHub rejects requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WinModes/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
