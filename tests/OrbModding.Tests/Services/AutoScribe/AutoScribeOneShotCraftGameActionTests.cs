using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeOneShotCraftGameActionTests : IDisposable
{
    private readonly AutoScribeNativeFixture _native = new();
    private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;
    private long _lifecycle = 1;
    private Exception? _lifecycleFailure;
    private bool _permit = true;

    public AutoScribeOneShotCraftGameActionTests() => AutoScribeNativeFixture.ResetStubs();

    public void Dispose() => AutoScribeNativeFixture.ResetStubs();

    [Fact]
    public void VerifiedQueueAdmissionCommitsTheExactWorkOutcome()
    {
        var fixture = Fixture();
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Single(fixture.Active.value);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 1), result.Outcome.CallOutcome);
    }

    [Fact]
    public void MoreExpensiveRecipeUsesItsOwnAffordableFrontierBelowSharedCeiling()
    {
        var fixture = Fixture();
        fixture.RecipeType.maxStartingLevel = 67;
        fixture.Recipe.MaximumAffordableLevel = 24;
        var request = AtLevel(fixture.Action, level: 1);
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, request);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(24d, Assert.Single(fixture.Active.value).Quantity.ToDouble());
        Assert.Equal(67, fixture.RecipeType.maxStartingLevel);
        Assert.Equal(1, fixture.Recipe.PurchaseCalls);
        Assert.False(AutoScribeActionHealth.IsFailure(in result));
    }

    [Fact]
    public void ProgressionRequestWaitsUntilItsOwnNextLevelIsAffordable()
    {
        var fixture = Fixture();
        fixture.RecipeType.maxStartingLevel = 67;
        fixture.Recipe.MaximumAffordableLevel = 24;
        var request = AtLevel(fixture.Action, level: 25);
        using var actionBoundary = GameAction();

        var waiting = Submit(actionBoundary, request);

        Assert.Equal(AutoScribeActionResultCodes.Unaffordable, waiting.Outcome.Code);
        Assert.Empty(fixture.Active.value);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);

        fixture.Recipe.MaximumAffordableLevel = 25;
        var advanced = Submit(actionBoundary, request);

        Assert.True(advanced.Outcome.Verified, advanced.Outcome.Reason);
        Assert.Equal(25d, Assert.Single(fixture.Active.value).Quantity.ToDouble());
        Assert.Equal(67, fixture.RecipeType.maxStartingLevel);
    }

    [Fact]
    public void VerifiedInstantAdmissionCommitsTheNativeCompletionOutcome()
    {
        var fixture = Fixture();
        fixture.Recipe.InstantCraftEnabled = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Empty(fixture.Active.value);
        Assert.Single(fixture.Scroll.consumableCounts);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 1), result.Outcome.CallOutcome);
        Assert.False(AutoScribeActionHealth.IsFailure(in result));
    }

    [Fact]
    public void MissingInstantCompletionCannotBeReportedAsVerified()
    {
        var fixture = Fixture();
        fixture.Recipe.InstantCraftEnabled = true;
        fixture.Recipe.SuppressInstantAdmission = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome.Mutation);
    }

    [Fact]
    public void FailureAfterPurchaseWithoutWorkOutcomeNamesWhatWasPaid()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowAfterPurchase = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, failed.Outcome.Code);
        Assert.StartsWith(
            $"Auto Scribe paid 5 {EntityIdentityFormatter.PlayerName(fixture.Resource.GetGuid())} for " +
            $"{EntityIdentityFormatter.PlayerName(fixture.Action.RecipeId)} and saw no work after Payment:",
            failed.Outcome.Reason);
        Assert.DoesNotContain("injected failure", failed.Outcome.Reason);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 0), failed.Outcome.CallOutcome);
        Assert.False(Observe(failed).HasFailure);
    }

    [Fact]
    public void AnUnexpectedThrowAfterPaymentIsAFaultThatNamesTheExceptionAndLogsItWhole()
    {
        var fixture = Fixture();
        fixture.Recipe.AfterPurchaseFault = new NullReferenceException("injected failure after purchase");
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        try
        {
            using var actionBoundary = GameAction();

            var failed = Submit(actionBoundary, fixture.Action);

            Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, failed.Outcome.Code);
            Assert.Equal(NativeMutationOutcome.ExecutionThrew, failed.Outcome.Mutation);
            Assert.Contains(
                "after Payment: System.NullReferenceException: injected failure after purchase.",
                failed.Outcome.Reason);
            Assert.Contains("System.NullReferenceException: injected failure after purchase", Assert.Single(log));
            Assert.Contains(" at ", log[0]);
            Assert.Equal(1, fixture.Recipe.PurchaseCalls);
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
        }
    }

    [Fact]
    public void FailureDuringConstructionWithoutWorkOutcomeNamesItsStage()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowDuringConstruction = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Contains("saw no work after Construction:", failed.Outcome.Reason);
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 0), failed.Outcome.CallOutcome);
        Assert.DoesNotContain("injected failure", failed.Outcome.Reason);
        Assert.False(health.HasFailure);
    }

    [Fact]
    public void FailureAfterInitiationWithoutWorkOutcomeNamesItsStage()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowAfterInitiation = true;
        using var actionBoundary = GameAction();

        var failed = Submit(actionBoundary, fixture.Action);
        var health = Observe(failed);

        Assert.Contains("saw no work after Initiation:", failed.Outcome.Reason);
        Assert.Equal(new NativeMutationCallOutcome(3, 1, 0), failed.Outcome.CallOutcome);
        Assert.DoesNotContain("injected failure", failed.Outcome.Reason);
        Assert.False(health.HasFailure);
    }

    [Fact]
    public void FailureAfterFinalAdmissionCommitsWhenExactWorkIsObservable()
    {
        var fixture = Fixture();
        fixture.Active.ThrowAfterAdd = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Single(fixture.Active.value);
    }

    [Fact]
    public void AnUnexpectedThrowAfterAdmissionCommitsWhenTheWorkIsVisibleAndLogsTheThrow()
    {
        var fixture = Fixture();
        fixture.Active.AfterAddFault = new NullReferenceException("injected failure after admission");
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        try
        {
            using var actionBoundary = GameAction();

            var result = Submit(actionBoundary, fixture.Action);

            Assert.True(result.Outcome.Verified, result.Outcome.Reason);
            Assert.Single(fixture.Active.value);
            Assert.Contains("System.NullReferenceException: injected failure after admission", Assert.Single(log));
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
        }
    }

    [Fact]
    public void QueueFullIsRejectedBeforePayment()
    {
        var fixture = Fixture();
        fixture.Active.Maximum = 0;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.QueueFull, result.Outcome.Code);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
        Assert.Equal(new BigDouble(100, 0), fixture.Resource.GetTrueQuantity());
        Assert.Equal(0, result.Outcome.CallOutcome.NativeCallsAttempted);
        Assert.False(Observe(result).HasFailure);
    }

    [Fact]
    public void PostPaymentFaultMapsToFaultedOutcomeWithItsExactCallShape()
    {
        var fixture = Fixture();
        fixture.Recipe.ThrowDuringConstruction = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action).Outcome.ToResult();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.ExecutionThrew, result.NativeEvidence.Outcome);
        Assert.Equal(2, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void OrdinaryPreflightRejectionCarriesNoMutationEvidence()
    {
        var fixture = Fixture();
        fixture.Active.Maximum = 0;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action).Outcome.ToResult();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoScribeActionResultCodes.QueueFull, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    [Fact]
    public void MissingExactQueueAdmissionCannotBeReportedAsVerified()
    {
        var fixture = Fixture();
        fixture.Active.SuppressAdd = true;
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome.Mutation);
    }

    [Fact]
    public void LifecycleAndUnityThreadAreRevalidatedInsideTheGameAction()
    {
        var fixture = Fixture();
        using var actionBoundary = GameAction();

        _lifecycle = 2;
        var stale = Submit(actionBoundary, fixture.Action);
        _lifecycle = 0;
        var epochZero = new AutoScribeCycleAction(
            fixture.Action.RecipeId,
            fixture.Action.ScrollId,
            fixture.Action.Level,
            collectedAtEpoch: 0);
        var invalid = Submit(actionBoundary, epochZero);
        _lifecycle = 1;
        var wrongThread = ForeignThread.Run(() => Submit(actionBoundary, fixture.Action));

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, stale.Outcome.Code);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, invalid.Outcome.Code);
        Assert.Equal(AutoScribeActionResultCodes.WrongThread, wrongThread.Outcome.Code);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void AnUnexpectedExceptionFromTheLifecycleReadEscapesTheGameAction()
    {
        var fixture = Fixture();
        using var actionBoundary = GameAction();
        _lifecycleFailure = new NullReferenceException("injected lifecycle read failure");

        Assert.Throws<NullReferenceException>(() => Submit(actionBoundary, fixture.Action));
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void AutomaticCompetingSupplyIsRejectedBeforePayment()
    {
        var fixture = Fixture();
        fixture.Automatic.value.Add(
            new CraftingInstance(fixture.Recipe, new BigDouble(3, 0))
            {
                Automatic = true,
            });
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.CompetingSupply, result.Outcome.Code);

        // The sentence names the queue; the UUID is identity and travels as a field, not as prose.
        Assert.Contains(
            EntityIdentityFormatter.PlayerName(KnownEntities.AutoScribeInstances.Uuid),
            result.Outcome.Reason,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            KnownEntities.AutoScribeInstances.Uuid.ToString("D"),
            result.Outcome.Reason,
            StringComparison.Ordinal);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void RecipeScrollMismatchIsRejectedBeforeAnyNativeCall()
    {
        var fixture = Fixture();
        var other = _profile.Roles[1];
        var mismatched = new AutoScribeCycleAction(
            fixture.Action.RecipeId,
            other.Scroll.Uuid,
            fixture.Action.Level,
            fixture.Action.CollectedAtEpoch);
        using var actionBoundary = GameAction();

        var result = actionBoundary.Submit(in mismatched);

        Assert.Equal(AutoScribeActionResultCodes.RelationshipMismatch, result.Outcome.Code);
        Assert.Contains("one audited Auto Scribe role", result.Outcome.Reason);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void MissingLiveTargetReasonIsNotDiscarded()
    {
        var fixture = Fixture(withTarget: false);
        using var actionBoundary = GameAction();

        var result = Submit(actionBoundary, fixture.Action);

        Assert.Equal(AutoScribeActionResultCodes.TargetUnavailable, result.Outcome.Code);
        Assert.Contains("no valid live target", result.Outcome.Reason);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void RegistryNotReadyIdentityRejectionIsQuietBackpressure()
    {
        var fixture = Fixture();
        using var actionBoundary = GameAction(
            static () => TypedRegistrySourceSnapshot.NotReady("Registry publication pending."));

        var result = Submit(actionBoundary, fixture.Action);
        var health = new AutoScribeActionHealth();

        Assert.Equal(AutoScribeActionResultCodes.IdentityUnavailable, result.Outcome.Code);
        Assert.True(result.Retryable);
        Assert.Contains("Status=RegistryNotReady", result.Outcome.Reason);
        Assert.Equal(
            ServiceActionDisposition.Rejected,
            result.Outcome.ToResult().Disposition);
        Assert.False(health.EntersFailure(in result));
        Assert.False(health.HasFailure);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    [Fact]
    public void WrongTypeIdentityRejectionEntersHealthOnceAndStaysUserVisible()
    {
        var fixture = Fixture();
        var wrongType = new ConsumableSO();
        wrongType.SetGuid(fixture.Action.RecipeId);
        _native.Registry[fixture.Action.RecipeId] = wrongType;
        using var actionBoundary = GameAction();

        var first = Submit(actionBoundary, fixture.Action);
        var repeated = Submit(actionBoundary, fixture.Action);
        var health = new AutoScribeActionHealth();

        Assert.Equal(AutoScribeActionResultCodes.IdentityUnavailable, first.Outcome.Code);
        Assert.False(first.Retryable);
        Assert.Contains("Status=WrongType", first.Outcome.Reason);
        Assert.Equal(
            ServiceActionDisposition.Faulted,
            first.Outcome.ToResult().Disposition);
        Assert.True(health.EntersFailure(in first));
        Assert.False(health.EntersFailure(in repeated));

        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            _profile,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            health,
            ServiceRetryGateSnapshot.Empty,
            default,
            cycleObserved: true,
            AutoScribeDecisionKind.Planned,
            blockedRole: -1,
            AutoScribeEvidenceReason.None);

        Assert.Equal(FeatureStatusState.ContractUnavailable, status.State);
        Assert.Equal(FeatureStatusReasonCode.IdentityMismatch, status.Reason);
        Assert.Contains("Status=WrongType", status.Summary);
        Assert.Equal(0, fixture.Recipe.PurchaseCalls);
    }

    private AutoScribeOneShotCraftGameAction GameAction(
        Func<TypedRegistrySourceSnapshot>? readRegistry = null)
    {
        return new AutoScribeOneShotCraftGameAction(
            _native.Resolver(() => _lifecycle, readRegistry),
            _profile,
            () => _lifecycleFailure is { } failure ? throw failure : _lifecycle,
            () => _permit,
            static () => string.Empty);
    }

    private static AutoScribeSubmission Submit(
        AutoScribeOneShotCraftGameAction boundary,
        AutoScribeCycleAction action) =>
        boundary.Submit(in action);

    private static AutoScribeCycleAction AtLevel(
        in AutoScribeCycleAction action,
        int level) =>
        new(
            action.RecipeId,
            action.ScrollId,
            level,
            action.CollectedAtEpoch);

    private AutoScribeNativeFixtureState Fixture(bool withTarget = true) =>
        _native.Build(_profile, withTarget);

    private static AutoScribeActionHealth Observe(in AutoScribeSubmission submission)
    {
        var health = new AutoScribeActionHealth();
        health.EntersFailure(in submission);
        return health;
    }
}
