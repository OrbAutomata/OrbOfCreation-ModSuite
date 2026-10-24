using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldReviewDependencyTests
{
    [Fact]
    public void FailedAlchemyIdentityCaptureKeepsUsageGraphClosedUntilSameEpochRecovery()
    {
        using var setup = new HealthyWorldReadSetup();
        var blocker = new global::UpgradeSO { level = 0, maxLevel = 1 };
        global::UpgradeSO.All.Add(blocker);
        var recipe = new global::AlchemyRecipeSO { MaxUsageReadFailure = new InvalidOperationException("private usage identity payload") };
        recipe.usagePrerequisites.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = blocker, reqType = Requirements.UpgradeRequirementType.OneLevel,
        });
        global::AlchemyRecipeSO.All.Add(recipe);
        var recipeId = recipe.GetGuid();
        var logs = new List<string>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var failed = collector.Collect(frame);
            var requirements = failed.For("entity requirements");
            Assert.False(requirements.IsClean);
            Assert.True(requirements.HasReadFault);
            Assert.True(requirements.Skipped > 0);
            Assert.NotEmpty(failed.For("alchemy recipes").Failure.Reason.Reference);
            Assert.Equal(failed.For("alchemy recipes").Failure.Reason.Reference, requirements.Failure.Reason.Reference);
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(
                GameWorldFrameDeriver.Build(frame), recipeId, 0, WorldRequirementProgramKind.Usage));
            var failedWorld = GameWorldFrameDeriver.Build(frame);
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(
                failedWorld, blocker.GetGuid(), 0, WorldRequirementProgramKind.NextLevel));
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(
                failedWorld, blocker.GetGuid(), 0, WorldRequirementProgramKind.Unlock));
            var generation = frame.StructuralGeneration;

            recipe.MaxUsageReadFailure = null;
            collector.Collect(frame);
            var recovered = GameWorldFrameDeriver.Build(frame);
            Assert.True(frame.StructuralGeneration > generation);
            Assert.True(frame.Report.For("entity requirements").IsClean);
            Assert.Equal(WorldRequirementVerdict.Unmet,
                WorldRequirementEvaluator.Evaluate(recovered, recipeId, 0, WorldRequirementProgramKind.Usage));
            collector.Collect(frame);
            Assert.Same(recovered.EntityRequirements, GameWorldFrameDeriver.Build(frame).EntityRequirements);
        }
        finally { GameActionFaultLog.ConfigureLog(null); }
    }

    [Fact]
    public void EffectFaultAnnouncementChangesWhenTheKnownUpgradeOwnerChanges()
    {
        using var setup = new HealthyWorldReadSetup();
        var targetA = new global::ResourceSO { GetGuidFailure = new InvalidOperationException("private effect A payload") };
        var targetB = new global::ResourceSO();
        var a = UpgradeWithEffect(targetA);
        var b = UpgradeWithEffect(targetB);
        var logs = new List<string>();
        var warnings = new List<WorldCollectionReport>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var port = new AutomataWorldCapturePort(new GameWorldCollector(), () => 1, () => 1, warnings.Add);
            var frame = new GameWorldCycleFrame();
            var first = port.Collect(frame);
            Assert.Equal(a.GetGuid(), first.For("effect factors").Failure.TargetId);
            port.Collect(frame);
            Assert.Equal(2, logs.Count);
            Assert.Single(warnings);

            targetA.GetGuidFailure = null;
            targetB.GetGuidFailure = new InvalidOperationException("private effect B payload");
            var changed = port.Collect(frame);
            Assert.Equal(b.GetGuid(), changed.For("effect factors").Failure.TargetId);
            Assert.Equal(3, logs.Count);
            Assert.Equal(2, warnings.Count);
        }
        finally { GameActionFaultLog.ConfigureLog(null); }
    }

    private static global::UpgradeSO UpgradeWithEffect(global::ResourceSO target)
    {
        var upgrade = new global::UpgradeSO();
        upgrade.permanentEffects.upgradeableObjectEffects.Add(new global::UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = target, propertyType = "power",
            modifier = new global::ValueModifier(global::ValueModifier.ValueModifierType.Raw, 1d, 0),
        });
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }
}
