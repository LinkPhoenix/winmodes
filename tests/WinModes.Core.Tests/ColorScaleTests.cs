namespace WinModes.Core.Tests;

public sealed class ColorScaleTests
{
    private static readonly Rgb Red = new(240, 0, 0);
    private static readonly Rgb Yellow = new(240, 240, 0);
    private static readonly Rgb Green = new(0, 240, 0);

    private static readonly (double, Rgb)[] Stops = [(0, Red), (30, Yellow), (60, Green), (100, Green)];

    [Fact]
    public void At_GivesTheStopColourExactlyAtAStop()
    {
        Assert.Equal(Red, ColorScale.At(0, Stops));
        Assert.Equal(Yellow, ColorScale.At(30, Stops));
        Assert.Equal(Green, ColorScale.At(60, Stops));
    }

    [Fact]
    public void At_BlendsBetweenTwoStops()
    {
        Assert.Equal(new Rgb(240, 120, 0), ColorScale.At(15, Stops));
        Assert.Equal(new Rgb(120, 240, 0), ColorScale.At(45, Stops));
    }

    [Fact]
    public void Scale_GetsRedderTowardZero()
    {
        // Red stays at its maximum while green falls to nothing: the nearer to 0, the less green and the more red.
        var greens = new[] { 60d, 40, 20, 5, 0 }.Select(value => ColorScale.At(value, Stops).G).ToArray();

        Assert.Equal(greens.OrderDescending(), greens);
        Assert.True(greens[^1] < greens[0]);
        Assert.Equal(greens[^1], ColorScale.At(0, Stops).G);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void At_BelowTheFirstStopOrUnknown_IsTheFirstColour(double value) => Assert.Equal(Red, ColorScale.At(value, Stops));

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(double.PositiveInfinity)]
    public void At_AboveTheLastStop_IsTheLastColour(double value) => Assert.Equal(Green, ColorScale.At(value, Stops));

    [Fact]
    public void At_RefusesAnEmptyScale() => Assert.Throws<ArgumentException>(() => ColorScale.At(1, []));

    [Fact]
    public void At_WithOneStop_IsAlwaysThatColour() => Assert.Equal(Yellow, ColorScale.At(80, [(0, Yellow)]));
}
