using System;
using System.Collections.Generic;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorResearchTests : GameWorldCollectorTestBase
{
    [Fact]
    public void PersistentChallengeRequirementAdjustmentUsesTheNativeVerdictAndKeepsItsSource()
    {
        var improvedScribing = Guid.NewGuid();
        var focusImprovedScribing = Guid.NewGuid();
        var authoredModifier = Guid.NewGuid();
        var challenge = new global::ChallengeSO();
        challenge.SetGuid(focusImprovedScribing);

        var requirementsAdjust = new FakeModifierRecord(0d);
        requirementsAdjust.passiveModifiers[authoredModifier] = new FakeValueModifier(
            FakeModifierKind.Raw,
            -5d,
            order: 0,
            reference: challenge);
        FakeResearch.All.Add(new FakeResearch
        {
            Identity = improvedScribing,
            level = 10,
            requirementsAdjust = requirementsAdjust,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Research, improvedScribing, out var research));
        Assert.Equal(10, research.BaseRequirementLevel);
        Assert.Equal(5, research.EffectiveRequirementLevel);
        Assert.Equal(-5, research.RequirementLevelAdjustment);

        var adjustment = Assert.Single(research.RequirementAdjustments.AsSpan().ToArray());
        Assert.Equal(authoredModifier, adjustment.ModifierId);
        Assert.Equal(focusImprovedScribing, adjustment.SourceId);
        Assert.Equal((int)FakeModifierKind.Raw, adjustment.ModifierType);
        Assert.Equal(-5d, adjustment.Amount.ToDouble());
        Assert.Equal(0, adjustment.Order);
        Assert.True(adjustment.Passive);
    }

    [Fact]
    public void ResearchDecisionPublishesNativeCostHoldingsInvestmentAndBonusCapacityTogether()
    {
        var resource = new FakeResource { Identity = Guid.NewGuid(), Quantity = new BigDouble(80) };
        var type = new FakeResearchType
        {
            Identity = Guid.NewGuid(),
            RemainingFreeBonusLevels = 2,
            CurrentInvestmentLevel = 1,
            MaxInvestmentLevel = 5,
        };
        var fill = new FakeResearchFillList.ResourceFillEntry
        {
            resource = resource,
            Quantity = new BigDouble(40),
            Capacity = new BigDouble(100),
        };
        var research = new FakeResearch
        {
            Identity = Guid.NewGuid(),
            maxLevel = 10,
            researchCost = new FakeCraftingResourceCostList().With(resource, new BigDouble(20)),
        };
        research.researchTypes.Add(type);
        research.resourceFillList.entries.Add(fill);
        FakeResource.All.Add(resource);
        FakeResearch.All.Add(research);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Research, research.Identity, out var row));
        Assert.True(row.Decision.Available);
        Assert.Equal(1, row.Decision.LevelsAvailable);
        var cost = Assert.Single(row.Decision.DevelopmentCosts.AsSpan().ToArray());
        Assert.Equal(resource.Identity, cost.ResourceId);
        Assert.Equal(20d, cost.Price.ToDouble());
        Assert.Equal(80d, cost.Held.ToDouble());
        var investment = Assert.Single(row.Decision.Investment.AsSpan().ToArray());
        Assert.Equal(40d, investment.Invested.ToDouble());
        var typeDecision = Assert.Single(row.Decision.ResearchTypes.AsSpan().ToArray());
        Assert.Equal(type.Identity, typeDecision.ResearchTypeId);
        Assert.Equal(2, typeDecision.RemainingBonusLevels);
    }

    [Fact]
    public void ResearchQueueDecisionReportsOnlyTheAffordableAcceptedCostPrefix()
    {
        var resource = new FakeResource { Identity = Guid.NewGuid(), Quantity = new BigDouble(15) };
        var research = new FakeResearch
        {
            Identity = Guid.NewGuid(),
            maxLevel = 10,
            researchCost = new FakeCraftingResourceCostList
            {
                affordabilityUsesResourceAmounts = true,
            }.With(resource, new BigDouble(10)),
        };
        FakeSettingsManager.QueueMode = true;
        FakeGlobalVariables.SetMultiBuy(3);
        FakeResource.All.Add(resource);
        FakeResearch.All.Add(research);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Research, research.Identity, out var row));
        Assert.True(row.Decision.QueueMode);
        Assert.Equal(1, row.Decision.LevelsAvailable);
        var cost = Assert.Single(row.Decision.DevelopmentCosts.AsSpan().ToArray());
        Assert.Equal(10d, cost.Price.ToDouble());
        Assert.Equal(15d, cost.Held.ToDouble());
    }

    [Fact]
    public void OnlyATimedChallengeCarriesTheLimitItsRunIsRacing()
    {
        // The limit is the game's own answer for the level the challenge is at — `SlowIncrement`
        // and the tooltip both ask `GetTimeLimit(ChallengeSO.level)` — so a condition that answers
        // in the level it is handed proves which level the collector asked for. A condition that
        // races nothing is not asked at all: its row carries no limit rather than a zero, which a
        // consumer would read as a run with no time left.
        var racing = Guid.NewGuid();
        var gated = Guid.NewGuid();
        FakeChallenge.All.Add(new FakeChallenge
        {
            Identity = racing,
            level = 3,
            maxLevel = -1,
            challengeCondition = new FakeChallengeCondition
            {
                beforeType = FakeBeforeType.Time,
                timeLimitSeconds = 600d,
            },
        });
        FakeChallenge.All.Add(new FakeChallenge
        {
            Identity = gated,
            level = 3,
            maxLevel = -1,
            challengeCondition = new FakeChallengeCondition
            {
                beforeType = FakeBeforeType.Prereq,
                timeLimitSeconds = 600d,
            },
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Challenges, racing, out var timed));
        Assert.Equal((int)FakeBeforeType.Time, timed.ConditionBeforeType);
        Assert.Equal(new BigDouble(1800), timed.TimeLimit);

        Assert.True(WorldLookup.TryFind(world.Challenges, gated, out var untimed));
        Assert.Equal((int)FakeBeforeType.Prereq, untimed.ConditionBeforeType);
        Assert.Equal(BigDouble.Zero, untimed.TimeLimit);
    }

    /// <summary>
    /// When the draft's buckets and the prestige decision both fail, the report names the first of
    /// them, so no counted shortfall goes unnamed behind a later one.
    /// </summary>
    [Fact]
    public void TheChallengeTypeFailureIsNamedFirstWhenPrestigeAlsoFails()
    {
        FakePersistentResetManager.instance.persistentResource = null!;

        var report = Collector(("ChallengeTypeSO", null)).Collect().For("challenge decisions");

        Assert.Equal(2, report.Skipped);
        Assert.Equal("the complete challenge type binding set was unavailable", report.FirstFailure);
    }

    [Fact]
    public void APrestigeDecisionThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakePersistentResetManager.instance.persistValue = new FakeCount { value = null! };

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void AChallengeDecisionThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakePersistentResetManager.instance.challengeRerollsLeft = new FakeCount { value = null! };

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void AChallengeTypeThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakeChallengeType.All.Add(new FakeChallengeType
        {
            GetGuidFailure = new NullReferenceException("challenge type"),
        });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    /// <summary>
    /// The challenge draft publishes its weighted buckets and which bucket each challenge is in.
    /// </summary>
    /// <remarks>
    /// <c>ChallengeTypeSO</c> derives straight from <c>IdScriptableObject</c> and carries no modifier
    /// records at all, so it is not in the type-modifier tables. What it does carry is the draft's
    /// weighting, which is what decides whether an offer can show a challenge at all.
    /// </remarks>
    [Fact]
    public void TheDraftPublishesItsWeightedBucketsAndTheBucketEachChallengeSitsIn()
    {
        var focus = new FakeChallengeType { weight = 4d, restrictedInstances = true };
        var handPlaced = new FakeChallengeType { weight = 0d, excludeFromRandomSelection = true };
        FakeChallengeType.All.Add(focus);
        FakeChallengeType.All.Add(handPlaced);

        var challenge = new FakeChallenge { challengeTypes = { focus } };
        FakeChallenge.All.Add(challenge);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(2, world.ChallengeTypes.Count);

        var buckets = new Dictionary<Guid, WorldChallengeTypeBucket>();
        for (var index = 0; index < world.ChallengeTypes.Count; index++)
        {
            var row = world.ChallengeTypes[index];
            buckets[row.ChallengeTypeId] = row;
        }

        Assert.Equal(4d, buckets[focus.Identity].Weight);
        Assert.True(buckets[focus.Identity].RestrictedInstances);
        Assert.False(buckets[focus.Identity].ExcludeFromRandomSelection);
        Assert.Equal(0d, buckets[handPlaced.Identity].Weight);
        Assert.True(buckets[handPlaced.Identity].ExcludeFromRandomSelection);

        var membership = Assert.Single(
            world.ChallengeTypeMemberships.AsSpan().ToArray());
        Assert.Equal(challenge.Identity, membership.ChallengeId);
        Assert.Equal(0, membership.Ordinal);
        Assert.Equal(focus.Identity, membership.ChallengeTypeId);
    }
}
