using WinModes.Core.Monitoring;

namespace WinModes.Core.Tests;

public sealed class SamplingDemandTests
{
    private const StatsSources Everything = StatsSources.Cpu | StatsSources.Memory | StatsSources.Network | StatsSources.AiTools;

    [Fact]
    public void NothingSwitchedOn_ReadsNothingAndRunsNoTimer()
    {
        var demand = new SamplingDemand();

        Assert.Equal(StatsSources.None, demand.Wanted);
        Assert.Equal(StatsSources.None, demand.Resolve(Everything));
        Assert.False(demand.NeedsTimer(Everything));
    }

    [Theory]
    [InlineData(true, false, false, false, StatsSources.Cpu)]
    [InlineData(false, true, false, false, StatsSources.Memory)]
    [InlineData(false, false, true, false, StatsSources.Network)]
    [InlineData(false, false, false, true, StatsSources.AiTools)]
    [InlineData(true, true, false, false, StatsSources.Cpu | StatsSources.Memory)]
    public void Widget_ReadsOnlyTheBlocksItDraws(bool cpu, bool memory, bool network, bool ai, StatsSources expected)
    {
        var demand = new SamplingDemand { Widget = true, WidgetCpu = cpu, WidgetMemory = memory, WidgetNetwork = network, WidgetAiTools = ai };

        Assert.Equal(expected, demand.Resolve(Everything));
    }

    [Fact]
    public void BlocksOfAWidgetThatIsNotShown_AreNotRead() =>
        Assert.Equal(StatsSources.None, new SamplingDemand { WidgetCpu = true, WidgetMemory = true, WidgetNetwork = true, WidgetAiTools = true }.Wanted);

    [Fact]
    public void WidgetOfPlansAlone_ReadsNothingButStillRunsATimer()
    {
        var demand = new SamplingDemand { Widget = true };

        Assert.Equal(StatsSources.None, demand.Resolve(Everything));
        Assert.True(demand.NeedsTimer(Everything));
        Assert.True(demand.NeedsTimer(StatsSources.None));
    }

    [Fact]
    public void ASourceTheUserTurnedOff_IsNeverRead()
    {
        var demand = new SamplingDemand { Widget = true, WidgetCpu = true, WidgetMemory = true, WidgetNetwork = true, WidgetAiTools = true };

        Assert.Equal(StatsSources.Cpu | StatsSources.Network, demand.Resolve(StatsSources.Cpu | StatsSources.Network));
        Assert.Equal(StatsSources.None, demand.Resolve(StatsSources.None));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void EveryFeatureOfTheAiTools_AsksForThem(bool tray, bool alerts, bool endIdle, bool record)
    {
        var demand = new SamplingDemand { TrayMeter = tray, AiMemoryAlerts = alerts, EndIdleSessions = endIdle, RecordUsage = record };

        Assert.Equal(StatsSources.AiTools, demand.Wanted);
        Assert.True(demand.NeedsTimer(Everything));
    }

    [Fact]
    public void FeaturesOfTheAiTools_AreInertWhileTheyAreTurnedOff()
    {
        var demand = new SamplingDemand { TrayMeter = true, AiMemoryAlerts = true, EndIdleSessions = true, RecordUsage = true };
        var withoutAiTools = StatsSources.Cpu | StatsSources.Memory | StatsSources.Network;

        Assert.Equal(StatsSources.None, demand.Resolve(withoutAiTools));
        Assert.False(demand.NeedsTimer(withoutAiTools));
    }
}
