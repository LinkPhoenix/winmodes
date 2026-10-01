using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using WinModes.Core.Updates;

namespace WinModes.App.Services;

/// <summary>Result of one check. <see cref="Error"/> is set when GitHub could not be reached.</summary>
internal sealed record UpdateStatus(bool IsNewer, string? LatestTag, string? Error);

/// <summary>
/// Asks GitHub for the latest published release and compares it with this build.
/// It only reads one public address and never downloads or runs anything: the user opens the release page.
/// </summary>
internal static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/LinkPhoenix/winmodes/releases/latest";
    private const string LatestReleaseApi = "https://api.github.com/repos/LinkPhoenix/winmodes/releases/latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Client = CreateClient();

    /// <summary>Last result of this run, so the About page can show it without asking again.</summary>
    public static UpdateStatus? Last { get; private set; }

    public static async Task<UpdateStatus> CheckAsync()
    {
        try
        {
            using var response = await Client.GetAsync(new Uri(LatestReleaseApi));
            if (!response.IsSuccessStatusCode)
            {
                return Last = new UpdateStatus(false, null, Loc.F("GitHub answered {0}.", (int)response.StatusCode));
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = document.RootElement.TryGetProperty("tag_name", out var value) ? value.GetString() : null;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Last = new UpdateStatus(ReleaseVersion.IsNewer(tag, current), tag, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Last = new UpdateStatus(false, null, Loc.T("GitHub could not be reached."));
        }
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
