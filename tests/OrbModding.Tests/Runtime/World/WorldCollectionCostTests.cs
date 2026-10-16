using System;
using System.Diagnostics;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// What a collection pass cost, category by category.
/// </summary>
/// <remarks>
/// Collection is the suite's largest main-thread cost, and for as long as the only published number
/// was the total, every proposal to make it cheaper was an argument about which category was
/// probably expensive. These pin the line that ends the argument: the dearest categories by name
/// with their milliseconds, and a remainder carried explicitly so the named ones can never be read
/// as the whole pass.
/// </remarks>
public sealed class WorldCollectionCostTests
{
    [Fact]
    public void TheDearestCategoriesAreNamedAndEverythingElseIsAccountedForAsARemainder()
    {
        var report = new WorldCollectionReport(
            Charged("resources", Milliseconds(1.5)),
            Charged("scribe relations", Milliseconds(4.25)),
            Charged("upgrades", Milliseconds(2)),
            Charged("views", Milliseconds(0.25)),
            Charged("rituals", Milliseconds(0.5)));

        Assert.Equal(
            "World collection cost 8.500 ms: scribe relations 4.250, upgrades 2.000, " +
            "resources 1.500; 2 more 0.750.",
            report.DescribeCost());
    }

    /// <summary>
    /// A category read once per epoch is absent from the passes that skip it, not free within them.
    /// </summary>
    [Fact]
    public void ACategoryThatCostThePassNothingIsLeftOutRatherThanListedAtZero()
    {
        var report = new WorldCollectionReport(
            Charged("resources", Milliseconds(1)),
            Charged("plot authoring", 0),
            Charged("effect blocks", 0));

        Assert.Equal("World collection cost 1.000 ms: resources 1.000.", report.DescribeCost());
    }

    [Fact]
    public void APassThatMeasuredNothingSaysSoRatherThanNamingACategory()
    {
        var report = new WorldCollectionReport(Charged("resources", 0));

        Assert.Equal("World collection cost 0.000 ms, unattributed.", report.DescribeCost());
    }

    /// <summary>
    /// Cost is reported beside the announce, never inside the key it repeats on. Collection runs four
    /// times a second and no two passes cost the same, so a key carrying the cost would announce
    /// every pass — which is the state the announce band exists to prevent.
    /// </summary>
    [Fact]
    public void WhatThePassCostDoesNotMoveWhatTheAnnounceComparesOn()
    {
        var cheap = new WorldCollectionReport(Charged("resources", Milliseconds(1)));
        var dear = new WorldCollectionReport(Charged("resources", Milliseconds(40)));

        Assert.Equal(cheap.Describe(), dear.Describe());
        Assert.NotEqual(cheap.DescribeCost(), dear.DescribeCost());
    }

    private static WorldCategoryReport Charged(string category, long elapsedTicks) =>
        new(category, WorldCategoryOutcome.Collected, sampled: 1, skipped: 0, string.Empty, elapsedTicks);

    private static long Milliseconds(double milliseconds) =>
        (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);
}
