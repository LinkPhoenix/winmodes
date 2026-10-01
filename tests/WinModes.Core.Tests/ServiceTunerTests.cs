using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Protection;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

public sealed class ServiceTunerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-tuner-{Guid.NewGuid():N}");
    private readonly FakeServices _services = new();
    private readonly ProtectionPolicy _policy;
    private readonly TweakStore _store;

    public ServiceTunerTests()
    {
        Directory.CreateDirectory(_directory);
        var policyPath = Path.Combine(_directory, "protected.json");
        File.WriteAllText(policyPath, """{ "requiredCapabilities": { "security": { "services": ["WinDefend"] } } }""");
        _policy = ProtectionPolicy.Load(policyPath);
        _store = new TweakStore(Path.Combine(_directory, "tweaks"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ServiceTuner Tuner(params string[] changedByMode) =>
        new(_services, _policy, _store, changedByMode.ToHashSet(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void SetStartMode_RecordsTheOriginalOnceAndRestore_PutsItBack()
    {
        _services.Add("Fax", ServiceStartMode.Automatic, running: true, delayed: true);

        Assert.Equal(TuneOutcome.Done, Assert.Single(Tuner().Apply(TuneAction.Manual, ["Fax"])).Outcome);
        Assert.Equal(TuneOutcome.Done, Assert.Single(Tuner().Apply(TuneAction.Disabled, ["Fax"])).Outcome);

        var tweak = Assert.Single(_store.Load());
        Assert.Equal(ServiceStartMode.Automatic, tweak.OriginalStartMode);
        Assert.True(tweak.OriginalDelayedAutoStart);
        Assert.Equal(ServiceStartMode.Disabled, tweak.SetTo);
        // Changing the start type never stops the service.
        Assert.True(_services.GetState("Fax")!.IsRunning);

        Assert.Equal(TuneOutcome.Done, Assert.Single(Tuner().Apply(TuneAction.Restore, ["Fax"])).Outcome);

        Assert.Equal(ServiceStartMode.Automatic, _services.GetState("Fax")!.StartMode);
        Assert.True(_services.IsDelayedAutoStart("Fax"));
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void SettingTheOriginalValueAgain_ClearsTheRestorePoint()
    {
        _services.Add("Fax", ServiceStartMode.Automatic, running: false);

        Tuner().Apply(TuneAction.Manual, ["Fax"]);
        Tuner().Apply(TuneAction.Automatic, ["Fax"]);

        Assert.Empty(_store.Load());
    }

    [Theory]
    [InlineData(TuneAction.Manual)]
    [InlineData(TuneAction.Disabled)]
    [InlineData(TuneAction.Stop)]
    public void ProtectedService_IsNeverChanged(TuneAction action)
    {
        _services.Add("WinDefend", ServiceStartMode.Automatic, running: true);

        var result = Assert.Single(Tuner().Apply(action, ["WinDefend"]));

        Assert.Equal(TuneOutcome.Skipped, result.Outcome);
        Assert.Equal(new ServiceState(ServiceStartMode.Automatic, true), _services.GetState("WinDefend"));
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Stop_IsRefusedWhileAnotherServiceNeedsIt()
    {
        _services.Add("Spooler", ServiceStartMode.Automatic, running: true);
        _services.Dependents["Spooler"] = ["Fax"];

        var result = Assert.Single(Tuner().Apply(TuneAction.Stop, ["Spooler"]));

        Assert.Equal(TuneOutcome.Skipped, result.Outcome);
        Assert.Contains("Fax", result.Detail, StringComparison.Ordinal);
        Assert.True(_services.GetState("Spooler")!.IsRunning);
    }

    [Fact]
    public void ServiceChangedByTheActiveMode_IsLeftToTheMode()
    {
        _services.Add("Fax", ServiceStartMode.Manual, running: false);

        var result = Assert.Single(Tuner("Fax").Apply(TuneAction.Disabled, ["Fax"]));

        Assert.Equal(TuneOutcome.Skipped, result.Outcome);
        Assert.Equal(ServiceStartMode.Manual, _services.GetState("Fax")!.StartMode);
    }

    [Fact]
    public void UnknownServiceAndUnchangedValue_AreSkipped()
    {
        _services.Add("Fax", ServiceStartMode.Manual, running: false);

        var results = Tuner().Apply(TuneAction.Manual, ["Fax", "Missing"]);

        Assert.All(results, result => Assert.Equal(TuneOutcome.Skipped, result.Outcome));
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Knowledge_RecommendsOnlyUnprotectedServicesThatDiffer()
    {
        var db = Path.Combine(_directory, "db");
        Directory.CreateDirectory(db);
        File.WriteAllText(Path.Combine(db, "services.json"), """
            [
              { "id": "Fax", "kind": "service", "recommendedStartMode": "Manual", "risk": "low", "verified": "2026-10-01" },
              { "id": "MapsBroker", "kind": "service", "recommendedStartMode": "Manual", "risk": "low", "verified": "2026-10-01-local" },
              { "id": "AlreadyManual", "kind": "service", "recommendedStartMode": "Manual", "risk": "low", "verified": "2026-10-01" },
              { "id": "Risky", "kind": "service", "recommendedStartMode": "Manual", "risk": "high", "verified": "2026-10-01" },
              { "id": "WinDefend", "kind": "service", "recommendedStartMode": "Disabled", "risk": "low", "verified": "2026-10-01" },
              { "id": "Kept", "kind": "service", "recommendedStartMode": "Keep", "risk": "low", "verified": "2026-10-01" },
              { "id": "OneDrive", "kind": "startup-app", "recommendedStartMode": "Disabled", "risk": "low", "verified": "2026-10-01" }
            ]
            """);
        static ServiceInfo Service(string name, ServiceStartMode mode) => new(name, name, true, mode, null);

        var recommendations = ServiceKnowledge.Load(db).Recommend(
            [
                Service("Fax", ServiceStartMode.Automatic), Service("MapsBroker", ServiceStartMode.Automatic),
                Service("AlreadyManual", ServiceStartMode.Manual), Service("Risky", ServiceStartMode.Automatic),
                Service("WinDefend", ServiceStartMode.Automatic), Service("Kept", ServiceStartMode.Automatic),
                Service("OneDrive", ServiceStartMode.Automatic),
            ],
            _policy);

        Assert.Equal(["Fax", "MapsBroker"], recommendations.Select(recommendation => recommendation.Service.Name));
        Assert.True(recommendations[0].IsConfident);
        Assert.False(recommendations[1].IsConfident);
        Assert.Equal(ServiceStartMode.Manual, recommendations[0].Advice.Recommended);
    }

    private sealed class FakeServices : IServiceControl
    {
        private readonly Dictionary<string, (ServiceStartMode Mode, bool Running, bool Delayed)> _state = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> Dependents { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, ServiceStartMode mode, bool running, bool delayed = false) => _state[name] = (mode, running, delayed);

        public ServiceState? GetState(string name) =>
            _state.TryGetValue(name, out var state) ? new ServiceState(state.Mode, state.Running) : null;

        public bool IsDelayedAutoStart(string name) => _state[name].Delayed;
        public IReadOnlyList<string> GetRunningDependents(string name) => Dependents.GetValueOrDefault(name, []);

        public void SetStartMode(string name, ServiceStartMode mode, bool delayedAutoStart) =>
            _state[name] = (mode, _state[name].Running, delayedAutoStart);

        public void StopService(string name) => _state[name] = (_state[name].Mode, false, _state[name].Delayed);
        public void StartService(string name) => _state[name] = (_state[name].Mode, true, _state[name].Delayed);
    }
}
