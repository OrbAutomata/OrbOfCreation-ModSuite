using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceCycleTypeSafetyFixtures
{
    /// <summary>
    /// Asserts a shape the configuration role rejects.
    /// </summary>
    /// <remarks>
    /// Asked of the walker directly rather than proved by registering a service that reads the
    /// shape. The suite names one configuration type, so there is no longer a registration that
    /// could carry another one — but the rule is about what may cross to a worker thread at all, and
    /// it outlives the type parameter that used to be the only way to state it.
    /// </remarks>
    internal static void AssertConfigurationRejected<TShape>(TShape configuration)
        where TShape : notnull
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            configuration.GetType(),
            ServiceCycleTypeRole.Configuration,
            "configuration");
        Assert.True(
            violation.HasValue,
            $"The configuration role accepted {typeof(TShape)}.");
    }

    /// <summary>Asserts a shape the configuration role admits.</summary>
    internal static void AssertConfigurationAccepted<TShape>(TShape configuration)
        where TShape : notnull
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            configuration.GetType(),
            ServiceCycleTypeRole.Configuration,
            "configuration");
        Assert.False(
            violation.HasValue,
            violation.HasValue ? violation.Value.Message : string.Empty);
    }

    /// <summary>
    /// Asserts a shape the capture-buffer role rejects.
    /// </summary>
    /// <remarks>
    /// Asked of the walker directly, for the same reason the configuration assertions are: the suite
    /// names one capture buffer, so no registration can carry another shape into the role. What the
    /// role admits is still a rule about what may cross to a worker thread, and it outlives the type
    /// parameter that used to be the only way to state it.
    /// </remarks>
    internal static void AssertFrameRejected<TShape>(TShape frame)
        where TShape : notnull
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            frame.GetType(),
            ServiceCycleTypeRole.Frame,
            "frame");
        Assert.True(violation.HasValue, $"The capture-buffer role accepted {typeof(TShape)}.");
    }

    /// <summary>Asserts a shape the capture-buffer role admits.</summary>
    internal static void AssertFrameAccepted<TShape>(TShape frame)
        where TShape : notnull
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            frame.GetType(),
            ServiceCycleTypeRole.Frame,
            "frame");
        Assert.False(
            violation.HasValue,
            violation.HasValue ? violation.Value.Message : string.Empty);
    }
}

internal sealed class TypeSafetyDefinition<TState, TAction> :
    IServiceCycleDefinition<TState, TAction>
{
    private readonly TState _state;

    internal TypeSafetyDefinition(TState state, string serviceId = "test.type-safety")
    {
        _state = state;
        ServiceId = new ServiceId(serviceId);
    }

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    public IServiceCycleWorkerDefinition<TState, TAction> CreateWorkerDefinition() =>
        new WorkerDefinition(_state);
    public ServiceStartDecision ShouldStart(in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    public ServiceActionJournalAttribution DescribeAction(in TAction action) =>
        ServiceActionJournalAttribution.Publication;
    public ServiceActionResult TryExecute(
        in TAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);

    private sealed class WorkerDefinition : IServiceCycleWorkerDefinition<TState, TAction>
    {
        private readonly TState _state;
        internal WorkerDefinition(TState state) => _state = state;
        public TState CreateState(LifecycleGeneration lifecycle) => _state;
        public void ReleaseState(ref TState state) { }

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref TState state,
            ServiceActionWriter<TAction> actions) => WakePolicy.Immediate;
        public void ProjectState(
            in TState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }
}

internal sealed class SafeFrame { }
internal sealed class SafeState { }
internal readonly struct ImmutableAction { }
