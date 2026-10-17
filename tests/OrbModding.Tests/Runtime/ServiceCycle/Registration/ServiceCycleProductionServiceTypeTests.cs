using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

/// <summary>
/// Registration audits a service's state and action types, so a service no test registers is never
/// audited until the game starts it. This proves every production service's shape without
/// registering it.
/// </summary>
public sealed class ServiceCycleProductionServiceTypeTests
{
    private static readonly Type[] DefinitionContracts =
    {
        typeof(IServiceCycleDefinition<,>),
        typeof(IServiceCycleSourceDefinition<,>),
    };

    [Fact]
    public void EveryProductionServicePassesStructuralSafety()
    {
        var services = ProductionServiceTypes();

        Assert.Contains((typeof(StrategistCycleState), typeof(StrategistCycleAction)), services);
        var violations = services
            .Select(service =>
                ServiceCycleTypeGraphWalker.Validate(service.State, ServiceCycleTypeRole.State, "state") ??
                ServiceCycleTypeGraphWalker.Validate(service.Action, ServiceCycleTypeRole.Action, "action"))
            .Where(violation => violation.HasValue)
            .Select(violation => violation!.Value.Message)
            .ToArray();
        Assert.Empty(violations);
    }

    private static HashSet<(Type State, Type Action)> ProductionServiceTypes()
    {
        var services = new HashSet<(Type State, Type Action)>();
        foreach (var type in typeof(ServiceCycleRegistry).Assembly.GetTypes())
        {
            Collect(type, services);
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Collect(method.ReturnType, services);
        }

        return services;
    }

    private static void Collect(Type type, ISet<(Type State, Type Action)> services)
    {
        if (type.ContainsGenericParameters) return;
        var contracts = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        foreach (var contract in contracts)
        {
            if (!contract.IsGenericType ||
                !DefinitionContracts.Contains(contract.GetGenericTypeDefinition()))
                continue;
            var arguments = contract.GetGenericArguments();
            services.Add((arguments[0], arguments[1]));
        }
    }
}
