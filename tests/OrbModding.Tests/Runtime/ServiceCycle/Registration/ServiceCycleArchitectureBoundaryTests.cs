using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleArchitectureTests
{
    [Fact]
    public void PublicServiceCycleSurfaceDoesNotLeakNativeLegacyDelegateOrMutableCollectionTypes()
    {
        var serviceCycleTypes = ServiceCycleTypes(publicOnly: true);
        var violations = new List<string>();
        foreach (var type in serviceCycleTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                AuditPublicSurface(property.PropertyType, type.FullName + "." + property.Name, violations);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                AuditPublicSurface(field.FieldType, type.FullName + "." + field.Name, violations);
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                AuditPublicSurface(method.ReturnType, type.FullName + "." + method.Name + " return", violations);
                foreach (var parameter in method.GetParameters())
                {
                    if (method.Name == nameof(object.Equals) && parameter.ParameterType == typeof(object)) continue;
                    AuditPublicSurface(parameter.ParameterType, type.FullName + "." + method.Name + " parameter", violations);
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RunnerKeepsTwoGenericExecutionShapeAndTheRegistryOwnsEveryPublication()
    {
        Assert.Equal(2, typeof(ServiceRunner<,>).GetGenericArguments().Length);
        Assert.DoesNotContain(typeof(ServiceRunner<,>).GetGenericArguments(), argument =>
            argument.Name.Contains("Strategy", StringComparison.Ordinal));
        Assert.Null(typeof(ServiceRunner<,>).GetProperty(
            "Configuration", BindingFlags.Instance | BindingFlags.Public));
        // Registration hands out no publication: the suite has one of each, and the registry owns them.
        Assert.Null(typeof(ServiceRegistration<,>).GetProperty(
            "Configuration", BindingFlags.Instance | BindingFlags.Public));
        // Installed nowhere: the registry constructs all three publications, so there is no step
        // that could be skipped, repeated, or run in the wrong order.
        Assert.Empty(typeof(ServiceCycleRegistry)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Use", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Registration is a list of services, not a negotiation. Nothing is bound: the publications are
    /// installed once on the registry, and the world gate follows from the composition's shape.
    /// </summary>
    [Fact]
    public void NoServiceBindsItsWayIntoAPublicationOrTheWorldGate()
    {
        Assert.Empty(typeof(ServiceRegistration<,>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Bind", StringComparison.Ordinal)));
        Assert.Empty(typeof(ServiceCycleRegistry)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Bind", StringComparison.Ordinal)));
    }

    /// <summary>
    /// One service reads the game and publishes it; every other service consumes that publication and
    /// acts. A second Source would mean two answers to "what does the world look like".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the contract rather than off a declared dispatch policy: taking the source contract
    /// is now the declaration, and the registration path supplies the policy. Open generic factories
    /// are excluded — <c>AutomataService.DefineSource</c> composes whatever it is handed and names no
    /// service.
    /// </para>
    /// <para>
    /// The other half of the shape rule — that a Source commits publications and never a native
    /// mutation — is a runtime fact about the collection service, and is asserted where that service
    /// executes rather than restated here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlySourceShapedServiceInProductionIsTheWorldCollector()
    {
        var declared = typeof(AutomataServiceCycleComposition).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method =>
                method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(IServiceCycleSourceDefinition<,>))
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.Equal(
            new[] { typeof(AutomataWorldCollectionService) },
            declared.Where(method => !method.ReturnType.ContainsGenericParameters)
                .Select(method => method.DeclaringType!)
                .Distinct()
                .ToArray());
    }

    /// <summary>
    /// What the main thread asks of a service carries no capture, and neither does the ordinary shape
    /// built on it.
    /// </summary>
    /// <remarks>
    /// The exact member sets rather than the absence of one name, so a capture cannot come back as
    /// <c>Read</c> or <c>Collect</c>. A capture is main-thread game access, and a service that has
    /// one costs the frame whatever it spends there; letting the shared contract carry one would make
    /// that cost available to every service and leave the runtime unable to say which services paid
    /// it.
    /// </remarks>
    [Fact]
    public void NeitherTheSharedNorTheOrdinaryContractHasACaptureMember()
    {
        Assert.Equal(
            new[]
            {
                "ServiceId",
                "DefaultWakePolicy",
                "FaultRecoveryPolicy",
                "ShouldStart",
                "DescribeAction",
                "TryExecute",
            },
            MemberNames(typeof(IServiceCycleMainThreadDefinition<>)));
        Assert.Equal(
            new[] { "CreateWorkerDefinition" },
            MemberNames(typeof(IServiceCycleDefinition<,>)));
    }

    /// <summary>
    /// The two shapes are siblings on one main-thread contract, and only one of them captures.
    /// </summary>
    /// <remarks>
    /// Siblings rather than source-extends-ordinary: the two hand back different worker contracts,
    /// because one evaluation reads the published world and the other the buffer its own capture
    /// filled. An inheritance would force the source to also satisfy the ordinary worker factory and
    /// hand back a worker it cannot honour.
    /// </remarks>
    [Fact]
    public void TheSourceContractIsTheOrdinarySharedHalfPlusACapture()
    {
        Assert.Equal(
            new[] { "CreateWorkerDefinition", "Capture" },
            MemberNames(typeof(IServiceCycleSourceDefinition<,>)));
        Assert.Equal(
            typeof(IServiceCycleMainThreadDefinition<SourceAction>),
            typeof(IServiceCycleSourceDefinition<SourceState, SourceAction>).GetInterfaces().Single());
        Assert.Equal(
            typeof(IServiceCycleMainThreadDefinition<ExecutionAction>),
            typeof(IServiceCycleDefinition<ExecutionState, ExecutionAction>).GetInterfaces().Single());
    }

    /// <summary>
    /// One registration path takes a capture, and it names the dispatch class itself.
    /// </summary>
    /// <remarks>
    /// A caller that could name the policy could declare a source and dispatch it as a mutation, and
    /// the runtime derives the shape from the policy — so the declaration and the shape would be free
    /// to disagree. Taking the source contract is the declaration; the policy follows from it.
    /// </remarks>
    [Fact]
    public void ExactlyOneRegistrationPathTakesASourceAndItIsAlwaysPublicationDispatched()
    {
        var entryPoints = typeof(ServiceCycleRegistry)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() ==
                    typeof(IServiceCycleSourceDefinition<,>)))
            .ToArray();

        Assert.Equal(new[] { "RegisterSource", "RegisterSource" }, entryPoints.Select(m => m.Name).ToArray());
        Assert.DoesNotContain(
            entryPoints,
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(ServiceActionDispatchPolicy)));

        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        using var registration = registry.RegisterSource(
            new SourceServiceDefinition("architecture.source.dispatch"),
            new LifecycleGeneration(1));

        Assert.Equal(ServiceShape.Source, registry.GetSlot(0).ActionDispatchPolicy.Shape);
    }

    private static string[] MemberNames(Type contract) => contract
        .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Select(member => member.Name)
        .Select(name => name.StartsWith("get_", StringComparison.Ordinal) ? name[4..] : name)
        .Distinct()
        .ToArray();

    /// <summary>
    /// A cycle is identified by its cycle id and the readings it pinned. The capture sequence is not
    /// one of them.
    /// </summary>
    /// <remarks>
    /// It was minted in lockstep with the cycle id and never disagreed with it, so every consumer
    /// that keyed on it was keying on the cycle id by another name — and an ordinary service, which
    /// captures nothing, had no honest value to put there. The exact member set is asserted rather
    /// than the absence alone, so a later re-addition under a different name fails here too. The
    /// sequence still identifies a capture on <see cref="ServiceCaptureContext"/>.
    /// </remarks>
    [Fact]
    public void TheCycleIdentityNamesNoCaptureSequence()
    {
        Assert.Equal(
            new[] { "Service", "Lifecycle", "Config", "Strategy", "World", "Cycle", "IsValid" },
            typeof(ServiceCycleIdentity)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray());
        Assert.Contains(
            typeof(ServiceCaptureContext).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.PropertyType == typeof(CaptureSequence));
    }

    /// <summary>
    /// One field per publication — world, configuration, strategy — all three constructed by the
    /// registry.
    /// </summary>
    /// <remarks>
    /// Constructed rather than installed, and readonly rather than assignable: the three publications
    /// exist for as long as the registry does, so a service registered before the plugin has read its
    /// settings file, before the first collection, and before any strategist exists still gets a real
    /// snapshot of each instead of a missing one.
    /// </remarks>
    [Fact]
    public void TheSuiteHasExactlyOneFieldPerPublicationAndBuildsAllThree()
    {
        var registryFields = typeof(ServiceCycleRegistry)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        var configuration = Assert.Single(registryFields.Where(field =>
            field.FieldType == typeof(ServiceConfigurationPublisher)));
        var world = Assert.Single(registryFields.Where(field =>
            field.FieldType == typeof(ServiceWorldPublisher<GameWorldState>)));
        var strategy = Assert.Single(registryFields.Where(field =>
            field.FieldType == typeof(ServiceStrategyPublisher)));
        Assert.True(configuration.IsInitOnly);
        Assert.True(world.IsInitOnly);
        Assert.True(strategy.IsInitOnly);
        // Nothing outside the runtime can make one: the suite's publications are the registry's.
        Assert.Empty(typeof(ServiceConfigurationPublisher)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(ServiceStrategyPublisher)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void LowerLayersDoNotDependOnUpperLayersAcrossStorageSignaturesLocalsOrIl()
    {
        var violations = new List<string>();

        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Contracts",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Configuration",
                "OrbModding.Common.Runtime.ServiceCycle.Execution",
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Configuration",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Execution",
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Execution",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Registration",
            new[] { "OrbModding.Common.Runtime.ServiceCycle.Orchestration" },
            violations);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
