using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldCollectionRetryTests
{
    [Fact]
    public void NativeStructuralFaultRetriesTwiceAndRecoversGraphInTheSameEpoch()
    {
        using var setup = new HealthyWorldReadSetup();
        var (owner, target) = setup.FaultingRequirement();
        var logs = new List<string>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var firstReport = collector.Collect(frame);
            Assert.True(firstReport.For("resource bar").IsClean);
            var first = GameWorldFrameDeriver.Build(frame);
            var failedGeneration = frame.StructuralGeneration;
            collector.Collect(frame);
            Assert.True(logs.Count == 2, "Expected exactly two requirement faults: " + string.Join(Environment.NewLine, logs));
            Assert.True(frame.StructuralGeneration > failedGeneration);

            target.GetGuidFailure = null;
            global::UpgradeSO.All.Add(target);
            collector.Collect(frame);
            var recovered = GameWorldFrameDeriver.Build(frame);
            Assert.NotSame(first.EntityRequirements, recovered.EntityRequirements);
            Assert.Equal(WorldRequirementVerdict.Met,
                WorldRequirementEvaluator.Evaluate(recovered, owner.GetGuid(), 1));
            Assert.True(frame.Report.For("entity requirements").IsClean);
            Assert.True(logs.Count == 2, "Expected exactly two requirement faults: " + string.Join(Environment.NewLine, logs));

            owner.prerequisitesPerLevel.prerequisites.Clear();
            collector.Collect(frame);
            Assert.Same(recovered.EntityRequirements, GameWorldFrameDeriver.Build(frame).EntityRequirements);
        }
        finally { GameActionFaultLog.ConfigureLog(null); }
    }

    [Fact]
    public void DurableUnsupportedAuthoredConditionDoesNotKeepStructuralReadsOpen()
    {
        using var setup = new HealthyWorldReadSetup();
        var owner = new global::UpgradeSO { maxLevel = 1 };
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.UnsupportedRequirement());
        global::UpgradeSO.All.Add(owner);
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        var first = GameWorldFrameDeriver.Build(frame);
        var generation = frame.StructuralGeneration;
        owner.prerequisitesPerLevel.prerequisites.Clear();

        collector.Collect(frame);

        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, GameWorldFrameDeriver.Build(frame).EntityRequirements);
        Assert.Equal(1, frame.Report.For("entity requirements").Skipped);
    }

    [Fact]
    public void RealCapturePortLogsEveryFaultButAnnouncesOnlyChangedReasonTargetOrOutcome()
    {
        using var setup = new HealthyWorldReadSetup();
        var (owner, target) = setup.FaultingRequirement();
        var resource = new global::ResourceSO();
        global::ResourceSO.All.Add(resource);
        var logs = new List<string>();
        var warnings = new List<WorldCollectionReport>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var port = new AutomataWorldCapturePort(new GameWorldCollector(), () => 1, () => 1, warnings.Add);
            var frame = new GameWorldCycleFrame();
            port.Collect(frame);
            port.Collect(frame);
            Assert.True(logs.Count == 2, "Expected exactly two requirement faults: " + string.Join(Environment.NewLine, logs));
            Assert.Single(warnings);

            target.GetGuidFailure = null;
            global::UpgradeSO.All.Remove(owner);
            setup.FaultingRequirement();
            port.Collect(frame);
            Assert.Equal(2, warnings.Count); // same sentence, another owner

            foreach (var upgrade in global::UpgradeSO.All)
                foreach (var condition in upgrade.prerequisitesPerLevel.prerequisites)
                    if (condition is Requirements.UpgradeRequirement leaf) leaf.item.GetGuidFailure = null;
            resource.GetGuidFailure = new InvalidOperationException("private resource payload");
            port.Collect(frame);
            Assert.Equal(3, warnings.Count); // another authored reason/category
            var loggedAttempts = logs.Count;
            Assert.True(loggedAttempts >= 4);
            resource.GetGuidFailure = null;
            port.Collect(frame);
            Assert.Equal(4, warnings.Count); // recovered outcome
            Assert.Equal(loggedAttempts, logs.Count);
            Assert.All(warnings, report => Assert.DoesNotContain("private", report.Describe(), StringComparison.Ordinal));
        }
        finally { GameActionFaultLog.ConfigureLog(null); }
    }
}

internal sealed class HealthyWorldReadSetup : IDisposable
{
    private readonly IReadOnlyList<Guid> _dynamicIdentities;
    internal HealthyWorldReadSetup()
    {
        global::UpgradeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::ResourceSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::ViewSO.All.Clear();
        foreach (var id in WorldUpgradeScreenLists.All)
        {
            var list = new global::UpgradeListVariable();
            list.SetGuid(id);
            global::IdScriptableObject.RuntimeLookup[id] = list;
        }
        _dynamicIdentities = WorldCaptureTestContext.SeedScribeRelations();
    }

    internal (global::UpgradeSO Owner, global::UpgradeSO Target) FaultingRequirement()
    {
        var owner = new global::UpgradeSO { maxLevel = 1 };
        var target = new global::UpgradeSO { level = 1, GetGuidFailure = new InvalidOperationException("private native payload") };
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = target, reqType = Requirements.UpgradeRequirementType.OneLevel,
        });
        global::UpgradeSO.All.Add(owner);
        // The compared row must be present after recovery, but its identity getter currently throws.
        // Only the authored condition owns this reference while it is faulting.
        return (owner, target);
    }

    public void Dispose()
    {
        global::UpgradeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::ResourceSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::ViewSO.All.Clear();
        foreach (var id in WorldUpgradeScreenLists.All)
            global::IdScriptableObject.RuntimeLookup.Remove(id);
        global::AlchemyManager.instance = null;
        foreach (var id in _dynamicIdentities)
            global::IdScriptableObject.RuntimeLookup.Remove(id);
    }
}
