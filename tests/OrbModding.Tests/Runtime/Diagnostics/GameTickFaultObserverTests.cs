using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using HarmonyLib;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameTickFaultObserverCollection
{
    public const string Name = "Game tick fault observer isolation";
}

[Collection(GameTickFaultObserverCollection.Name)]
public sealed class GameTickFaultObserverTests : IDisposable
{
    private long _now;

    public GameTickFaultObserverTests()
    {
        GameTickFaultObserver.Reset();
        GameTickFaultObserver.UseClockForTests(() => _now);
    }

    public void Dispose()
    {
        GameTickFaultObserver.Reset();
        GameTickFaultObserver.UseClockForTests(Stopwatch.GetTimestamp);
    }

    [Fact]
    public void AThrowingElementPropagatesTheSameExceptionWithTheThrowersFrameOnTop()
    {
        var element = new ThrowingElement();

        var thrown = Assert.Throws<NullReferenceException>(
            () => GameTickFaultObserver.IncrementElement(element, 0.02f));

        Assert.Same(element.Thrown, thrown);
        var top = new StackTrace(thrown, false).GetFrame(0)!.GetMethod()!;
        Assert.Equal(nameof(ThrowingElement.Increment), top.Name);
        Assert.Equal(typeof(ThrowingElement), top.DeclaringType);
    }

    [Fact]
    public void AnElementIsLoggedOnceAndItsRepeatsAreCounted()
    {
        var element = new ThrowingElement();
        var logged = new List<GameTickFaultReport>();

        Throw(element, 3);
        GameTickFaultObserver.Drain(_ => "Faulting Element", logged.Add);
        Throw(element, 2);
        GameTickFaultObserver.Drain(_ => "Faulting Element", logged.Add);

        var report = Assert.Single(logged);
        Assert.Equal("Faulting Element", report.Subject);
        Assert.Equal(typeof(NullReferenceException).FullName, report.ExceptionType);
        Assert.Contains(nameof(ThrowingElement.Increment), report.FirstTrace);
        Assert.Equal(5, Assert.Single(GameTickFaultObserver.Snapshot()).Count);
    }

    [Fact]
    public void ANewExceptionTypeFromTheSameElementIsItsOwnFault()
    {
        var element = new ThrowingElement();
        Throw(element, 1);
        element.Next = () => new InvalidOperationException("other");
        Throw<InvalidOperationException>(element, 1);

        Assert.Equal(2, GameTickFaultObserver.Snapshot().Count);
    }

    [Fact]
    public void EveryTickFailingForTheThresholdMakesTheElementRepeating()
    {
        var element = new ThrowingElement();

        Throw(element, GameTickFaultObserver.RepeatThreshold - 1);
        Assert.Null(GameTickFaultObserver.Repeating);
        Throw(element, 1);

        var repeating = GameTickFaultObserver.Repeating;
        Assert.NotNull(repeating);
        Assert.Equal(GameTickFaultObserver.RepeatThreshold, repeating!.Count);
    }

    [Fact]
    public void ThrowsSeparatedByMoreThanASecondNeverAddUpToRepeating()
    {
        var element = new ThrowingElement();

        for (var index = 0; index < GameTickFaultObserver.RepeatThreshold * 2; index++)
        {
            _now += Stopwatch.Frequency + 1;
            Throw(element, 1);
        }

        Assert.Null(GameTickFaultObserver.Repeating);
    }

    [Fact]
    public void DistinctFaultsStopBeingRecordedAtTheCapButStillPropagate()
    {
        for (var index = 0; index < GameTickFaultObserver.MaxDistinctFaults + 3; index++)
            Throw(new ThrowingElement(), 1);

        Assert.Equal(GameTickFaultObserver.MaxDistinctFaults, GameTickFaultObserver.Snapshot().Count);
    }

    [Fact]
    public void ResetForgetsEveryFaultAndTheRepeatingOne()
    {
        var element = new ThrowingElement();
        Throw(element, GameTickFaultObserver.RepeatThreshold);

        GameTickFaultObserver.Reset();

        Assert.Null(GameTickFaultObserver.Repeating);
        Assert.Empty(GameTickFaultObserver.Snapshot());
        var logged = 0;
        GameTickFaultObserver.Drain(_ => "x", _ => logged++);
        Assert.Equal(0, logged);
    }

    [Fact]
    public void AViewWhoseVisibilityCheckThrowsIsRecordedAsAView()
    {
        var view = new ManagedView();
        var failure = new ArgumentException("view");
        view.CheckViewVisibilityForTest = () => throw failure;
        var logged = new List<GameTickFaultReport>();

        Assert.Same(failure, Assert.Throws<ArgumentException>(() => GameTickFaultObserver.CheckView(view)));
        GameTickFaultObserver.Drain(_ => "Spells view", logged.Add);

        Assert.Equal(GameTickFaultSite.View, Assert.Single(logged).Site);
        Assert.StartsWith("The game's own view update threw System.ArgumentException from Spells view", logged[0].Headline);
    }

