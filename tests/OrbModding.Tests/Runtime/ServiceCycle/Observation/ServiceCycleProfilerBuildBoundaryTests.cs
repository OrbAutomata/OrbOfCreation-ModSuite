using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation;

public sealed class ServiceCycleProfilerBuildBoundaryTests
{
    private static readonly string[] ProfileNamespaces =
    {
        "OrbModding.Common.Runtime.ServiceCycle.Observation.Profile",
        "OrbAutomata.Runtime.ServiceCycle.Profile",
        "OrbModConfig.Runtime.ServiceCycle.Profile",
        "OrbAutomata.GameMcp",
    };

    [Fact]
    public void OrdinaryAssembliesContainNoProfilerTypes()
    {
        // Runtime, features and plugin ship as one assembly; scanning it three times said nothing
        // three times.
        var assemblies = new[] { typeof(global::OrbModding.Plugin).Assembly };

        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(assembly.GetTypes(), type =>
                ProfileNamespaces.Any(root => IsNamespaceOrChild(type.Namespace, root)));
        }
    }

    [Fact]
    public void OrdinaryCompositionAndHotPathsContainNoProfilerOrWriterReferences()
    {
        var forbidden = ProfileNamespaces
            .Concat(new[]
            {
                "OrbModding.Common.Runtime.Tracing.BufferedSegments",
                "System.Diagnostics",
            })
            .ToArray();
        var guardedTypes = new[]
        {
            typeof(SuiteFramePump),
            typeof(ServiceCycleStartCoordinator<,>),
            typeof(ServiceCycleWorker<,>),
            typeof(ServiceCycleSemanticRuntimeTrace),
            typeof(ServiceCycleSemanticRuntimeTraceMultiplexer),
            typeof(AutomataServiceCycleComposition),
            typeof(AutomataServiceCycleRuntime),
            typeof(AutoHarvestServiceCycleFeature),
            typeof(AutoHarvestFeatureRuntime),
            typeof(AutoHarvestServiceAdapterComposition),
            typeof(AutoHarvestFrameProjector),
            typeof(AutoHarvestBindingResolver),
            typeof(AutoHarvestNativeStateReader),
            typeof(AutoHarvestActionSafety),
            typeof(AutoHarvestStableIdAccessor),
            typeof(AutoHarvestReflectionAccess),
            typeof(AutoItemsServiceCycleFeature),
            typeof(AutoItemsFeatureRuntime),
            typeof(AutoItemsServiceAdapterComposition),
            typeof(AutoItemsConsumableUseGameAction),
            typeof(AutoItemsNativeBindings),
            typeof(AutoItemsScrollTargetPreflight),
            typeof(global::OrbModding.Plugin),
        };
        var violations = new List<string>();
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in guardedTypes)
        {
            foreach (var field in type.GetFields(members))
            {
                if (forbidden.Any(root => IsNamespaceOrChild(field.FieldType.Namespace, root)))
                    violations.Add(type.FullName + "." + field.Name + " field");
            }
            var callables = type.GetMethods(members).Cast<MethodBase>()
                .Concat(type.GetConstructors(members));
            if (type.TypeInitializer is not null) callables = callables.Append(type.TypeInitializer);
            foreach (var callable in callables)
            {
                var body = callable.GetMethodBody();
                if (body is null) continue;
                ServiceCycleIlDependencyScanner.Audit(
                    callable,
                    body,
                    (type.FullName ?? type.Name) + "." + callable.Name,
                    violations,
                    forbidden);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool IsNamespaceOrChild(string? candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal) ||
        candidate?.StartsWith(root + ".", StringComparison.Ordinal) == true;
}

/// <summary>
/// Stable acceptance-test name for the Game MCP's ordinary-build absence.
/// </summary>
public sealed class GameMcpBuildBoundaryTests
{
    [Fact]
    public void OrdinaryAssemblyContainsNoGameMcpTypes()
    {
        var assembly = typeof(global::OrbModding.Plugin).Assembly;
        Assert.DoesNotContain(
            assembly.GetTypes(),
            type => string.Equals(type.Namespace, "OrbAutomata.GameMcp", StringComparison.Ordinal) ||
                    type.Namespace?.StartsWith("OrbAutomata.GameMcp.", StringComparison.Ordinal) == true);
    }
}
