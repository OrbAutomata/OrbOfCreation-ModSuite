using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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
        TargetingManager.ThrowOnIsTargeting = false;
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

        Assert.Equal(AutoItemsActionResultCodes.FamilyChanged, incoherentResult.Code);
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

        Assert.Equal(AutoItemsActionResultCodes.TemporaryDurationChanged, durationResult.Code);
        Assert.Contains("durationBase", durationResult.Reason);

        var costChanged = Item(AutoItemsConsumableFamily.Potion);
        var otherResource = new ResourceSO();
        costChanged.usageCost.costs.Add(new ResourceTuple(otherResource, new BigDouble(1)));
        var costAction = Action(costChanged, AutoItemsConsumableFamily.Potion);

        var costResult = gameAction.Submit(in costAction);

        Assert.Equal(AutoItemsActionResultCodes.TemporaryCostChanged, costResult.Code);
        Assert.Contains("extra resource", costResult.Reason);
    }

    [Fact]
    public void AnActiveTemporaryEffectBlocksOnlyAnotherTemporaryItem()
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

        var blockedTemporary = gameAction.Submit(in temporaryAction);
        var firedScroll = gameAction.Submit(in scrollAction);

        Assert.Equal(AutoItemsActionResultCodes.TemporaryEffectPresent, blockedTemporary.Code);
        Assert.Contains("pending or active", blockedTemporary.Reason);
        Assert.Equal(1, anotherTemporary.GetQuantity());
        Assert.True(firedScroll.Verified, firedScroll.Reason);
        Assert.Equal(1, scroll.GetQueued());
    }

    [Fact]
    public void ATemporaryItemStillPreparingHoldsARelicAtTheGamesOwnGate()
    {
        var temporary = Item(AutoItemsConsumableFamily.Thread);
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var temporaryAction = Action(temporary, AutoItemsConsumableFamily.Thread);
        var relicAction = Action(relic, AutoItemsConsumableFamily.Relic);

        var submitted = gameAction.Submit(in temporaryAction);
        var held = gameAction.Submit(in relicAction);

        Assert.True(submitted.Verified, submitted.Reason);
        Assert.Equal(AutoItemsActionResultCodes.NativeBusy, held.Code);
        Assert.Equal(1, relic.GetQuantity());
    }

    [Fact]
    public void NativeScrollOrRelicPreparationBlocksTemporaryUse()
    {
        Inventory.Preparing = true;
        var temporary = Item(AutoItemsConsumableFamily.Thread);
        using var gameAction = GameAction();
        var action = Action(temporary, AutoItemsConsumableFamily.Thread);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsActionResultCodes.NativeBusy, result.Code);
        Assert.Contains("Inventory.CanUseConsumable()", result.Reason);
        Assert.Equal(1, temporary.GetQuantity());
        Assert.Empty(temporary.consumableUsages);
    }

    [Fact]
    public void AnUnverifiedTemporaryUseFaultsEachTimeItIsTriedAndBlocksNoOtherItem()
    {
        var broken = Item(AutoItemsConsumableFamily.Thread);
        var healthy = Item(AutoItemsConsumableFamily.Thread);
        broken.SelectionNoOp = true;
        using var gameAction = GameAction();
        var brokenAction = Action(broken, AutoItemsConsumableFamily.Thread);
        var healthyAction = Action(healthy, AutoItemsConsumableFamily.Thread);

        var first = gameAction.Submit(in brokenAction);
        var again = gameAction.Submit(in brokenAction);
        var unrelated = gameAction.Submit(in healthyAction);

        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, first.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, first.Mutation);
        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, again.Code);
        Assert.Equal(1, again.CallOutcome.MutationAttempts);
        Assert.Equal(2, broken.SelectAndFireTimestamps.Count);
        Assert.True(unrelated.Verified, unrelated.Reason);
    }

    [Fact]
    public void ChangedFamilyDoesNotMutate()
    {
        var item = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(item, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsActionResultCodes.FamilyChanged, result.Code);
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

        Assert.Equal(AutoItemsActionResultCodes.NativeBusy, result.Code);
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

        Assert.Equal(AutoItemsActionResultCodes.TargetingInProgress, result.Code);
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

        Assert.Equal(AutoItemsActionResultCodes.MutationPermitUnavailable, result.Code);
        Assert.Equal(
            "Automata Auto Items could not claim ConsumableUse; Other Items owns it.",
            result.Reason);
        Assert.False(scroll.randomized);
        Assert.Equal(1, scroll.GetQuantity());
    }

    [Fact]
    public void AUseWhoseQueueCannotBeReadAfterwardsSaysSoRatherThanThatItDidNotEnterPreparation()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.QueueUnreadableAfterFire = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action);

        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, result.Code);
        Assert.Contains("its queue could not be read afterwards", result.Reason);
        Assert.DoesNotContain("did not enter native preparation", result.Reason);
        Assert.DoesNotContain("injected queue outage", result.Reason);
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

        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, result.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Mutation);
        Assert.Contains("did not enter native preparation", result.Reason);
        Assert.Contains(scroll.GetGuid().ToString("D"), result.Reason);
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

        Assert.Equal(AutoItemsActionResultCodes.CanFireRefused, result.Code);

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

        Assert.Equal(AutoItemsActionResultCodes.TargetUnavailable, result.Code);
        Assert.Contains("no valid structure target", result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void AnUnverifiedRelicUseLeavesTheGameActionOpenForTheNextAttempt()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var unverified = gameAction.Submit(in action);
        relic.SelectionNoOp = false;
        var retried = gameAction.Submit(in action);

        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, unverified.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, unverified.Mutation);
        Assert.True(retried.Verified, retried.Reason);
        Assert.Equal(1, relic.GetQueued());
    }

    [Fact]
    public void TargetUnavailableMapsToANamedExpectedResultCode()
    {
        var scroll = Item(AutoItemsConsumableFamily.Scroll, withTarget: false);
        using var gameAction = GameAction();
        var action = Action(scroll, AutoItemsConsumableFamily.Scroll);

        var result = gameAction.Submit(in action).ToResult();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.TargetUnavailable, result.Code);
    }

    [Fact]
    public void TargetingInProgressMapsToANamedExpectedResultCode()
    {
        TargetingManager.OpenRequests = 1;
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action).ToResult();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.TargetingInProgress, result.Code);
    }

    [Fact]
    public void AnUnverifiedUseIsAFaultReceiptThatRetainsAttemptEvidence()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        relic.SelectionNoOp = true;
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        var result = gameAction.Submit(in action).ToResult();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoItemsActionResultCodes.UseNotVerified, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.NativeEvidence.Outcome);
        Assert.Equal(1, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void LifecycleResetRebindsTheCompleteContract()
    {
        var relic = Item(AutoItemsConsumableFamily.Relic);
        using var gameAction = GameAction();
        var action = Action(relic, AutoItemsConsumableFamily.Relic);

        gameAction.InvalidateLifecycle();
        var retried = gameAction.Submit(in action);

        Assert.True(gameAction.BindingsAvailable, gameAction.BindingFailure);
        Assert.True(retried.Verified, retried.Reason);
    }

    private AutoItemsConsumableUseGameAction GameAction(
        Func<bool>? permit = null,
        Func<string>? failure = null)
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            static () => 1,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject entity ? entity.GetGuid() : Guid.Empty);
        return new AutoItemsConsumableUseGameAction(
            resolver,
            permit ?? (static () => true),
            failure ?? (static () => string.Empty));
    }

    private ConsumableSO Item(
        AutoItemsConsumableFamily family,
        bool withTarget = true,
        Guid? itemId = null) =>
        ConsumableTestItems.Create(_registry, family, withTarget, itemId);

    private static AutoItemsCycleAction Action(
        ConsumableSO item,
        AutoItemsConsumableFamily family) =>
        new(item.GetGuid(), family, collectedAtEpoch: 1, plannedLevel: family ==
            AutoItemsConsumableFamily.Scroll ? 1 : 0);
}
