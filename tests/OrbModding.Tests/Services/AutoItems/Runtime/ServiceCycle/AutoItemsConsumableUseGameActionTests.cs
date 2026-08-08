using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsConsumableUseGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();

    public AutoItemsConsumableUseGameActionTests()
    {
        Inventory.Preparing = false;
        TargetingManager.OpenRequests = 0;
        ConsumableSO.All.Clear();
        Inventory.ResetLists();
        EffectResultInfo.SuppressCancel = false;
        EffectResultInfo.ThrowAfterCancel = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    public void Dispose()
    {
        Inventory.Preparing = false;
        TargetingManager.OpenRequests = 0;
        ConsumableSO.All.Clear();
        Inventory.ResetLists();
        EffectResultInfo.SuppressCancel = false;
        EffectResultInfo.ThrowAfterCancel = false;
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    [Fact]
    public void ScrollSubmissionUsesScopedTargetRevalidationAndVerifiesExactEvidence()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.True(scroll.IsRandomized());
        Assert.Equal(0, scroll.GetQuantity());
        Assert.Equal(1, scroll.GetQueued());
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.CallOutcome.MutationAttempts);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void RelicSubmissionVerifiesOneNativeTransaction()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, relic.GetQuantity());
        Assert.Equal(1, relic.GetQueued());
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void ContinuousCoconutFruitRelicTopologyRevalidatesAsPermanentRelic()
    {
        var relic = Item(
            AutoItemsConsumableFamily.Relic,
            itemId: Guid.Parse("a1799c52-f9ff-4556-b052-f577ac3e7270"));
        var fruit = new ConsumableTypeSO();
        fruit.SetGuid(KnownEntities.ConsumableFruitType.Uuid);
        relic.consumableTypes.Insert(0, fruit);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, relic.GetQuantity());
        Assert.Equal(1, relic.GetQueued());
        Assert.Empty(relic.consumableUsages);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);

        var incoherent = Item(AutoItemsConsumableFamily.Relic);
        var potion = new ConsumableTypeSO();
        potion.SetGuid(KnownEntities.ConsumablePotionType.Uuid);
        incoherent.consumableTypes.Insert(0, potion);
        var incoherentResult = gameAction.Submit(
            Action(incoherent, AutoItemsConsumableFamily.Relic));

        Assert.Equal(AutoItemsPreflight.FamilyChanged, incoherentResult.Preflight);
        Assert.Contains("[Relic, Potion]", incoherentResult.Reason);
        Assert.Equal(1, incoherent.GetQuantity());
        Assert.Equal(0, incoherentResult.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void TemporarySubmissionModelsStockDurationToxicityAndUsageEvidence()
    {
        var temporary = Item(AutoItemsConsumableFamily.Thread);
        var toxicity = temporary.consumeCost.costs[0].resource;
        using var gameAction = GameAction();
        var action = Action(temporary, AutoItemsConsumableFamily.Thread);

        var result = gameAction.Submit(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, temporary.GetQuantity());
        Assert.Equal(1, temporary.GetQueued());
        var usage = Assert.Single(temporary.consumableUsages);
        Assert.False(usage.en);
        Assert.Equal(new BigDouble(60), usage.dr);
        Assert.Equal(new BigDouble(60), usage.maxDr);
        Assert.Equal(new BigDouble(4), toxicity.quantity);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void TemporaryBoundaryRepeatsDurationAndToxicityOnlyCostChecks()
    {
        var durationChanged = Item(AutoItemsConsumableFamily.Thread);
        durationChanged.durationBase = double.PositiveInfinity;
        using var gameAction = GameAction();
        var durationAction = Action(durationChanged, AutoItemsConsumableFamily.Thread);

        var durationResult = gameAction.Submit(in durationAction);

        Assert.Equal(AutoItemsPreflight.TemporaryDurationChanged, durationResult.Preflight);
        Assert.Contains("durationBase", durationResult.Reason);

        var costChanged = Item(AutoItemsConsumableFamily.Potion);
        var otherResource = new ResourceSO();
        costChanged.usageCost.costs.Add(new ResourceTuple(otherResource, new BigDouble(1)));
        var costAction = Action(costChanged, AutoItemsConsumableFamily.Potion);

        var costResult = gameAction.Submit(in costAction);

        Assert.Equal(AutoItemsPreflight.TemporaryCostChanged, costResult.Preflight);
        Assert.Contains("extra resource", costResult.Reason);
    }

    [Fact]
    public void TemporaryUsageBlocksScrollAndAnotherTemporaryItem()
    {
        var active = Item(AutoItemsConsumableFamily.Fruit);
        active.consumableUsages.Add(new ConsumableUsage
        {
            en = true,
            dr = new BigDouble(30),
            maxDr = new BigDouble(60),
        });
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        var anotherTemporary = Item(AutoItemsConsumableFamily.Thread);
        using var gameAction = GameAction();
        var scrollAction = Action(scroll, AutoItemsConsumableFamily.Scroll);
        var temporaryAction = Action(anotherTemporary, AutoItemsConsumableFamily.Thread);

        var blockedScroll = gameAction.Submit(in scrollAction);
        var blockedTemporary = gameAction.Submit(in temporaryAction);

        Assert.Equal(AutoItemsPreflight.TemporaryEffectPresent, blockedScroll.Preflight);
        Assert.Contains("pending or active", blockedScroll.Reason);
        Assert.Equal(AutoItemsPreflight.TemporaryEffectPresent, blockedTemporary.Preflight);
        Assert.Equal(1, scroll.GetQuantity());
        Assert.Equal(1, anotherTemporary.GetQuantity());
    }

    [Fact]
    public void NativeScrollOrRelicPreparationBlocksTemporaryUse()
    {
        Inventory.Preparing = true;
        var temporary = Item(AutoItemsConsumableFamily.Thread);
        using var gameAction = GameAction();
        var action = Action(temporary, AutoItemsConsumableFamily.Thread);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.NativeBusy, result.Preflight);
        Assert.Contains("Inventory.CanUseConsumable()", result.Reason);
        Assert.Equal(1, temporary.GetQuantity());
        Assert.Empty(temporary.consumableUsages);
    }

    [Fact]
    public void AmbiguousTemporaryMutationQuarantinesOnlyTheExactItem()
    {
        var broken = Item(AutoItemsConsumableFamily.Thread);
        var healthy = Item(AutoItemsConsumableFamily.Thread);
        broken.SelectionNoOp = true;
        using var gameAction = GameAction();
        var brokenAction = Action(broken, AutoItemsConsumableFamily.Thread);
        var healthyAction = Action(healthy, AutoItemsConsumableFamily.Thread);

        var ambiguous = gameAction.Submit(in brokenAction);
        var exactBlocked = gameAction.Submit(in brokenAction);
        var unrelated = gameAction.Submit(in healthyAction);

        Assert.Equal(AutoItemsPreflight.Quarantined, ambiguous.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, ambiguous.Outcome);
        Assert.Equal(AutoItemsPreflight.Quarantined, exactBlocked.Preflight);
        Assert.Contains(broken.GetGuid().ToString("D"), exactBlocked.Reason);
        Assert.True(unrelated.Verified, unrelated.Reason);
    }

    [Fact]
    public void ChangedFamilyDoesNotMutate()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(item, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.FamilyChanged, result.Preflight);
        Assert.Contains("Expected live execution family Scroll", result.Reason);
        Assert.Equal(1, item.GetQuantity());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void NativeBusyRefusalNamesInventoryValidatorAndDoesNotMutate()
    {
        Inventory.Preparing = true;
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.NativeBusy, result.Preflight);
        Assert.Contains("Inventory.CanUseConsumable()", result.Reason);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
    }

    [Fact]
    public void LiveTargetingInteractionRefusesBeforeAnyConsumableMutation()
    {
        TargetingManager.OpenRequests = 1;
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TargetingInProgress, result.Preflight);
        Assert.Contains("TargetingManager.IsTargeting()", result.Reason);
        Assert.Equal(1, relic.GetQuantity());
        Assert.Equal(0, relic.GetQueued());
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LostMutationPermitPreservesTheExactOwnershipReason()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction(
            static () => false,
            static () =>
                "Automata Auto Items could not claim ConsumableUse; Other Items owns it.");
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.MutationPermitUnavailable, result.Preflight);
        Assert.Equal(
            "Automata Auto Items could not claim ConsumableUse; Other Items owns it.",
            result.Reason);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
    }

    [Fact]
    public void ManualStockRaceAfterPreflightIsAttemptedAndFailsExactPostcondition()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        using var gameAction = GameAction(
            () =>
            {
                scroll.SetStock(0, 0, 0);
                return true;
            });
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.Quarantined, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
        Assert.Contains("ambiguous consumable mutation", result.Reason);
        Assert.Contains("quarantined", result.Reason);
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 0), result.CallOutcome);
    }

    [Fact]
    public void MissingLiveStockIsRefusedByCanFireBeforeMutation()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll);
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);
        scroll.SetStock(0, 0, 0);
        using var gameAction = GameAction();

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.CanFireRefused, result.Preflight);

        // The player is told the game refuses, not which native predicate said so. The code
        // carries that distinction; the sentence carries what a player can act on.
        Assert.Equal(
            "The game refuses to use " + scroll.GetGuid().ToString("D") + " right now.",
            result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void TargetUnavailableHasAnExactNonEmptyExplanation()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll, withTarget: false);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.TargetUnavailable, result.Preflight);
        Assert.Contains("no valid structure target", result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AmbiguousPostconditionQuarantinesTheWholeGameActionForTheLifecycle()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var ambiguous = gameAction.Submit(in action);
        var blocked = gameAction.Submit(in action);

        Assert.Equal(AutoItemsPreflight.Quarantined, ambiguous.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, ambiguous.Outcome);
        Assert.Equal(AutoItemsPreflight.Quarantined, blocked.Preflight);
        Assert.Contains(relic.GetGuid().ToString("D"), blocked.Reason);
        Assert.Equal(0, blocked.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void LifecycleResetRebindsTheCompleteContractAndClearsQuarantine()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);
        Assert.False(gameAction.Submit(in action).Verified);

        relic.SelectionNoOp = false;
        gameAction.InvalidateLifecycle();
        var retried = gameAction.Submit(in action);

        Assert.True(gameAction.BindingsAvailable, gameAction.BindingFailure);
        Assert.True(retried.Verified, retried.Reason);
    }

    [Fact]
    public void PlayerUseUsesTheSharedGameActionAndGatesOnlyTheRequestedQueueOutcome()
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
        Assert.Equal(new NativeMutationCallOutcome(3, 1, 1), result.CallOutcome);
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

        Assert.Equal(ConsumablePlayerPreflight.NotVisible, hidden.Preflight);
        Assert.Equal(ConsumablePlayerPreflight.NoCancellableUsage, noUsage.Preflight);
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

        foreach (var missing in ConsumablePlayerNativeBindings.ContractIds)
        {
            using var gameAction = GameAction(
                includePlayerContract: id => id != missing);
            var result = gameAction.Submit(in action);

            Assert.False(gameAction.PlayerBindingsAvailable);
            Assert.Equal(ConsumablePlayerPreflight.ContractUnavailable, result.Preflight);
            Assert.Contains(missing, result.Reason);
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

        Assert.Equal(ConsumablePlayerPreflight.VerificationFailed, faulted.Preflight);
        Assert.Equal(new[] { first, second }, Inventory.Current.allConsumables.value);
        Assert.True(next.Verified, next.Reason);
    }

    private AutoItemsConsumableUseGameAction GameAction(
        Func<bool>? permit = null,
        Func<string>? failure = null,
        Func<long>? readLifecycleEpoch = null,
        Func<string, bool>? includePlayerContract = null)
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            static () => 1,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject entity ? entity.GetGuid() : null);
        return new AutoItemsConsumableUseGameAction(
            resolver,
            permit ?? (static () => true),
            failure ?? (static () => string.Empty),
            readLifecycleEpoch,
            includePlayerContract: includePlayerContract);
    }

    private ConsumableSO Item(
        AutoItemsConsumableFamily family,
        bool withTarget = true,
        Guid? itemId = null)
    {
        var familyType = new ConsumableTypeSO();
        familyType.SetGuid(FamilyId(family));
        var item = new ConsumableSO
        {
            visible = true,
            canBeRandomized = family == AutoItemsConsumableFamily.Scroll,
        };
        item.SetGuid(itemId ?? Guid.NewGuid());
        item.SetStock(1, 0, 0);
        item.consumableTypes.Add(familyType);
        if (family == AutoItemsConsumableFamily.Scroll)
        {
            item.consumableCounts.Add(new ConsumableCount { Level = 1, Quantity = 1 });
            var targeting = new Targeting.TargetStructure();
            if (withTarget) targeting.Candidates.Add(new Target());
            var request = new RequestTargetEffectScript
            {
                targetOptions = new Targeting.TargetSelectOptions { Targeting = targeting },
            };
            var block = new InstantEffectBlock();
            block.effectScripts.Add(request);
            item.onUseEffects.Add(block);
        }
        if (AutoItemsConsumableFamilies.IsTemporary(family))
        {
            var toxicity = new ResourceSO
            {
                uuid = KnownEntities.PotionToxicity.Uuid.ToString("D"),
                quantity = new BigDouble(5),
            };
            item.hasDuration = true;
            item.durationBase = 60;
            item.consumeCost.costs.Add(
                new ResourceTuple(toxicity, new BigDouble(1)));
        }
        ConsumableSO.All.Add(item);
        _registry.Add(item.GetGuid(), item);
        return item;
    }

    private static Guid FamilyId(AutoItemsConsumableFamily family) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => KnownEntities.ConsumableScrollType.Uuid,
            AutoItemsConsumableFamily.Relic => KnownEntities.ConsumableRelicType.Uuid,
            AutoItemsConsumableFamily.Fruit => KnownEntities.ConsumableFruitType.Uuid,
            AutoItemsConsumableFamily.Potion => KnownEntities.ConsumablePotionType.Uuid,
            AutoItemsConsumableFamily.Thread => KnownEntities.ConsumableThreadType.Uuid,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };

    private static AutoItemsCycleAction Action(
        ConsumableSO item,
        AutoItemsConsumableFamily family) =>
        new(item.GetGuid(), family, collectedAtEpoch: 1, plannedLevel: family ==
            AutoItemsConsumableFamily.Scroll ? 1 : 0);

    private sealed class Target : Targeting.ITargetable
    {
    }
}
