using System;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceReferenceResourceValidationTests
{
    [Fact]
    public void NullWorkerDefinitionIsRejectedAndConstructionResourcesRemainReusable()
    {
        using var registry = new ServiceCycleRegistry(1);
        var invalid = new SyntheticServiceDefinition("test.reference-worker.null")
        {
            ReturnNullWorkerDefinition = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(
            invalid,
            new LifecycleGeneration(1)));

        Assert.Contains("worker definition", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, invalid.WorkerDefinitionCreateCount);
        Assert.Equal(0, invalid.StateCreateCount);
        Assert.Equal(0, registry.Count);

        using var valid = registry.Register(
            new SyntheticServiceDefinition("test.reference-worker.recovery"),
            new LifecycleGeneration(1));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void NullReferenceStatePublishesStateFactoryFaultAndRecoversOnRetry()
    {
        var clock = new ThreadSafeTestClock(1_000);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SyntheticServiceDefinition("test.reference-state.null")
        {
            ReturnNullState = true,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;

        var retryDue = default(MonotonicTimestamp);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.True(runner.TryStartCycle(clock.Now).Queued);
            Assert.True(SpinWait.SpinUntil(
                () => runner.Snapshot.Handoff.Phase == ServiceHandoffPhase.ResponseReady,
                ServiceCycleTestDeadline.Value));
            Assert.True(runner.TryAcquireResponse());
            var faulted = runner.Snapshot;
            retryDue = faulted.NextWakeDue;

            Assert.Equal(ServiceFaultCategory.StateFactory, faulted.Fault.Category);
            Assert.Equal(attempt, definition.StateCreateCount);
            Assert.Equal(0, definition.StateReleaseCount);
            Assert.Equal(1, ledger.LiveClaimCount);
            if (attempt < 4) clock.AdvanceTo(retryDue);
        }

        definition.ReturnNullState = false;
        clock.AdvanceTo(retryDue);
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.Handoff.Phase == ServiceHandoffPhase.ResponseReady,
            ServiceCycleTestDeadline.Value));
        Assert.True(runner.TryAcquireResponse());

        Assert.False(runner.Snapshot.Fault.IsValid);
        Assert.Equal(5, definition.StateCreateCount);
        Assert.Equal(2, ledger.LiveClaimCount);
    }

    [Fact]
    public void DefaultValueFrameAndStateRemainValidResources()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var registration = registry.Register(
            new DefaultValueServiceDefinition(),
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.Handoff.Phase == ServiceHandoffPhase.ResponseReady,
            ServiceCycleTestDeadline.Value));
        Assert.True(runner.TryAcquireResponse());

        var projection = runner.Snapshot.Projection.Snapshot;
        Assert.Equal(1, projection.Count);
        Assert.Equal(1L, projection.GetEntry(0).Value.Integer);
    }

    private struct DefaultValueState
    {
        internal int EvaluationCount;
    }
    private readonly struct DefaultValueAction { }

    private sealed class DefaultValueServiceDefinition :
        IServiceCycleDefinition<DefaultValueState, DefaultValueAction>
    {
        public ServiceId ServiceId { get; } = new("test.value-defaults");
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; } = new(
            new MonotonicDuration(10),
            new MonotonicDuration(80));

        public IServiceCycleWorkerDefinition<DefaultValueState, DefaultValueAction>
            CreateWorkerDefinition() => new DefaultValueWorkerDefinition();

        public ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);

        public ServiceActionJournalAttribution DescribeAction(in DefaultValueAction action) =>
            ServiceActionJournalAttribution.Publication;

        public ServiceActionResult TryExecute(
            in DefaultValueAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
    }

    private sealed class DefaultValueWorkerDefinition :
        IServiceCycleWorkerDefinition<DefaultValueState, DefaultValueAction>
    {
        public DefaultValueState CreateState(LifecycleGeneration lifecycle) => default;
        public void ReleaseState(ref DefaultValueState state) { }

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref DefaultValueState state,
            ServiceActionWriter<DefaultValueAction> actions)
        {
            state.EvaluationCount++;
            return WakePolicy.Immediate;
        }

        public void ProjectState(
            in DefaultValueState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) =>
            output.Add(
                new ServiceProjectionKey(1),
                ServiceProjectionValue.FromInteger(state.EvaluationCount));
    }
}
