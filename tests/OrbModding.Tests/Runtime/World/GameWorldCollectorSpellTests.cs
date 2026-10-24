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
        var resource = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = new BigDouble(20),
            Visible = true,
        };
        ResourceSO.All.Add(resource);
        ResourceCostList Cost(int amount) =>
            new ResourceCostList()
                .With(resource, new BigDouble(amount));

        var equipment = new EquipmentTypeSO
        {
            level = 3,
            freeLevels = 2,
            LevelCost = Cost(5),
            BonusLevelCost = Cost(7),
        };
        var glyph = new GlyphSO
        {
            level = 4,
            freeLevels = 1,
            discovered = false,
            NativeAvailable = true,
            LevelCost = Cost(6),
            BonusLevelCost = Cost(8),
        };
        var resourceType = new ResourceTypeSO
        {
            level = 5,
            freeLevels = 3,
            LevelCost = Cost(9),
            BonusLevelCost = Cost(10),
        };
        var timeRune = new TimeRuneSO
        {
            level = 6,
            masteryXp = new BigDouble(1.96),
            LevelCost = Cost(11),
        };
        timeRune.MasteryBar.CachedRequiredXp = new BigDouble(10);
        EquipmentTypeSO.All.Add(equipment);
        GlyphSO.All.Add(glyph);
        ResourceTypeSO.All.Add(resourceType);
        TimeRuneSO.All.Add(timeRune);

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
        SpellRecipeSO.All.Add(new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = ready,
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(8d),
            masteryXpContainer = new ExperienceContainer { CachedRequiredXp = new BigDouble(8d) },
        });
        SpellRecipeSO.All.Add(new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = banking,
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(7d),
            masteryXpContainer = new ExperienceContainer { CachedRequiredXp = new BigDouble(8d) },
            levelCost = new ResourceCostList { affordable = false },
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
        var spell = new SpellRecipeSO
        {
            Identity = Guid.NewGuid(),
            discovered = true,
            masteryLevel = 4,
            masteryExperience = new BigDouble(8d),
            masteryXpContainer = new ExperienceContainer { CachedRequiredXp = new BigDouble(8d) },
            baseLevelingCost = new ResourceCostList(),
        };
        SpellRecipeSO.All.Add(spell);

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
        SpellRecipeSO.All.Add(new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = unmet, penaltyUsageCost = 6, NativeUsageRequirementsMet = false,
        });
        SpellRecipeSO.All.Add(new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost(), Identity = met, penaltyUsageCost = 6 });

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
        var first = new GlyphSO { Identity = Guid.NewGuid(), level = 7, discovered = true };
        var second = new GlyphSO { Identity = Guid.NewGuid(), level = 3, discovered = true };
        var discoveryResource = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = new BigDouble(9d, 6),
        };
        var recipe = new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = Guid.NewGuid(),
            discovered = false,
            coreRecipe = { first, second },
            baseDiscoveryCost = new ResourceCostList
            {
                affordable = true,
                costs =
                {
                    new ResourceTuple
                    {
                        resource = discoveryResource,
                        Amount = new BigDouble(4.4d, 3),
                    },
                },
            },
        };
        GlyphSO.All.Add(first);
        GlyphSO.All.Add(second);
        SpellRecipeSO.All.Add(recipe);

        var manager = new SpellManager();
        manager.activeSpells.Maximum = 3;
        manager.activeSpells.value.Add(new Spell());
        SpellManager.instance = manager;

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
        var first = new SpellRecipeSO { baseLevelingCost = new ResourceCostList().With(firstResource, 3) };
        var second = new SpellRecipeSO { baseLevelingCost = new ResourceCostList().With(secondResource, 7) };
        SpellRecipeSO.All.Add(new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost(), Identity = Guid.Empty });
        SpellRecipeSO.All.Add(first);
        SpellRecipeSO.All.Add(second);

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
        SpellRecipeSO.All.Add(new SpellRecipeSO { baseLevelingCost = null! });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void ASpellGraphRowThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        SpellRecipeSO.All.Add(new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost(), maxChannel = null! });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    [Fact]
    public void ASpellIsAnAttackOnlyWhenEveryScriptItCastsIsACombatEffect()
    {
        static InstantEffectBlock Block(params IInstantEffectScript[] scripts)
        {
            var block = new InstantEffectBlock();
            block.effectScripts.AddRange(scripts);
            return block;
        }

        var firebolt = new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost(), Identity = Guid.NewGuid(), onCastEffects = { Block(new Combat.CombatEffect()) } };
        var mixed = new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = Guid.NewGuid(),
            onCastEffects = { Block(new Combat.CombatEffect()), Block(new RequestTargetEffectScript()) },
        };
        var ToggledSpell = new SpellRecipeSO
        { baseLevelingCost = AuthoredSpellLevelCost(),
            Identity = Guid.NewGuid(),
            onCastEffects = { Block(new Combat.CombatEffect()) },
            toggledEffects =
            {
                new PersistentEffectBlock
                {
                    effectScripts = { new NumberVariable.PersistentEffect { numberVariable = new NumberVariable() } },
                },
            },
        };
        var silent = new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost(), Identity = Guid.NewGuid() };
        SpellRecipeSO.All.AddRange(new[] { firebolt, mixed, ToggledSpell, silent });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        bool CombatOnly(SpellRecipeSO recipe)
        {
            Assert.True(WorldSpellGraphLookup.TryFindAuthoring(world.SpellRecipeAuthoring, recipe.Identity, out var row));
            return row.CombatOnly;
        }
        Assert.True(CombatOnly(firebolt));
        Assert.False(CombatOnly(mixed));
        Assert.False(CombatOnly(ToggledSpell));
        Assert.False(CombatOnly(silent));
    }

    [Fact]
    public void PendingTargetRequestPublishesEveryEligibleStructureInNativeOrder()
    {
        var owner = new StructureSO { Identity = Guid.NewGuid() };
        var first = new StructureSO { Identity = Guid.NewGuid(), Level = 3 };
        var second = new StructureSO { Identity = Guid.NewGuid(), Level = 7 };
        StructureSO.All.AddRange(new[] { owner, first, second });
        TargetingManager.Current = new TargetingManager.TargetLink(owner, first, second);

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
        var owner = new StructureSO { Identity = Guid.NewGuid() };
        var candidate = new StructureSO { GetGuidFailure = new NullReferenceException("candidate") };
        TargetingManager.Current = new TargetingManager.TargetLink(owner, candidate);

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
