using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class ConsumableUseCompositionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();

    public ConsumableUseCompositionTests()
    {
        OrbModding.GameStubs.GameStubState.Reset();
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
        NativeMultiBuyScope.ResetForTests();
    }

    public void Dispose()
    {
        NativeMultiBuyScope.ResetForTests();
        NativeMultiBuyScope.ConfigureFailureLog(null);
        GameActionFaultLog.ConfigureLog(null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothUseCallersCommitVisibleQueueGrowthWhenMultiBuyRestorationUnexpectedlyThrows(
        bool automation)
    {
        var clock = new VirtualMonotonicClock();
        NativeMultiBuyScope.ResetForTests(clock: clock);
        var restoreWarnings = new List<string>();
        NativeMultiBuyScope.ConfigureFailureLog(restoreWarnings.Add);
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var item = ConsumableTestItems.Create(_registry, AutoItemsConsumableFamily.Thread);
        item.SetStock(9, 0, 0);
        GlobalVariables.MultiBuy.SetFailure = message => new NullReferenceException(message);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;
        using var actions = new LifecycleGameActions();
        var composed = Compose(actions);
        using var player = PlayerAction();

        var result = automation
            ? composed.GameAction.Submit(new AutoItemsCycleAction(
                item.GetGuid(), AutoItemsConsumableFamily.Thread, 1, 0))
            : player.Submit(new ConsumablePlayerAction(ConsumablePlayerActionKind.Use, item.GetGuid(), 1));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
        Assert.Equal(1, item.GetQueued());
        Assert.Equal(8, item.GetQuantity());
        Assert.Single(item.SelectAndFireTimestamps);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.Contains(logged, line => line.Contains("NullReferenceException", StringComparison.Ordinal) &&
            line.Contains("setter rejected 7 before write", StringComparison.Ordinal));
        Assert.Contains("attempt 1", Assert.Single(restoreWarnings), StringComparison.Ordinal);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));
        Assert.Contains("restoration to 7 is outstanding", reason, StringComparison.Ordinal);

        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = null;
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out reason), reason);
        scope.Dispose();

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.Equal(2, restoreWarnings.Count);
        Assert.Contains("restoration to 7 verified", restoreWarnings[1], StringComparison.Ordinal);
        Assert.Single(item.SelectAndFireTimestamps);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void BothUseCallersVerifyOneQueueEntryAndRestoreThePlayersMultiBuy(
        bool automation,
        bool throwAfterQueueEntry)
    {
        var item = ConsumableTestItems.Create(_registry, AutoItemsConsumableFamily.Thread);
        item.SetStock(9, 0, 0);
        if (throwAfterQueueEntry) item.consumeCost.ThrowAfterCostRows = 1;
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        using var actions = new LifecycleGameActions();
        var composed = Compose(actions);
        using var player = PlayerAction();

        var result = automation
            ? composed.GameAction.Submit(new AutoItemsCycleAction(
                item.GetGuid(), AutoItemsConsumableFamily.Thread, 1, 0))
            : player.Submit(new ConsumablePlayerAction(ConsumablePlayerActionKind.Use, item.GetGuid(), 1));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, item.GetQueued());
        Assert.Equal(8, item.GetQuantity());
        Assert.Equal(7, GlobalVariables.GetMultiBuy().AsInt());
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
        Assert.Single(item.SelectAndFireTimestamps);
        if (throwAfterQueueEntry)
            Assert.Contains(logged, line => line.Contains("injected failure after 1 cost rows", StringComparison.Ordinal));
        else
            Assert.Empty(logged);
    }

    [Fact]
    public void AutomationRetainsTargetAndRandomizationPolicyWhilePlayerUseKeepsItsChoice()
    {
        var item = ConsumableTestItems.Create(_registry, AutoItemsConsumableFamily.Scroll, withTarget: false);
        using var actions = new LifecycleGameActions();
        var composed = Compose(actions);
        using var player = PlayerAction();

        var automated = composed.GameAction.Submit(
            new AutoItemsCycleAction(item.GetGuid(), AutoItemsConsumableFamily.Scroll, 1, 1));
        var explicitUse = player.Submit(
            new ConsumablePlayerAction(ConsumablePlayerActionKind.Use, item.GetGuid(), 1));

        Assert.Equal(AutoItemsActionResultCodes.TargetUnavailable, automated.Code);
        Assert.True(explicitUse.Verified, explicitUse.Reason);
        Assert.False(item.IsRandomized());
        Assert.Equal(1, item.GetQueued());
        Assert.Equal(7, GlobalVariables.GetMultiBuy().AsInt());
    }

    [Fact]
    public void CompositionRegistersTheShippingAutomationActionForLifecycleAndDisposal()
    {
        var item = ConsumableTestItems.Create(_registry, AutoItemsConsumableFamily.Relic);
        var actions = new LifecycleGameActions();
        var composed = Compose(actions);
        var action = new AutoItemsCycleAction(item.GetGuid(), AutoItemsConsumableFamily.Relic, 1, 0);
        Assert.Same(composed.GameAction, actions.Find<AutoItemsConsumableUseGameAction>());

        actions.InvalidateLifecycle();
        var rebound = composed.GameAction.Submit(in action);
        actions.Dispose();
        var disposed = composed.GameAction.Submit(in action);

        Assert.True(rebound.Verified, rebound.Reason);
        Assert.False(composed.GameAction.BindingsAvailable);
        Assert.Equal(AutoItemsActionResultCodes.ContractUnavailable, disposed.Code);
        Assert.Single(item.SelectAndFireTimestamps);
    }

    [Fact]
    public void ComposedAdapterLogsRetryableNativeFailureThroughTheInjectedWarning()
    {
        var warnings = new List<string>();
        using var actions = new LifecycleGameActions();
        var composed = Compose(actions, warnings.Add);
        var action = new AutoItemsCycleAction(Guid.NewGuid(), AutoItemsConsumableFamily.Relic, 1, 0);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration { Mode = AutoItemsOperationMode.Active, UseRelics = true },
        };
        TargetingManager.IsTargetingFault = new InvalidOperationException("injected Items warning cause");

        var result = composed.Definition.TryExecute(in action, in config, default);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Contains("injected Items warning cause", Assert.Single(warnings));
        Assert.False(composed.Health.HasFailure);
    }

    private AutoItemsServiceAdapterComposition Compose(LifecycleGameActions actions, Action<string>? warn = null) =>
        AutoItemsServiceAdapterComposition.Create(new AutoItemsFeatureDependencies(
            TestRegistryResolver.For(static () => 1, (IDictionary)_registry),
            static () => 1, static () => true, static () => true, static () => string.Empty,
            Status(), warn ?? (static _ => { })), actions);

    private AutoItemsConsumablePlayerGameAction PlayerAction() => new(
        TestRegistryResolver.For(static () => 1, (IDictionary)_registry),
        static () => 1, static () => true, static () => string.Empty);

    private static AutomataFeatureStatusReporter Status() => new(
        new FeatureStatusRegistry(),
        new FeatureStatusSnapshot(new FeatureStatusKey(PluginIds.SuiteGuid, AutoItemsFeatureContribution.FeatureId),
            "Auto Items", true, FeatureStatusState.NotReady,
            new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"), lifecycleGeneration: 1));
}