    [Fact]
    public void ANamerThatThrowsStillLogsTheFaultUnderItsNativeType()
    {
        Throw(new ThrowingElement(), 1);
        var logged = new List<GameTickFaultReport>();

        GameTickFaultObserver.Drain(_ => throw new InvalidOperationException("no name"), logged.Add);

        Assert.StartsWith(typeof(ThrowingElement).FullName + " (name unavailable:", Assert.Single(logged).Subject);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void AnElementThatDoesNotThrowRunsThroughTheObserverWithoutAllocating()
    {
        var element = new CountingElement();

        var allocated = AllocationProbe.MeasureRepeated(
            10_000,
            () => GameTickFaultObserver.IncrementElement(element, 0.02f));

        Assert.Equal(0, allocated);
        Assert.Equal(20_000, element.Calls);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void AViewThatDoesNotThrowRunsThroughTheObserverWithoutAllocating()
    {
        var calls = 0;
        var view = new ManagedView { CheckViewVisibilityForTest = () => calls++ };

        var allocated = AllocationProbe.MeasureRepeated(10_000, () => GameTickFaultObserver.CheckView(view));

        Assert.Equal(0, allocated);
        Assert.Equal(20_000, calls);
    }

    [Fact]
    public void TheTranspilerSwapsEveryAuditedCallSiteForTheObserver()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Ldarg_0),
            new(OpCodes.Callvirt, GameTickFaultHooks.NativeIncrement),
            new(OpCodes.Ldarg_1),
            new(OpCodes.Callvirt, GameTickFaultHooks.NativeIncrement),
            new(OpCodes.Ret),
        };

        var rewritten = GameTickFaultHooks.TranspileElementIterator(instructions);

        var calls = new List<CodeInstruction>();
        foreach (var instruction in rewritten)
            if (Equals(instruction.operand, GameTickFaultHooks.ObservedIncrement)) calls.Add(instruction);
        Assert.Equal(GameTickFaultHooks.ElementCallSites, calls.Count);
        Assert.All(calls, call => Assert.Equal(OpCodes.Call, call.opcode));
        Assert.DoesNotContain(rewritten, instruction => Equals(instruction.operand, GameTickFaultHooks.NativeIncrement));
    }

    [Fact]
    public void ABodyWhoseCallSitesDifferFromTheAuditIsNotPatched()
    {
        var instructions = new List<CodeInstruction>
        {
            new(OpCodes.Callvirt, GameTickFaultHooks.NativeCheckView),
            new(OpCodes.Callvirt, GameTickFaultHooks.NativeCheckView),
        };

        var refused = Assert.Throws<InvalidOperationException>(
            () => GameTickFaultHooks.TranspileViewManager(instructions));

        Assert.Equal(
            "ViewManager:CheckViews has 2 ManagedView.CheckViewVisibility call site(s), not the audited 1.",
            refused.Message);
    }

    [Fact]
    public void AnElementWithAStableIdentityIsNamedByItsUuidAndNativeType()
    {
        var binding = new RuntimeIdentityRegistryBinding(() => typeof(IdScriptableObject));
        Assert.True(binding.Read().IsReady);
        var view = new ViewSO();
        var managed = new ManagedView { viewReference = view };

        var described = GameTickFaultSubject.Describe(managed, binding);

        Assert.Contains(view.uuid.ToString("D"), described);
        Assert.EndsWith("[" + typeof(ManagedView).FullName + "]", described);
    }

    [Fact]
    public void AnElementWithoutAStableIdentityIsNamedByItsNativeType()
    {
        var binding = new RuntimeIdentityRegistryBinding(() => typeof(IdScriptableObject));

        Assert.Equal(
            "[" + typeof(CountingElement).FullName + "]",
            GameTickFaultSubject.Describe(new CountingElement(), binding));
    }

    private static void Throw(ThrowingElement element, int times) =>
        Throw<NullReferenceException>(element, times);

    private static void Throw<TException>(ThrowingElement element, int times)
        where TException : Exception
    {
        for (var index = 0; index < times; index++)
            Assert.Throws<TException>(() => GameTickFaultObserver.IncrementElement(element, 0.02f));
    }

    private sealed class ThrowingElement : IIncrement
    {
        internal Func<Exception> Next { get; set; } = () => new NullReferenceException("native");

        internal Exception? Thrown { get; private set; }

        public void Increment(float deltaTime)
        {
            Thrown = Next();
            throw Thrown;
        }
    }

    private sealed class CountingElement : IIncrement
    {
        internal int Calls { get; private set; }

        public void Increment(float deltaTime) => Calls++;
    }
}
