using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Tests;

public sealed class ModeTweaksTests : IDisposable
{
    private readonly string _policyPath = Path.Combine(Path.GetTempPath(), $"winmodes-modetweaks-{Guid.NewGuid():N}.json");
    private readonly ProtectionPolicy _policy;

    public ModeTweaksTests()
    {
        File.WriteAllText(_policyPath, """{ "requiredCapabilities": { "security": { "services": ["WinDefend"] } } }""");
        _policy = ProtectionPolicy.Load(_policyPath);
    }

    public void Dispose() => File.Delete(_policyPath);

    [Theory]
    [InlineData(false, "none", true)]
    [InlineData(true, "none", false)]
    [InlineData(false, "explorer", false)]
    [InlineData(false, "sign-out", false)]
    [InlineData(false, "restart", false)]
    public void ASettingIsOnlyAppliedByAMode_WhenItNeedsNoAdministratorAndNoRestart(bool elevation, string restart, bool expected) =>
        Assert.Equal(expected, ModeTweaks.CanBeApplied(new TweakInfo("x", "X", false, elevation, restart)));

    [Fact]
    public void Diff_KeepsTheSettingsBothModesAsk_AndSwapsTheOthers()
    {
        var (undo, apply) = ModeTweaks.Diff(["dnd", "sounds"], ["sounds", "transparency"]);

        Assert.Equal(["dnd"], undo);
        Assert.Equal(["transparency"], apply);
    }

    [Fact]
    public void Diff_WithNothingOwnedAppliesEverythingOnce()
    {
        var (undo, apply) = ModeTweaks.Diff([], ["dnd", "DND", "sounds"]);

        Assert.Empty(undo);
        Assert.Equal(["dnd", "sounds"], apply);
    }

    [Fact]
    public void Diff_FromAModeThatSetNothingOnlyApplies()
    {
        var (undo, apply) = ModeTweaks.Diff([], ["dnd"]);

        Assert.Empty(undo);
        Assert.Equal(["dnd"], apply);
    }

    [Fact]
    public void Plan_ListsTheSettingsNotYetApplied_AndSkipsTheOthers()
    {
        var probe = new FakeTweaks(
            new TweakInfo("dnd", "Do not disturb", false, false, "none"),
            new TweakInfo("sounds", "Mute the notification sounds", true, false, "none"),
            new TweakInfo("policy", "A machine policy", false, true, "none"),
            new TweakInfo("slow", "Needs a sign-out", false, false, "sign-out"));
        var planner = new ModePlanner(new NoMachine(), _policy, probe);

        var plan = planner.Plan(new ModeProfile { Mode = "focus", TweakIds = ["dnd", "sounds", "policy", "slow", "unknown"] });

        var change = Assert.Single(plan.Changes, change => change.Kind == ChangeKind.ApplyTweak);
        Assert.Equal("Do not disturb", change.Target);
        Assert.Contains(plan.Skipped, line => line.StartsWith("sounds: already applied", StringComparison.Ordinal));
        Assert.Contains(plan.Skipped, line => line.StartsWith("policy: not available", StringComparison.Ordinal));
        Assert.Contains(plan.Skipped, line => line.StartsWith("slow: not available", StringComparison.Ordinal));
        Assert.Contains(plan.Skipped, line => line.StartsWith("unknown: not available", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_WithoutACatalogPlansNoSetting()
    {
        var planner = new ModePlanner(new NoMachine(), _policy);

        var plan = planner.Plan(new ModeProfile { Mode = "focus", TweakIds = ["dnd"] });

        Assert.DoesNotContain(plan.Changes, change => change.Kind == ChangeKind.ApplyTweak);
    }

    private sealed class NoMachine : ISystemProbe
    {
        public ServiceState? GetService(string name) => null;

        public bool IsProcessRunning(string processFileName) => false;

        public bool IsWslRunning() => false;
    }

    private sealed class FakeTweaks(params TweakInfo[] tweaks) : ITweakProbe
    {
        public TweakInfo? Find(string id) => tweaks.FirstOrDefault(tweak => tweak.Id == id);
    }
}
