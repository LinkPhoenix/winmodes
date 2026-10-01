using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WinModes.Core.Updates;

namespace WinModes.App.Services;

/// <summary>
/// Downloads the installer of a release and starts it, after the user asked for it.
/// Only files published on this project's GitHub releases are accepted, and the installer must match
/// the SHA-256 listed in the release. That protects against a damaged or truncated download; it is not
/// a signature, so Windows may still show its usual warning for an unsigned installer.
/// </summary>
internal static class UpdateInstaller
{
    private const string ReleaseApi = "https://api.github.com/repos/LinkPhoenix/winmodes/releases/tags/";
    private const string DownloadPrefix = "https://github.com/LinkPhoenix/winmodes/releases/download/";
    private const string InstallerSuffix = "-setup-win-x64.exe";
    private const string PortableSuffix = "-portable-win-x64.zip";
    private const string ChecksumFile = "SHA256SUMS.txt";
    private const string UninstallerFile = "unins000.exe";
    private const int BufferSize = 81920;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>True when the app was set up by the installer; a portable copy is updated by hand.</summary>
    public static bool IsInstalledBuild => File.Exists(Path.Combine(AppContext.BaseDirectory, UninstallerFile));

    /// <summary>Downloads and checks the installer. Returns its path, or throws <see cref="UpdateException"/>.</summary>
    public static Task<string> DownloadAsync(string tag, IProgress<double> progress, CancellationToken cancellation) =>
        DownloadAsync(tag, InstallerSuffix, Path.Combine(Path.GetTempPath(), "WinModes-update"), progress, cancellation);

    /// <summary>
    /// Downloads and checks the portable zip of a release into the user's Downloads folder, for a copy that was
    /// not set up by the installer. Returns its path, or throws <see cref="UpdateException"/>.
    /// </summary>
    public static Task<string> DownloadPortableAsync(string tag, IProgress<double> progress, CancellationToken cancellation) =>
        DownloadAsync(tag, PortableSuffix, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), progress, cancellation);

    /// <summary>Opens the folder of a downloaded file with the file selected.</summary>
    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add($"/select,{path}");
        using var process = Process.Start(start);
    }

    private static async Task<string> DownloadAsync(string tag, string suffix, string folder, IProgress<double> progress, CancellationToken cancellation)
    {
        if (!ReleaseVersion.TryParse(tag, out _))
        {
            throw new UpdateException(Loc.T("The release name is not a version."));
        }

        using var client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WinModes/{AppInfo.Version}");

        try
        {
            var (installerName, installerUrl, checksumUrl) = await FindAssetsAsync(client, tag, suffix, cancellation);
            var expected = FindChecksum(await client.GetStringAsync(new Uri(checksumUrl), cancellation), installerName);

            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, installerName);

            var actual = await DownloadFileAsync(client, installerUrl, path, progress, cancellation);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new UpdateException(Loc.T("The downloaded file does not match the checksum of the release. It was deleted."));
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException
            || (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            throw new UpdateException(Loc.T("The update could not be downloaded. Check the connection and try again."), ex);
        }
    }

    /// <summary>Starts the installer; it closes the running app itself.</summary>
    public static void Run(string installerPath)
    {
        using var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
    }

    private static async Task<(string Name, string Url, string ChecksumUrl)> FindAssetsAsync(HttpClient client, string tag, string suffix, CancellationToken cancellation)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri(ReleaseApi + tag), cancellation));
        string? name = null;
        string? url = null;
        string? checksumUrl = null;
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString() ?? "";
            var assetUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            // Never follow an address outside this project's release downloads.
            if (!assetUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && assetName == Path.GetFileName(assetName))
            {
                (name, url) = (assetName, assetUrl);
            }
            else if (assetName == ChecksumFile)
            {
                checksumUrl = assetUrl;
            }
        }

        return name is null || url is null || checksumUrl is null
            ? throw new UpdateException(Loc.T("This release has no such file with a checksum. Open the release page instead."))
            : (name, url, checksumUrl);
    }

    /// <summary>Reads "hash  file name" lines, the format of sha256sum.</summary>
    private static string FindChecksum(string checksums, string fileName)
    {
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName && parts[0].Length == SHA256.HashSizeInBytes * 2)
            {
                return parts[0];
            }
        }

        throw new UpdateException(Loc.T("The release does not list a checksum for this file. Open the release page instead."));
    }

    private static async Task<string> DownloadFileAsync(HttpClient client, string url, string path, IProgress<double> progress, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                hash.AppendData(buffer, 0, read);
                done += read;
                if (total > 0)
                {
                    progress.Report(done * 100d / total);
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>An update that could not be downloaded or checked; the message is shown to the user.</summary>
internal sealed class UpdateException : Exception
{
    public UpdateException()
    {
    }

    public UpdateException(string message)
        : base(message)
    {
    }

    public UpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
