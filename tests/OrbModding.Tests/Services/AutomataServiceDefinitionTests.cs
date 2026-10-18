using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceDefinitionTests
{
    [Fact]
    public void ADefinedServiceRegistersAsAnOrdinaryService()
    {
        var metadata = Metadata();
        var definition = AutomataService.Define<State, Action>(
            in metadata,
            createWorker: () => new WorkerDefinition(),
            shouldStart: (
                in SuiteRuntimeConfiguration _,
                in ServiceCycleStartContext _) =>
                ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready),
            describeAction: (in Action _) =>
                ServiceActionJournalAttribution.Publication,
            execute: (
                in Action _,
                in SuiteRuntimeConfiguration _,
                in ServiceActionContext _) =>
                ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected));

        using var registry = new OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1));
        using var registration = registry.Register(definition);

        Assert.Equal(0, registration.Ordinal);
        Assert.Equal(ServiceShape.Ordinary, registry.GetSlot(0).ActionDispatchPolicy.Shape);
    }

    private static AutomataServiceMetadata Metadata() => new(
        new ServiceId("test.composed"),
        WakePolicy.Immediate,
        new ServiceFaultRecoveryPolicy(
            new MonotonicDuration(1),
            new MonotonicDuration(2)));

    private readonly struct State { }
    private readonly struct Action { }

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<State, Action>
    {
        public State CreateState(LifecycleGeneration lifecycle) => default;
        public void ReleaseState(ref State state) => state = default;

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref State state,
            ServiceActionWriter<Action> actions) =>
            WakePolicy.Immediate;

        public void ProjectState(
            in State state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output)
        {
        }
    }
}
