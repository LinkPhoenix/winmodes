using WinModes.Core.Planning;
using WinModes.Core.Software;

namespace WinModes.Core.Tests;

public sealed class SoftwareInstallerTests
{
    private static readonly SoftwareEntry Firefox = SoftwareCatalog.Entries.Single(entry => entry.Id == "firefox");
    private static readonly SoftwareEntry Brave = SoftwareCatalog.Entries.Single(entry => entry.Id == "brave");

    [Fact]
    public void InventoryMatchesAnExactIdAndSourceAndKeepsVersion()
    {
        var inventory = SoftwareInventory.Parse("""
            {"Sources":[{"SourceDetails":{"Name":"winget"},"Packages":[{"PackageIdentifier":"Mozilla.Firefox","Version":"144.0"},{"PackageIdentifier":"Mozilla.Firefox.ESR"}]},
            {"SourceDetails":{"Name":"untrusted"},"Packages":[{"PackageIdentifier":"Brave.Brave"}]}]}
            """);
        Assert.Equal("144.0", inventory.Find(Firefox)?.Version);
        Assert.Null(inventory.Find(Brave));
    }

    [Fact]
    public async Task MissingInventoryPreventsAnyInstallationOrIntent()
    {
        var probe = new FakeProbe { Inventory = SoftwareInventory.Unavailable("offline") };
        var runner = new FakeRunner(probe);
        var journal = new FakeJournal();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SoftwareInstaller(probe, runner, journal).InstallAsync([Firefox.Id]));
        Assert.Empty(runner.Calls);
        Assert.Empty(journal.Phases);
    }

    [Fact]
    public async Task ExistingPackagesAreNeverReinstalled()
    {
        var probe = new FakeProbe();
        probe.Add(Firefox);
        var runner = new FakeRunner(probe);
        var journal = new FakeJournal();
        var result = await new SoftwareInstaller(probe, runner, journal).InstallAsync([Firefox.Id]);
        Assert.Equal(SoftwareInstallOutcome.AlreadyInstalled, Assert.Single(result).Outcome);
        Assert.Empty(runner.Calls);
        Assert.Empty(journal.Phases);
    }

    [Fact]
    public async Task IntentIsDurableBeforeLaunchAndSuccessRequiresObservation()
    {
        var probe = new FakeProbe();
        var journal = new FakeJournal();
        var runner = new FakeRunner(probe) { BeforeRun = () => Assert.Equal("intent", Assert.Single(journal.Phases)) };
        var results = await new SoftwareInstaller(probe, runner, journal).InstallAsync([Firefox.Id, Firefox.Id]);
        Assert.Equal(SoftwareInstallOutcome.Installed, Assert.Single(results).Outcome);
        Assert.Equal<string>(["intent", "outcome"], journal.Phases);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task ZeroExitCodeWithoutInstalledPackageIsUnverifiedAndStopsQueue()
    {
        var probe = new FakeProbe();
        var runner = new FakeRunner(probe) { AddInstalled = false };
        var result = await new SoftwareInstaller(probe, runner, new FakeJournal()).InstallAsync([Firefox.Id, Brave.Id]);
        Assert.Equal(SoftwareInstallOutcome.Unverified, Assert.Single(result).Outcome);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task CancellationFinishesCurrentAppAndStopsBeforeNext()
    {
        var probe = new FakeProbe();
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner(probe) { BeforeRun = cancel.Cancel };
        var result = await new SoftwareInstaller(probe, runner, new FakeJournal()).InstallAsync([Firefox.Id, Brave.Id], cancellationToken: cancel.Token);
        Assert.Equal(SoftwareInstallOutcome.Installed, Assert.Single(result).Outcome);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task FailedJournalPreventsLaunch()
    {
        var probe = new FakeProbe();
        var runner = new FakeRunner(probe);
        await Assert.ThrowsAsync<IOException>(() => new SoftwareInstaller(probe, runner, new FakeJournal { Fail = true }).InstallAsync([Firefox.Id]));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task UnknownOrUnsupportedIdsFailBeforeAnyProbe()
    {
        var probe = new FakeProbe();
        var installer = new SoftwareInstaller(probe, new FakeRunner(probe), new FakeJournal());
        await Assert.ThrowsAsync<ArgumentException>(() => installer.InstallAsync(["--arbitrary-command"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(["rustdesk"]));
        Assert.Equal(0, probe.Reads);
    }

    [Theory]
    [InlineData(3010, SoftwareInstallOutcome.NeedsRestart)]
    [InlineData(-1978334967, SoftwareInstallOutcome.NeedsRestart)]
    [InlineData(-1978335189, SoftwareInstallOutcome.AlreadyInstalled)]
    [InlineData(-1978334966, SoftwareInstallOutcome.Failed)]
    [InlineData(1, SoftwareInstallOutcome.Failed)]
    public void ExitCodesDoNotTreatAnIncompleteInstallAsSuccess(int exit, SoftwareInstallOutcome expected) => Assert.Equal(expected, SoftwareInstaller.ClassifyExitCode(exit));

    private sealed class FakeProbe : ISystemProbe
    {
        public SoftwareInventory Inventory { get; set; } = new(true, [], DateTimeOffset.UtcNow);
        public int Reads { get; private set; }
        public SoftwareInventory GetSoftwareInventory() { Reads++; return Inventory; }
        public void Add(SoftwareEntry entry) => Inventory = Inventory with { Packages = [.. Inventory.Packages, new(entry.WingetId!, entry.Source, "1.0")] };
        public ServiceState? GetService(string name) => null;
        public bool IsProcessRunning(string processFileName) => false;
        public bool IsWslRunning() => false;
    }

    private sealed class FakeRunner(FakeProbe probe) : ISoftwarePackageRunner
    {
        public bool AddInstalled { get; init; } = true;
        public Action? BeforeRun { get; init; }
        public List<string> Calls { get; } = [];
        public Task<int?> InstallAsync(SoftwareEntry entry)
        {
            BeforeRun?.Invoke(); Calls.Add(entry.Id);
            if (AddInstalled) probe.Add(entry);
            return Task.FromResult<int?>(0);
        }
    }

    private sealed class FakeJournal : ISoftwareInstallJournal
    {
        public List<string> Phases { get; } = [];
        public bool Fail { get; init; }
        public void Record(string phase, SoftwareEntry entry, InstalledSoftware? before, SoftwareInstallResult? result)
        {
            if (Fail) throw new IOException("journal unavailable");
            Phases.Add(phase);
        }
    }
}
