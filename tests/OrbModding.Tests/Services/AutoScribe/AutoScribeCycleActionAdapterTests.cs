using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeCycleActionAdapterTests
{
    [Fact]
    public void ActionFamilyContentionStopsBeforeTheWorkerCanPlanActions()
    {
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
            },
        };
        var context = default(ServiceCycleStartContext);

        var decision = AutoScribeService.ShouldStart(
            in configuration,
            in context,
            ownsActionFamily: static () => false,
            isQuarantined: static () => false);

        Assert.False(decision.ShouldStart);
        Assert.Equal(AutoScribeServiceDecisionCodes.ActionFamilyUnavailable, decision.Code);
        Assert.Equal(WakePolicy.OnPublication, decision.WakePolicy);

        var disabledConfiguration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Disabled,
            },
        };
        var disabled = AutoScribeService.ShouldStart(
            in disabledConfiguration,
            in context,
            ownsActionFamily: static () => false,
            isQuarantined: static () => false);

        Assert.False(disabled.ShouldStart);
        Assert.Equal(CommonServiceDecisionCodes.NotReady, disabled.Code);
    }

    [Fact]
    public void BackpressureAndFailuresHaveOneSharedClassification()
    {
        var health = new AutoScribeActionHealth();
        var registryWait = AutoScribeSubmission.Reject(
            AutoScribeActionResultCodes.IdentityUnavailable,
            "The lifecycle registry is not ready.",
            retryable: true);
        var permitWait = AutoScribeSubmission.Reject(
            AutoScribeActionResultCodes.MutationPermitUnavailable,
            "CraftingQueueSubmission is busy.");
        var contradiction = AutoScribeSubmission.Fault(
            AutoScribeActionResultCodes.RelationshipMismatch,
            "The live recipe relation contradicted the audited role.");

        Assert.Equal(
            ServiceActionDisposition.Rejected,
            registryWait.Outcome.ToResult().Disposition);
        Assert.False(health.Observe(in registryWait));
        Assert.Equal(
            ServiceActionDisposition.Rejected,
            permitWait.Outcome.ToResult().Disposition);
        Assert.False(health.Observe(in permitWait));
        Assert.False(health.HasFailure);

        Assert.Equal(
            ServiceActionDisposition.Faulted,
            contradiction.Outcome.ToResult().Disposition);
        Assert.True(health.Observe(in contradiction));
        Assert.True(health.HasFailure);
    }

    [Fact]
    public void PersistentQuarantineDoesNotReenterTheWarningState()
    {
        const string reason = "Exact queue admission was not observed after payment.";
        var failure = new AutoScribeSubmission(NativeActionOutcome.Fault(
            AutoScribeActionResultCodes.VerificationFailed,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(4, 1, 0),
            reason));
        var quarantine = AutoScribeSubmission.Fault(
            AutoScribeActionResultCodes.Quarantined,
            reason);
        var health = new AutoScribeActionHealth();

        Assert.True(health.Observe(in failure));
        var failureRevision = health.Revision;
        Assert.False(health.Observe(in quarantine));

        Assert.Equal(failureRevision, health.Revision);
        Assert.Equal(AutoScribeActionResultCodes.VerificationFailed, health.Code);
        Assert.Equal(ServiceActionDisposition.Faulted,
            quarantine.Outcome.ToResult().Disposition);
    }

    [Fact]
    public void QuarantineRepeatReplacesAnUnrelatedInterveningFailureBeforeDeduplicating()
    {
        const string quarantineReason = "Exact queue admission was not observed after payment.";
        var root = new AutoScribeSubmission(NativeActionOutcome.Fault(
            AutoScribeActionResultCodes.VerificationFailed,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(4, 1, 0),
            quarantineReason));
        var unrelated = AutoScribeSubmission.Fault(
            AutoScribeActionResultCodes.WrongThread,
            "Unity thread ownership changed.");
        var quarantine = AutoScribeSubmission.Fault(
            AutoScribeActionResultCodes.Quarantined,
            quarantineReason);
        var health = new AutoScribeActionHealth();

        Assert.True(health.Observe(in root));
        Assert.True(health.Observe(in unrelated));
        Assert.True(health.Observe(in quarantine));
        Assert.Equal(AutoScribeActionResultCodes.Quarantined, health.Code);
        Assert.Equal(quarantineReason, health.Reason);
        Assert.False(health.Observe(in quarantine));
    }

    [Fact]
    public void VerifiedActionsAndLifecycleInvalidationClearHealthAndAllowRewarning()
    {
        var failure = AutoScribeSubmission.Fault(
            AutoScribeActionResultCodes.RelationshipMismatch,
            "The live recipe relation contradicted the audited role.");
        var verified = new AutoScribeSubmission(NativeActionOutcome.Commit(
            new NativeMutationCallOutcome(4, 1, 1),
            "The exact work was admitted."));
        var health = new AutoScribeActionHealth();

        Assert.True(health.Observe(in failure));
        Assert.False(health.Observe(in verified));
        Assert.False(health.HasFailure);
        Assert.True(health.Observe(in failure));

        health.InvalidateLifecycle();
        Assert.False(health.HasFailure);
        Assert.True(health.Observe(in failure));
    }
}
