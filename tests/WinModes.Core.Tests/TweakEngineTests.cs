using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

public sealed class TweakEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-tweaks-{Guid.NewGuid():N}");
    private readonly FakeRegistry _registry = new();
    private readonly FakeTasks _tasks = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private TweakEngine Engine(bool machineScope) =>
        new(_registry, _tasks, new TweakJournal(Path.Combine(_directory, machineScope ? "machine.json" : "user.json")), machineScope);

    private static Tweak Sample(params string[] tasks) => new()
    {
        Id = "sample",
        Title = "Sample",
        Values =
        [
            new TweakValue(TweakHive.User, @"Software\Test", "UserValue", TweakValueKind.Number, "0"),
            new TweakValue(TweakHive.Machine, @"SOFTWARE\Policies\Test", "MachineValue", TweakValueKind.Number, "1"),
        ],
        Tasks = tasks,
    };

    [Fact]
    public void Apply_ReportsAValueThatWindowsRefusedAndRecordsNothingForIt()
    {
        _registry.Refused.Add("UserValue");
        var tweak = Sample();

        var result = Engine(false).Apply(tweak);

        // Nothing was changed, so there is nothing to undo and the page can say why instead of showing a false success.
        Assert.Equal(TuneOutcome.Failed, result.Outcome);
        Assert.Contains("UserValue", result.Detail, StringComparison.Ordinal);
        Assert.Empty(Engine(false).JournaledIds());
        Assert.False(_registry.Read(TweakHive.User, @"Software\Test", "UserValue").Exists);
    }

    [Fact]
    public void Apply_KeepsWhatWorkedWhenOnlyOneValueOfTheTweakWasRefused()
    {
        _registry.Refused.Add("MachineValue");
        var tweak = new Tweak
        {
            Id = "mixed",
            Title = "Mixed",
            Values =
            [
                new TweakValue(TweakHive.Machine, @"SOFTWARE\Policies\Test", "OkValue", TweakValueKind.Number, "1"),
                new TweakValue(TweakHive.Machine, @"SOFTWARE\Policies\Test", "MachineValue", TweakValueKind.Number, "1"),
            ],
        };

        var result = Engine(true).Apply(tweak);

        Assert.Equal(TuneOutcome.Done, result.Outcome);
        Assert.Contains("MachineValue", result.Detail, StringComparison.Ordinal);
        Assert.Equal(TuneOutcome.Done, Engine(true).Undo("mixed").Outcome);
        Assert.False(_registry.Read(TweakHive.Machine, @"SOFTWARE\Policies\Test", "OkValue").Exists);
    }

    [Fact]
    public void ApplyAndUndo_RestoreTheExactPreviousState()
    {
        const string Task = @"\Microsoft\Windows\Feedback\Siuf\DmClient";
        _registry.Write(TweakHive.User, @"Software\Test", "UserValue", TweakValueKind.Number, "7");
        _tasks.Enabled[Task] = true;
        var tweak = Sample(Task, @"\Microsoft\Windows\Feedback\Siuf\Missing");

        Assert.Equal(TweakState.NotApplied, Engine(false).GetState(tweak));
        Assert.Equal(TuneOutcome.Done, Engine(false).Apply(tweak).Outcome);
        Assert.Equal(TweakState.Partial, Engine(false).GetState(tweak));
        Assert.Equal(TuneOutcome.Done, Engine(true).Apply(tweak).Outcome);

        Assert.Equal(TweakState.Applied, Engine(false).GetState(tweak));
        Assert.False(_tasks.Enabled[Task]);
        // Applying again changes nothing and keeps the first "before".
        Assert.Equal(TuneOutcome.Skipped, Engine(false).Apply(tweak).Outcome);

        Assert.Equal(TuneOutcome.Done, Engine(false).Undo("sample").Outcome);
        Assert.Equal(TuneOutcome.Done, Engine(true).Undo("sample").Outcome);

        Assert.Equal(new RegistrySnapshot(true, TweakValueKind.Number, "7"), _registry.Read(TweakHive.User, @"Software\Test", "UserValue"));
        // The machine value did not exist before: it is removed, not set to a default.
        Assert.False(_registry.Read(TweakHive.Machine, @"SOFTWARE\Policies\Test", "MachineValue").Exists);
        Assert.True(_tasks.Enabled[Task]);
        Assert.Empty(Engine(false).JournaledIds());
        Assert.Empty(Engine(true).JournaledIds());
    }

    [Fact]
    public void EachScope_WritesOnlyItsOwnHive()
    {
        Engine(false).Apply(Sample());

        Assert.True(_registry.Read(TweakHive.User, @"Software\Test", "UserValue").Exists);
        Assert.False(_registry.Read(TweakHive.Machine, @"SOFTWARE\Policies\Test", "MachineValue").Exists);
    }

    [Fact]
    public void Undo_LeavesAValueThatSomethingElseChangedSince()
    {
        Engine(false).Apply(Sample());
        _registry.Write(TweakHive.User, @"Software\Test", "UserValue", TweakValueKind.Number, "5");

        var result = Engine(false).Undo("sample");

        Assert.Equal(TuneOutcome.Done, result.Outcome);
        Assert.NotNull(result.Detail);
        Assert.Equal("5", _registry.Read(TweakHive.User, @"Software\Test", "UserValue").Value);
    }

    [Fact]
    public void ValueOfAnotherType_IsNotOverwritten()
    {
        _registry.Values[(TweakHive.User, @"Software\Test", "UserValue")] = new RegistrySnapshot(true, null, null);

        Assert.Equal(TuneOutcome.Skipped, Engine(false).Apply(Sample()).Outcome);
        Assert.Empty(Engine(false).JournaledIds());
    }

    [Theory]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\WinDefend", "Start")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA")]
    public void Guard_RefusesSecurityUpdateAndServiceKeys(string path, string name)
    {
        var tweak = new Tweak { Id = "bad", Title = "Bad", Values = [new TweakValue(TweakHive.Machine, path, name, TweakValueKind.Number, "0")] };

        Assert.NotEmpty(TweakGuard.Validate(tweak));
        Assert.Equal(TuneOutcome.Skipped, Engine(true).Apply(tweak).Outcome);
        Assert.False(_registry.Read(TweakHive.Machine, path, name).Exists);
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\WindowsUpdate\Scheduled Start")]
    [InlineData(@"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan")]
    [InlineData(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan")]
    [InlineData(@"\SomeVendor\Updater")]
    public void Guard_RefusesProtectedTasks(string task)
    {
        Assert.NotEmpty(TweakGuard.Validate(new Tweak { Id = "bad", Title = "Bad", Tasks = [task] }));
    }

    [Fact]
    public void ShippedCatalog_LoadsEveryTweakAndPassesTheGuard()
    {
        var root = RepositoryLocator.Find(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var path = Path.Combine(root, "data", "tweaks.json");
        var raw = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetArrayLength();

        var catalog = TweakCatalog.Load(path);

        // Nothing was dropped by the guard, and no tweak is recommended without evidence.
        Assert.Equal(raw, catalog.Tweaks.Count);
        Assert.All(catalog.Tweaks.Where(tweak => tweak.Recommended), tweak =>
        {
            Assert.NotEmpty(tweak.Tools);
            Assert.Equal("low", tweak.Risk);
        });
    }

    private sealed class FakeRegistry : IRegistryAccess
    {
        public Dictionary<(TweakHive, string, string), RegistrySnapshot> Values { get; } = [];

        /// <summary>Value names that Windows silently refuses to change, as a protection driver does.</summary>
        public HashSet<string> Refused { get; } = new(StringComparer.OrdinalIgnoreCase);

        public RegistrySnapshot Read(TweakHive hive, string path, string name) => Values.GetValueOrDefault((hive, path, name), RegistrySnapshot.Missing);

        public void Write(TweakHive hive, string path, string name, TweakValueKind kind, string value)
        {
            if (!Refused.Contains(name))
            {
                Values[(hive, path, name)] = new RegistrySnapshot(true, kind, value);
            }
        }

        public void Delete(TweakHive hive, string path, string name) => Values.Remove((hive, path, name));
    }

    private sealed class FakeTasks : ITaskControl
    {
        public Dictionary<string, bool> Enabled { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool? IsEnabled(string taskPath) => Enabled.TryGetValue(taskPath, out var enabled) ? enabled : null;

        public void SetEnabled(string taskPath, bool enabled) => Enabled[taskPath] = enabled;
    }
}
