using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestCycleActionAdapterTests
{
    [Theory]
    [InlineData(RejectionScenario.Disabled)]
    [InlineData(RejectionScenario.Quarantined)]
    [InlineData(RejectionScenario.Ownership)]
    [InlineData(RejectionScenario.Permit)]
    [InlineData(RejectionScenario.Lifecycle)]
    public void RejectionsBeforeFinalReadDoNotReachNativeMutation(
        RejectionScenario scenario)
    {
        using var fixture = new Fixture();
        var expectedCode = fixture.ConfigureScenario(scenario);

        var result = fixture.Execute();

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, fixture.Mutation.SubmitCount);
    }

    [Fact]
    public void NativeSubmissionRunsAfterOwnershipAndMutationPermit()
    {
        using var fixture = new Fixture();

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        AssertOrdered(fixture.Events, "ownership", "permit", "resolve", "mutate");
        Assert.Equal(1, fixture.Gates.QuarantineReadCount);
    }

    /// <summary>
    /// The facts and the safety verdict the worker planned against reach the mutation, which is what
    /// lets the boundary decide without asking the game a question the plan already answered.
    /// </summary>
    [Fact]
    public void ThePlannedFactsAndSafetyReachTheMutationUnchanged()
    {
        using var fixture = new Fixture();

        fixture.Execute();

        Assert.Equal(AutoHarvestEvidenceState.Verified, fixture.Mutation.Facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Verified, fixture.Mutation.Facts.PlotVisibility);
        Assert.Equal(AutoHarvestEvidenceState.Verified, fixture.Mutation.Facts.ActionAvailability);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            fixture.Mutation.Facts.Prerequisites);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, fixture.Mutation.Facts.Readiness);
        Assert.Equal(AutoHarvestActionSafetyState.ResourceDrain, fixture.Mutation.Safety);
    }

    [Fact]
    public void AttemptedUnverifiedMutationQuarantinesThePair()
    {
        using var fixture = new Fixture();
        fixture.Mutation.Result = AutoHarvestMutationAdapter.FromVerifier(
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(1, fixture.Gates.QuarantineCount);
    }

    /// <summary>
    /// An outcome read without a mutation having been attempted is not this pair's fault.
    /// </summary>
    /// <remarks>
    /// The quarantine exists to stop retrying something the game refused to do. Nothing was asked of
    /// the game here, so there is nothing to stop retrying, and naming it a pair fault would make the
    /// worker give up on a pair over a failure that never touched it.
    /// </remarks>
    [Fact]
    public void AnUnverifiedOutcomeWithNoAttemptIsNotAPairFault()
    {
        using var fixture = new Fixture();
        fixture.Mutation.Result = AutoHarvestMutationAdapter.FromVerifier(
            NativeMutationOutcome.BeforeCaptureFailed,
            new NativeMutationCallOutcome(0, 0, 0));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.Equal(0, fixture.Gates.QuarantineCount);
    }

    [Fact]
    public void NonAttemptedFinalPreflightRejectionDoesNotQuarantine()
    {
        using var fixture = new Fixture();
        fixture.Mutation.Result = NativeActionOutcome.Reject(
            CommonActionResultCodes.PolicyRejected,
            string.Empty);

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(0, fixture.Gates.QuarantineCount);
    }

    /// <summary>
    /// A refused binding is reported in the terms the worker records it in: which pairs it stops.
    /// </summary>
    /// <remarks>
    /// The worker keeps this service's fault memory, and its only view of what happened is the
    /// receipt's code. A pair-scoped failure reported as a feature-wide one would stop the sibling
    /// too; the reverse would keep retrying a feature the build cannot support.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoHarvestRuntimeFailureScope.Pair, 1025)]
    [InlineData((int)AutoHarvestRuntimeFailureScope.Feature, 1026)]
    public void ARefusedBindingReportsHowFarItsContractFailureReaches(int scope, int expectedCode)
    {
        using var fixture = new Fixture();
        fixture.FailResolution((AutoHarvestRuntimeFailureScope)scope);

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(new ServiceActionResultCode(expectedCode, "expected"), result.Code);
        Assert.Equal(0, fixture.Mutation.SubmitCount);
    }

    /// <summary>
    /// A resolution that failed without tripping the circuit is not reported as a contract the build
    /// does not have.
    /// </summary>
    [Fact]
    public void ARefusedBindingWithNoTrippedCircuitStaysAnUnattributedFault()
    {
        using var fixture = new Fixture();
        fixture.FailResolution(scope: null);

        var result = fixture.Execute();

        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
    }

    public static IEnumerable<object[]> ContractIds()
    {
        foreach (var id in AutoHarvestNativeBindings.ContractIds) yield return new object[] { id };
    }

    /// <summary>
    /// The shipping resolver over a build missing one member: the whole feature refuses with the
    /// member's id, before any registry read or native call.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContractIds))]
    public void EveryMissingMemberDisablesTheCompleteSet(string missing)
    {
        using var fixture = new Fixture(circuit => new AutoHarvestBindingResolver(
            new TypedRegistryResolver(
                () => throw new InvalidOperationException("the registry was read"),
                () => throw new InvalidOperationException("the registry was read"),
                _ => throw new InvalidOperationException("the registry was read")),
            circuit,
            includeContract: id => id != missing));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoHarvestActionResultCodes.FeatureContractUnavailable, result.Code);
        Assert.Contains("auto-harvest binding set is unavailable", result.Reason, StringComparison.Ordinal);
        Assert.Contains(missing, result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Mutation.SubmitCount);
    }

    private static void AssertOrdered(IReadOnlyList<string> events, params string[] expected)
    {
        var previous = -1;
        foreach (var item in expected)
        {
            var current = -1;
            for (var index = previous + 1; index < events.Count; index++)
            {
                if (events[index] != item) continue;
                current = index;
                break;
            }
            Assert.True(current > previous, $"'{item}' was not observed after index {previous}.");
            previous = current;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private bool _owns = true;
        private bool _capturePermit = true;
        private bool _operational = true;
        private ulong _nativeLifecycle = 7;
        private readonly AutoHarvestCycleActionAdapter _adapter;

        public Fixture(Func<AutoHarvestContractCircuit, IAutoHarvestBindingPort>? createBindings = null)
        {
            Events = new List<string>();
            Bindings = new BindingPort(Events, () => _nativeLifecycle);
            Mutation = new MutationPort(Events);
            Gates = new GatePort();
            ContractCircuit = new AutoHarvestContractCircuit();
            _adapter = new AutoHarvestCycleActionAdapter(
                createBindings?.Invoke(ContractCircuit) ?? Bindings,
                Mutation,
                Gates,
                ContractCircuit,
                () =>
                {
                    Events.Add("ownership");
                    return _owns;
                },
                () =>
                {
                    Events.Add("permit");
                    return _capturePermit;
                });
        }

        public List<string> Events { get; }
        public BindingPort Bindings { get; }
        public MutationPort Mutation { get; }
        public GatePort Gates { get; }
        public AutoHarvestContractCircuit ContractCircuit { get; }

        public ServiceActionResultCode ConfigureScenario(RejectionScenario scenario)
        {
            return scenario switch
            {
                RejectionScenario.Disabled => SetDisabled(),
                RejectionScenario.Quarantined => SetQuarantined(),
                RejectionScenario.Ownership => SetOwnershipUnavailable(),
                RejectionScenario.Permit => SetPermitUnavailable(),
                RejectionScenario.Lifecycle => SetLifecycleReplaced(),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }

        private ServiceActionResultCode SetDisabled()
        {
            _operational = false;
            return CommonActionResultCodes.ServiceDisabled;
        }

        private ServiceActionResultCode SetQuarantined()
        {
            Gates.Quarantined = true;
            return AutoHarvestActionResultCodes.PairFaulted;
        }

        private ServiceActionResultCode SetOwnershipUnavailable()
        {
            _owns = false;
            return AutoHarvestActionResultCodes.ActionFamilyUnavailable;
        }

        private ServiceActionResultCode SetPermitUnavailable()
        {
            _capturePermit = false;
            return AutoHarvestActionResultCodes.ActionFamilyUnavailable;
        }

        private ServiceActionResultCode SetLifecycleReplaced()
        {
            _nativeLifecycle = 8;
            return CommonActionResultCodes.LifecycleReplaced;
        }

        /// <summary>
        /// Makes binding resolution fail, having tripped the circuit at <paramref name="scope"/>
        /// first — the order the resolver itself produces, since it blocks the circuit as it fails.
        /// </summary>
        public void FailResolution(AutoHarvestRuntimeFailureScope? scope)
        {
            Bindings.FailsToResolve = true;
            if (scope is not null) ContractCircuit.Block(AutoHarvestPair.FruitTree, scope.Value);
        }

        public ServiceActionResult Execute()
        {
            var configuration = AutoHarvestConfigurationFactory.Create(
                masterEnabled: _operational,
                emergencyDisabled: false,
                activeMode: true,
                fruitSelected: true,
                treasureSelected: false);
            var cycle = new ServiceCycleIdentity(
                new ServiceId("AutoHarvest"),
                new LifecycleGeneration(7),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1));
            var context = new ServiceActionContext(
                cycle,
                new BatchId(1),
                new ActionId(1),
                actionIndex: 0,
                new MonotonicTimestamp(1));
            // Deliberately not all-verified, and deliberately not the safe verdict, so an adapter
            // that substituted a plan of its own would not match.
            var action = new AutoHarvestCycleAction(
                AutoHarvestPair.FruitTree,
                new AutoHarvestPairFacts(
                    AutoHarvestEvidenceState.Verified,
                    AutoHarvestEvidenceState.Verified,
                    AutoHarvestEvidenceState.Verified,
                    PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
                    AutoHarvestEvidenceState.Unknown),
                AutoHarvestActionSafetyState.ResourceDrain);
            return _adapter.TryExecute(action, configuration, context);
        }

        public void Dispose() { }
    }

    private sealed class BindingPort : IAutoHarvestBindingPort
    {
        private readonly List<string> _events;
        private readonly Func<ulong> _readLifecycle;

        public BindingPort(List<string> events, Func<ulong> readLifecycle)
        {
            _events = events;
            _readLifecycle = readLifecycle;
        }

        public bool FailsToResolve { get; set; }

        public AutoHarvestResolvedPairSet ResolvePairSet()
        {
            _events.Add("resolve");
            if (FailsToResolve)
            {
                return AutoHarvestResolvedPairSet.Create(
                    null!,
                    null!,
                    fruit: null,
                    AutoHarvestNativeFailure.Create(
                        AutoHarvestRuntimeFailureKind.Contract,
                        AutoHarvestRuntimeFailureScope.Pair),
                    treasure: null,
                    AutoHarvestNativeFailure.Create(
                        AutoHarvestRuntimeFailureKind.Contract,
                        AutoHarvestRuntimeFailureScope.Pair));
            }

            var shared = new AutoHarvestSharedBinding(
                new object(),
                null!,
                null!,
                checked((long)_readLifecycle()));
            var fruit = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                new object(),
                new object(),
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"),
                new object(),
                null!,
                null!,
                null!);
            return AutoHarvestResolvedPairSet.Create(
                null!,
                shared,
                fruit,
                default,
                treasure: null,
                AutoHarvestNativeFailure.Create(
                    AutoHarvestRuntimeFailureKind.Retryable,
                    AutoHarvestRuntimeFailureScope.Pair));
        }

    }

    private sealed class MutationPort : IAutoHarvestMutationPort
    {
        private readonly List<string> _events;

        public MutationPort(List<string> events)
        {
            _events = events;
            Result = AutoHarvestMutationAdapter.FromVerifier(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1));
        }

        public NativeActionOutcome Result { get; set; }
        public int SubmitCount { get; private set; }
        public AutoHarvestPairFacts Facts { get; private set; }
        public AutoHarvestActionSafetyState Safety { get; private set; }

        public NativeActionOutcome Submit(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestPairFacts facts,
            AutoHarvestActionSafetyState safety,
            Action quarantine)
        {
            _events.Add("mutate");
            SubmitCount++;
            Facts = facts;
            Safety = safety;
            if (Result.CallOutcome.MutationAttempts > 0 && !Result.Verified) quarantine();
            return Result;
        }
    }

    private sealed class GatePort : IAutoHarvestGatePort
    {
        public bool Quarantined { get; set; }
        public int QuarantineReadCount { get; private set; }
        public int QuarantineCount { get; private set; }
        public void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs) { }
        public bool IsQuarantined(AutoHarvestPair pair)
        {
            QuarantineReadCount++;
            return Quarantined;
        }
        public void Quarantine(AutoHarvestPair pair)
        {
            Quarantined = true;
            QuarantineCount++;
        }
    }

    public enum RejectionScenario
    {
        Disabled,
        Quarantined,
        Ownership,
        Permit,
        Lifecycle,
    }
}
