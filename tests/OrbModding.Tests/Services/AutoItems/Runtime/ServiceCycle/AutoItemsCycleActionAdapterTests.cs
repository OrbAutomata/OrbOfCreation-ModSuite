using OrbModding.TestSupport;
using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoItemsCycleActionAdapterTests : IDisposable
{
    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Fact]
    public void EveryFailedOwnershipReadLogsItsCauseWithoutChangingTheFailureSentence()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var failure = new InvalidOperationException("private ownership cause", new Exception("original inner cause"));
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction();
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction, static () => false, () => throw failure, health, static _ => { });
        var config = Configuration(AutoItemsOperationMode.Active);
        var action = new AutoItemsCycleAction(Guid.NewGuid(), AutoItemsConsumableFamily.Relic, 7, 0);
        var context = Context();

        adapter.TryExecute(in action, in config, in context);
        var revision = health.Revision;
        var firstCause = failure.ToString();
        adapter.TryExecute(in action, in config, in context);

        Assert.Equal(2, logged.Count);
        Assert.Contains(firstCause, logged[0]);
        Assert.Contains(failure.ToString(), logged[1]);
        Assert.DoesNotContain("private ownership cause", health.Reason);
        Assert.Contains(logged[1].Split(' ')[3], health.Reason);
        Assert.Equal(revision, health.Revision);
    }

    [Fact]
    public void DirectAdapterUsesOnlyThePinnedConfigurationAndRejectsDisabledIntent()
    {
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction();
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction,
            static () => true,
            static () => string.Empty,
            health,
            static _ => { });
        var config = Configuration(AutoItemsOperationMode.Disabled);
        var action = new AutoItemsCycleAction(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Scroll,
            collectedAtEpoch: 7,
            plannedLevel: 1);
        var context = Context();

        var result = adapter.TryExecute(in action, in config, in context);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
        Assert.False(health.HasFailure);
    }

    [Fact]
    public void DirectAdapterCarriesExactOwnershipFailureIntoHealth()
    {
        const string reason =
            "Automata Auto Items could not claim ConsumableUse; Other Items currently owns it.";
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction();
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction,
            static () => false,
            static () => reason,
            health,
            static _ => { });
        var config = Configuration(AutoItemsOperationMode.Active);
        var action = new AutoItemsCycleAction(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Relic,
            collectedAtEpoch: 7,
            plannedLevel: 0);
        var context = Context();

        var result = adapter.TryExecute(in action, in config, in context);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.True(health.HasFailure);
        Assert.Equal(reason, health.Reason);
    }

    [Fact]
    public void AnUnreadableLifecycleEpochFaults()
    {
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction(
            static () => throw new InvalidOperationException("The lifecycle epoch is unreadable."));
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction,
            static () => true,
            static () => string.Empty,
            health,
            static _ => { });
        var config = Configuration(AutoItemsOperationMode.Active);
        var action = new AutoItemsCycleAction(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Scroll,
            collectedAtEpoch: 7,
            plannedLevel: 1);
        var context = Context();

        var result = adapter.TryExecute(in action, in config, in context);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.ContractUnavailable, result.Code);
    }

    [Fact]
    public void AnExpectedNativeThrowFaultsAsAnAdapterFaultThatKeepsItsCauseBehindAReference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction();
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction,
            static () => true,
            static () => string.Empty,
            health,
            static _ => { });
        var config = Configuration(AutoItemsOperationMode.Active);
        var action = new AutoItemsCycleAction(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Scroll,
            collectedAtEpoch: 7,
            plannedLevel: 1);
        var context = Context();
        global::TargetingManager.IsTargetingFault = new InvalidOperationException("stub targeting read");
        try
        {
            var result = adapter.TryExecute(in action, in config, in context);

            Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
            Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
            Assert.False(health.HasFailure);
            var reference = Assert.Single(logged);
            Assert.Contains("stub targeting read", reference);
            var id = reference.Split(' ')[3];
            Assert.Equal(
                "Auto Items faulted at the native boundary. Reference " + id + " is in the suite log.",
                result.Reason);
        }
        finally
        {
            global::TargetingManager.IsTargetingFault = null;
        }
    }

    [Fact]
    public void AnUnexpectedNativeThrowEscapesTheAdapterInsteadOfBecomingAnAdapterFault()
    {
        var health = new AutoItemsActionHealth();
        using var gameAction = GameAction();
        var adapter = new AutoItemsCycleActionAdapter(
            gameAction,
            static () => true,
            static () => string.Empty,
            health,
            static _ => { });
        var config = Configuration(AutoItemsOperationMode.Active);
        var action = new AutoItemsCycleAction(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Scroll,
            collectedAtEpoch: 7,
            plannedLevel: 1);
        var context = Context();
        global::TargetingManager.IsTargetingFault = new NullReferenceException("stub targeting read");
        try
        {
            Assert.Throws<NullReferenceException>(() => adapter.TryExecute(in action, in config, in context));
            Assert.False(health.HasFailure);
        }
        finally
        {
            global::TargetingManager.IsTargetingFault = null;
        }
    }

    private static AutoItemsConsumableUseGameAction GameAction(Func<long>? readLifecycleEpoch = null)
    {
        IDictionary registry = new System.Collections.Generic.Dictionary<Guid, object>();
        return new AutoItemsConsumableUseGameAction(
            TestRegistryResolver.For(
                static () => 7,
                () => TypedRegistrySourceSnapshot.Ready(registry),
                static _ => Guid.Empty),
            readLifecycleEpoch ?? (static () => 7),
            static () => true,
            static () => string.Empty);
    }

    private static SuiteRuntimeConfiguration Configuration(AutoItemsOperationMode mode) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = mode,
                UseScrolls = true,
                UseRelics = true,
            },
        };

    private static ServiceActionContext Context() =>
        new(
            new ServiceCycleIdentity(
                AutoItemsServicePolicies.ServiceId,
                new LifecycleGeneration(7),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(1));
}
