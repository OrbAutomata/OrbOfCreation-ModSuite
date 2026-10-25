using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoLevels;

public sealed class AutoLevelsProjectionTests
{
    [Fact]
    public void AllFourDecisionsRetainExactUnboundedComparisonsAcrossTheWorkerProjection()
    {
        var required = BigDouble.Normalize(1.2345, 900);
        var budget = BigDouble.Normalize(9.8765, 899);
        var resource = Guid.NewGuid();
        var state = new AutoLevelsCycleState { Observed = true,
            Output = new(LevelDial.Output, 4, 3, AutoLevelsDecisionKind.Lower,
                AutoLevelsDecisionReason.ImmediateCost, resource, required, budget),
            Reserve = new(LevelDial.Reserve, 4, 5, AutoLevelsDecisionKind.Raise, AutoLevelsDecisionReason.ReserveMaximum),
            Druidry = new(LevelDial.Druidry, 4, 4, AutoLevelsDecisionKind.Hold, AutoLevelsDecisionReason.NextDrain, resource, required, budget),
            Alchemy = new(LevelDial.Alchemy, 4, 4, AutoLevelsDecisionKind.Hold, AutoLevelsDecisionReason.NoActiveWork) };
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        AutoLevelsServiceProjection.Write(in state, new ServiceStateProjectionBuilder(buffer));
        var projection = buffer.CreateSnapshot();
        Assert.Equal(41, projection.Count);
        for (var index = 0; index < 4; index++)
        {
            var expected = state.Decision(index);
            var actual = AutoLevelsServiceProjection.Read(in projection, index);
            Assert.Equal(expected.Dial, actual.Dial);
            Assert.Equal(expected.CurrentLevel, actual.CurrentLevel);
            Assert.Equal(expected.TargetLevel, actual.TargetLevel);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Reason, actual.Reason);
            Assert.Equal(expected.ResourceId, actual.ResourceId);
            Assert.Equal(expected.Required, actual.Required);
            Assert.Equal(expected.Budget, actual.Budget);
        }
        var text = AutoLevelsServiceProjection.Describe(projection);
        Assert.Contains("Output 4 → 3", text);
        Assert.Contains("Reserve 4 → 5", text);
        Assert.Contains("Druidry 4: holding", text);
        Assert.Contains("Alchemy 4: holding", text);
        Assert.Equal(FeatureStatusState.Operational, AutoLevelsServiceProjection.ProjectHealth(projection).State);
        Assert.Equal(FeatureStatusReasonCode.None, AutoLevelsServiceProjection.ProjectHealth(projection).Reason);
    }

    [Fact]
    public void UnevaluatedProjectionHasNoStaleThinkingAndWaits()
    {
        var state = default(AutoLevelsCycleState);
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        AutoLevelsServiceProjection.Write(in state, new ServiceStateProjectionBuilder(buffer));
        var projection = buffer.CreateSnapshot();
        Assert.Empty(AutoLevelsServiceProjection.Describe(projection));
        Assert.Equal(FeatureStatusState.NotReady, AutoLevelsServiceProjection.ProjectHealth(projection).State);
    }
}
