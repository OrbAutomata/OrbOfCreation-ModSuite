using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestNativeBindingsTests
{
    [Fact]
    public void TheGameShapedQueueBinds()
    {
        Assert.True(AutoHarvestNativeBindings.TryCreate(out var bindings, out var reason), reason);
        Assert.Equal(typeof(PlotNodeActionInstanceListVariable), bindings!.ActiveActionsType);
    }

    /// <summary>
    /// The game declares the queue's entries as a <c>List</c> of instances; an array of the same
    /// instances is a different game, not a looser fit.
    /// </summary>
    [Fact]
    public void AQueueWhoseEntriesAreNotTheDeclaredListLeavesTheSetUnbound()
    {
        var bound = AutoHarvestNativeBindings.TryCreate(
            out var bindings,
            out var reason,
            Queue<ArrayBacked.Queue>());

        Assert.False(bound, "an array-backed queue bound as the game's list");
        Assert.Null(bindings);
        Assert.Contains("abstract-list.value", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The room reads are declared on the queue's generic base, so they are bound there, where a
    /// lookup on the queue itself would not see a member the base keeps private.
    /// </summary>
    [Fact]
    public void TheRoomReadsBindOnTheGenericBaseThatDeclaresThem()
    {
        Assert.True(
            AutoHarvestNativeBindings.TryCreate(out var bindings, out var reason, Queue<PrivateBase.Queue>()),
            reason);

        var queue = new PrivateBase.Queue();
        Assert.Equal(3, bindings!.ActiveUsedSpots(queue));
        Assert.True(bindings.ActiveHasEmptySpot(queue));
    }

    private static Func<string, Type?> Queue<T>() =>
        name => name == "PlotNodeActionInstanceListVariable" ? typeof(T) : ReflectionUtil.FindLoadedType(name);

    private static class ArrayBacked
    {
        public class EmptyTypeListVariable<T>
        {
            public T[] value = Array.Empty<T>();
            public int GetUsedSpots() => value.Length;
            public bool HasEmptySpot() => true;
        }

        public sealed class Queue : EmptyTypeListVariable<PlotNodeActionInstance>
        {
            public void AddInstance(PlotNodeActionInstance instance, int quantity)
            {
            }
        }
    }

    private static class PrivateBase
    {
        public class EmptyTypeListVariable<T>
        {
            public List<T> value = new();
            private int GetUsedSpots() => 3;
            private bool HasEmptySpot() => true;
        }

        public sealed class Queue : EmptyTypeListVariable<PlotNodeActionInstance>
        {
            public void AddInstance(PlotNodeActionInstance instance, int quantity)
            {
            }
        }
    }
}
