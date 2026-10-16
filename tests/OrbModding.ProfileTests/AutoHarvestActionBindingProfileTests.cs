using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

/// <summary>
/// Binding resolution is measured where it now happens: at the action boundary.
/// </summary>
/// <remarks>
/// It moved out of capture with the rest of the native reads, and stage 1001 moved with it rather
/// than being retired. It is the same reflective work against the same registries, only now it runs
/// once for the pair about to be mutated instead of once per cycle for both.
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class AutoHarvestActionBindingProfileTests
{
    [Fact]
    public void ResolvingThePairToMutateCompletesTheBindingSample()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var bindings = new BindingPort(operations);
        var adapter = CreateActionAdapter(bindings, operations);

        adapter.TryExecute(
            new AutoHarvestCycleAction(
                AutoHarvestPair.FruitTree,
                default,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving),
            Configuration(),
            ActionContext(serviceOrdinal: 3, frameIdentity: 41));

        var completed = Assert.Single(measurement.Completed);
        Assert.Equal(1001, completed.Context.StageCode);
        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, completed.Context.Temperature);
        Assert.Equal((uint)2, completed.Operations.StableIdReads);
        Assert.Empty(measurement.Abandoned);
        Assert.Equal(ServiceCycleProfileTemperature.Warm, bindings.CurrentTemperature);
    }

    /// <summary>
    /// A pair that did not bind faults the action and abandons the sample it was being timed by.
    /// </summary>
    [Fact]
    public void AnUnresolvedPairAbandonsTheBindingSample()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var bindings = new BindingPort(operations, treasureAvailable: false);
        var adapter = CreateActionAdapter(bindings, operations);

        adapter.TryExecute(
            new AutoHarvestCycleAction(
                AutoHarvestPair.TreasureTree,
                default,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving),
            Configuration(),
            ActionContext(serviceOrdinal: 0, frameIdentity: 1));

        Assert.Empty(measurement.Completed);
        Assert.Equal(
            new[] { ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence },
            Spans(measurement.Abandoned));
        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, bindings.CurrentTemperature);
    }

    /// <summary>
    /// A rebind that lands inside a warm sample abandons that sample rather than mislabelling it.
    /// </summary>
    [Fact]
    public void DriftRacingTheWarmPreflightAbandonsTheSample()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var bindings = new BindingPort(operations, driftDuringNextResolve: true);
        Assert.True(bindings.TryComplete(ServiceCycleProfileTemperature.ColdProcess));
        var adapter = CreateActionAdapter(bindings, operations);

        adapter.TryExecute(
            new AutoHarvestCycleAction(
                AutoHarvestPair.FruitTree,
                default,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving),
            Configuration(),
            ActionContext(serviceOrdinal: 0, frameIdentity: 1));

        Assert.Empty(measurement.Completed);
        Assert.Equal(
            new[] { ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence },
            Spans(measurement.Abandoned));
        Assert.Equal(ServiceCycleProfileTemperature.LifecycleRebind, bindings.CurrentTemperature);
    }
}
