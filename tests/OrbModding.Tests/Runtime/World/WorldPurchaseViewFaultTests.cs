using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldPurchaseViewFaultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachCategoryReadFailureLogsAndReachesTheCapturedResolution(bool membership)
    {
        using var setup = new HealthyWorldReadSetup();
        var structure = new global::StructureSO();
        var badMember = new global::StructureSO { GetGuidFailure = new InvalidOperationException("private category payload") };
        global::StructureSO.All.Add(structure);
        if (membership)
            structure.structureType.SetStructuresForTests(new List<global::StructureSO> { structure, badMember });
        else
            structure.structureType.GetGuidFailure = new InvalidOperationException("private category payload");
        Assert.True(NativePurchaseViewAdmissionResolver.TryCreate(WorldNativeTypes.Resolve, out var resolver, out var binding), binding);
        var logs = new List<string>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                resolver!.ReadAll(1, new WorldRelationBuffer<WorldPurchaseViewRelation>(),
                    new WorldRelationBuffer<WorldPurchaseViewRoute>(), new WorldRelationBuffer<WorldUpgradeListMembership>(),
                    out _, out _, out _, out _);
                Assert.True(resolver.TryGetCaptured(WorldPurchaseCandidateKind.Structure, structure.GetGuid(), 1, out var resolution));
                Assert.Equal(WorldPurchaseViewRelationStatus.Unreadable, resolution.Relation.Status);
                Assert.Contains("Reference MCP-", resolution.Failure.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("private", resolution.Failure.Text, StringComparison.Ordinal);
            }
            Assert.Equal(2, logs.Count);
            Assert.All(logs, log => Assert.Contains("private category payload", log, StringComparison.Ordinal));
        }
        finally { GameActionFaultLog.ConfigureLog(null); }
    }

    [Fact]
    public void AvailabilityAndDiagnosticFailuresLogEachAttemptAndRecoverWithoutALifecycle()
    {
        using var setup = new HealthyWorldReadSetup();
        var upgrade = new global::UpgradeSO();
        global::UpgradeSO.All.Add(upgrade);
        var list = new global::UpgradeListVariable();
        list.value.Add(upgrade);
        var view = new ThrowingView();
        view.relevantLists.Add(list);
        ThrowingView.All.Clear();
        ThrowingView.All.Add(view);
        Assert.True(NativePurchaseViewAdmissionResolver.TryCreate(
            name => name == "ViewSO" ? typeof(ThrowingView) : WorldNativeTypes.Resolve(name), out var resolver, out var binding), binding);
        resolver!.ReadAll(1, new WorldRelationBuffer<WorldPurchaseViewRelation>(),
            new WorldRelationBuffer<WorldPurchaseViewRoute>(), new WorldRelationBuffer<WorldUpgradeListMembership>(),
            out _, out _, out _, out _);
        Assert.True(resolver.TryGetCaptured(WorldPurchaseCandidateKind.Upgrade, upgrade.GetGuid(), 1, out var resolution));
        var logs = new List<string>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var references = new List<string>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Assert.False(resolver.TryReadAvailability(in resolution, out _, out var failure));
                Assert.DoesNotContain("private", failure.Text, StringComparison.Ordinal);
                Assert.NotEmpty(failure.Reason.Reference);
                references.Add(failure.Reason.Reference);
                Assert.Contains("Reference MCP-", Assert.Single(resolver.DescribeCaptured(1)), StringComparison.Ordinal);
            }
            Assert.NotEqual(references[0], references[1]);
            Assert.Equal(4, logs.Count);
            view.Failure = null;
            Assert.True(resolver.TryReadAvailability(in resolution, out var available, out _));
            Assert.True(available);
            Assert.Equal(4, logs.Count);
            view.Failure = new NullReferenceException("unexpected defect");
            Assert.Throws<NullReferenceException>(() => resolver.TryReadAvailability(in resolution, out _, out _));
        }
        finally { GameActionFaultLog.ConfigureLog(null); ThrowingView.All.Clear(); }
    }

    private sealed class ThrowingView : global::ViewSO
    {
        public new static readonly List<ThrowingView> All = new();
        internal Exception? Failure = new InvalidOperationException("private availability payload");
        public new bool IsAvailable() => Failure is { } failure ? throw failure : true;
    }
}
