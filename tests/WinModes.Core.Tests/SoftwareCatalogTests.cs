using System.Text.RegularExpressions;
using WinModes.Core.Software;

namespace WinModes.Core.Tests;

public sealed partial class SoftwareCatalogTests
{
    [Fact]
    public void InstallationRoutesAreFixedHttpsPagesWithoutCredentials()
    {
        Assert.Equal(SoftwareCatalog.Entries.Count, SoftwareCatalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var entry in SoftwareCatalog.Entries)
        {
            var uri = new Uri(entry.Website);
            Assert.Equal("https", uri.Scheme);
            Assert.Empty(uri.UserInfo);
            if (entry.WingetId is { } packageId)
            {
                if (entry.Source == "msstore") Assert.Matches("^[A-Z0-9]{12}$", packageId);
                else Assert.Matches(PackageIdPattern(), packageId);
                Assert.Contains($"--exact --source {entry.Source} --interactive", entry.InstallCommand!, StringComparison.Ordinal);
                Assert.Contains("--no-upgrade", entry.InstallCommand!, StringComparison.Ordinal);
                Assert.DoesNotContain("--force", entry.InstallCommand!, StringComparison.Ordinal);
                Assert.DoesNotContain("--accept-", entry.InstallCommand!, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(entry.InstallCommand);
            }
        }
    }

    [Fact]
    public void ChatAppsAndCodingAgentsHaveDistinctInstallationRoutes()
    {
        var entries = SoftwareCatalog.Entries.ToDictionary(entry => entry.Id);
        Assert.NotEqual(entries["codex"].Website, entries["chatgpt"].Website);
        Assert.NotEqual(entries["claude-code"].Website, entries["claude"].Website);
        foreach (var id in new[] { "cursor", "t3", "opencode", "codex", "claude-code", "grok-bot" })
        {
            Assert.Equal(SoftwareAudience.AiCoding, entries[id].Audience);
        }
        Assert.Contains(SoftwareCatalog.Entries, entry => entry.Audience == SoftwareAudience.Everyday && entry.Category == "Browsers");
        Assert.Contains(SoftwareCatalog.Entries, entry => entry.Audience == SoftwareAudience.Everyday && entry.Category == "Notes & documents");
    }

    [GeneratedRegex(@"^[A-Za-z0-9+]+(?:[.-][A-Za-z0-9+]+)+$")]
    private static partial Regex PackageIdPattern();

    [Fact]
    public void ClaudeDesktopAndTerminalAgentHaveSeparateKindsAndIdentities()
    {
        var desktop = SoftwareCatalog.Entries.Single(entry => entry.Id == "claude");
        var cli = SoftwareCatalog.Entries.Single(entry => entry.Id == "claude-code");
        Assert.Equal(SoftwareKind.Desktop, desktop.Kind);
        Assert.Equal(SoftwareKind.Cli, cli.Kind);
        Assert.Equal("Anthropic.Claude", desktop.WingetId);
        Assert.Equal("Anthropic.ClaudeCode", cli.WingetId);
        Assert.NotEqual(desktop.Category, cli.Category);
    }

    [Fact]
    public void MistralEditorBridgeDoesNotStandInForInteractiveCli()
    {
        var cli = SoftwareCatalog.Entries.Single(entry => entry.Id == "mistral-vibe");
        var bridge = SoftwareCatalog.Entries.Single(entry => entry.Id == "mistral-acp");
        Assert.Null(cli.WingetId);
        Assert.Equal(SoftwareKind.Cli, cli.Kind);
        Assert.Equal(SoftwareKind.EditorBridge, bridge.Kind);
        Assert.Equal("MistralAI.MistralVibe.ACP", bridge.WingetId);
        Assert.NotEqual(cli.Category, bridge.Category);
    }

    [Fact]
    public void OfficialDeepSeekGuideDoesNotRouteToSimilarlyNamedCommunityPackages()
    {
        var entry = SoftwareCatalog.Entries.Single(entry => entry.Id == "deepseek-harness");
        Assert.Equal("www.deepseek.com", new Uri(entry.Website).Host);
        Assert.Null(entry.WingetId);
        Assert.Null(entry.InstallCommand);
        Assert.False(WinModes.Core.Protection.ProtectionPolicy.CanInstallSoftware(entry));
    }
}
