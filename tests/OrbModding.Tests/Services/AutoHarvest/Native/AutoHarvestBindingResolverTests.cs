using OrbModding.TestSupport;
using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestBindingResolverTests
{
    [Theory]
    [InlineData(
        (int)AutoHarvestPair.FruitTree,
        AutoHarvestKnownIds.FruitTreePlot,
        AutoHarvestKnownIds.FruitTreeCollect,
        AutoHarvestKnownIds.FruitTreeRewardPool)]
    [InlineData(
        (int)AutoHarvestPair.TreasureTree,
        AutoHarvestKnownIds.TreasureTreePlot,
        AutoHarvestKnownIds.TreasureTreeCollect,
        AutoHarvestKnownIds.TreasureTreeRewardPool)]
    public void PairSpecificationKeepsGeneratedIdentities(
        int pairValue,
        string plotUuid,
        string actionUuid,
        string rewardPoolUuid)
    {
        var pair = (AutoHarvestPair)pairValue;
        var specification = AutoHarvestPairSpecification.For(pair);

        Assert.Equal(pair, specification.Pair);
        Assert.Equal(plotUuid, specification.PlotUuid);
        Assert.Equal(actionUuid, specification.ActionUuid);
        Assert.Equal(rewardPoolUuid, specification.RewardPool.Uuid.ToString("D"));
    }

    [Fact]
    public void PairSetReturnsBothBindingsInEachResolvedSnapshot()
    {
        var shared = new AutoHarvestSharedBinding(
            new object(),
            null!,
            null!,
            lifecycleGeneration: 7);
        var fruit = PairBinding(AutoHarvestPair.FruitTree);
        var treasure = PairBinding(AutoHarvestPair.TreasureTree);

        var pairs = AutoHarvestResolvedPairSet.Create(
            null!,
            shared,
            fruit,
            default,
            treasure,
            default);

        Assert.True(pairs.Fruit.Succeeded);
        Assert.True(pairs.Treasure.Succeeded);
        Assert.Same(fruit, pairs.Treasure.Pair.Fruit);
        Assert.Same(treasure, pairs.Fruit.Pair.Treasure);
        Assert.Equal(7, pairs.Fruit.Pair.LifecycleGeneration);
        Assert.Equal(7, pairs.Treasure.Pair.LifecycleGeneration);
    }

    [Fact]
    public void PairSetCoherenceRejectsTargetFromPreviousLifecycle()
    {
        var lifecycle = 7L;
        var registry = new Dictionary<Guid, object>();
        var identities = new Dictionary<object, Guid>();
        var resolver = TestRegistryResolver.For(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.Ready((IDictionary)registry),
            value => identities[value]);
        var sharedSeven = SharedBinding(resolver, registry, identities);
        var fruitSeven = PairBinding(
            AutoHarvestPair.FruitTree,
            resolver,
            registry,
            identities);

        lifecycle = 8;
        registry.Clear();
        var sharedEight = SharedBinding(resolver, registry, identities);
        var treasureEight = PairBinding(
            AutoHarvestPair.TreasureTree,
            resolver,
            registry,
            identities);

        Assert.False(AutoHarvestBindingCoherence.IsCurrent(
            resolver,
            sharedEight,
            fruitSeven,
            treasureEight));
        Assert.True(AutoHarvestBindingCoherence.IsCurrent(
            resolver,
            sharedEight,
            null,
            treasureEight));
        Assert.True(AutoHarvestBindingCoherence.IsCurrent(
            resolver,
            sharedEight,
            treasureEight));
        Assert.False(AutoHarvestBindingCoherence.IsCurrent(
            resolver,
            sharedEight,
            fruitSeven));
        Assert.Equal(7, sharedSeven.LifecycleGeneration);
    }

    [Fact]
    public void PairCircuitAdmissionRunsOnceBeforeSharedResolution()
    {
        var circuit = new BlockingCircuit();
        var resolver = new AutoHarvestBindingResolver(
            TestRegistryResolver.For(
                () => throw new InvalidOperationException("shared resolution was not admitted"),
                () => throw new InvalidOperationException("shared resolution was not admitted"),
                _ => throw new InvalidOperationException("shared resolution was not admitted")),
            circuit);

        var pairs = resolver.ResolvePairSet();

        Assert.False(pairs.Fruit.Succeeded);
        Assert.False(pairs.Treasure.Succeeded);
        Assert.Equal(AutoHarvestRuntimeFailureScope.Pair, pairs.Fruit.Failure.Scope);
        Assert.Equal(AutoHarvestRuntimeFailureScope.Pair, pairs.Treasure.Failure.Scope);
        Assert.Equal(1, circuit.FruitReads);
        Assert.Equal(1, circuit.TreasureReads);
    }

    private static AutoHarvestPairBinding PairBinding(AutoHarvestPair pair) =>
        new(
            pair,
            new object(),
            new object(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            new object(),
            null!,
            null!,
            null!);

    private static AutoHarvestSharedBinding SharedBinding(
        TypedRegistryResolver resolver,
        IDictionary<Guid, object> registry,
        IDictionary<object, Guid> identities)
    {
        var active = Resolve(resolver, registry, identities);
        var scaling = Resolve(resolver, registry, identities);
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
        IDictionary<object, Guid> identities) =>
        new(
            pair,
            new object(),
            new object(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            new object(),
            Resolve(resolver, registry, identities),
            Resolve(resolver, registry, identities),
            Resolve(resolver, registry, identities));

    private static TypedRegistryResolution Resolve(
        TypedRegistryResolver resolver,
        IDictionary<Guid, object> registry,
        IDictionary<object, Guid> identities)
    {
        var uuid = Guid.NewGuid();
        var value = new object();
        registry.Add(uuid, value);
        identities.Add(value, uuid);
        return resolver.Resolve(uuid, typeof(object));
    }

    private sealed class BlockingCircuit : IAutoHarvestContractCircuit
    {
        public void InvalidateLifecycle() { }

        public int FruitReads { get; private set; }
        public int TreasureReads { get; private set; }

        public AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair)
        {
            if (pair == AutoHarvestPair.FruitTree) FruitReads++;
            else if (pair == AutoHarvestPair.TreasureTree) TreasureReads++;
            return AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Contract,
                AutoHarvestRuntimeFailureScope.Pair);
        }

        public void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope) =>
            throw new NotSupportedException();
    }
}
