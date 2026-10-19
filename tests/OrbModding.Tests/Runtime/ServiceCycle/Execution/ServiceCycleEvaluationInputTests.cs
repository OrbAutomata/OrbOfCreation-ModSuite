using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

/// <summary>
/// What the runtime hands a worker's evaluation, and which reading of the world it is.
/// </summary>
/// <remarks>
/// This is where a service reads its inputs, so the guarantees around it are the ones the whole
/// shared-world design rests on: one world per cycle, chosen by the runtime, pinned before any
/// service code runs and unchanged by anything that happens after. See W50.
/// </remarks>
public sealed class ServiceCycleEvaluationInputTests
{
    /// <summary>
    /// The snapshot the evaluation is handed is the one the registry has published.
    /// </summary>
    /// <remarks>
    /// A service is given no way to reach the publisher, so if the runtime handed over anything other
    /// than the live publication — a default, a copy, a stale pin — the service would have no way to
    /// tell and would report an empty game as a fact about the save.
    /// </remarks>
    [Fact]
    public void TheEvaluationIsHandedTheWorldTheRegistryPublished()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.projection.world");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.WorldPublication.Publish(WorldOfThreeStructures(), new WorldGeneration(2));

        ServiceRunnerTestWait.RunZeroActionCycle(registration.Runner, clock);

        Assert.Equal(3, definition.LastEvaluatedStructures);
        Assert.False(definition.LastEvaluatedWorldWasTheEmptyDefault);
    }

    /// <summary>
    /// Nothing published yet is the empty world, not a null one.
    /// </summary>
    /// <remarks>
    /// Collection is a service like any other and has not run on the first cycle of a lifecycle.
    /// Every consumer would need a null check it would get wrong exactly once.
    /// </remarks>
    [Fact]
    public void AnEvaluationBeforeAnyCollectionIsHandedTheEmptyWorld()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.projection.empty-world");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));

        ServiceRunnerTestWait.RunZeroActionCycle(registration.Runner, clock);

        Assert.True(definition.LastEvaluatedWorldWasTheEmptyDefault);
    }

    /// <summary>
    /// A world published from inside a service's own capture is not the world that cycle runs against.
    /// </summary>
    /// <remarks>
    /// The pin happens once, before capture, and both halves of the cycle get that reading. Reading
    /// the publication again on the way to the worker would compile and would almost always agree —
    /// and on the cycle where it did not, the service would have decided to act from one world and
    /// then evaluated against another, with nothing downstream able to tell. This is the same hole
    /// [W49] closed for the strategy generation, in the same place. Only a source can prove it: the
    /// capture is the one piece of service code that runs after the pin.
    /// </remarks>
    [Fact]
    public void AWorldPublishedDuringCaptureIsNotTheOneTheCycleRunsAgainst()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("test.projection.published-during-capture");
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        definition.CaptureCallback = () =>
            registry.WorldPublication.Publish(WorldOfThreeStructures(), new WorldGeneration(2));

        ServiceRunnerTestWait.RunZeroActionCycle(registration.Runner, clock);

        Assert.Equal(0, definition.LastCapturedWorldStructures);
        Assert.Equal(new WorldGeneration(1), definition.LastEvaluatedWorld);
    }

    /// <summary>
    /// The same guarantee on the pump's path, which publishes the request without blocking.
    /// </summary>
    /// <remarks>
    /// There are two ways a cycle reaches the worker — the blocking publish and the pump's
    /// non-blocking probe, which can also defer and retry later — and they are separate code. A rule
    /// proved on one of them is proved on the path production does not use.
    /// </remarks>
    [Fact]
    public void TheSamePinReachesTheWorkerOnThePumpsPath()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("test.projection.pump-pin");
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        definition.CaptureCallback = () => registry.WorldPublication.Publish(
            WorldOfThreeStructures(),
            new WorldGeneration((ulong)definition.CaptureCount + 1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);

        Assert.Equal(0, definition.LastCapturedWorldStructures);
        Assert.Equal(new WorldGeneration(1), definition.LastEvaluatedWorld);
    }

    /// <summary>
    /// The cycle names the reading of the world it ran against, and it is the reading the evaluation
    /// was handed.
    /// </summary>
    /// <remarks>
    /// The world was the one pinned input a cycle could not say it had used: its configuration and
    /// strategy generations were on the identity and its world was not, so a decision could be read
    /// back with no way to ask which collection it was true of. Before any collection there is still
    /// an answer — the publisher's seed — rather than an absence.
    /// </remarks>
    [Fact]
    public void TheCycleIdentityNamesTheWorldTheEvaluationWasHanded()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.projection.identity-world");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));

        ServiceRunnerTestWait.RunZeroActionCycle(registration.Runner, clock);
        var seeded = registration.Runner.Snapshot;

        Assert.Equal(new WorldGeneration(1), seeded.ActiveCycle.World);
        Assert.True(definition.LastEvaluatedWorldWasTheEmptyDefault);

        registry.WorldPublication.Publish(WorldOfThreeStructures(), new WorldGeneration(4));
        ServiceRunnerTestWait.RunZeroActionCycle(registration.Runner, clock);
        var collected = registration.Runner.Snapshot;

        Assert.Equal(new WorldGeneration(4), collected.ActiveCycle.World);
        Assert.Equal(3, definition.LastEvaluatedStructures);
    }

    private static GameWorldState WorldOfThreeStructures() => new()
    {
        Structures = WorldTable.Create(
            new[]
            {
                WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(Guid.NewGuid())),
                WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(Guid.NewGuid())),
                WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(Guid.NewGuid())),
            }),
    };
}
