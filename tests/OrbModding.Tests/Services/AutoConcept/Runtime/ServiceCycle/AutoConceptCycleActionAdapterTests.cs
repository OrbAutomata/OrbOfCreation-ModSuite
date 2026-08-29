using System;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptCycleActionAdapterTests
{
    private const long PlannedEpoch = 7;
    private static readonly Guid Recipe = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void AVerifiedQuantityMutationCommitsWithNativeEvidence()
    {
        var submission = AutoConceptSubmission.Attempted(
            new NativeMutationCallOutcome(1, 1, 1),
            NativeMutationOutcome.Verified,
            string.Empty,
            1);

        var result = Execute(submission);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
    }

    [Fact]
    public void AProjectionRefusalIsAnOrdinaryNonLatchingRejection()
    {
        var result = Execute(AutoConceptSubmission.Rejected(
            AutoConceptPreflight.ProjectionRefused,
            "resource would fall below reserve"));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoConceptActionResultCodes.ProjectionRefused, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    [Fact]
    public void BoundaryTimeResourceBackpressureIsAnExactQuietSkip()
    {
        var result = Execute(AutoConceptSubmission.Rejected(
            AutoConceptPreflight.ResourceBackpressure,
            "resource would fall below reserve"));

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    [Fact]
    public void AnAttemptedButUnverifiedMutationFaultsWithEvidence()
    {
        var submission = AutoConceptSubmission.Attempted(
            new NativeMutationCallOutcome(1, 1, 0),
            NativeMutationOutcome.PostconditionFailed,
            "postcondition failed",
            1);

        var result = Execute(submission);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.NativeEvidence.Outcome);
    }

    [Fact]
    public void LifecycleEpochDriftRefusesBeforeNativePreflight()
    {
        var native = new FakeNativePort(AutoConceptSubmission.Attempted(
            new NativeMutationCallOutcome(1, 1, 1),
            NativeMutationOutcome.Verified,
            string.Empty,
            1));

        var result = Execute(native, nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(0, native.Submissions);
    }

    [Fact]
    public void LosingTheActionFamilyRefusesBeforeNativePreflight()
    {
        var native = new FakeNativePort(AutoConceptSubmission.Attempted(
            new NativeMutationCallOutcome(1, 1, 1),
            NativeMutationOutcome.Verified,
            string.Empty,
            1));

        var result = Execute(native, ownsActionFamily: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoConceptActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(0, native.Submissions);
    }

    [Fact]
    public void DisabledConfigurationRefusesBeforeNativePreflight()
    {
        var native = new FakeNativePort(AutoConceptSubmission.Attempted(
            new NativeMutationCallOutcome(1, 1, 1),
            NativeMutationOutcome.Verified,
            string.Empty,
            1));

        var result = Execute(native, mode: AutoConceptOperationMode.Disabled);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
        Assert.Equal(0, native.Submissions);
    }

    [Fact]
    public void NativeContractInvocationFailureFaultsInsteadOfDegrading()
    {
        var result = Execute(new ThrowingNativePort());

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    [Theory]
    [InlineData(2, 3329)]
    [InlineData(3, 3330)]
    [InlineData(4, 3331)]
    [InlineData(5, 3332)]
    [InlineData(7, 3334)]
    public void LiveStateDriftIsRejectedWithoutFaulting(
        int preflight,
        int expectedCode)
    {
        var result = Execute(AutoConceptSubmission.Rejected(
            (AutoConceptPreflight)preflight, "changed"));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(new ServiceActionResultCode(expectedCode), result.Code);
    }

    private static ServiceActionResult Execute(
        AutoConceptSubmission submission,
        long nativeEpoch = PlannedEpoch,
        bool ownsActionFamily = true,
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active) =>
        Execute(
            new FakeNativePort(submission),
            nativeEpoch,
            ownsActionFamily,
            mode);

    private static ServiceActionResult Execute(
        IAutoConceptNativePort native,
        long nativeEpoch = PlannedEpoch,
        bool ownsActionFamily = true,
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active)
    {
        var adapter = new AutoConceptCycleActionAdapter(
            native, () => nativeEpoch, () => ownsActionFamily);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = mode,
            },
        };
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 1);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, Recipe, 1, Guid.Empty, PlannedEpoch, in belief);
        var context = new ServiceActionContext(
            new ServiceCycleIdentity(
                AutoConceptServicePolicies.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(1));
        return adapter.TryExecute(in action, in config, in context);
    }

    private sealed class FakeNativePort : IAutoConceptNativePort
    {
        private readonly AutoConceptSubmission _submission;

        internal FakeNativePort(AutoConceptSubmission submission) => _submission = submission;

        internal int Submissions { get; private set; }

        public AutoConceptSubmission Submit(
            in AutoConceptCycleAction action,
            in AutoConceptConfiguration config)
        {
            Submissions++;
            return _submission;
        }
    }

    private sealed class ThrowingNativePort : IAutoConceptNativePort
    {
        public AutoConceptSubmission Submit(
            in AutoConceptCycleAction action,
            in AutoConceptConfiguration config) =>
            throw new TargetInvocationException(
                new InvalidOperationException("CanAddInstance failed"));
    }
}
