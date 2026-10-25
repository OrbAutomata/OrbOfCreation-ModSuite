using OrbModding.TestSupport;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestProfileBindingObservationTests
{
    [Fact]
    public void StaleWarmBindingIsClassifiedBeforeTheRebindStageStarts()
    {
        var lifecycle = 1L;
        var registry = new Dictionary<Guid, object>();
        var identities = new Dictionary<object, Guid>();
        var nativeResolver = TestRegistryResolver.For(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.Ready((IDictionary)registry),
            value => identities[value]);
        var probe = new ServiceCycleProfileProbe();
        var operations = new AutomataProfileOperations(probe);
        var resolver = new AutoHarvestBindingResolver(
            nativeResolver,
            operations);
        var shared = SharedBinding(nativeResolver, registry, identities);
        var fruit = PairBinding(
            AutoHarvestPair.FruitTree,
            nativeResolver,
            registry,
            identities,
            out _);
        var treasure = PairBinding(
            AutoHarvestPair.TreasureTree,
            nativeResolver,
            registry,
            identities,
            out _);
        SetField(resolver, "_shared", shared);
        SetField(resolver, "_fruit", fruit);
        SetField(resolver, "_treasure", treasure);
        var observation = (IAutoHarvestProfileBindingObservation)resolver;

        Assert.True(observation.TryComplete(ServiceCycleProfileTemperature.ColdProcess));
        Assert.Equal(ServiceCycleProfileTemperature.Warm, observation.PrepareTemperature());

        lifecycle++;

        Assert.Equal(
            ServiceCycleProfileTemperature.LifecycleRebind,
            observation.PrepareTemperature());
        Assert.False(observation.TryComplete(ServiceCycleProfileTemperature.Warm));
    }

    private static AutoHarvestSharedBinding SharedBinding(
        TypedRegistryResolver resolver,
        IDictionary<Guid, object> registry,
        IDictionary<object, Guid> identities)
    {
        var active = Resolve(resolver, registry, identities, out _);
        var scaling = Resolve(resolver, registry, identities, out _);
        return new AutoHarvestSharedBinding(
            active.Value!,
            active,
            scaling,
            active.LifecycleGeneration);
    }

    private static AutoHarvestPairBinding PairBinding(
        AutoHarvestPair pair,
        TypedRegistryResolver resolver,
        IDictionary<Guid, object> registry,
        IDictionary<object, Guid> identities,
        out Guid plotId)
    {
        var plot = Resolve(resolver, registry, identities, out plotId);
        var action = Resolve(resolver, registry, identities, out var actionId);
        var reward = Resolve(resolver, registry, identities, out _);
        return new AutoHarvestPairBinding(
            pair,
            plot.Value!,
            action.Value!,
            plotId.ToString("D"),
            actionId.ToString("D"),
            reward.Value!,
            plot,
            action,
            reward);
    }

    private static TypedRegistryResolution Resolve(
        TypedRegistryResolver resolver,
        IDictionary<Guid, object> registry,
        IDictionary<object, Guid> identities,
        out Guid id)
    {
        id = Guid.NewGuid();
        var value = new object();
        registry.Add(id, value);
        identities.Add(value, id);
        return resolver.Resolve(id, typeof(object));
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

}
