using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorSpellTests : GameWorldCollectorTestBase
{
    [Fact]
    public void UnifiedLevelRowsPublishEachConcreteTypesLiveNextDecision()
    {
        var resource = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = new BigDouble(20),
            Visible = true,
        };
        FakeResource.All.Add(resource);
        FakeCraftingResourceCostList Cost(int amount) =>
            new FakeCraftingResourceCostList { affordabilityUsesResourceAmounts = true }
                .With(resource, new BigDouble(amount));

        var equipment = new FakeEquipmentType
        {
            level = 3,
            freeLevels = 2,
            LevelCost = Cost(5),
            BonusLevelCost = Cost(7),
        };
        var glyph = new FakeGlyph
        {
            level = 4,
            freeLevels = 1,
            discovered = false,
            NativeAvailable = true,
            LevelCost = Cost(6),
            BonusLevelCost = Cost(8),
        };
        var resourceType = new FakeResourceType
        {
            level = 5,
            freeLevels = 3,
            LevelCost = Cost(9),
            BonusLevelCost = Cost(10),
        };
        var timeRune = new FakeTimeRune
        {
            level = 6,
            masteryXp = new BigDouble(1.96),
            LevelCost = Cost(11),
        };
        timeRune.xpContainer.cachedRequiredXp = new BigDouble(10);
        FakeEquipmentType.All.Add(equipment);
        FakeGlyph.All.Add(glyph);
        FakeResourceType.All.Add(resourceType);
        FakeTimeRune.All.Add(timeRune);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.EquipmentTypes, equipment.Identity, out var equipmentRow));
        AssertLevelDecision(equipmentRow.LevelDecision, 5, 2, 5, 7);
        Assert.True(WorldLookup.TryFind(world.AugmentGlyphs, glyph.Identity, out var glyphRow));
        Assert.True(glyphRow.Learned);
        Assert.False(glyphRow.Discovered);
        AssertLevelDecision(glyphRow.LevelDecision, 5, 1, 6, 8);
        Assert.True(WorldLookup.TryFind(world.ResourceTypes, resourceType.Identity, out var resourceTypeRow));
        AssertLevelDecision(resourceTypeRow.LevelDecision, 8, 3, 9, 10);
        Assert.True(WorldLookup.TryFind(world.TimeRunes, timeRune.Identity, out var timeRuneRow));
        AssertLevelDecision(timeRuneRow.LevelDecision, 6, 0, 11, null);
        Assert.Equal(1.96, timeRuneRow.MasteryXp.ToDouble(), 6);
        Assert.Equal(10d, timeRuneRow.RequiredMasteryXp.ToDouble());
    }

    [Fact]
    public void ASpellsMasteryReadinessIsDerivedFromItsPublishedThreshold()
    {
        // Readiness is cheap accessor math over the published experience and its container's cached
        // threshold. Capture must not ask the native predicate for the answer.
        var ready = Guid.NewGuid();
        var banking = Guid.NewGuid();
        FakeSpellRecipe.All.Add(new FakeSpellRecipe
        {
            Identity = ready,
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(8d),
            masteryXpContainer = new FakeExperienceContainer { cachedRequiredXp = new BigDouble(8d) },
        });
        FakeSpellRecipe.All.Add(new FakeSpellRecipe
        {
            Identity = banking,
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(7d),
            masteryXpContainer = new FakeExperienceContainer { cachedRequiredXp = new BigDouble(8d) },
            levelCost = new FakeSpellLevelCost { affordable = false },
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.SpellRecipes, ready, out var readyRow));
        Assert.True(readyRow.MasteryLevelReady);
        Assert.True(WorldLookup.TryFind(world.SpellRecipes, banking, out var bankingRow));
        Assert.False(bankingRow.MasteryLevelReady);

        // Same mastery level on both: readiness is its own fact, not one the level implies.
        Assert.Equal(readyRow.MasteryLevel, bankingRow.MasteryLevel);
    }

    [Fact]
    public void ASpellWithoutAnAuthoredLevelCostIsUnaffordableAndNamedInTheReport()
    {
        var spell = new FakeSpellRecipe
        {
            Identity = Guid.NewGuid(),
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(8d),
            masteryXpContainer = new FakeExperienceContainer { cachedRequiredXp = new BigDouble(8d) },
            baseLevelingCost = new FakeSpellCostList(),
        };
        FakeSpellRecipe.All.Add(spell);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(report.IsComplete);
        Assert.Contains("has no authored level cost", report.Describe());
        Assert.True(WorldLookup.TryFind(world.SpellRecipes, spell.Identity, out var row));
        Assert.True(row.MasteryLevelReady);
        Assert.False(row.MasteryLevelAffordable);
    }

    [Fact]
    public void TheTypeXpEachConfirmedMasteryLevelGrantsIsPublished()
    {
        var collector = Collector();
        collector.Collect();

        Assert.Equal(30d, collector.Build().SpellTypeXpPerMastery);
    }

    /// <summary>
    /// Whether a recipe's "use effectively" requirements hold is the game's answer, and it decides
    /// whether <c>penaltyUsageCost</c> is part of the spell's weight.
    /// </summary>
    [Fact]
    public void ASpellRecipePublishesWhetherItsUsageRequirementsHold()
    {
        var unmet = Guid.NewGuid();
        var met = Guid.NewGuid();
        FakeSpellRecipe.All.Add(new FakeSpellRecipe
        {
            Identity = unmet, penaltyUsageCost = 6, usageRequirementsMet = false,
        });
        FakeSpellRecipe.All.Add(new FakeSpellRecipe { Identity = met, penaltyUsageCost = 6 });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.SpellRecipes, unmet, out var unmetRow));
        Assert.False(unmetRow.UsageRequirementsMet);
        Assert.Equal(6, unmetRow.PenaltyUsageCost);
        Assert.True(WorldLookup.TryFind(world.SpellRecipes, met, out var metRow));
        Assert.True(metRow.UsageRequirementsMet);
    }

    [Fact]
    public void SpellDiscoveryAndLoadoutCapacityArePublishedFromTheirOwningNativeSurfaces()
    {
        var first = new FakeGlyph { Identity = Guid.NewGuid(), level = 7, discovered = true };
        var second = new FakeGlyph { Identity = Guid.NewGuid(), level = 3, discovered = true };
        var discoveryResource = new FakeSpellWorkbenchResource
        {
            Identity = Guid.NewGuid(),
            amount = new BigDouble(9d, 6),
        };
        var recipe = new FakeSpellRecipe
        {
            Identity = Guid.NewGuid(),
            discovered = false,
            coreRecipe = { first, second },
            discoveryCost = new FakeSpellWorkbenchCostList
            {
                affordable = true,
                costs =
                {
                    new FakeSpellWorkbenchCost
                    {
                        resource = discoveryResource,
                        amount = new BigDouble(4.4d, 3),
                    },
                },
            },
        };
        FakeGlyph.All.Add(first);
        FakeGlyph.All.Add(second);
        FakeSpellRecipe.All.Add(recipe);

        var manager = new FakeSpellManager();
        manager.activeSpells.maximum = 3;
        manager.activeSpells.value.Add(new FakeSpell());
        FakeSpellManager.instance = manager;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.SpellRecipes, recipe.Identity, out var recipeRow));
        Assert.Equal(new[] { first.Identity, second.Identity },
            recipeRow.CoreGlyphs.AsSpan().ToArray().Select(glyph => glyph.GlyphId));
        var discoveryCost = Assert.Single(recipeRow.DiscoveryCosts.AsSpan().ToArray());
        Assert.Equal(discoveryResource.Identity, discoveryCost.ResourceId);
        Assert.Equal(4400d, discoveryCost.Price.ToDouble());
        Assert.Equal(9e6, discoveryCost.Held.ToDouble());
        Assert.True(recipeRow.DiscoveryAffordable);

        Assert.Equal(1, world.SpellWorkbench.EquippedCount);
        Assert.Equal(3, world.SpellWorkbench.MaximumEquipped);
        Assert.True(world.SpellWorkbench.HasEmptySlot);
        Assert.Equal(1, world.SpellWorkbench.OutputLevel);
        Assert.Equal(100, world.SpellWorkbench.MaximumOutputLevel);
        Assert.Equal(1, world.SpellWorkbench.ReserveLevel);
        Assert.Equal(100, world.SpellWorkbench.MaximumReserveLevel);
    }

    /// <summary>
    /// A spell's level costs are filed under that spell, even when the registry holds an entry the
    /// spell reader skipped before it.
    /// </summary>
    [Fact]
    public void ASpellsLevelCostsAreFiledUnderThatSpellPastASkippedRegistryEntry()
    {
        var firstResource = Guid.NewGuid();
        var secondResource = Guid.NewGuid();
        var first = new FakeSpellRecipe { baseLevelingCost = new FakeSpellCostList().With(firstResource, 3) };
        var second = new FakeSpellRecipe { baseLevelingCost = new FakeSpellCostList().With(secondResource, 7) };
        FakeSpellRecipe.All.Add(new FakeSpellRecipe { Identity = Guid.Empty });
        FakeSpellRecipe.All.Add(first);
        FakeSpellRecipe.All.Add(second);

        var collector = Collector();
        var report = collector.Collect().For("spell level costs");
        var costs = collector.Build().MasteryCosts;

        Assert.Equal(2, report.Sampled);
        var filed = new List<(Guid Recipe, Guid Resource)>();
        for (var index = 0; index < costs.Count; index++)
            filed.Add((costs[index].RecipeId, costs[index].ResourceId));
        Assert.Equal(
            new[] { (first.Identity, firstResource), (second.Identity, secondResource) }
                .OrderBy(row => row.Item1),
            filed.OrderBy(row => row.Recipe));
    }

    [Fact]
    public void ASpellLevelCostThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakeSpellRecipe.All.Add(new FakeSpellRecipe { baseLevelingCost = null! });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void ASpellGraphRowThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakeSpellRecipe.All.Add(new FakeSpellRecipe { maxChannel = null! });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void ASpellIsAnAttackOnlyWhenEveryScriptItCastsIsACombatEffect()
    {
        static FakeEffectBlock Block(params object[] scripts)
        {
            var block = new FakeEffectBlock();
            block.effectScripts.AddRange(scripts);
            return block;
        }

        var firebolt = new FakeSpellRecipe { Identity = Guid.NewGuid(), onCastEffects = { Block(new FakeCombatEffect()) } };
        var mixed = new FakeSpellRecipe
        {
            Identity = Guid.NewGuid(),
            onCastEffects = { Block(new FakeCombatEffect()), Block(new FakeScribeRequestTargetEffect()) },
        };
        var toggled = new FakeSpellRecipe
        {
            Identity = Guid.NewGuid(),
            onCastEffects = { Block(new FakeCombatEffect()) },
            toggledEffects = { Block(new object()) },
        };
        var silent = new FakeSpellRecipe { Identity = Guid.NewGuid() };
        FakeSpellRecipe.All.AddRange(new[] { firebolt, mixed, toggled, silent });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        bool CombatOnly(FakeSpellRecipe recipe)
        {
            Assert.True(WorldSpellGraphLookup.TryFindAuthoring(world.SpellRecipeAuthoring, recipe.Identity, out var row));
            return row.CombatOnly;
        }
        Assert.True(CombatOnly(firebolt));
        Assert.False(CombatOnly(mixed));
        Assert.False(CombatOnly(toggled));
        Assert.False(CombatOnly(silent));
    }

    [Fact]
    public void PendingTargetRequestPublishesEveryEligibleStructureInNativeOrder()
    {
        var owner = new FakeStructure { Identity = Guid.NewGuid() };
        var first = new FakeStructure { Identity = Guid.NewGuid(), Level = 3 };
        var second = new FakeStructure { Identity = Guid.NewGuid(), Level = 7 };
        FakeStructure.All.AddRange(new[] { owner, first, second });
        FakeTargetingManager.Current = new FakeTargetingManager.TargetLink(owner, first, second);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        var request = Assert.Single(world.Targeting.AsSpan().ToArray());
        Assert.Equal(owner.GetName(), request.OwnerName);
        Assert.True(request.CancelAvailable);
        Assert.Equal(new[] { first.Identity, second.Identity },
            request.Candidates.AsSpan().ToArray().Select(candidate => candidate.StructureId));
        Assert.Equal(new[] { 0, 1 },
            request.Candidates.AsSpan().ToArray().Select(candidate => candidate.Position));
    }

    [Fact]
    public void ATargetCandidateThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        var owner = new FakeStructure { Identity = Guid.NewGuid() };
        var candidate = new FakeStructure { GetGuidFailure = new NullReferenceException("candidate") };
        FakeTargetingManager.Current = new FakeTargetingManager.TargetLink(owner, candidate);

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    private static void AssertLevelDecision(
        WorldLevelableDecision decision,
        int total,
        int bonus,
        int paidCost,
        int? bonusCost)
    {
        Assert.Equal(total, decision.TotalLevel);
        Assert.Equal(bonus, decision.BonusLevels);
        Assert.True(decision.CanPurchase);
        Assert.True(decision.PurchaseAffordable);
        Assert.Equal((double)paidCost,
            Assert.Single(decision.PaidCosts.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(bonusCost.HasValue, decision.SupportsBonus);
        if (!bonusCost.HasValue) return;
        Assert.True(decision.BonusResourcesVisible);
        Assert.True(decision.BonusAffordable);
        Assert.Equal((double)bonusCost.Value,
            Assert.Single(decision.BonusCosts.AsSpan().ToArray()).Amount.ToDouble());
    }
}
