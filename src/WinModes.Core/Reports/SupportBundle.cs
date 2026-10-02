using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace WinModes.Core.Reports;

/// <summary>A text file to put in the support bundle.</summary>
public sealed record SupportFile(string Name, string Content);

/// <summary>
/// The support file a user can attach to a bug report: a zip of text files (version and system facts, the settings, the error
/// logs) from which what identifies the user is taken out first. Nothing is sent anywhere; the user decides whether to share it.
/// </summary>
public static partial class SupportBundle
{
    private const int MinimumNameLength = 3;
    private const string Removed = "<redacted>";

    /// <summary>
    /// Takes out the Windows user name and the PC name (wherever they appear: in paths, in messages), e-mail addresses, bearer
    /// tokens, web tokens and any long run of letters and digits that can only be a secret. Anything else is left as it is, so the
    /// file stays useful.
    /// </summary>
    public static string Redact(string text, string? userName, string? machineName)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = text;
        // Longest first: the PC name may contain the user name.
        foreach (var name in new[] { machineName, userName }.Where(name => name is { Length: >= MinimumNameLength }).OrderByDescending(name => name!.Length))
        {
            result = Regex.Replace(result, Regex.Escape(name!), name == machineName ? "<pc>" : "<user>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        result = Email().Replace(result, "<email>");
        result = Bearer().Replace(result, "Bearer " + Removed);
        result = WebToken().Replace(result, Removed);
        return Secret().Replace(result, Removed);
    }

    /// <summary>Writes the zip: each file redacted, under a plain name.</summary>
    public static void Write(Stream output, IEnumerable<SupportFile> files, string? userName, string? machineName)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(files);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            // A name is a plain file name: the content of a file never decides where it lands.
            var entry = archive.CreateEntry(Path.GetFileName(file.Name), CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(Redact(file.Content, userName, machineName));
        }
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/\-]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*")]
    private static partial Regex WebToken();

    // Forty or more letters, digits and token symbols in a row: a key or a token, never a word, a path part or a GUID.
    [GeneratedRegex(@"[A-Za-z0-9+/_\-]{40,}={0,2}")]
    private static partial Regex Secret();
}
