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
