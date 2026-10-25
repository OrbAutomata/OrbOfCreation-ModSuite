using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameStubStateTests
{
    [Fact]
    public void ResetEmptiesEveryRegistryUsedByTheShippingNativeTypes()
    {
        Assert.Same(typeof(ResourceSO), WorldNativeTypes.Resolve("ResourceSO"));
        var types = new HashSet<Type>();
        foreach (var type in typeof(ResourceSO).Assembly.GetTypes().Where(type => !type.ContainsGenericParameters))
            for (var current = type; current is not null; current = current.BaseType)
                types.Add(current);
        var registries = types.SelectMany(type => type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => field.Name is "All" or "RuntimeLookup" or "globalTooltips")
            .Select(field => (Field: field, Value: field.GetValue(null)))
            .ToArray();
        Assert.NotEmpty(registries);
        foreach (var (field, value) in registries)
        {
            if (value is IDictionary dictionary)
            {
                dictionary.Add(Guid.NewGuid(), new ResourceSO());
            }
            else if (value is IList list)
            {
                // Null is a row too: reset must empty a registry even if a previous test left a
                // destroyed native reference in it. This also populates abstract element lists.
                list.Add(null);
            }
            else throw new InvalidOperationException($"Unmodelled registry {field.DeclaringType}.{field.Name}.");
        }

        OrbModding.GameStubs.GameStubState.Reset();

        foreach (var (field, _) in registries)
        {
            var current = field.GetValue(null);
            Assert.True(current is ICollection collection && collection.Count == 0,
                $"Reset left rows in {field.DeclaringType}.{field.Name}.");
        }
    }

    [Fact]
    public void TheInstalledPerTestHookPreservesConstructorStateAndCleansUpEachCase()
    {
        Assert.Equal(nameof(TheInstalledPerTestHookPreservesConstructorStateAndCleansUpEachCase),
            OrbModding.TestSupport.ResetGameStubsAttribute.CurrentCase?.Name);
        var hook = typeof(GameStubStateTests).Assembly
            .GetCustomAttributes<OrbModding.TestSupport.ResetGameStubsAttribute>().Single();
        var method = typeof(GameStubStateTests).GetMethod(nameof(TheInstalledPerTestHookPreservesConstructorStateAndCleansUpEachCase))!;
        ResourceSO.All.Add(new ResourceSO());
        TargetingManager.OpenRequests = 2;
        hook.Before(method);
        Assert.Single(ResourceSO.All);
        Assert.Equal(2, TargetingManager.OpenRequests);
        hook.After(method);
        Assert.Empty(ResourceSO.All);
        Assert.Equal(0, TargetingManager.OpenRequests);
        ResourceSO.All.Add(new ResourceSO());
        hook.After(method);
        Assert.Empty(ResourceSO.All);
    }
}
