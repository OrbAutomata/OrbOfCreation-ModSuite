using System;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins the five frame-wide globals by value, and what each degrades to when the game does not hold
/// up its end.
/// </summary>
/// <remarks>
/// Nothing pinned these before: the collector tests reach them only through the rows they price, so
/// two of the five were never varied through this reader at all. They are read once per pass through
/// five static accessors and one field, which is exactly the surface a change of read mechanism
/// moves, so they are pinned here by value rather than by their downstream effect.
/// </remarks>
public sealed class WorldFrameGlobalsReaderTests : IDisposable
{
    public WorldFrameGlobalsReaderTests() => Clear();

    public void Dispose() => Clear();

    [Fact]
    public void EveryGlobalIsReadOnItsOwnScale()
    {
        Globals.Overflow = Variable(250d);
        Globals.OverflowLoss = Variable(40d);
        Globals.ResetTimePassed = Variable(61.5d);
        Globals.StructureCost = Variable(175d);
        Globals.AttributeQualityBonus = Variable(3d);
        var reader = new WorldFrameGlobalsReader(ResolveGlobals);

        Assert.True(reader.IsAvailable);
        var globals = reader.Read(0.02d);

        // The four multipliers land on the percent scale the game keeps them on; the exponent does
        // not, because putting it there would take a hundredth root instead of the power.
        Assert.Equal(2.5d, globals.ResourceOverflowPercent.ToDouble());
        Assert.Equal(0.4d, globals.ResourceOverflowLossPercent.ToDouble());
        Assert.Equal(61.5d, globals.ResetTimePassed.ToDouble());
        Assert.Equal(1.75d, globals.StructureCostPercent.ToDouble());
        Assert.Equal(3d, globals.AttributeQualityBonus.ToDouble());
        Assert.Equal(0.02d, globals.FixedDeltaTime);
        Assert.Equal(string.Empty, reader.Degradation);
    }

    /// <summary>
    /// A record the game will recompute is recomputed here too, rather than read as the memo it
    /// deserialised with — the defect that priced every structure at nothing on a cold collection.
    /// </summary>
    [Fact]
    public void ADirtyRecordIsRecomputedRatherThanReadAsItsStaleMemo()
    {
        var record = new global::ValueModifierRecord(new BigDouble(0d, 0)).Dirty();
        record.activeModifiers[Guid.NewGuid()] = new global::ValueModifier(
            global::ValueModifier.ValueModifierType.Raw, new BigDouble(150d, 0));
        Globals.StructureCost = new global::DoubleVariable { value = record };
        FillTheRest();

        var globals = new WorldFrameGlobalsReader(ResolveGlobals).Read(0.02d);

        Assert.Equal(1.5d, globals.StructureCostPercent.ToDouble());
    }

    /// <summary>
    /// One accessor answering with nothing costs that term alone. Zero is right for it: an absent
    /// overflow rate is no overflow, not an unknown one.
    /// </summary>
    [Fact]
    public void AnAccessorAnsweringWithNothingCostsOnlyItsOwnTerm()
    {
        FillTheRest();
        Globals.Overflow = null;

        var globals = new WorldFrameGlobalsReader(ResolveGlobals).Read(0.02d);

        Assert.Equal(0d, globals.ResourceOverflowPercent.ToDouble());
        Assert.Equal(1d, globals.StructureCostPercent.ToDouble());
        Assert.Equal(2d, globals.ResetTimePassed.ToDouble());
    }

    /// <summary>
    /// A build the reader cannot bind at all degrades every term to the value that leaves what it
    /// feeds alone — which is one for the multiplier and zero for everything else, never uniform.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnbindableReaderDegradesPerTermRatherThanUniformly(bool absentType)
    {
        FillTheRest();
        var reader = new WorldFrameGlobalsReader(
            absentType ? ResolveNothing : ResolvePartialGlobals);

        Assert.False(reader.IsAvailable);
        var globals = reader.Read(0.05d);

        Assert.Equal(0d, globals.ResourceOverflowPercent.ToDouble());
        Assert.Equal(0d, globals.ResourceOverflowLossPercent.ToDouble());
        Assert.Equal(0d, globals.ResetTimePassed.ToDouble());
        Assert.Equal(1d, globals.StructureCostPercent.ToDouble());
        Assert.Equal(0d, globals.AttributeQualityBonus.ToDouble());
        Assert.Equal(0.05d, globals.FixedDeltaTime);
    }

    private static void FillTheRest()
    {
        Globals.Overflow ??= Variable(100d);
        Globals.OverflowLoss ??= Variable(100d);
        Globals.ResetTimePassed ??= Variable(2d);
        Globals.StructureCost ??= Variable(100d);
        Globals.AttributeQualityBonus ??= Variable(0d);
    }

    private static global::DoubleVariable Variable(double amount) =>
        new() { value = new global::ValueModifierRecord(new BigDouble(amount)) };

    private static Type? ResolveGlobals(string name) => name == "Player" ? typeof(Globals) : null;

    private static Type? ResolvePartialGlobals(string name) =>
        name == "Player" ? typeof(PartialGlobals) : null;

    private static Type? ResolveNothing(string name) => null;

    private static void Clear()
    {
        Globals.Overflow = null;
        Globals.OverflowLoss = null;
        Globals.ResetTimePassed = null;
        Globals.StructureCost = null;
        Globals.AttributeQualityBonus = null;
    }

    /// <summary>Stands in for the game's <c>Player</c>: five static accessors of one shape.</summary>
    /// <remarks>
    /// Not the shared <c>GameStubs</c> <c>Player</c>, which now carries all five. This is the file
    /// that varies them, and two of the things it varies that stub cannot express: an accessor here
    /// has to be able to answer with nothing, and the fifth has to be able to be absent altogether —
    /// see <see cref="PartialGlobals"/>. Its statics are also the process-wide ones every other test
    /// collects through, so emptying one here to prove a degradation would empty it there too.
    /// </remarks>
    private sealed class Globals
    {
        internal static global::DoubleVariable? Overflow;
        internal static global::DoubleVariable? OverflowLoss;
        internal static global::DoubleVariable? ResetTimePassed;
        internal static global::DoubleVariable? StructureCost;
        internal static global::DoubleVariable? AttributeQualityBonus;

        public static global::DoubleVariable? GetResourceOverflow() => Overflow;
        public static global::DoubleVariable? GetResourceOverflowLoss() => OverflowLoss;
        public static global::DoubleVariable? GetResetTimePassed() => ResetTimePassed;
        public static global::DoubleVariable? GetStructureCost() => StructureCost;
        public static global::DoubleVariable? GetAttributeQualityBonus() => AttributeQualityBonus;
    }

    /// <summary>A build that renamed the fifth accessor, which unbinds the reader as a whole.</summary>
    private sealed class PartialGlobals
    {
        public static global::DoubleVariable? GetResourceOverflow() => Globals.Overflow;
        public static global::DoubleVariable? GetResourceOverflowLoss() => Globals.OverflowLoss;
        public static global::DoubleVariable? GetResetTimePassed() => Globals.ResetTimePassed;
        public static global::DoubleVariable? GetStructureCost() => Globals.StructureCost;
    }
}
