using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutomataProfileOperationsTests
{
    [Fact]
    public void NestedStageFaultsTheProfileWithoutEscapingIntoGameplay()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var operations = new AutomataProfileOperations(probe);
        var context = ActionContext(serviceOrdinal: 0, frameIdentity: 1);
        var outer = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
            context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
            var nested = operations.Begin(
                ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
                context,
                ServiceCycleProfileTemperature.Warm);
            Assert.False(nested.IsActive);
            nested.Abandon();
            outer.Complete();
        }
        finally
        {
            outer.Abandon();
        }

        Assert.Equal(ServiceCycleProfileProbeFault.StageOverlapRejected, probe.Fault);
        Assert.Equal(ServiceCycleProfileMeasurementFault.None, recorder.Fault);
        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        Assert.Equal(0, recorder.GroupCount);
    }

    [Fact]
    public void CounterExhaustionFaultsWithoutPublishingInventedOperations()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var operations = new AutomataProfileOperations(probe);
        var stage = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
            ActionContext(serviceOrdinal: 0, frameIdentity: 1),
            ServiceCycleProfileTemperature.Warm);
        try
        {
            for (var entry = 0u; entry < uint.MaxValue; entry++) operations.AddListEntry();
            Assert.Equal(ServiceCycleProfileProbeFault.None, probe.Fault);
            operations.AddListEntry();
            stage.Complete();
        }
        finally
        {
            stage.Abandon();
        }

        Assert.Equal(ServiceCycleProfileProbeFault.OperationCounterExhausted, probe.Fault);
        Assert.Equal(ServiceCycleProfileMeasurementFault.None, recorder.Fault);
        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        Assert.Equal(0, recorder.GroupCount);
    }

    [Fact]
    public void ActionStageUsesThePumpCoordinatesCarriedByTheActionContext()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var stage = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
            ActionContext(serviceOrdinal: 3, frameIdentity: 42),
            ServiceCycleProfileTemperature.Warm);

        stage.Complete();

        var completed = Assert.Single(measurement.Completed);
        Assert.Equal(
            (int)ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
            completed.Context.StageCode);
        Assert.Equal(3, completed.Context.ServiceOrdinal);
        Assert.Equal(42UL, completed.Context.Frame);
        Assert.Equal(5UL, completed.Context.Cycle);
    }
}
