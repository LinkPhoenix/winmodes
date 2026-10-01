using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Tests;

public sealed class ModeEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-engine-{Guid.NewGuid():N}");
    private readonly FakeServices _services = new();
    private readonly JournalStore _journal;
    private readonly ModeEngine _engine;

    public ModeEngineTests()
    {
        Directory.CreateDirectory(_directory);
        var policyPath = Path.Combine(_directory, "protected.json");
        File.WriteAllText(policyPath, """{ "requiredCapabilities": { "security": { "services": ["WinDefend"] } } }""");
        var policy = ProtectionPolicy.Load(policyPath);

        _journal = new JournalStore(Path.Combine(_directory, "journal"));
        _engine = new ModeEngine(_services, new ModePlanner(_services, policy), policy, _journal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ModeProfile Stopping(params string[] ids) => new()
    {
        Mode = "test",
        Services = new ServiceSettings { Stop = [.. ids.Select(id => new ServiceStop { Id = id })] },
    };

    [Fact]
    public void Apply_StopsServiceAndRevert_RestoresStartModeAndRunningState()
    {
        _services.Add("Scanner", ServiceStartMode.Automatic, running: true, delayed: true);

        var session = _engine.Apply(Stopping("Scanner"));

        Assert.Equal(new ServiceState(ServiceStartMode.Manual, IsRunning: false), _services.GetState("Scanner"));
        Assert.Equal(EntryOutcome.Done, Assert.Single(session.Entries).Outcome);

        _engine.Revert(session);

        Assert.Equal(new ServiceState(ServiceStartMode.Automatic, IsRunning: true), _services.GetState("Scanner"));
        Assert.True(_services.IsDelayedAutoStart("Scanner"));
        Assert.True(session.Reverted);
    }

    [Fact]
    public void Apply_SkipsServiceStillNeededByARunningDependent()
    {
        _services.Add("Base", ServiceStartMode.Automatic, running: true);
        _services.Dependents["Base"] = ["Consumer"];

        var session = _engine.Apply(Stopping("Base"));

        Assert.Equal(EntryOutcome.Skipped, Assert.Single(session.Entries).Outcome);
        Assert.True(_services.GetState("Base")!.IsRunning);
    }

    [Fact]
    public void Revert_LeavesServiceAloneWhenStartModeChangedSinceTheSwitch()
    {
        _services.Add("Scanner", ServiceStartMode.Automatic, running: true);
        var session = _engine.Apply(Stopping("Scanner"));
        _services.SetStartMode("Scanner", ServiceStartMode.Disabled, delayedAutoStart: false);

        _engine.Revert(session);

        Assert.Equal(ServiceStartMode.Disabled, _services.GetState("Scanner")!.StartMode);
        Assert.Equal(EntryOutcome.RevertSkipped, session.Entries[0].Outcome);
    }

    [Fact]
    public void Apply_RefusesProfileThatTargetsAProtectedService()
    {
        _services.Add("WinDefend", ServiceStartMode.Automatic, running: true);

        Assert.Throws<ProfileException>(() => _engine.Apply(Stopping("WinDefend")));
        Assert.True(_services.GetState("WinDefend")!.IsRunning);
    }

    [Fact]
    public void Journal_FindsTheLastSessionThatIsNotReverted()
    {
        _services.Add("Scanner", ServiceStartMode.Automatic, running: true);
        var session = _engine.Apply(Stopping("Scanner"));

        Assert.Equal(session.Id, _journal.FindActive()?.Id);

        _engine.Revert(session);

        Assert.Null(_journal.FindActive());
    }

    [Fact]
    public void Revert_RestoresAStopThatWasInterruptedBeforeItWasMarkedDone()
    {
        _services.Add("Scanner", ServiceStartMode.Automatic, running: true);
        var session = new JournalSession { Id = "crashed", Mode = "test", StartedUtc = DateTimeOffset.UtcNow };
        session.Entries.Add(new JournalEntry
        {
            Target = "Scanner",
            Kind = EntryKind.StopService,
            BeforeStartMode = ServiceStartMode.Automatic,
            BeforeRunning = true,
            Outcome = EntryOutcome.Pending,
        });
        _journal.Save(session);
        // The helper died after the start type changed but before the service was stopped or the entry saved.
        _services.SetStartMode("Scanner", ServiceStartMode.Manual, delayedAutoStart: false);

        Assert.Equal("crashed", _journal.FindActive()?.Id);

        _engine.Revert(session);

        Assert.Equal(ServiceStartMode.Automatic, _services.GetState("Scanner")!.StartMode);
        Assert.True(session.Reverted);
        Assert.Null(_journal.FindActive());
    }

    [Fact]
    public void Revert_KeepsTheSessionActiveWhenAnEntryCannotBeRestored()
    {
        _services.Add("Scanner", ServiceStartMode.Automatic, running: true);
        var session = _engine.Apply(Stopping("Scanner"));
        _services.FailSetStartMode = true;

        _engine.Revert(session);

        Assert.False(session.Reverted);
        Assert.Equal(session.Id, _journal.FindActive()?.Id);

        _services.FailSetStartMode = false;
        _engine.Revert(session);

        Assert.True(session.Reverted);
        Assert.Equal(ServiceStartMode.Automatic, _services.GetState("Scanner")!.StartMode);
    }

    private sealed class FakeServices : IServiceControl, ISystemProbe
    {
        public bool FailSetStartMode { get; set; }

        private readonly Dictionary<string, (ServiceStartMode Mode, bool Running, bool Delayed)> _state = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> Dependents { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, ServiceStartMode mode, bool running, bool delayed = false) => _state[name] = (mode, running, delayed);

        public ServiceState? GetState(string name) =>
            _state.TryGetValue(name, out var state) ? new ServiceState(state.Mode, state.Running) : null;

        public ServiceState? GetService(string name) => GetState(name);
        public bool IsProcessRunning(string processFileName) => false;
        public bool IsWslRunning() => false;
        public bool IsDelayedAutoStart(string name) => _state[name].Delayed;
        public IReadOnlyList<string> GetRunningDependents(string name) => Dependents.GetValueOrDefault(name, []);

        public void SetStartMode(string name, ServiceStartMode mode, bool delayedAutoStart)
        {
            if (FailSetStartMode)
            {
                throw new InvalidOperationException("Simulated failure.");
            }

            _state[name] = (mode, _state[name].Running, delayedAutoStart);
        }

        public void StopService(string name) => _state[name] = (_state[name].Mode, false, _state[name].Delayed);
        public void StartService(string name) => _state[name] = (_state[name].Mode, true, _state[name].Delayed);
    }
}
