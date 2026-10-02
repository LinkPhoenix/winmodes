using WinModes.Core.Planning;
using WinModes.Core.Profiles;

namespace WinModes.Core.Tests;

public sealed class PowerCatalogTests
{
    private static PowerSettings Power(params PowerValue[] values) => new() { Plan = "balanced", Values = values };

    [Fact]
    public void AValidSet_BreaksNoRule() =>
        Assert.Empty(PowerCatalog.Validate(Power(
            new PowerValue { Setting = "display-off", Ac = 0, Dc = 0 },
            new PowerValue { Setting = "cpu-max", Dc = 70 })));

    [Theory]
    [InlineData("nonsense", 1, "not one a mode may change")]
    [InlineData("cpu-max", 101, "outside")]
    [InlineData("cpu-max", 4, "outside")]
    [InlineData("display-off", -1, "outside")]
    [InlineData("usb-suspend", 2, "outside")]
    public void ABadSetting_IsRefused(string setting, int value, string reason)
    {
        var violations = PowerCatalog.Validate(Power(new PowerValue { Setting = setting, Ac = value }));

        Assert.Contains(violations, line => line.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void ASettingWithoutAValue_OrListedTwice_IsRefused()
    {
        Assert.Contains(PowerCatalog.Validate(Power(new PowerValue { Setting = "display-off" })), line => line.Contains("no value", StringComparison.Ordinal));
        Assert.Contains(PowerCatalog.Validate(Power(
            new PowerValue { Setting = "display-off", Ac = 0 }, new PowerValue { Setting = "DISPLAY-OFF", Ac = 5 })), line => line.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Commands_AreBuiltPerSide_WithInvariantNumbers()
    {
        var commands = PowerCatalog.Commands("guid", new PowerValue { Setting = "sleep-after", Ac = 0, Dc = 1800 });

        Assert.Equal(["/setacvalueindex", "guid", "SUB_SLEEP", "STANDBYIDLE", "0"], commands[0]);
        Assert.Equal(["/setdcvalueindex", "guid", "SUB_SLEEP", "STANDBYIDLE", "1800"], commands[1]);
        Assert.Single(PowerCatalog.Commands("guid", new PowerValue { Setting = "cpu-max", Dc = 70 }));
    }

    [Fact]
    public void Commands_RefuseAnUnknownSetting() =>
        Assert.Throws<ProfileException>(() => PowerCatalog.Commands("guid", new PowerValue { Setting = "x", Ac = 1 }));

    [Theory]
    [InlineData(0, "never")]
    [InlineData(90, "90 s")]
    [InlineData(1800, "30 min")]
    [InlineData(7200, "2 h")]
    public void Seconds_AreDescribedForThePreview(int seconds, string expected) => Assert.Equal(expected, PowerCatalog.DescribeSeconds(seconds));

    [Fact]
    public void EverySettingOfTheCatalog_HasASaneRange() =>
        Assert.All(PowerCatalog.Settings, setting => Assert.True(setting.Minimum < setting.Maximum && setting.Minimum >= 0));
}
