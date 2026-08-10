using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins what the Scribe relationship reader publishes out of a multi-row world: which rows, in which
/// order, carrying which values, and what it does instead when the game does not hold up its end.
/// </summary>
/// <remarks>
/// The capture-port tests seed an empty Scribe world, which proves the reader binds and walks but
/// leaves every published value unpinned. How the reader spells a member read — reflective
/// invocation or a compiled accessor — is an implementation choice underneath these assertions, and
/// pinning values and order rather than counts is what makes a change of mechanism reproduce the
/// whole publication to pass.
/// </remarks>
public sealed class WorldScribeRelationReaderTests : IDisposable
{
    private static readonly (Guid Scroll, Guid Enchantment)[] ScrollRoles =
    {
        (KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
        (KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
        (KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
        (KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
        (KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
        (KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
        (KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
        (KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
    };

    private static readonly Guid FirstRecipeId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondRecipeId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FirstOutputId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondOutputId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FirstStructureId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SecondStructureId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TableEnchantmentId = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid OtherEnchantmentId = new("88888888-8888-8888-8888-888888888888");

    public WorldScribeRelationReaderTests() => Clear();

    public void Dispose() => Clear();

    [Fact]
    public void CollectsEveryScribeRelationInNativeOrderWithItsValues()
    {
        Seed();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(0, report.Skipped);

        // 2 recipes + (2 queues + 3 work rows) + 3 enchantments + 8 roles x (1 target + 1 evidence).
        Assert.Equal(26, report.Sampled);

        Assert.Equal(2, frame.ScribeRecipes.Count);
        AssertRecipe(frame.ScribeRecipes[0], FirstRecipeId, FirstOutputId, true, false);
        AssertRecipe(frame.ScribeRecipes[1], SecondRecipeId, SecondOutputId, false, true);
        Assert.Equal(KnownEntities.ScribeCrafting.Uuid, frame.ScribeRecipes[0].RecipeTypeId);
        Assert.Equal(KnownEntities.ScribeCrafting.Uuid, frame.ScribeRecipes[1].RecipeTypeId);

        Assert.Equal(2, frame.ScribeQueues.Count);
        Assert.Equal(KnownEntities.ActiveScribeInstances.Uuid, frame.ScribeQueues[0].QueueId);
        Assert.False(frame.ScribeQueues[0].IsAutomatic);
        Assert.Equal(2, frame.ScribeQueues[0].Used);
        Assert.Equal(5, frame.ScribeQueues[0].Maximum);
        Assert.Equal(KnownEntities.AutoScribeInstances.Uuid, frame.ScribeQueues[1].QueueId);
        Assert.True(frame.ScribeQueues[1].IsAutomatic);
        Assert.Equal(1, frame.ScribeQueues[1].Used);
        Assert.Equal(9, frame.ScribeQueues[1].Maximum);

        Assert.Equal(3, frame.ScribeWork.Count);
        AssertWork(
            frame.ScribeWork[0],
            KnownEntities.ActiveScribeInstances.Uuid,
            FirstRecipeId,
            level: 4,
            isAutomatic: false,
            isExpired: false);
        AssertWork(
            frame.ScribeWork[1],
            KnownEntities.ActiveScribeInstances.Uuid,
            SecondRecipeId,
            level: 7,
            isAutomatic: false,
            isExpired: true);
        AssertWork(
            frame.ScribeWork[2],
            KnownEntities.AutoScribeInstances.Uuid,
            FirstRecipeId,
            level: 2,
            isAutomatic: true,
            isExpired: false);

        Assert.Equal(3, frame.StructureEnchantments.Count);
        AssertEnchantment(frame.StructureEnchantments[0], FirstStructureId, TableEnchantmentId, 6);
        AssertEnchantment(frame.StructureEnchantments[1], FirstStructureId, OtherEnchantmentId, 11);
        AssertEnchantment(frame.StructureEnchantments[2], SecondStructureId, TableEnchantmentId, 2);

        // One candidate per role, published in the reader's own role order rather than in whatever
        // order the identity registry happens to hold the scrolls.
        Assert.Equal(ScrollRoles.Length, frame.ScrollTargets.Count);
        Assert.Equal(ScrollRoles.Length, frame.ScrollTargetEvidence.Count);
        for (var index = 0; index < ScrollRoles.Length; index++)
        {
            var (scroll, enchantment) = ScrollRoles[index];
            Assert.Equal(scroll, frame.ScrollTargets[index].ConsumableId);
            Assert.Equal(enchantment, frame.ScrollTargets[index].EnchantmentId);
            Assert.Equal(SecondStructureId, frame.ScrollTargets[index].StructureId);
            Assert.Equal(scroll, frame.ScrollTargetEvidence[index].ConsumableId);
            Assert.Equal(enchantment, frame.ScrollTargetEvidence[index].EnchantmentId);
            Assert.Equal(1, frame.ScrollTargetEvidence[index].CandidateCount);
        }
    }

    [Fact]
    public void RereadingTheSameWorldRepublishesTheSameRows()
    {
        Seed();
        var reader = Reader();
        var first = new GameWorldCycleFrame { CollectedAtEpoch = 3 };
        var second = new GameWorldCycleFrame { CollectedAtEpoch = 4 };

        var firstReport = reader.Collect(new HashSet<Guid>(), first);
        var secondReport = reader.Collect(new HashSet<Guid>(), second);

        Assert.Equal(firstReport.Sampled, secondReport.Sampled);
        Assert.Equal(first.StructureEnchantments.Count, second.StructureEnchantments.Count);
        for (var index = 0; index < first.StructureEnchantments.Count; index++)
        {
            Assert.Equal(
                first.StructureEnchantments[index].StructureId,
                second.StructureEnchantments[index].StructureId);
            Assert.Equal(
                first.StructureEnchantments[index].EnchantmentId,
                second.StructureEnchantments[index].EnchantmentId);
            Assert.Equal(
                first.StructureEnchantments[index].Level,
                second.StructureEnchantments[index].Level);
        }
    }

    [Fact]
    public void CollectingTwiceIntoOneFrameLeavesOnlyTheSecondReadsRows()
    {
        Seed();
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        reader.Collect(new HashSet<Guid>(), frame);
        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.Equal(26, report.Sampled);
        Assert.Equal(2, frame.ScribeRecipes.Count);
        Assert.Equal(3, frame.StructureEnchantments.Count);
        Assert.Equal(ScrollRoles.Length, frame.ScrollTargets.Count);
    }

    [Fact]
    public void AMemberThatCannotBindLeavesTheCategoryUnavailableAndNamesIt()
    {
        Seed();
        var reader = new WorldScribeRelationReader(
            name => name == "EnchantmentInstance" ? null : Resolve(name));
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.False(reader.IsAvailable);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains("EnchantmentInstance", report.FirstFailure, StringComparison.Ordinal);
        Assert.Equal(0, frame.ScribeRecipes.Count);
        Assert.Equal(0, frame.StructureEnchantments.Count);
    }

    [Fact]
    public void ARecipeWithTwoCraftingTypesDegradesTheCategoryRatherThanPublishingOne()
    {
        Seed();
        Registered<global::CraftingRecipeListVariable>(KnownEntities.ScribeCraftingRecipes.Uuid)
            .value[0]
            .craftingTypes.Add(new global::CraftingRecipeTypeSO());
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains("recipe types", report.FirstFailure, StringComparison.Ordinal);
        Assert.Equal(0, frame.ScribeRecipes.Count);
    }

    [Fact]
    public void AQueueThatContradictsItsAutomationRoleDegradesTheCategory()
    {
        Seed();
        Registered<global::CraftingInstanceListVariable>(KnownEntities.AutoScribeInstances.Uuid)
            .isAutoList = false;
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains(
            "contradicted its native automation role",
            report.FirstFailure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AScrollWhoseEnchantEffectMovedDegradesTheCategory()
    {
        Seed();
        var scroll = Registered<global::ConsumableSO>(KnownEntities.ScrollEcho.Uuid);
        ((global::EnchantmentSO.EnchantItemScript)scroll.onUseEffects[0].effectScripts[1])
            .enchantment = Enchantment(OtherEnchantmentId);
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 3 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains("enchant effects", report.FirstFailure, StringComparison.Ordinal);
    }

    private static void AssertRecipe(
        in WorldScribeRecipe row,
        Guid recipeId,
        Guid outputId,
        bool visible,
        bool usesQuantityAsLevel)
    {
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(recipeId, row.EntityId);
        Assert.Equal(outputId, row.OutputConsumableId);
        Assert.Equal(visible, row.Visible);
        Assert.Equal(usesQuantityAsLevel, row.UsesQuantityAsLevel);
    }

    private static void AssertWork(
        in WorldScribeWork row,
        Guid queueId,
        Guid recipeId,
        int level,
        bool isAutomatic,
        bool isExpired)
    {
        Assert.Equal(queueId, row.QueueId);
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(level, row.Level);
        Assert.Equal(isAutomatic, row.IsAutomatic);
        Assert.Equal(isExpired, row.IsExpired);
    }

    private static void AssertEnchantment(
        in WorldStructureEnchantment row,
        Guid structureId,
        Guid enchantmentId,
        int level)
    {
        Assert.Equal(structureId, row.StructureId);
        Assert.Equal(enchantmentId, row.EnchantmentId);
        Assert.Equal(level, row.Level);
    }

    private static WorldScribeRelationReader Reader() => new(Resolve);

    private static Type? Resolve(string name) =>
        typeof(global::CraftingRecipeSO).Assembly.GetType(name, throwOnError: false);

    private static T Registered<T>(Guid id) where T : global::IdScriptableObject =>
        (T)global::IdScriptableObject.RuntimeLookup[id];

    private static void Clear()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::StructureSO.All.Clear();
        global::CraftingRecipeSO.All.Clear();
        global::UnityEngine.Resources.Objects.Clear();
    }

    private static void Seed()
    {
        var recipeType = Register(
            new global::CraftingRecipeTypeSO { maxStartingLevel = 1 },
            KnownEntities.ScribeCrafting.Uuid);

        var recipes = Register(
            new global::CraftingRecipeListVariable(),
            KnownEntities.ScribeCraftingRecipes.Uuid);
        var first = Recipe(FirstRecipeId, FirstOutputId, recipeType, visible: true);
        var second = Recipe(SecondRecipeId, SecondOutputId, recipeType, visible: false);
        second.useQuantityAsLevel = true;
        recipes.value.Add(first);
        recipes.value.Add(second);

        var active = Register(
            new global::CraftingInstanceListVariable { isAutoList = false, Maximum = 5 },
            KnownEntities.ActiveScribeInstances.Uuid);
        active.value.Add(Work(first, quantity: 4, automatic: false, expired: false));
        active.value.Add(Work(second, quantity: 7, automatic: false, expired: true));

        // An empty queue position is a null slot. The reader counts it as unused rather than as a
        // row, and seeding one keeps the used-count assertion honest about that.
        active.value.Add(null!);

        var automatic = Register(
            new global::CraftingInstanceListVariable { isAutoList = true, Maximum = 9 },
            KnownEntities.AutoScribeInstances.Uuid);
        automatic.value.Add(Work(first, quantity: 2, automatic: true, expired: false));

        var firstStructure = Structure(FirstStructureId);
        firstStructure.enchantTable.enchantments.Add(Enchant(TableEnchantmentId, level: 6));
        firstStructure.enchantTable.enchantments.Add(Enchant(OtherEnchantmentId, level: 11));
        var secondStructure = Structure(SecondStructureId);
        secondStructure.enchantTable.enchantments.Add(Enchant(TableEnchantmentId, level: 2));
        global::StructureSO.All.Add(firstStructure);
        global::StructureSO.All.Add(secondStructure);

        foreach (var (scroll, enchantment) in ScrollRoles)
        {
            Register(Scroll(enchantment, secondStructure), scroll);
        }
    }

    private static global::CraftingRecipeSO Recipe(
        Guid id,
        Guid outputId,
        global::CraftingRecipeTypeSO recipeType,
        bool visible)
    {
        var output = new global::ConsumableSO();
        output.SetGuid(outputId);
        var block = new global::InstantEffectBlock();
        block.effectScripts.Add(
            new global::ConsumableSO.ConsumableGainEffect { consumable = output });
        var recipe = new global::CraftingRecipeSO { uuid = id, visible = visible };
        recipe.craftingTypes.Add(recipeType);
        recipe.completeEffects.Add(block);
        return recipe;
    }

    private static global::CraftingInstance Work(
        global::CraftingRecipeSO recipe,
        int quantity,
        bool automatic,
        bool expired) =>
        new(recipe, new BigDouble(quantity, 0))
        {
            Automatic = automatic,
            Expired = expired,
        };

    private static global::StructureSO Structure(Guid id)
    {
        var structure = new global::StructureSO();
        structure.SetGuid(id);
        return structure;
    }

    private static global::EnchantmentInstance Enchant(Guid enchantmentId, int level) =>
        new() { reference = Enchantment(enchantmentId), Level = level };

    private static global::EnchantmentSO Enchantment(Guid id)
    {
        var enchantment = new global::EnchantmentSO();
        enchantment.SetGuid(id);
        return enchantment;
    }

    private static global::ConsumableSO Scroll(Guid enchantmentId, global::StructureSO candidate)
    {
        var targeting = new global::Targeting.TargetStructure();
        targeting.Candidates.Add(candidate);
        var block = new global::InstantEffectBlock();
        block.effectScripts.Add(new global::RequestTargetEffectScript
        {
            targetOptions = new global::Targeting.TargetSelectOptions { Targeting = targeting },
        });
        block.effectScripts.Add(new global::EnchantmentSO.EnchantItemScript
        {
            enchantment = Enchantment(enchantmentId),
        });
        var scroll = new global::ConsumableSO();
        scroll.onUseEffects.Add(block);
        return scroll;
    }

    private static T Register<T>(T entity, Guid id) where T : global::IdScriptableObject
    {
        entity.SetGuid(id);
        global::IdScriptableObject.RuntimeLookup[id] = entity;
        return entity;
    }
}
