using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Tests;

public sealed class ModePlannerTests : IDisposable
{
    private readonly string _policyPath = Path.Combine(Path.GetTempPath(), $"winmodes-{Guid.NewGuid():N}.json");
    private readonly ProtectionPolicy _policy;

    public ModePlannerTests()
    {
        File.WriteAllText(_policyPath, """
            {
              "requiredCapabilities": {
                "security": { "services": ["WinDefend", "NPSMSvc"] },
                "user-apps": { "startupIds": ["Discord", "OpenCodexTray-*"] }
              }
            }
            """);
        _policy = ProtectionPolicy.Load(_policyPath);
    }

    public void Dispose() => File.Delete(_policyPath);

    private static ModeProfile ProfileStopping(params string[] serviceIds) => new()
    {
        Mode = "test",
        Services = new ServiceSettings { Stop = [.. serviceIds.Select(id => new ServiceStop { Id = id })] },
    };

    [Fact]
    public void Plan_RejectsProfileStoppingProtectedService()
    {
        var planner = new ModePlanner(new FakeProbe(), _policy);

        var error = Assert.Throws<ProfileException>(() => planner.Plan(ProfileStopping("WinDefend")));

        Assert.Contains("WinDefend", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDisabledStartModeAndProtectedApps()
    {
        var profile = new ModeProfile
        {
            Mode = "test",
            Services = new ServiceSettings { Stop = [new ServiceStop { Id = "StiSvc", SetStartMode = "Disabled" }] },
            Apps = new AppSettings
            {
                Close =
                [
                    new AppClose { Id = "Discord", Process = "Discord.exe" },
                    new AppClose { Id = "OpenCodexTray-cd84", Process = "wscript.exe" },
                ],
            },
        };

        Assert.Equal(3, _policy.Validate(profile).Count);
    }

    [Fact]
    public void Validate_RejectsPerUserInstanceOfProtectedService() =>
        Assert.Single(_policy.Validate(ProfileStopping("NPSMSvc_1a2b3c")));

    [Fact]
    public void Validate_RejectsProtectedProcessHiddenBehindAnotherId()
    {
        var profile = new ModeProfile
        {
            Mode = "test",
            Apps = new AppSettings { Close = [new AppClose { Id = "Steam", Process = "Discord.exe" }] },
        };

        Assert.Single(_policy.Validate(profile));
    }

    [Fact]
    public void Load_RefusesPolicyWithoutProtectedServices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winmodes-empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "requiredCapabilities": {} }""");
        try
        {
            Assert.Throws<ProfileException>(() => ProtectionPolicy.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plan_OnlyListsServiceStopsThatHaveAnEffect()
    {
        var probe = new FakeProbe
        {
            Services =
            {
                ["Running"] = new ServiceState(ServiceStartMode.Automatic, IsRunning: true),
                ["AlreadyStopped"] = new ServiceState(ServiceStartMode.Manual, IsRunning: false),
                ["UserDisabled"] = new ServiceState(ServiceStartMode.Disabled, IsRunning: false),
            },
        };

        var plan = new ModePlanner(probe, _policy).Plan(ProfileStopping("Running", "AlreadyStopped", "UserDisabled", "Missing"));

        var change = Assert.Single(plan.Changes);
        Assert.Equal("Running", change.Target);
        Assert.Equal(3, plan.Skipped.Count);
    }

    [Fact]
    public void Plan_ShutsDownWslOnlyWhenItRuns()
    {
        var profile = new ModeProfile { Mode = "work", Wsl = new WslSettings { Running = false } };

        var whenRunning = new ModePlanner(new FakeProbe { WslRunning = true }, _policy).Plan(profile);
        var whenStopped = new ModePlanner(new FakeProbe { WslRunning = false }, _policy).Plan(profile);

        Assert.Contains(whenRunning.Changes, change => change.Kind == ChangeKind.ShutdownWsl);
        Assert.DoesNotContain(whenStopped.Changes, change => change.Kind == ChangeKind.ShutdownWsl);
    }

    private sealed class FakeProbe : ISystemProbe
    {
        public Dictionary<string, ServiceState> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool WslRunning { get; init; }

        public ServiceState? GetService(string name) => Services.GetValueOrDefault(name);
        public bool IsProcessRunning(string processFileName) => false;
        public bool IsWslRunning() => WslRunning;
    }
}
