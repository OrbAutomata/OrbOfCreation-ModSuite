using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleStateSurfaceTypeSafetyTests
{
    [Fact]
    public void AnUnsealedFrameIsRejected() => AssertFrameRejected(new UnsealedFrame());

    /// <summary>
    /// A worker's state may hold the scratch its evaluation fills.
    /// </summary>
    /// <remarks>
    /// It cannot: state and projection are both the worker's, filled and read on the same thread
    /// inside one evaluation, and every cycle overwrites the scratch before reading it. What the
    /// rule actually forbade was reusing the row arrays underneath — the whole reason a buffer is
    /// worth keeping. Actions still may not retain a frame; those do cross a thread, and that rule
    /// stands.
    /// </remarks>
    [Fact]
    public void WorkerStateMayHoldTheScratchItsEvaluationFills()
    {
        using var registry = new ServiceCycleRegistry(1);

        using var registration = registry.Register(
            new TypeSafetyDefinition<StateWithScratch, ImmutableAction>(
                new StateWithScratch(new SafeFrame())),
            new LifecycleGeneration(1));

        Assert.NotNull(registration);
    }

    [Fact]
    public void NeutralStorageIsNotRejectedByAdapterNamingConvention()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<StateWithAdapterName, ImmutableAction>(
                new StateWithAdapterName(new NeutralAdapter())),
            new LifecycleGeneration(1));
        Assert.NotNull(registration);
    }

    [Fact]
    public void FrameAndStateCannotExposeArraysOrCollectionProperties()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameRejected(new FrameWithPublicArray());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<StateWithPublicCollection, ImmutableAction>(
                new StateWithPublicCollection()),
            new LifecycleGeneration(1)));
    }

    [Fact]
    public void PublicAndProtectedBackingMemoryViewsAreRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameRejected(new FrameWithPublicSpan());
        AssertFrameRejected(new FrameWithProtectedReadOnlySpan());
        AssertFrameRejected(new FrameWithPublicMemory());
        AssertFrameRejected(new FrameWithPublicReadOnlyMemory());
    }

    [Fact]
    public void ComputedPropertiesCannotBypassGraphSafety()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(new ComputedCollectionConfig());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeState, ActionWithComputedCaptureBuffer>(
                new SafeState()),
            new LifecycleGeneration(1)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeState, ActionWithComputedHandle>(
                new SafeState()),
            new LifecycleGeneration(1)));
    }

    [Fact]
    public void PublicSettersAreRejectedWhileInternalMutationRemainsAllowed()
    {
        using var registry = new ServiceCycleRegistry(2);
        AssertConfigurationRejected(new ComputedMutableConfig());
        AssertFrameRejected(new FrameWithPublicSetter());
        AssertFrameAccepted(new FrameWithInternalSetter());
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeState, ImmutableAction>(new SafeState()),
            new LifecycleGeneration(1));
        Assert.NotNull(registration);
    }

    private class UnsealedFrame { }
    private sealed class StateWithScratch
    {
        private readonly SafeFrame _scratch;
        internal StateWithScratch(SafeFrame scratch) => _scratch = scratch;
    }
    private sealed class StateWithAdapterName
    {
        private readonly NeutralAdapter _adapter;
        internal StateWithAdapterName(NeutralAdapter adapter) => _adapter = adapter;
    }
    private sealed class NeutralAdapter { }
    private sealed class FrameWithPublicArray
    {
        private readonly int[] _items = { 1 };
        public int[] Items => _items;
    }
    private sealed class StateWithPublicCollection
    {
        private readonly int[] _items = { 1 };
        public IReadOnlyList<int> Items => _items;
    }
    private sealed class FrameWithPublicSpan
    {
        private readonly int[] _items = { 1 };
        public Span<int> Items => _items;
    }
    private abstract class ProtectedReadOnlySpanSurface
    {
        private readonly int[] _items = { 1 };
        protected ReadOnlySpan<int> Items => _items;
    }
    private sealed class FrameWithProtectedReadOnlySpan : ProtectedReadOnlySpanSurface { }
    private sealed class FrameWithPublicMemory
    {
        private readonly int[] _items = { 1 };
        public Memory<int> Items => _items;
    }
    private sealed class FrameWithPublicReadOnlyMemory
    {
        private readonly int[] _items = { 1 };
        public ReadOnlyMemory<int> Items => _items;
    }
    private sealed class ComputedCollectionConfig
    {
        public IReadOnlyList<int> Values => new[] { 1 };
    }
    private sealed class ComputedMutableConfig
    {
        public int Value { get => 0; set { } }
    }
    private readonly struct ActionWithComputedCaptureBuffer
    {
        public GameWorldCycleFrame Frame => new();
    }
    private readonly struct ActionWithComputedHandle
    {
        public IntPtr Handle => IntPtr.Zero;
    }
    private sealed class FrameWithPublicSetter { public int Value { get; set; } }
    private sealed class FrameWithInternalSetter { internal int Value { get; set; } }
}
