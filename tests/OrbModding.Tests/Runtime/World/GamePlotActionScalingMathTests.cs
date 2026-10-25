using System;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class GamePlotActionScalingMathTests
{
    [Fact]
    public void PersistentDrainUsesCombinedSpeedAfterItsNativeCeilingAndParallelRatio()
    {
        var action = Values(new(5, 200), new(6, 400));
        var plot = Values(new(5, 50), new(6, 200));
        Assert.True(GamePlotActionScalingMath.TryDrainPercent(action, plot,
            PublicationTable<GamePlotTransmuter>.Empty, PublicationTable<GamePlotSizeConverter>.Empty,
            1, 500, 3, out var drain, out var reason), reason);
        Assert.Equal(new BigDouble(1500), drain); // 200*.5 * min(400*2,500)/100 *3.
    }

    [Fact]
    public void ProspectiveDrainReevaluatesTheSpeedCeilingInsteadOfScalingCurrentDrainByLevelRatio()
    {
        var current = Values(new(5, 200), new(6, 100));
        var next = Values(new(5, 400), new(6, 225));
        Assert.True(GamePlotActionScalingMath.TryDrainPercent(current, Values(),
            PublicationTable<GamePlotTransmuter>.Empty, PublicationTable<GamePlotSizeConverter>.Empty,
            1, 150, 1, out var before, out _));
        Assert.True(GamePlotActionScalingMath.TryDrainPercent(next, Values(),
            PublicationTable<GamePlotTransmuter>.Empty, PublicationTable<GamePlotSizeConverter>.Empty,
            1, 150, 1, out var after, out _));
        Assert.Equal(new BigDouble(200), before);
        Assert.Equal(new BigDouble(600), after);
    }

    [Theory]
    [InlineData(0, false, 200)]
    [InlineData(0, true, 50)]
    [InlineData(1, false, 150)]
    [InlineData(1, true, 75)]
    [InlineData(2, false, 110)]
    [InlineData(2, true, 95)]
    [InlineData(3, false, 300)]
    [InlineData(3, true, -100)]
    [InlineData(4, false, 10100)]
    [InlineData(4, true, -9900)]
    [InlineData(5, false, 2100)]
    [InlineData(5, true, -1900)]
    public void TransmutersKeepTheNativeExtractionModeAndInversion(int mode, bool invert, double expected)
    {
        var modifiers = PublicationTable<GameValueModifier>.Create(new[]
            { new GameValueModifier(GameValueModifierType.Raw, 10) });
        var list = new GamePlotModifierList(modifiers, PublicationTable<GameValueModifier>.Empty);
        var row = new GamePlotTransmuter(5, mode, invert, false, 200,
            new GameValueModifier(GameValueModifierType.MultiDiminishing, .5), list);
        Assert.True(GamePlotActionScalingMath.TryTransmute(100, row, out var value));
        Assert.Equal(expected, value.ToDouble(), 8);
    }

    [Fact]
    public void TransmuterOrderAndSizeConversionFeedTheDrainBeforeFullSpeedScaling()
    {
        var first = new GamePlotTransmuter(5, 3, false, false, 100, default, GamePlotModifierListDefaults.Empty);
        var second = new GamePlotTransmuter(5, 0, false, false, 50, default, GamePlotModifierListDefaults.Empty);
        var converter = new GamePlotSizeConverter(5, 10,
            new GameValueModifier(GameValueModifierType.Raw, 10));
        Assert.True(GamePlotActionScalingMath.TryDrainPercent(Values(new(5, 100), new(6, 200)), Values(),
            PublicationTable<GamePlotTransmuter>.Create(new[] { first, second }),
            PublicationTable<GamePlotSizeConverter>.Create(new[] { converter }),
            100, 500, 1, out var drain, out var reason), reason);
        Assert.Equal(240d, drain.ToDouble(), 8); // ((100+100)*.5 +log10(100)*10) *2.
    }

    [Fact]
    public void UnknownOrNonfiniteProgramsFailClosed()
    {
        Assert.False(GamePlotActionScalingMath.TryDrainPercent(Values(new GamePlotScalingValue(99, 100)), Values(),
            PublicationTable<GamePlotTransmuter>.Empty, PublicationTable<GamePlotSizeConverter>.Empty,
            1, 100, 1, out _, out var reason));
        Assert.Contains("unknown", reason);
        Assert.False(GamePlotActionScalingMath.TryTransmute(100,
            new GamePlotTransmuter(5, 0, true, false, 0, default, GamePlotModifierListDefaults.Empty), out _));
    }

    private static PublicationTable<GamePlotScalingValue> Values(params GamePlotScalingValue[] rows) =>
        PublicationTable<GamePlotScalingValue>.Create(rows);
}
