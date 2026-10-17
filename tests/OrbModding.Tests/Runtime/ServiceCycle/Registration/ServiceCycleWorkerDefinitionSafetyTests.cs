using System;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleWorkerDefinitionSafetyTests
{
    [Fact]
    public void WorkerCannotRetainMainDefinition() => AssertRejected(main => new MainRetainingWorker(main));

    [Fact]
    public void WorkerCannotHideBoundaryBehindObject() => AssertRejected(_ => new ObjectRetainingWorker(new object()));

    [Fact]
    public void WorkerCannotHideBoundaryBehindInterface() => AssertRejected(_ => new InterfaceRetainingWorker(new HiddenValue()));

    [Fact]
    public void WorkerCannotRetainDelegate() => AssertRejected(_ => new DelegateRetainingWorker(() => { }));

    [Fact]
    public void WorkerCannotRetainOpaqueFrameworkReference() =>
        AssertRejected(_ => new FrameworkRetainingWorker(new ManualResetEventSlim(false)));

    [Fact]
    public void WorkerCannotRetainMutableCommonRuntimeObject()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertRejected(_ => new CommonRuntimeRetainingWorker(registry));
    }

    [Fact]
    public void WorkerMayRetainAnAuditedPublicationValueFromTheRuntimeNamespace()
    {
        // PublicationTable lives under the very namespace that now marks mutable runtime plumbing, so
        // the audited-value admission has to be consulted before the namespace rejection. Swapping
        // those two checks would reject every worker holding a published table, and nothing else in
        // the suite would notice until a service failed to register.
        var main = new SafetyMainDefinition();
        var worker = new PublicationTableRetainingWorker(
            PublicationTable<SafetyRow>.Create(stackalloc SafetyRow[] { new SafetyRow(1) }));

        ServiceCycleWorkerDefinitionValidator.EnsureSeparated(main, worker);
    }

    [Fact]
    public void WorkerCannotHideStorageInPrivateBaseOrStaticField()
    {
        AssertRejected(_ => new InheritedObjectRetainingWorker(new object()));
        AssertRejected(_ => new StaticObjectRetainingWorker());
    }

    private static void AssertRejected(Func<SafetyMainDefinition, SafetyWorkerBase> factory)
    {
        var main = new SafetyMainDefinition();
        var worker = factory(main);
        Assert.Throws<InvalidOperationException>(() =>
            ServiceCycleWorkerDefinitionValidator.EnsureSeparated(main, worker));
    }

    private sealed class SafetyState { }
    private readonly struct SafetyAction { }

    private sealed class SafetyMainDefinition :
        IServiceCycleDefinition<SafetyState, SafetyAction>
    {
        public ServiceId ServiceId => new("test.worker-safety");
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(new MonotonicDuration(1), new MonotonicDuration(2));
        public IServiceCycleWorkerDefinition<SafetyState, SafetyAction>
            CreateWorkerDefinition() => new SafeWorker();
        public ServiceStartDecision ShouldStart(in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        public ServiceActionJournalAttribution DescribeAction(in SafetyAction action) =>
            ServiceActionJournalAttribution.Publication;
        public ServiceActionResult TryExecute(in SafetyAction action, in SuiteRuntimeConfiguration config, in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
    }

    private abstract class SafetyWorkerBase :
        IServiceCycleWorkerDefinition<SafetyState, SafetyAction>
    {
        public SafetyState CreateState(LifecycleGeneration lifecycle) => new();
        public void ReleaseState(ref SafetyState state) => state = null!;

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref SafetyState state,
            ServiceActionWriter<SafetyAction> actions) => WakePolicy.Immediate;
        public void ProjectState(
            in SafetyState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) { }
    }

    private sealed class SafeWorker : SafetyWorkerBase { }
    private sealed class MainRetainingWorker : SafetyWorkerBase
    {
        private readonly SafetyMainDefinition _main;
        internal MainRetainingWorker(SafetyMainDefinition main) => _main = main;
    }
    private sealed class ObjectRetainingWorker : SafetyWorkerBase
    {
        private readonly object _hidden;
        internal ObjectRetainingWorker(object hidden) => _hidden = hidden;
    }
    private interface IHiddenValue { }
    private sealed class HiddenValue : IHiddenValue { }
    private sealed class InterfaceRetainingWorker : SafetyWorkerBase
    {
        private readonly IHiddenValue _hidden;
        internal InterfaceRetainingWorker(IHiddenValue hidden) => _hidden = hidden;
    }
    private sealed class DelegateRetainingWorker : SafetyWorkerBase
    {
        private readonly Action _hidden;
        internal DelegateRetainingWorker(Action hidden) => _hidden = hidden;
    }
    private sealed class FrameworkRetainingWorker : SafetyWorkerBase
    {
        private readonly ManualResetEventSlim _hidden;
        internal FrameworkRetainingWorker(ManualResetEventSlim hidden) => _hidden = hidden;
    }
    private sealed class CommonRuntimeRetainingWorker : SafetyWorkerBase
    {
        private readonly ServiceCycleRegistry _hidden;
        internal CommonRuntimeRetainingWorker(ServiceCycleRegistry hidden) => _hidden = hidden;
    }
    private readonly struct SafetyRow
    {
        internal SafetyRow(int value) => Value = value;
        internal int Value { get; }
    }
    private sealed class PublicationTableRetainingWorker : SafetyWorkerBase
    {
        private readonly PublicationTable<SafetyRow> _rows;
        internal PublicationTableRetainingWorker(PublicationTable<SafetyRow> rows) => _rows = rows;
    }
    private abstract class ObjectRetainingWorkerBase : SafetyWorkerBase
    {
        private readonly object _hidden;
        protected ObjectRetainingWorkerBase(object hidden) => _hidden = hidden;
    }
    private sealed class InheritedObjectRetainingWorker : ObjectRetainingWorkerBase
    {
        internal InheritedObjectRetainingWorker(object hidden) : base(hidden) { }
    }
    private sealed class StaticObjectRetainingWorker : SafetyWorkerBase
    {
        private static readonly object Hidden = new();
    }
}
