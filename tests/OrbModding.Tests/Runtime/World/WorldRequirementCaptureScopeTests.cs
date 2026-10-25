using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldRequirementCaptureScopeTests
{
    [Fact]
    public void FaultingOwnerDoesNotChangeAnotherOwnersEmptyProgramWithOrWithoutOtherProgramRows()
    {
        using var setup = new HealthyWorldReadSetup();
        var (faulting, _) = setup.FaultingRequirement();
        var empty = new global::UpgradeSO();
        global::UpgradeSO.All.Add(empty);
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(world, faulting.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, empty.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, empty.GetGuid(), 0, WorldRequirementProgramKind.Unlock));

        empty.prerequisites.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = faulting, reqType = Requirements.UpgradeRequirementType.OneLevel,
        });
        collector.Collect(frame);
        world = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, empty.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(world, empty.GetGuid(), 0, WorldRequirementProgramKind.Unlock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DurableIncompleteRecipeRegistryWithholdsOnlyItsGraphAndReusesStructuralCapture(bool spell)
    {
        using var setup = new HealthyWorldReadSetup();
        var owner = new global::UpgradeSO();
        global::UpgradeSO.All.Add(owner);
        owner.permanentEffects.upgradeableObjectEffects.Add(new global::UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = new global::ResourceSO(), propertyType = "power",
            modifier = new global::ValueModifier(global::ValueModifier.ValueModifierType.Raw, 1d, 0),
        });
        var recipeId = Guid.Empty;
        if (spell)
        {
            var recipe = new global::SpellRecipeSO();
            recipeId = recipe.GetGuid();
            global::SpellRecipeSO.All.Add(recipe);
            global::SpellRecipeSO.All.Add(null!);
        }
        else
        {
            var recipe = new global::AlchemyRecipeSO();
            recipeId = recipe.GetGuid();
            recipe.usagePrerequisites.prerequisites.Add(new Requirements.UpgradeRequirement
            {
                item = owner, reqType = Requirements.UpgradeRequirementType.OneLevel,
            });
            global::AlchemyRecipeSO.All.Add(recipe);
            global::AlchemyRecipeSO.All.Add(null!);
        }
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var firstReport = collector.Collect(frame);
        var upstream = firstReport.For(spell ? "spell recipes" : "alchemy recipes");
        Assert.Equal(1, upstream.Skipped);
        Assert.False(upstream.HasReadFault);
        var category = spell ? "spell authored graph" : "entity requirements";
        Assert.False(firstReport.For(category).IsClean);
        Assert.False(firstReport.For(category).HasReadFault);
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.True(first.LevelEffects.Count > 0);
        if (spell)
            Assert.False(WorldSpellGraphLookup.TryFindAuthoring(first.SpellRecipeAuthoring, recipeId, out _));
        else
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(
                first, recipeId, 0, WorldRequirementProgramKind.Usage));
        var generation = frame.StructuralGeneration;

        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.LevelEffects, second.LevelEffects);
        Assert.False(frame.Report.For(category).IsClean);
        Assert.False(frame.Report.For(category).HasReadFault);
        if (!spell)
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(
                second, recipeId, 0, WorldRequirementProgramKind.Usage));
    }
}
