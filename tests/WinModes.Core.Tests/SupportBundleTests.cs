using System.IO.Compression;
using System.Text;
using WinModes.Core.Reports;

namespace WinModes.Core.Tests;

public sealed class SupportBundleTests
{
    [Fact]
    public void UserAndPcNames_AreTakenOutWhereverTheyAppear()
    {
        const string text = @"at C:\Users\Jane.Doe\AppData\Local\WinModes\x.cs on JANE-LAPTOP, user jane.doe, cwd D:\work\jane.doe-notes";

        var redacted = SupportBundle.Redact(text, "Jane.Doe", "Jane-Laptop");

        Assert.DoesNotContain("Jane", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\<user>\AppData", redacted, StringComparison.Ordinal);
        Assert.Contains("on <pc>,", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePcNameContainingTheUserName_IsReplacedAsAWhole()
    {
        Assert.Equal("host <pc> user <user>", SupportBundle.Redact("host ALICE-PC user alice", "alice", "ALICE-PC"));
    }

    [Theory]
    [InlineData("al")]
    [InlineData("")]
    [InlineData(null)]
    public void AVeryShortOrMissingName_IsLeftAlone(string? name) =>
        Assert.Equal("a calm alarm", SupportBundle.Redact("a calm alarm", name, name));

    [Fact]
    public void EmailsAndSecrets_AreTakenOut()
    {
        const string token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEfGh";
        var text = $"mail jane@example.com, Authorization: Bearer abc.def-123, jwt eyJhbGciOi.eyJzdWIiOiJ4.sig_9, key {token}, guid 0f8fad5b-d9cb-469f-a165-70867728950e";

        var redacted = SupportBundle.Redact(text, null, null);

        Assert.DoesNotContain("example.com", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def-123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(token, redacted, StringComparison.Ordinal);
        // A GUID is not a secret and helps to follow an id from the log to the settings.
        Assert.Contains("0f8fad5b-d9cb-469f-a165-70867728950e", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryText_IsNotChanged()
    {
        const string text = "2026-10-02T09:00:00Z [Token statistics] System.IO.IOException: The process cannot access the file 'token-index.json'.";

        Assert.Equal(text, SupportBundle.Redact(text, "jane", "jane-pc"));
    }

    [Fact]
    public void Zip_HoldsEachFileRedacted_UnderAPlainName()
    {
        using var stream = new MemoryStream();

        SupportBundle.Write(stream, [new SupportFile("info.txt", "user jane on jane-pc"), new SupportFile(@"..\..\evil\errors.log", "mail a@b.co")], "jane", "jane-pc");

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(["errors.log", "info.txt"], archive.Entries.Select(entry => entry.FullName).Order());
        using var reader = new StreamReader(archive.GetEntry("info.txt")!.Open(), Encoding.UTF8);
        Assert.Equal("user <user> on <pc>", reader.ReadToEnd());
        using var other = new StreamReader(archive.GetEntry("errors.log")!.Open(), Encoding.UTF8);
        Assert.Equal("mail <email>", other.ReadToEnd());
    }
}
