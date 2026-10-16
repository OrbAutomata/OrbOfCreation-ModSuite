using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// A lifecycle boundary destroys the run a published world was read from, so the publication has to
/// go with it. Round 6 proved it did not: after Back to Menu the suite still served the destroyed
/// run's economy under <c>status: available</c>.
/// </summary>
public sealed class WorldLifecycleFlushTests
{
    private static GameWorldState Collected(long ticks) =>
        GameWorldStateDefaults.Empty with { CollectedAtUtcTicks = ticks };

    [Fact]
    public void FlushReturnsThePublicationToTheStateItWasConstructedIn()
    {
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        var seed = publisher.ReadLatest();

        publisher.Publish(Collected(12345), new WorldGeneration(72814));
        Assert.Equal(72814UL, publisher.ReadLatest().Generation.Value);

        publisher.Flush(GameWorldStateDefaults.Empty);

        var flushed = publisher.ReadLatest();
        Assert.Equal(seed.Generation.Value, flushed.Generation.Value);
        Assert.Equal(0, flushed.Snapshot.CollectedAtUtcTicks);
    }

    [Fact]
    public void CollectionAfterAFlushPublishesAgainFromNothing()
    {
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(Collected(12345), new WorldGeneration(72814));
        publisher.Flush(GameWorldStateDefaults.Empty);

        publisher.Publish(Collected(67890), new WorldGeneration(72900));

        var reloaded = publisher.ReadLatest();
        Assert.Equal(72900UL, reloaded.Generation.Value);
        Assert.Equal(67890, reloaded.Snapshot.CollectedAtUtcTicks);
    }

    [Fact]
    public void EveryAcceptedLifecycleReplacementTrashesThePublishedWorld()
    {
        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        TestWorldCollector.CollectedAt(registry, 72814, Collected(12345));
        Assert.True(registry.World.ReadLatest().Generation.Value > 1);

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(11)));

        var flushed = registry.World.ReadLatest();
        Assert.Equal(1UL, flushed.Generation.Value);
        Assert.Equal(0, flushed.Snapshot.CollectedAtUtcTicks);
    }

    /// <summary>
    /// A repeated request for the lifecycle already in force is not a boundary and must not throw the
    /// live world away — the run is still running.
    /// </summary>
    [Fact]
    public void ARejectedLifecycleRequestLeavesTheLiveWorldAlone()
    {
        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(11)));
        TestWorldCollector.CollectedAt(registry, 72814, Collected(12345));

        Assert.False(registry.RequestLifecycle(new LifecycleGeneration(11)));

        Assert.Equal(72814UL, registry.World.ReadLatest().Generation.Value);
    }
}
