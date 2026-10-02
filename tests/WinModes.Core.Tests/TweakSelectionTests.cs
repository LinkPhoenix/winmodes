using WinModes.Core.Engine;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

public sealed class TweakSelectionTests
{
    private static readonly Tweak Mixed = new()
    {
        Id = "mixed",
        Title = "Mixed",
        Values =
        [
            new TweakValue(TweakHive.User, @"Software\Test", "UserValue", TweakValueKind.Number, "0"),
            new TweakValue(TweakHive.Machine, @"SOFTWARE\Policies\Test", "MachineValue", TweakValueKind.Text, "off"),
        ],
        Tasks = [@"\Microsoft\Windows\Feedback\Siuf\DmClient"],
        PartLabels = ["Your own setting", "", "The task"],
    };

    [Theory]
    [InlineData("widgets", "widgets", null)]
    [InlineData("start-suggestions#0,2,5", "start-suggestions", "0,2,5")]
    [InlineData("a#7", "a", "7")]
    public void TryParse_ReadsAnIdWithOrWithoutParts(string text, string id, string? parts)
    {
        Assert.True(TweakSelection.TryParse(text, out var selection));
        Assert.Equal(id, selection.Id);
        Assert.Equal(parts, selection.Parts is null ? null : string.Join(',', selection.Parts.Order()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#1")]
    [InlineData("a#")]
    [InlineData("a#1,")]
    [InlineData("a#-1")]
    [InlineData("a#x")]
    [InlineData("a#64")]
    [InlineData("a#1#2")]
    public void TryParse_RefusesAnythingElse(string? text) => Assert.False(TweakSelection.TryParse(text, out _));

    [Fact]
    public void Format_WritesAllPartsAsAPlainId_AndEverythingElseAsAList()
    {
        Assert.Equal("mixed", TweakSelection.Format(Mixed, [0, 1, 2]));
        Assert.Equal("mixed#0,2", TweakSelection.Format(Mixed, [2, 0, 2]));
    }

    [Fact]
    public void ASelectionTravelsAsOneHelperArgument()
    {
        var command = HelperArguments.Parse(["change", ":tweak", "mixed#0,2", "widgets", ":untweak", "mixed#1"]);

        Assert.NotNull(command);
        Assert.Equal(["mixed#0,2", "widgets"], command.Groups![0].Names);
        Assert.Equal(["mixed#1"], command.Groups[1].Names);
    }

    [Fact]
    public void Parts_ListTheValuesThenTheTasks_WithALabelOrTheTechnicalName()
    {
        Assert.Collection(
            Mixed.Parts,
            part =>
            {
                Assert.Equal((0, TweakPartKind.Value, "Your own setting", @"HKCU\Software\Test\UserValue", "0", false), (part.Index, part.Kind, part.Label, part.Target, part.Setting, part.MachineWide));
            },
            part =>
            {
                // No label in the catalog: the name of the value stands in.
                Assert.Equal((1, "MachineValue", @"HKLM\SOFTWARE\Policies\Test\MachineValue", "\"off\"", true), (part.Index, part.Label, part.Target, part.Setting, part.MachineWide));
            },
            part =>
            {
                Assert.Equal((2, TweakPartKind.Task, "The task", true), (part.Index, part.Kind, part.Label, part.MachineWide));
                Assert.Null(part.Setting);
            });
    }

    [Fact]
    public void ElevationAndUserPart_DependOnTheChosenParts()
    {
        Assert.False(Mixed.NeedsElevationFor(new HashSet<int> { 0 }));
        Assert.True(Mixed.NeedsElevationFor(new HashSet<int> { 0, 1 }));
        Assert.True(Mixed.NeedsElevationFor(new HashSet<int> { 2 }));
        Assert.True(Mixed.NeedsElevationFor(null));

        Assert.True(Mixed.HasUserPartFor(new HashSet<int> { 0 }));
        Assert.False(Mixed.HasUserPartFor(new HashSet<int> { 1, 2 }));
        Assert.True(Mixed.HasUserPartFor(null));
    }

    [Fact]
    public void ShippedCatalog_LabelsEveryPartOfATweakWithSeveralParts()
    {
        var root = RepositoryLocator.Find(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var catalog = TweakCatalog.Load(Path.Combine(root, "data", "tweaks.json"));

        var problems = catalog.Tweaks
            .Where(tweak => tweak.Parts.Count > 1 && (tweak.PartLabels.Count != tweak.Parts.Count || tweak.PartLabels.Any(string.IsNullOrWhiteSpace)))
            .Select(tweak => $"{tweak.Id}: {tweak.PartLabels.Count} labels for {tweak.Parts.Count} parts")
            .ToList();

        Assert.Empty(problems);
    }
}
