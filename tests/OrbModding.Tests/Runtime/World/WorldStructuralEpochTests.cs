using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Authored content is read once per lifecycle epoch, not once per cycle.
/// </summary>
/// <remarks>
/// <para>
/// What a plot's author wrote and what completing an action applies cannot change while the game is
/// running, so reading them four times a second is four hundred walks a minute for an answer that
/// could not have moved. The epoch is exactly the fact that says when it can have.
/// </para>
/// <para>
/// Every case here drives the real collector across two passes, and proves a pass was skipped by
/// emptying the registries between them: rows that survive an empty registry are rows nobody re-read.
/// The played-state categories fed by the same registries are the control — they follow the emptying,
/// which is what makes the surviving rows a skip rather than a stuck collector.
/// </para>
/// </remarks>
public sealed class WorldStructuralEpochTests : IDisposable
{
    private static readonly Guid PlotId = new("3f1c1b7e-0000-4000-8000-00000000ce01");
    private static readonly Guid ActionId = new("3f1c1b7e-0000-4000-8000-00000000ce02");

    public WorldStructuralEpochTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void AuthoredContentIsReadOnTheFirstPass()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The point of the whole thing: within one run of the game the native walks do not happen again.
    /// </summary>
    [Fact]
    public void AuthoredContentIsNotReadAgainWithinOneEpoch()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);

        ClearRegistries();
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The control for the case above. The same registries feed categories that are not authored
    /// content, and those do follow the emptying — so the surviving rows are a skipped read rather
    /// than a collector that stopped reading anything.
    /// </summary>
    [Fact]
    public void PlayedStateIsStillReadOnASkippedPass()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);
        Assert.Equal(1, GameWorldFrameDeriver.Build(frame).PlotNodes.Count);

        ClearRegistries();
        collector.Collect(frame);

        Assert.Equal(0, GameWorldFrameDeriver.Build(frame).PlotNodes.Count);
    }

    /// <summary>
    /// A lifecycle boundary is the one thing that can change authored content, and it is the one thing
    /// that makes the collector look again.
    /// </summary>
    [Fact]
    public void AuthoredContentIsReadAgainWhenTheEpochMoves()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);

        ClearRegistries();
        frame.CollectedAtEpoch = 6;
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(0, world.PlotAuthoring.Count);
        Assert.Equal(0, world.PlotPhaseDescriptors.Count);
        Assert.Equal(0, world.EffectBlocks.Count);
        Assert.Equal(0, world.EntityRequirements.Count);
    }

    /// <summary>
    /// A skip is decided on the frame as well as the epoch. Two frames under one epoch are two sets of
    /// buffers and only one of them was filled; skipping the other on the epoch alone would hand it an
    /// empty table and call it collected.
    /// </summary>
    [Fact]
    public void AuthoredContentIsReadIntoEveryFrameThatAsksForIt()
    {
        Author();
        var collector = new GameWorldCollector();
        collector.Collect(new GameWorldCycleFrame { CollectedAtEpoch = 5 });

        var second = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(second);
        var world = GameWorldFrameDeriver.Build(second);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// A frame re-read within one epoch is reset rather than appended to, exactly as it is on the pass
    /// that first filled it. Skipping means skipping the read, and never means letting two reads
    /// stack.
    /// </summary>
    [Fact]
    public void AFrameReadTwiceHoldsOneCopyOfTheAuthoredContent()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        frame.CollectedAtEpoch = 6;
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The report says what the buffer holds, not what this pass did. A skipped category reporting
    /// nothing would read to an operator as content the build could not reach.
    /// </summary>
    [Fact]
    public void ASkippedPassStillReportsTheAuthoredContentItKept()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        var first = collector.Collect(frame);

        ClearRegistries();
        var second = collector.Collect(frame);

        Assert.Equal(first.For("plot authoring").Sampled, second.For("plot authoring").Sampled);
        Assert.Equal(first.For("effect blocks").Sampled, second.For("effect blocks").Sampled);
        Assert.Equal(
            first.For("entity requirements").Sampled, second.For("entity requirements").Sampled);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("plot authoring").Outcome);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("effect blocks").Outcome);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("entity requirements").Outcome);
    }

    [Fact]
    public void PrerequisiteLinkAuthoredGraphStaysStructuralWhileItsNativeGatesRefresh()
    {
        var link = new global::PrerequisiteLinkSO();
        var tier = new global::PrerequisiteLinkSO.LinkDefinition();
        link.linkTiers.Add(tier);
        global::PrerequisiteLinkSO.All.Add(link);
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.True(WorldPrerequisiteLinkTierLookup.TryFind(
            first.PrerequisiteLinkTiers, link.GetGuid(), 0, out var firstTier));
        Assert.True(firstTier.ActiveEnabled);
        Assert.Equal(1, first.EntityRequirements.Count);

        tier.isActiveEnabled = false;
        global::GameManager.currentFrame = 1;
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, second.EntityRequirements.Count);
        Assert.True(WorldPrerequisiteLinkTierLookup.TryFind(
            second.PrerequisiteLinkTiers, link.GetGuid(), 0, out var secondTier));
        Assert.False(secondTier.ActiveEnabled);
        Assert.Equal(1, secondTier.CollectedFrame);
    }

    /// <summary>
    /// The requirement graph is read once an epoch, and no pass asks the game for a verdict.
    /// </summary>
    /// <remarks>
    /// The authored list is deliberately destroyed between the two passes: a second traversal would
    /// publish the emptied one. The verdict half used to refresh here and no longer exists — the
    /// game's own answer is asked for one entity at a time, by request.
    /// </remarks>
    [Fact]
    public void RequirementGraphStaysStructuralAndNoPassAsksTheGame()
    {
        Author();
        var upgrade = global::UpgradeSO.All[0];
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(1, first.EntityRequirements.Count);

        upgrade.prerequisitesPerLevel.prerequisites.Clear();
        upgrade.level = 3;
        upgrade.queuedLevels = 2;
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, second.EntityRequirements.Count);
        Assert.Equal(0, upgrade.prerequisitesPerLevel.ParameterizedCheckCalls);
        Assert.Equal(0, upgrade.prerequisitesPerLevel.CheckCalls);
    }

    [Fact]
    public void CraftingGraphStaysStructuralWhileEveryLiveRecipeVerdictRefreshes()
    {
        var type = new global::CraftingRecipeTypeSO { isLevelType = true };
        var input = new global::ResourceSO { quantity = new BigDouble(100d) };
        var output = new global::ResourceSO { quantity = new BigDouble(5d) };
        var consumable = new global::ConsumableSO();
        var engagement = new global::PersistentEffectBlock
        {
            NecessaryDrainRatio = BigDouble.One,
        };
        var completion = new global::InstantEffectBlock();
        completion.effectScripts.Add(new global::ConsumableSO.ConsumableGainEffect
        {
            consumable = consumable,
        });
        var recipe = new global::CraftingRecipeSO
        {
            visible = true,
            BuyAllowed = true,
            StartingQuantity = BigDouble.One,
            useQuantityAsLevel = true,
            timeToComplete = 4d,
        };
        recipe.craftingTypes.Add(type);
        recipe.recipeCost.costs.Add(new global::ResourceTuple(input, new BigDouble(3d)));
        recipe.generatedResources.costs.Add(new global::ResourceTuple(output, new BigDouble(2d)));
        recipe.engagementEffects.Add(engagement);
        recipe.completeEffects.Add(completion);
        global::CraftingRecipeTypeSO.All.Add(type);
        global::ResourceSO.All.Add(input);
        global::ResourceSO.All.Add(output);
        global::ConsumableSO.All.Add(consumable);
        global::CraftingRecipeSO.All.Add(recipe);

        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.True(WorldLookup.TryFind(first.CraftingRecipes, recipe.GetGuid(), out var firstRow));
        Assert.True(firstRow.Reading.Visible);
        Assert.True(firstRow.Reading.CanBuyAtStartingQuantity);
        Assert.True(firstRow.Reading.OutputWithinCapacity);
        Assert.Equal(1, firstRow.Types.Count);
        Assert.Equal(2, firstRow.Resources.Count);
        Assert.Equal(1, firstRow.ConsumableOutputs.Count);
        Assert.Equal(1, firstRow.DrainBlocks.Count);
        Assert.Equal(2, recipe.VisibilityCalls);
        Assert.Equal(2, recipe.StartingQuantityCalls);
        Assert.Equal(1, recipe.CanBuyCalls);

        // Destroy every authored edge and independently move every live verdict. A second ordinary
        // capture must retain the former and observe the latter without traversing the registries.
        recipe.craftingTypes.Clear();
        recipe.recipeCost.costs.Clear();
        recipe.generatedResources.costs.Clear();
        recipe.completeEffects.Clear();
        recipe.engagementEffects.Clear();
        recipe.visible = false;
        recipe.BuyAllowed = false;
        recipe.generatedResources.WithinCapacity = false;
        engagement.NecessaryDrainRatio = new BigDouble(0.5d);
        global::CraftingRecipeSO.All.Clear();
        global::CraftingRecipeTypeSO.All.Clear();
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);

        Assert.True(WorldLookup.TryFind(second.CraftingRecipes, recipe.GetGuid(), out var secondRow));
        Assert.False(secondRow.Reading.Visible);
        Assert.False(secondRow.Reading.CanBuyAtStartingQuantity);
        Assert.False(secondRow.Reading.OutputWithinCapacity);
        Assert.Equal(1, secondRow.Types.Count);
        Assert.Equal(2, secondRow.Resources.Count);
        Assert.Equal(1, secondRow.ConsumableOutputs.Count);
        var drain = Assert.Single(secondRow.DrainBlocks.AsSpan().ToArray());
        Assert.Equal(0.5d, drain.NecessaryRatio.ToDouble());
        // The lifecycle-cached crafting graph still refreshes this recipe. The player-decision pass
        // walks the live recipe registry, which this test deliberately emptied above, so it sampled
        // the recipe only on the first capture.
        Assert.Equal(3, recipe.VisibilityCalls);
        Assert.Equal(3, recipe.StartingQuantityCalls);
        Assert.Equal(2, recipe.CanBuyCalls);
    }

    [Fact]
    public void StaticBaseCostStaysStructuralWhileCurrentAffordabilityRefreshes()
    {
        var resource = new global::ResourceSO
        {
            quantity = new BigDouble(5d),
            visible = true,
        };
        var perQuantity = new global::ValueModifierVariable
        {
            value = new ValueModifier(
                ValueModifier.ValueModifierType.Raw,
                BigDouble.Zero),
        };
        var structure = new global::StructureSO();
        structure.costPerQuantity.variable = perQuantity;
        structure.baseCost.costs.Add(
            new global::ResourceTuple(resource, new BigDouble(10d)));
        global::ResourceSO.All.Add(resource);
        global::ValueModifierVariable.All.Add(perQuantity);
        global::StructureSO.All.Add(structure);

        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            first.PurchaseCosts, structure.GetGuid(), out var firstStart, out var firstCount));
        Assert.Equal(1, firstCount);
        Assert.Equal(10d, first.PurchaseCosts[firstStart].BaseExactAmount.ToDouble());
        Assert.False(first.PurchaseCosts[firstStart].Affordable);

        structure.baseCost.costs[0] =
            new global::ResourceTuple(resource, new BigDouble(999d));
        resource.quantity = new BigDouble(100d);
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);

        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            second.PurchaseCosts, structure.GetGuid(), out var secondStart, out var secondCount));
        Assert.Equal(1, secondCount);
        Assert.Equal(10d, second.PurchaseCosts[secondStart].BaseExactAmount.ToDouble());
        Assert.Equal(100d, second.PurchaseCosts[secondStart].AvailableAmount.ToDouble());
        Assert.True(second.PurchaseCosts[secondStart].Affordable);
    }

    /// <summary>
    /// A read that failed did not read, so the gate stays open until one does.
    /// </summary>
    /// <remarks>
    /// The gate's claim is that the structural rows in this frame describe this run of the game.
    /// Closing it over a bound reader that threw made that claim about an emptied buffer, and since
    /// the epoch only moves at a lifecycle boundary, recovery took a prestige — one transient native
    /// exception in the owning-view reader disabled every purchase for the rest of the run.
    /// </remarks>
    [Fact]
    public void AFailedStructuralReadIsTakenAgainOnTheNextPass()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        var registry = global::PlotNodeSO.All;
        try
        {
            global::PlotNodeSO.All = null!;
            Assert.Equal(
                WorldCategoryOutcome.Unavailable,
                collector.Collect(frame).For("plot authoring").Outcome);
        }
        finally
        {
            global::PlotNodeSO.All = registry;
        }

        collector.Collect(frame);

        Assert.Equal(1, GameWorldFrameDeriver.Build(frame).PlotAuthoring.Count);
    }

    /// <summary>
    /// A category this build never had is not a failed read. Holding the gate open for one would
    /// re-walk every other structural category four times a second for an answer that cannot arrive.
    /// </summary>
    [Fact]
    public void AStructuralReaderThatNeverBoundStillClosesTheGate()
    {
        Author();
        var collector = new GameWorldCollector(
            static name => name == "SpellRecipeSO" ? null : WorldNativeTypes.Resolve(name));
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        Assert.Equal(
            WorldCategoryOutcome.Unavailable,
            collector.Collect(frame).For("spell authored graph").Outcome);

        ClearRegistries();
        collector.Collect(frame);

        Assert.Equal(1, GameWorldFrameDeriver.Build(frame).PlotAuthoring.Count);
    }

    /// <summary>
    /// One plot authoring the three phases the game ships, and one action whose completion applies one
    /// block. Enough shape for both structural readers to produce rows; the terms themselves are the
    /// safety audit's business, not this file's.
    /// </summary>
    private static void Author()
    {
        var upgrade = new global::UpgradeSO { maxLevel = 1 };
        var research = new global::ResearchSO();
        upgrade.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = research,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        });
        global::UpgradeSO.All.Add(upgrade);
        global::ResearchSO.All.Add(research);

        var action = new global::PlotNodeActionSO();
        action.SetGuid(ActionId);
        action.completeEffects.Add(new global::InstantEffectBlock());

        var plot = new global::PlotNodeSO();
        plot.SetGuid(PlotId);
        plot.phaseInfos = new List<global::PlotNodePhaseInfo>
        {
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Idle },
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Growing },
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Resting },
        };

        global::PlotNodeSO.All.Add(plot);
        global::PlotNodeActionSO.All.Add(action);
    }

    private static void ClearRegistries()
    {
        global::PlotNodeSO.All.Clear();
        global::PlotNodeActionSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::GameManager.currentFrame = 0;
        global::ResearchSO.All.Clear();
        global::ResourceSO.All.Clear();
        global::ValueModifierVariable.All.Clear();
        global::CraftingRecipeTypeSO.All.Clear();
        global::CraftingRecipeSO.All.Clear();
        global::ConsumableSO.All.Clear();
    }
}
