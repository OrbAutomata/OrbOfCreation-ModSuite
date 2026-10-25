using System;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using Xunit;

namespace OrbModding.Tests.Runtime.Strategy;

/// <summary>
/// The bulletin is a service-cycle publication, so its shape is enforced by the framework rather
/// than by review — the audit itself lives beside the configuration's, where the pinned type is
/// named. These tests pin that the neutral bulletin is genuinely equivalent to having no strategy,
/// and that stance resolution holds up across the sorted table the builder produces.
/// </summary>
public sealed class SuiteStrategyTests
{
    private static readonly Guid Mana = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Knowledge = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Wood = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void APublisherStartsOnTheNeutralBulletinAtGenerationOne()
    {
        using var publisher = new ServiceStrategyPublisher(SuiteStrategyDefaults.Neutral);

        Assert.Equal(1UL, publisher.ReadLatest().Generation.Value);
        Assert.Same(SuiteStrategyDefaults.Neutral, publisher.ReadLatest().Bulletin);
    }

    [Fact]
    public void TheNeutralBulletinConstrainsNothing()
    {
        var neutral = SuiteStrategyDefaults.Neutral;

        Assert.Equal(SuiteStrategyProvenance.Neutral, neutral.Provenance);
        Assert.Equal(Guid.Empty, neutral.ActiveGoalId);
        Assert.Equal(0, neutral.Resources.Count);
        Assert.Equal(SuiteResourceStanceKind.Free, neutral.StanceFor(Mana).Kind);
        Assert.Equal(SuiteResourceStanceKind.Free, neutral.StanceFor(Guid.NewGuid()).Kind);
    }

    [Fact]
    public void ResourcesAbsentFromTheTableResolveAsFree()
    {
        var bulletin = new SuiteStrategyBuilder()
            .With(SuiteResourceStance.FloorOf(Knowledge, new BigDouble(5)))
            .Build(SuiteStrategyProvenance.Goal, Guid.NewGuid());

        Assert.Equal(SuiteResourceStanceKind.FloorAbsolute, bulletin.StanceFor(Knowledge).Kind);
        Assert.Equal(SuiteResourceStanceKind.Free, bulletin.StanceFor(Mana).Kind);
        Assert.Equal(SuiteResourceStanceKind.Free, bulletin.StanceFor(Wood).Kind);
    }

    [Fact]
    public void EveryStanceResolvesRegardlessOfInsertionOrder()
    {
        // The builder sorts; StanceFor binary-searches. Insert deliberately out of order so a
        // broken sort cannot pass by accident.
        var identities = new Guid[16];
        for (var index = 0; index < identities.Length; index++)
            identities[index] = new Guid($"{index + 1:x8}-0000-0000-0000-000000000000");

        var builder = new SuiteStrategyBuilder();
        for (var index = identities.Length - 1; index >= 0; index--)
            builder.With(SuiteResourceStance.TrivialOnly(identities[index], 0.01d * (index + 1)));

        var bulletin = builder.Build(SuiteStrategyProvenance.Goal, Guid.NewGuid());

        Assert.Equal(identities.Length, bulletin.Resources.Count);
        for (var index = 0; index < identities.Length; index++)
        {
            var stance = bulletin.StanceFor(identities[index]);
            Assert.Equal(SuiteResourceStanceKind.TrivialOnly, stance.Kind);
            Assert.Equal(0.01d * (index + 1), stance.MaxSpendFraction, 10);
        }

        Assert.Equal(SuiteResourceStanceKind.Free, bulletin.StanceFor(Mana).Kind);
    }

    [Fact]
    public void TheBuilderRejectsDuplicateAndAnonymousResources()
    {
        var builder = new SuiteStrategyBuilder().With(SuiteResourceStance.Embargo(Mana));

        Assert.Throws<ArgumentException>(() => builder.With(SuiteResourceStance.Free(Mana)));
        Assert.Throws<ArgumentException>(() => builder.With(SuiteResourceStance.Free(Guid.Empty)));
        Assert.Equal(1, builder.Count);
    }

    [Fact]
    public void PublishedBulletinsSupersedeEachOtherAndAdvanceTheGeneration()
    {
        using var publisher = new ServiceStrategyPublisher(SuiteStrategyDefaults.Neutral);
        var goal = Guid.NewGuid();

        var generation = publisher.Publish(new SuiteStrategyBuilder()
            .With(SuiteResourceStance.FloorOf(Knowledge, new BigDouble(5)))
            .Build(SuiteStrategyProvenance.Goal, goal));

        Assert.Equal(2UL, generation.Value);
        var latest = publisher.ReadLatest();
        Assert.Equal(goal, latest.Bulletin.ActiveGoalId);
        Assert.Equal(SuiteResourceStanceKind.FloorAbsolute, latest.Bulletin.StanceFor(Knowledge).Kind);

        // Replacement is total: a stance dropped from the next bulletin stops applying, with no
        // expiry bookkeeping anywhere.
        publisher.Publish(SuiteStrategyDefaults.Neutral);
        Assert.Equal(3UL, publisher.ReadLatest().Generation.Value);
        Assert.Equal(SuiteResourceStanceKind.Free, publisher.ReadLatest().Bulletin.StanceFor(Knowledge).Kind);
    }

    [Fact]
    public void APinnedBulletinIsUnaffectedByLaterPublications()
    {
        using var publisher = new ServiceStrategyPublisher(SuiteStrategyDefaults.Neutral);
        publisher.Publish(new SuiteStrategyBuilder()
            .With(SuiteResourceStance.Embargo(Wood))
            .Build(SuiteStrategyProvenance.Goal, Guid.NewGuid()));

        // What a cycle pins when it opens is a reference to an immutable value; publishing again
        // cannot reach into it. This is the property the whole zero-copy handoff depends on.
        var pinned = publisher.ReadLatest().Bulletin;
        publisher.Publish(SuiteStrategyDefaults.Neutral);

        Assert.Equal(SuiteResourceStanceKind.Embargo, pinned.StanceFor(Wood).Kind);
        Assert.Equal(SuiteResourceStanceKind.Free, publisher.ReadLatest().Bulletin.StanceFor(Wood).Kind);
    }
}
