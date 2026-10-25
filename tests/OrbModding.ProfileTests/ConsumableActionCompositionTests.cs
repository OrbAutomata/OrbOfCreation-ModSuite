using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
public sealed class ConsumableActionCompositionTests
{
    [Fact]
    public void ProfileCompositionOwnsBothConsumableCallersThroughTheSameLifecycle()
    {
        GlobalVariables.MultiBuy = new IntVariable { Value = 8 };
        NativeMultiBuyScope.ResetForTests();
        var epoch = 1L;
        IDictionary registry = new Dictionary<Guid, object>();
        var item = new ConsumableSO { visible = true };
        item.SetGuid(Guid.NewGuid());
        item.SetStock(2, 0, 0);
        var family = new ConsumableTypeSO();
        family.SetGuid(KnownEntities.ConsumableRelicType.Uuid);
        item.consumableTypes.Add(family);
        ConsumableSO.All.Add(item);
        registry.Add(item.GetGuid(), item);
        using var actions = new LifecycleGameActions();
        var composed = AutoItemsServiceAdapterComposition.Create(new AutoItemsFeatureDependencies(
            TestRegistryResolver.For(() => epoch, registry), () => epoch,
            static () => true, static () => true, static () => string.Empty,
            new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(new FeatureStatusKey(PluginIds.SuiteGuid, AutoItemsFeatureContribution.FeatureId),
                    "Auto Items", true, FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"), lifecycleGeneration: 1)),
            static _ => { }), actions);
        var player = Assert.IsType<AutoItemsConsumablePlayerGameAction>(
            actions.Find<AutoItemsConsumablePlayerGameAction>());
        Assert.Same(composed.GameAction, actions.Find<AutoItemsConsumableUseGameAction>());
        epoch = 2;
        actions.InvalidateLifecycle();

        var stale = composed.GameAction.Submit(
            new AutoItemsCycleAction(item.GetGuid(), AutoItemsConsumableFamily.Relic, 1, 0));
        var automated = composed.GameAction.Submit(
            new AutoItemsCycleAction(item.GetGuid(), AutoItemsConsumableFamily.Relic, 2, 0));
        Inventory.Preparing = false;
        var explicitUse = player.Submit(new ConsumablePlayerAction(ConsumablePlayerActionKind.Use, item.GetGuid(), 2));
        actions.Dispose();

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, stale.Code);
        Assert.True(automated.Verified, automated.Reason);
        Assert.True(explicitUse.Verified, explicitUse.Reason);
        Assert.Equal(2, item.GetQueued());
        Assert.Equal(8, GlobalVariables.GetMultiBuy().AsInt());
        Assert.False(composed.GameAction.BindingsAvailable);
        Assert.False(player.BindingsAvailable);
        Assert.Equal(ConsumablePlayerActionResultCodes.ContractUnavailable,
            player.Submit(new ConsumablePlayerAction(ConsumablePlayerActionKind.Use, item.GetGuid(), 2)).Code);
        NativeMultiBuyScope.ResetForTests();
    }
}
