using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins what the Concept reader publishes out of a multi-row world: which rows, in which order,
/// carrying which values, and what it does instead when the game does not hold up its end.
/// </summary>
/// <remarks>
/// The collector tests reach this reader through a whole capture and assert one instance at a time by
/// identity lookup, which proves it binds and walks but leaves emission order and most published
/// values unpinned. How the reader spells a member read — reflective invocation or a compiled
/// accessor — is an implementation choice underneath these assertions, and pinning values and order
/// rather than counts is what makes a change of mechanism reproduce the whole publication to pass.
/// </remarks>
public sealed class WorldConceptInstanceReaderTests : IDisposable
{
    private static readonly Guid FirstRecipeId = new("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondRecipeId = new("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid UnscopedRecipeId = new("a3333333-3333-3333-3333-333333333333");
    private static readonly Guid CoreTypeId = new("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCoreTypeId = new("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid ScalingId = new("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid DrainResourceId = new("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid BandwidthResourceId = new("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid CurrentResourceId = new("d3333333-3333-3333-3333-333333333333");

    public WorldConceptInstanceReaderTests() => Clear();

    public void Dispose() => Clear();

    [Fact]
    public void CollectsEveryConceptRowInNativeOrderWithItsValues()
    {
        Seed();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(0, report.Skipped);

        // Sampling counts recipes; instances ride along on the recipes they name.
        Assert.Equal(2, report.Sampled);

        // Both scoped recipes, in the order the native recipe list holds them. The slot count is the
        // whole Active Concepts list including the empty capacity slot, and only the second recipe's
        // instance has already taken every usage slot its recipe allows.
        Assert.Equal(2, frame.ConceptRecipes.Count);
        AssertRecipe(frame.ConceptRecipes[0], FirstRecipeId, CoreTypeId, canAddNow: true, slotCount: 3);
        AssertRecipe(frame.ConceptRecipes[1], SecondRecipeId, OtherCoreTypeId, canAddNow: false, slotCount: 3);

        // The empty capacity slot publishes nothing, so two rows for three list positions, in the
        // order the Active Concepts list holds them rather than in recipe order.
        Assert.Equal(2, frame.AlchemyInstances.Count);
        AssertInstance(
            frame.AlchemyInstances[0], SecondRecipeId, quantity: 4, queuedQuantity: 4,
            drainReadable: true, isDrainApplied: false, currentRatio: 0.25d, usageRatio: 0.75d);
        AssertInstance(
            frame.AlchemyInstances[1], FirstRecipeId, quantity: 1, queuedQuantity: 2,
            drainReadable: true, isDrainApplied: true, currentRatio: 0.5d, usageRatio: 0.125d);

        // Authored drains and bandwidths come out per recipe in recipe order, and the current drain
        // vectors follow behind them in instance order — one flat table, three kinds.
        Assert.Equal(5, frame.AlchemyCosts.Count);
        AssertCost(
            frame.AlchemyCosts[0], FirstRecipeId, WorldAlchemyCostKind.RecipeDrain, DrainResourceId, 12d);
        AssertCost(
            frame.AlchemyCosts[1], FirstRecipeId, WorldAlchemyCostKind.Bandwidth, BandwidthResourceId, 3d);
        AssertCost(
            frame.AlchemyCosts[2], SecondRecipeId, WorldAlchemyCostKind.RecipeDrain, DrainResourceId, 40d);
        AssertCost(
            frame.AlchemyCosts[3], SecondRecipeId, WorldAlchemyCostKind.CurrentDrain, CurrentResourceId, 9d);
        AssertCost(
            frame.AlchemyCosts[4], FirstRecipeId, WorldAlchemyCostKind.CurrentDrain, CurrentResourceId, 3d);

        // The drain basis is one row per recipe, carrying the core type's two requirement penalties by
        // value and the rarity questions the scaling's blacklist answers per term.
        Assert.Equal(2, frame.ConceptDrainBasis.Count);
        var first = frame.ConceptDrainBasis[0];
        Assert.Equal(FirstRecipeId, first.RecipeId);
        Assert.Equal(CoreTypeId, first.CoreTypeId);
        Assert.Equal(ScalingId, first.ScalingId);
        Assert.Equal(7, first.AdvancementLevel);
        Assert.Equal(GameValueModifierType.MultiStacking, first.RequirementCostPenalty.Type);
        Assert.Equal(2d, first.RequirementCostPenalty.Amount.ToDouble());
        Assert.Equal(3, first.RequirementCostPenalty.Order);
        Assert.Equal(GameValueModifierType.Reduction, first.RequirementSpeedPenalty.Type);
        Assert.Equal(5d, first.RequirementSpeedPenalty.Amount.ToDouble());
        Assert.Equal(1, first.RequirementSpeedPenalty.Order);

        // Rarity is on for this scaling, and the blacklist names the cost term only.
        Assert.False(first.CostUsesRarity);
        Assert.True(first.SpeedUsesRarity);

        var second = frame.ConceptDrainBasis[1];
        Assert.Equal(SecondRecipeId, second.RecipeId);
        Assert.Equal(OtherCoreTypeId, second.CoreTypeId);
        Assert.Equal(ScalingId, second.ScalingId);
        Assert.Equal(0, second.AdvancementLevel);
        Assert.Equal(GameValueModifierType.Raw, second.RequirementCostPenalty.Type);
        Assert.Equal(0d, second.RequirementCostPenalty.Amount.ToDouble());
        Assert.Equal(GameValueModifierType.Raw, second.RequirementSpeedPenalty.Type);
        Assert.False(second.CostUsesRarity);
        Assert.True(second.SpeedUsesRarity);

        // Seven modifier programs per recipe in a fixed role order, and the shared instance scaling's
        // two lists captured once behind the first recipe that named it.
        Assert.Equal(16, frame.ModifierPrograms.Count);
        AssertProgram(frame.ModifierPrograms[0], FirstRecipeId, WorldModifierProgramRole.ConceptDrain, true);
        AssertProgram(frame.ModifierPrograms[1], FirstRecipeId, WorldModifierProgramRole.ConceptSpeed, true);
        AssertProgram(
            frame.ModifierPrograms[2], FirstRecipeId, WorldModifierProgramRole.ConceptFreeUsageSlots, true);
        AssertProgram(
            frame.ModifierPrograms[3], FirstRecipeId, WorldModifierProgramRole.ConceptOverdriveSpeed, true);
        AssertProgram(
            frame.ModifierPrograms[4], FirstRecipeId, WorldModifierProgramRole.ConceptOverdriveDrain, true);
        AssertProgram(
            frame.ModifierPrograms[5], FirstRecipeId, WorldModifierProgramRole.ConceptCompletionCost, false);
        AssertProgram(
            frame.ModifierPrograms[6], FirstRecipeId, WorldModifierProgramRole.ConceptDrainLevel, false);
        AssertProgram(
            frame.ModifierPrograms[7], ScalingId, WorldModifierProgramRole.InstanceScalingCost, false);
        AssertProgram(
            frame.ModifierPrograms[8], ScalingId, WorldModifierProgramRole.InstanceScalingSpeed, false);
        AssertProgram(frame.ModifierPrograms[9], SecondRecipeId, WorldModifierProgramRole.ConceptDrain, true);
        AssertProgram(
            frame.ModifierPrograms[15], SecondRecipeId, WorldModifierProgramRole.ConceptDrainLevel, false);

        // The first recipe's drain record carries the game's own memo and dirty flag, and the one
        // modifier list behind the shared scaling reaches the entry table by value.
        Assert.Equal(11d, frame.ModifierPrograms[0].BaseValue);
        Assert.False(frame.ModifierPrograms[0].CalculationDirty);
        Assert.Equal(11d, frame.ModifierPrograms[0].CalculatedValue.ToDouble());

        var entry = Assert.Single(Entries(frame, ScalingId, WorldModifierProgramRole.InstanceScalingCost));
        Assert.Equal(WorldModifierProgramEntrySet.Modifier, entry.Set);
        Assert.Equal(0, entry.Position);
        Assert.Equal(GameValueModifierType.Exponent, entry.Type);
        Assert.Equal(4, entry.Order);
        Assert.Equal(6d, entry.Amount.ToDouble());
    }

    /// <summary>
    /// A recipe the game holds twice, or one whose core type carries no identity, is skipped by name
    /// rather than published or thrown over.
    /// </summary>
    [Fact]
    public void ARecipeWithoutAUsableIdentityIsSkippedLoudly()
    {
        Seed();
        var recipes = Registered<global::AlchemyRecipeListVariable>(KnownEntities.ConceptRecipes.Uuid);
        recipes.value.Add(recipes.value[0]);

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(2, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("recipe 2 had an invalid identity or core type", report.FirstFailure);
        Assert.Equal(2, frame.ConceptRecipes.Count);
    }

    /// <summary>An absent list position is a hole in the registry, not a recipe the reader can read.</summary>
    [Fact]
    public void AnAbsentRecipePositionIsSkippedByItsPosition()
    {
        Seed();
        var recipes = Registered<global::AlchemyRecipeListVariable>(KnownEntities.ConceptRecipes.Uuid);
        recipes.value.Insert(0, null!);

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(2, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("recipe 0 had an unexpected native type", report.FirstFailure);
    }

    /// <summary>
    /// An active assignment naming a recipe outside the scoped registry is skipped rather than
    /// published against an identity no published recipe row explains.
    /// </summary>
    [Fact]
    public void AnUnscopedActiveInstanceIsSkippedLoudly()
    {
        Seed();
        var active = Registered<global::AlchemyInstanceListVariable>(KnownEntities.ActiveConcepts.Uuid);
        active.value.Add(new global::AlchemyInstance(Recipe(UnscopedRecipeId, CoreType(CoreTypeId))));

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(2, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("active instance 3 did not name a scoped Concept recipe", report.FirstFailure);
        Assert.Equal(2, frame.AlchemyInstances.Count);
    }

    /// <summary>
    /// A native refusal degrades the whole category with the game's own message, rather than being
    /// published as a partial reading or escaping into the pass.
    /// </summary>
    [Fact]
    public void ANativeThrowDegradesTheCategoryWithItsMessage()
    {
        Seed();
        var active = Registered<global::AlchemyInstanceListVariable>(KnownEntities.ActiveConcepts.Uuid);
        active.ThrowOnCanAdd = true;

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Equal("reading Concept instances threw: CanAddInstance failed", report.FirstFailure);
        Assert.Equal(0, frame.ConceptRecipes.Count);
        Assert.Equal(0, frame.AlchemyInstances.Count);
    }

    /// <summary>
    /// A member the build does not expose leaves the category unavailable and says so, and an
    /// unavailable reader still resets what it would have filled.
    /// </summary>
    [Fact]
    public void AMemberThatCannotBindLeavesTheCategoryUnavailableAndNamesIt()
    {
        var reader = new WorldAlchemyInstanceReader(
            Resolve("IdScriptableObject"),
            Resolve("AlchemyInstanceListVariable"),
            Resolve("AlchemyRecipeListVariable"),
            name => name == "InstanceScalingSO" ? null : Resolve(name));

        Assert.False(reader.IsAvailable);

        var report = reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame());

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Equal(
            "the active Concept instance or drain-vector members were unavailable", report.FirstFailure);
    }

    /// <summary>A build without the Concept registry types at all names that instead.</summary>
    [Fact]
    public void AnAbsentRegistryTypeIsNamedSeparately()
    {
        var reader = new WorldAlchemyInstanceReader(
            Resolve("IdScriptableObject"), null, Resolve("AlchemyRecipeListVariable"), Resolve);

        Assert.False(reader.IsAvailable);
        Assert.Equal(
            "the Concept registry types were not found on this build",
            reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame()).FirstFailure);
    }

    private static void AssertRecipe(
        in WorldConceptRecipe row,
        Guid recipeId,
        Guid coreTypeId,
        bool canAddNow,
        int slotCount)
    {
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(coreTypeId, row.CoreTypeId);
        Assert.Equal(canAddNow, row.CanAddNow);
        Assert.Equal(slotCount, row.SlotCount);
    }

    private static void AssertInstance(
        in RawWorldAlchemyInstance row,
        Guid recipeId,
        int quantity,
        int queuedQuantity,
        bool drainReadable,
        bool isDrainApplied,
        double currentRatio,
        double usageRatio)
    {
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(quantity, row.Quantity);
        Assert.Equal(queuedQuantity, row.QueuedQuantity);
        Assert.Equal(drainReadable, row.DrainReadable);
        Assert.Equal(isDrainApplied, row.IsDrainApplied);
        Assert.Equal(currentRatio, row.CurrentRatio.ToDouble());
        Assert.Equal(usageRatio, row.UsageRatio.ToDouble());
    }

    private static void AssertCost(
        in WorldAlchemyCost row,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        Guid resourceId,
        double amount)
    {
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(resourceId, row.ResourceId);
        Assert.Equal(amount, row.Amount.ToDouble());
    }

    private static void AssertProgram(
        in WorldModifierProgram row,
        Guid ownerId,
        WorldModifierProgramRole role,
        bool isRecord)
    {
        Assert.Equal(ownerId, row.OwnerId);
        Assert.Equal(role, row.Role);
        Assert.Equal(isRecord, row.IsRecord);
    }

    private static List<WorldModifierProgramEntry> Entries(
        GameWorldCycleFrame frame,
        Guid ownerId,
        WorldModifierProgramRole role)
    {
        var matches = new List<WorldModifierProgramEntry>();
        for (var index = 0; index < frame.ModifierProgramEntries.Count; index++)
        {
            var entry = frame.ModifierProgramEntries[index];
            if (entry.OwnerId == ownerId && entry.Role == role) matches.Add(entry);
        }
        return matches;
    }

    private static WorldAlchemyInstanceReader Reader() => new(
        Resolve("IdScriptableObject"),
        Resolve("AlchemyInstanceListVariable"),
        Resolve("AlchemyRecipeListVariable"),
        Resolve);

    private static Type? Resolve(string name) =>
        typeof(global::AlchemyRecipeSO).Assembly.GetType(name, throwOnError: false);

    private static T Registered<T>(Guid id) where T : global::IdScriptableObject =>
        (T)global::IdScriptableObject.RuntimeLookup[id];

    private static void Clear()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::AlchemyTypeSO.All.Clear();
        global::InstanceScalingSO.All.Clear();
        global::UnityEngine.Resources.Objects.Clear();
    }

    private static void Seed()
    {
        var scaling = new global::InstanceScalingSO { useRarity = true };
        scaling.SetGuid(ScalingId);
        scaling.rarityAttributeBlacklist.Add(global::ScalingType.CostMod);
        scaling.instanceScaling.Set(
            global::ScalingType.CostMod,
            ModifierList(global::ValueModifier.ValueModifierType.Exponent, 6d, 4));

        var firstCore = CoreType(CoreTypeId);
        firstCore.reqCostPenalty = new global::ValueModifier(
            global::ValueModifier.ValueModifierType.MultiStacking, new BigDouble(2d, 0), 3);
        firstCore.reqSpeedPenalty = new global::ValueModifier(
            global::ValueModifier.ValueModifierType.Reduction, new BigDouble(5d, 0), 1);

        var first = Recipe(FirstRecipeId, firstCore);
        first.advancementLevel = 7;
        first.instanceScaling.scaling = scaling;
        first.maxUsageSlots = new global::ValueModifierRecord(new BigDouble(9d, 0));
        first.drainCostMod = new global::ValueModifierRecord(new BigDouble(11d, 0));
        first.drainCost = new global::ConceptCostVector(
            new global::ConceptCostEntry(Resource(DrainResourceId), new BigDouble(12d, 0)));
        first.bandwidthCost = new global::ConceptCostVector(
            new global::ConceptCostEntry(Resource(BandwidthResourceId), new BigDouble(3d, 0)));

        var second = Recipe(SecondRecipeId, CoreType(OtherCoreTypeId));
        second.instanceScaling.scaling = scaling;
        second.drainCost = new global::ConceptCostVector(
            new global::ConceptCostEntry(Resource(DrainResourceId), new BigDouble(40d, 0)));

        var recipes = new global::AlchemyRecipeListVariable();
        recipes.value.Add(first);
        recipes.value.Add(second);
        Register(recipes, KnownEntities.ConceptRecipes.Uuid);

        var active = new global::AlchemyInstanceListVariable();
        active.value.Add(Instance(second, quantity: 4, queued: 4, applied: false, current: 0.25d, usage: 0.75d));
        active.value.Add(new global::AlchemyInstance());
        active.value.Add(Instance(first, quantity: 1, queued: 2, applied: true, current: 0.5d, usage: 0.125d));
        Register(active, KnownEntities.ActiveConcepts.Uuid);
    }

    private static global::AlchemyInstance Instance(
        global::AlchemyRecipeSO recipe,
        int quantity,
        int queued,
        bool applied,
        double current,
        double usage) =>
        new(recipe)
        {
            quantity = quantity,
            queuedQuantity = queued,
            resourceDrain = new global::ConceptDrainState
            {
                isDrainApplied = applied,
                currentRatio = new BigDouble(current),
                usageRatio = new BigDouble(usage),
                Current = new global::ConceptCostVector(
                    new global::ConceptCostEntry(
                        Resource(CurrentResourceId), new BigDouble(quantity * 2d + 1d, 0))),
            },
        };

    private static global::AlchemyRecipeSO Recipe(Guid id, global::AlchemyTypeSO core)
    {
        var recipe = new global::AlchemyRecipeSO(id.ToString(), "Concept", new[] { core })
        {
            coreType = core,
        };
        return recipe;
    }

    private static global::AlchemyTypeSO CoreType(Guid id)
    {
        var core = new global::AlchemyTypeSO();
        core.SetGuid(id);
        return core;
    }

    private static global::ConceptResource Resource(Guid id) => new() { uuid = id.ToString() };

    private static global::ValueModifierList ModifierList(
        global::ValueModifier.ValueModifierType type,
        double amount,
        int order)
    {
        var list = new global::ValueModifierList();
        list.modifiers.Add(new global::ValueModifier(type, new BigDouble(amount, 0), order));
        return list;
    }

    private static T Register<T>(T entity, Guid id) where T : global::IdScriptableObject
    {
        entity.SetGuid(id);
        global::IdScriptableObject.RuntimeLookup[id] = entity;
        return entity;
    }
}
