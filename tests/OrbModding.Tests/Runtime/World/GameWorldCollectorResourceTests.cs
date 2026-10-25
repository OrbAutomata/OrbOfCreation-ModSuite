using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorResourceTests : GameWorldCollectorTestBase
{

    [Fact]
    public void AnUncappedResourceKeepsTheGamesNegativeSentinel()
    {
        // The collector must not normalise the game's "no ceiling" marker into something friendlier.
        // Negative is what HasMaxQuantity() tests for, and the deriver reads the same convention.
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = 1e9d,
            maxQuantity = new ValueModifierRecord(-1d),
        });

        var collector = Collector();
        collector.Collect();

        var resource = collector.Build().Resources[0];
        Assert.True(resource.Reading.Capacity.ToDouble() < 0d);
        Assert.False(resource.IsCapped);
    }

    [Fact]
    public void CollectingNeverAsksTheGameToRecalculateACachedValue()
    {
        // The load-bearing property of the whole collector. ValueModifierRecord.GetValue() is not a
        // read: on a dirty record it runs an allocating pass over both modifier dictionaries and
        // writes calculatedValue, calculationDirty, observedId, and updateObservers. Calling it would
        // make the game recompute and re-stamp its own observable at whatever point in the frame the
        // suite's pump happens to run, which is a mutation the suite has no business performing
        // outside the action boundary.
        //
        // The collector reproduces GetValue() from the record's own fields instead — its memo when it
        // is clean, its base value and modifier sets when it is dirty — which asks the game nothing
        // and still answers what GetValue() would have.
        var record = new ValueModifierRecord(250d);
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        var report = collector.Collect();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingIgnoresACacheTheGameHasNotFilled()
    {
        // The cycle-1 defect, at the level it was introduced. calculatedValue is [NonSerialized], so
        // a record comes back from a save at zero, and one that is dirty with it is one the game will
        // recompute the moment anything asks. Publishing that zero is what priced 180 structures at
        // nothing.
        var record = new ValueModifierRecord(250d).WithMemo(0d).Dirty();
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    /// <summary>
    /// The other half of the same rule, and the one that cost every structure price. A clean record's
    /// memo is not a stale number the game is about to replace — nothing will dirty it, so nothing
    /// will recompute it, and the memo is what the game charges from for the rest of the session.
    /// Publishing a recomputation instead would be publishing a number the game never uses.
    /// </summary>
    [Fact]
    public void CollectingReadsTheMemoTheGameWillNotRecompute()
    {
        var record = new ValueModifierRecord(250d).WithMemo(0d);
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.False(record.IsCalculationDirty);
        Assert.Equal(0d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingFoldsTheModifiersOnARecordRatherThanReadingItsValue()
    {
        var record = new ValueModifierRecord(100d).WithMemo(999d).Dirty();
        record.passiveModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.Raw, new BigDouble(50d));
        record.activeModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, new BigDouble(2d), order: 1);
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        // (100 + 50) * 2 = 300 — both sets, lowest order first, and nothing from the cache.
        Assert.Equal(300d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    /// <summary>
    /// The whole point of collecting the rate terms is that the ported chain can then run off the
    /// snapshot rather than calling <c>GetTrueRate()</c> on the Unity thread. That needs every
    /// argument, including the ones the game keeps private and the ones it only exposes as
    /// <c>HasActiveElements()</c>, which is an active-modifier count compared against zero.
    /// </summary>
    [Fact]
    public void AResourceCarriesEveryArgumentThePortedRateChainReads()
    {
        var mana = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO
        {
            Identity = mana,
            rate = new ValueModifierRecord(12d, activeCount: 2),
            rateSplash = new ValueModifierRecord(3d, activeCount: 1),
            rateMaxPercent = new ValueModifierRecord(4d),
            rateInterestPercent = new ValueModifierRecord(5d),
            rateMissingPercent = new ValueModifierRecord(6d),
            rateLifetimePercent = new ValueModifierRecord(7d),
            lossPercent = new ValueModifierRecord(8d),
            displayRate = new ValueModifierRecord(9d),
            CalcRarityValue = new BigDouble(2.5d),
            baseLoss = 0.5d,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var row));

        var inputs = row.Reading.RateInputs;
        Assert.Equal(12d, inputs.Rate.ToDouble());
        Assert.Equal(3d, inputs.RateSplash.ToDouble());
        Assert.Equal(4d, inputs.RateMaxPercent.ToDouble());
        Assert.Equal(5d, inputs.RateInterestPercent.ToDouble());
        Assert.Equal(6d, inputs.RateMissingPercent.ToDouble());
        Assert.Equal(7d, inputs.RateLifetimePercent.ToDouble());
        Assert.Equal(8d, inputs.LossPercent.ToDouble());
        Assert.Equal(9d, inputs.DisplayRate.ToDouble());
        Assert.Equal(2.5d, inputs.CalcRarityValue.ToDouble());
        Assert.Equal(0.5d, inputs.BaseLoss);

        // HasActiveElements() is this count against zero, and the chain branches on it — a term with
        // no active modifiers drops out rather than contributing zero.
        Assert.Equal(2, inputs.RateModifiers);
        Assert.Equal(1, inputs.RateSplashModifiers);
        Assert.Equal(0, inputs.RateMaxPercentModifiers);
        Assert.Equal(0, inputs.RateLifetimePercentModifiers);
    }

    /// <summary>
    /// The five grouping types shared one row while each was read for two numbers. They are not one
    /// shape: a spell type alone carries twenty-two cached records, so the shared row could only stay
    /// shared by continuing to leave them out.
    /// </summary>
    [Fact]
    public void EachGroupingTypeCarriesItsOwnState()
    {
        var spell = new SpellTypeSO { typeLevel = 6, typeXp = new BigDouble(1200d) };
        spell.power = new ValueModifierRecord(250d);
        spell.critEffectMod = new ValueModifierRecord(180d);
        SpellTypeSO.All.Add(spell);

        var resourceType = new ResourceTypeSO { level = 3, freeLevels = 1, ignoreAudit = true };
        ResourceTypeSO.All.Add(resourceType);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.SpellTypes, spell.Identity, out var type));
        Assert.Equal(6, type.TypeLevel);
        Assert.Equal(1200d, type.TypeXp.ToDouble());
        Assert.Equal(250d, type.Power.ToDouble());
        Assert.Equal(180d, type.CritEffectMod.ToDouble());

        Assert.True(WorldLookup.TryFind(world.ResourceTypes, resourceType.Identity, out var kind));
        Assert.Equal(3, kind.Level);
        Assert.Equal(1, kind.FreeLevels);
        Assert.True(kind.IgnoreAudit);
    }

    /// <summary>
    /// A harvest element owns a resource outright: it creates one with
    /// <c>ScriptableObject.CreateInstance</c>, never registers it, and marks it excluded from
    /// globals. The resource registry therefore cannot see it, and reading it through its owner is
    /// the only path — with the same member list every other resource is read with, so the two
    /// populations cannot drift apart.
    /// </summary>
    [Fact]
    public void AResourceAnElementOwnsIsReadThroughTheElement()
    {
        var element = new HarvestElementSO();
        element.Resource.Quantity = new BigDouble(42d);
        element.Resource.maxQuantity = new ValueModifierRecord(100d);
        element.Resource.quality = new ValueModifierRecord(200d);
        HarvestElementSO.All.Add(element);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(1, world.HarvestResources.Count);

        var row = world.HarvestResources[0];
        Assert.Equal(element.Identity, row.ElementId);
        Assert.Equal(42d, row.Resource.Reading.Quantity.ToDouble());

        // Derived exactly as any other resource row is — the quality scaling included.
        Assert.Equal(84d, row.Resource.TrueQuantity.ToDouble());
        Assert.True(row.Resource.IsCapped);

        // Its identity is the resource's own, not the element's: identity is claimed once across
        // every category, and the element has already claimed its own.
        Assert.NotEqual(element.Identity, row.EntityId);
        Assert.True(WorldLookup.TryFind(world.HarvestElements, element.Identity, out _));

        // And it stays out of the resource table, because the game keeps it out of the registry.
        Assert.Equal(0, world.Resources.Count);
    }

    /// <summary>
    /// The base agromancy verbs are a registry of their own, and every scalar the class stores is on
    /// the row: <c>HarvestActionSO.GetScalingInfo()</c> loads exactly <c>power</c>, <c>speed</c> and
    /// <c>costMod</c>, so a row that carried fewer would leave a bonus on this verb unreadable.
    /// </summary>
    [Fact]
    public void AHarvestActionPublishesTheThreeRecordsThatScaleIt()
    {
        var action = new HarvestActionSO
        {
            power = new ValueModifierRecord(140d),
            speed = new ValueModifierRecord(110d),
            costMod = new ValueModifierRecord(90d),
        };
        HarvestActionSO.All.Add(action);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(1, world.HarvestActions.Count);
        Assert.True(WorldLookup.TryFind(world.HarvestActions, action.Identity, out var row));
        Assert.Equal(140d, row.Power.ToDouble());
        Assert.Equal(110d, row.Speed.ToDouble());
        Assert.Equal(90d, row.CostMod.ToDouble());
    }

    [Fact]
    public void Harvest_element_and_action_controls_publish_active_counts_and_native_costs()
    {
        var resource = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = new BigDouble(20),
            maxQuantity = new ValueModifierRecord(100d),
            Visible = true,
        };
        ResourceSO.All.Add(resource);
        var element = new HarvestElementSO { masteryLevel = 3, MaximumAdditional = 7 };
        element.Resource.maxQuantity = new ValueModifierRecord(100d);
        element.usageCost.costs.Add(new ResourceTuple
        {
            resource = resource,
            Amount = new BigDouble(4),
        });
        var action = new HarvestActionSO { NextDrainPercent = new BigDouble(150) };
        action.DrainCost.costs.Add(new ResourceTuple
        {
            resource = resource,
            Amount = new BigDouble(3),
        });
        element.masteryLevel = 3;
        var prototype = new HarvestActionInstance(element, action);
        element.GetActionInstances().Add(prototype);
        HarvestElementSO.All.Add(element);
        ActiveHarvestElements.Stack(element, 2);
        ActiveHarvestActions.value.Add(new HarvestActionInstance(element, action) { instances = 1 });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        var elementControl = Assert.Single(world.HarvestElementControls.AsSpan().ToArray());
        Assert.Equal(2, elementControl.Active);
        Assert.Equal(7, elementControl.MaximumAdditional);
        Assert.True(elementControl.AddAvailable);
        var actionControl = Assert.Single(world.HarvestActionControls.AsSpan().ToArray());
        Assert.Equal(action.Identity, actionControl.ActionId);
        Assert.Equal(1, actionControl.Active);
        Assert.Equal(4, actionControl.Maximum);
        Assert.True(actionControl.AddAvailable);
        Assert.Equal(2, world.HarvestLifecycleCosts.Count);
        Assert.Contains(world.HarvestLifecycleCosts.AsSpan().ToArray(), cost =>
            cost.Kind == WorldHarvestLifecycleCostKind.ElementUsage &&
            cost.Amount == new BigDouble(4));
        Assert.Contains(world.HarvestLifecycleCosts.AsSpan().ToArray(), cost =>
            cost.Kind == WorldHarvestLifecycleCostKind.NextActionDrain &&
            cost.Amount == new BigDouble(4.5));
    }

    /// <summary>
    /// A single-valued reference travels as the identity of what it points at, never as the object.
    /// The alchemy type's selected level is the case that motivated the rule: the game holds the
    /// choice in an <c>IntVariable</c> the global registry already collects, so building the row from
    /// the type's own fields found a reference, called it unpublishable, and dropped the edge — which
    /// left the snapshot unable to say which level was selected at all.
    /// </summary>
    [Fact]
    public void AReferenceToAnotherEntityTravelsAsThatEntitysIdentity()
    {
        var chosen = new IntVariable();
        var rerolls = new IntVariable();
        var choice = Guid.NewGuid();
        var levelling = new IntVariable();

        var typeWithChoice = new AlchemyTypeSO { selectedLevel = chosen };
        var typeWithout = new AlchemyTypeSO();
        AlchemyTypeSO.All.Add(typeWithChoice);
        AlchemyTypeSO.All.Add(typeWithout);

        DiscoveryTreeSO.All.Add(new DiscoveryTreeSO
        {
            selectedChoiceId = new GuidContainer(choice),
            overrideDiscoveryRerolls = rerolls,
        });

        var experience = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = experience,
            appliedLevels = 14L,
            levelVariable = levelling,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithChoice.Identity, out var chose));
        Assert.Equal(chosen.Identity, chose.SelectedLevelId);

        // A type with no such choice reads as empty, which is the suite's own "no entity" and cannot
        // be mistaken for an edge — WorldTable refuses to admit a row keyed on it.
        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithout.Identity, out var didNot));
        Assert.Equal(Guid.Empty, didNot.SelectedLevelId);

        Assert.Equal(1, world.DiscoveryTrees.Count);
        var tree = world.DiscoveryTrees[0];
        Assert.Equal(choice, tree.SelectedChoiceId);
        Assert.Equal(rerolls.Identity, tree.OverrideRerollsId);
        Assert.Equal(Guid.Empty, tree.OverrideChoicesId);

        Assert.True(WorldLookup.TryFind(world.Resources, experience, out var resource));
        Assert.Equal(14L, resource.Reading.AppliedLevels);
        Assert.Equal(levelling.Identity, resource.Reading.LevelVariableId);
    }

    [Fact]
    public void APinnedResourceThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        var pinned = (ResourceListVariable)IdScriptableObject.RuntimeLookup[KnownEntities.ResourceBarCore.Uuid];
        pinned.value.Add(new ResourceSO { GetGuidFailure = new NullReferenceException("pinned") });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }
}
