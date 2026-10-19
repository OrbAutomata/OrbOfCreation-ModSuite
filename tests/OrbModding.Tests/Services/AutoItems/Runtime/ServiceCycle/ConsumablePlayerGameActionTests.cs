using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class ConsumablePlayerGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();

    public ConsumablePlayerGameActionTests()
    {
        Inventory.Preparing = false;
        TargetingManager.OpenRequests = 0;
        ConsumableSO.All.Clear();
        Inventory.ResetLists();
        EffectResultInfo.SuppressCancel = false;
        EffectResultInfo.ThrowAfterCancel = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        NativeMultiBuyScope.ResetForTests();
    }

    public void Dispose()
    {
        Inventory.Preparing = false;
        TargetingManager.OpenRequests = 0;
        TargetingManager.ThrowOnIsTargeting = false;
        ConsumableSO.All.Clear();
        Inventory.ResetLists();
        EffectResultInfo.SuppressCancel = false;
        EffectResultInfo.ThrowAfterCancel = false;
        NativeMultiBuyScope.ResetForTests();
        GameActionFaultLog.ConfigureLog(null);
    }

    [Fact]
    public void PlayerUseGatesOnlyTheRequestedQueueOutcome()
    {
        var item = Item(AutoItemsConsumableFamily.Thread);
        using var gameAction = GameAction();
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Use,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, item.GetQuantity());
        Assert.Single(item.consumableUsages);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void PlayerUseLetsAnUnexpectedExceptionFromANativeReadEscape()
    {
        var item = Item(AutoItemsConsumableFamily.Thread);
        TargetingManager.ThrowOnIsTargeting = true;
        using var gameAction = GameAction();
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Use,
            item.GetGuid(),
            lifecycleEpoch: 1);

        Assert.Throws<NullReferenceException>(() => gameAction.Submit(in action));
        Assert.Empty(item.consumableUsages);
    }

    [Fact]
    public void PlayerCancelProvesTheExactPendingUsageWasCancelledAndRemoved()
    {
        var item = Item(AutoItemsConsumableFamily.Thread);
        using var gameAction = GameAction();
        var use = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Use,
            item.GetGuid(),
            lifecycleEpoch: 1);
        Assert.True(gameAction.Submit(in use).Verified);
        var usage = Assert.Single(item.consumableUsages);
        var cancel = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Cancel,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var result = gameAction.Submit(in cancel);

        Assert.True(result.Verified, result.Reason);
        Assert.True(usage.GetResultInfo().IsCancelled());
        Assert.Empty(item.consumableUsages);
    }

    [Fact]
    public void PlayerDiscardClampsToTheLiveHoldingAndVerifiesTheRequestedRemoval()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Discard,
            item.GetGuid(),
            lifecycleEpoch: 1,
            amount: 3);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, item.GetQuantity());
    }

    [Fact]
    public void PlayerRandomizationAndInventoryMoveVerifyExactPostState()
    {
        var first = Item(AutoItemsConsumableFamily.Scroll);
        var second = Item(AutoItemsConsumableFamily.Relic);
        Inventory.Current.allConsumables.value.Add(first);
        Inventory.Current.allConsumables.value.Add(second);
        using var gameAction = GameAction();
        var randomize = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.SetRandomization,
            first.GetGuid(),
            lifecycleEpoch: 1,
            randomized: true);
        var move = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Move,
            first.GetGuid(),
            lifecycleEpoch: 1,
            list: ConsumablePlayerListKind.Inventory,
            destination: 1);

        var randomized = gameAction.Submit(in randomize);
        var moved = gameAction.Submit(in move);

        Assert.True(randomized.Verified, randomized.Reason);
        Assert.True(moved.Verified, moved.Reason);
        Assert.True(first.IsRandomized());
        Assert.Equal(new[] { second, first }, Inventory.Current.allConsumables.value);
        Assert.Equal(1, Inventory.Current.allConsumables.UpdateObservableCalls);
    }

    [Fact]
    public void PlayerHotbarMoveRunsTheAuditedSetAtTailAndReturnsTheCompleteOrder()
    {
        var first = Item(AutoItemsConsumableFamily.Scroll);
        var second = Item(AutoItemsConsumableFamily.Relic);
        Inventory.Current.hotBar.value.Add(first);
        Inventory.Current.hotBar.value.Add(second);
        using var gameAction = GameAction();
        var move = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Move,
            first.GetGuid(),
            lifecycleEpoch: 1,
            list: ConsumablePlayerListKind.Hotbar,
            destination: 1);

        var result = gameAction.Submit(in move);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { second, first }, Inventory.Current.hotBar.value);
        Assert.Equal(1, Inventory.Current.hotBar.SetAtCalls);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void PlayerPreflightRefusalsDoNotAttemptNativeMutation()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        item.visible = false;
        using var gameAction = GameAction();
        var use = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Use,
            item.GetGuid(),
            lifecycleEpoch: 1);
        var cancel = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Cancel,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var hidden = gameAction.Submit(in use);
        var noUsage = gameAction.Submit(in cancel);

        Assert.Equal(ConsumablePlayerActionResultCodes.NotVisible, hidden.Code);
        Assert.Equal(ConsumablePlayerActionResultCodes.NoCancellableUsage, noUsage.Code);
        Assert.Equal(0, hidden.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, noUsage.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, item.GetQuantity());
    }

    [Fact]
    public void MissingOnePlayerBindingFailsTheCompleteFamilyClosed()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Discard,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        foreach (var missing in ConsumablePlayerNativeBindings.ContractIds)
        {
            logged.Clear();
            using var gameAction = GameAction(includeContract: id => id != missing);
            var result = gameAction.Submit(in action);

            Assert.False(gameAction.BindingsAvailable);
            Assert.Equal(ConsumablePlayerActionResultCodes.ContractUnavailable, result.Code);

            // Which binding was absent does not change what a player does next, so the answer
            // only says the family is shut for the run. The maintainer who fixes it needs the
            // binding, so that goes to the suite log under the reference the answer ends with.
            Assert.StartsWith(
                "The suite could not attach to Inventory in this run, so it will refuse every " +
                "press there until the run restarts. Reference MCP-",
                result.Reason,
                StringComparison.Ordinal);
            var line = Assert.Single(logged);
            Assert.Contains(missing, line, StringComparison.Ordinal);
        }
        Assert.Equal(1, item.GetQuantity());
    }

    [Fact]
    public void WrongPlayerOutcomeDoesNotBlockTheNextPlayerAction()
    {
        var first = Item(AutoItemsConsumableFamily.Scroll);
        var second = Item(AutoItemsConsumableFamily.Relic);
        Inventory.Current.allConsumables.value.Add(first);
        Inventory.Current.allConsumables.value.Add(second);
        Inventory.Current.allConsumables.SuppressSwap = true;
        using var gameAction = GameAction();
        var move = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Move,
            first.GetGuid(),
            lifecycleEpoch: 1,
            list: ConsumablePlayerListKind.Inventory,
            destination: 1);
        var discard = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Discard,
            second.GetGuid(),
            lifecycleEpoch: 1);

        var faulted = gameAction.Submit(in move);
        var next = gameAction.Submit(in discard);

        Assert.Equal(ConsumablePlayerActionResultCodes.VerificationFailed, faulted.Code);
        Assert.Equal(
            "Consumable Verification failed on " + EntityIdentityFormatter.PlayerName(first.GetGuid()) +
            ": " + GameActionAnswer.ChangeNotSeen("Inventory"),
            faulted.Reason);
        Assert.Equal(new[] { first, second }, Inventory.Current.allConsumables.value);
        Assert.True(next.Verified, next.Reason);
    }

    [Fact]
    public void AnUnreadableLifecycleEpochFaultsBeforeTheNativeCall()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction(
            readLifecycleEpoch: static () => throw new InvalidOperationException("epoch unreadable"));
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Discard,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var result = gameAction.Submit(in action);

        Assert.True(result.IsFault, result.Reason);
        Assert.Equal(ConsumablePlayerActionResultCodes.ContractUnavailable, result.Code);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        Assert.Equal(1, item.GetQuantity());
    }

    [Fact]
    public void AConsumableResolvedFromAReplacedRegistryIsRefusedBeforeAnyPress()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        const int readsWhileResolving = 3;
        var reads = 0;
        using var gameAction = GameAction(readGeneration: () => ++reads <= readsWhileResolving ? 11 : 12);
        var action = new ConsumablePlayerAction(
            ConsumablePlayerActionKind.Discard,
            item.GetGuid(),
            lifecycleEpoch: 1);

        var result = gameAction.Submit(in action);

        Assert.Equal(ConsumablePlayerActionResultCodes.ItemUnavailable, result.Code);
        Assert.Equal(EntityIdentityFormatter.PlayerName(item.GetGuid()) + " became stale.", result.Reason);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        Assert.Equal(1, item.GetQuantity());
    }

    private AutoItemsConsumablePlayerGameAction GameAction(
        Func<long>? readLifecycleEpoch = null,
        Func<string, bool>? includeContract = null,
        Func<long>? readGeneration = null)
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            readGeneration ?? (static () => 1),
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject entity ? entity.GetGuid() : Guid.Empty);
        return new AutoItemsConsumablePlayerGameAction(
            resolver,
            readLifecycleEpoch ?? (static () => 1),
            static () => true,
            static () => string.Empty,
            includeContract: includeContract);
    }

    private ConsumableSO Item(AutoItemsConsumableFamily family) =>
        ConsumableTestItems.Create(_registry, family);
}
