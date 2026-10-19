using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceCycleCompositionTests
{
    [Fact]
    public void HostStartupContainsARecoverableFailureAndPropagatesAProcessFatalOne()
    {
        var log = new ManualLogSource();

        Assert.Null(TryCreate(log, _ => throw new InvalidOperationException("the factory failed")));
        Assert.Contains(log.Entries, entry =>
            entry.ToString()!.Contains("the factory failed", StringComparison.Ordinal));
        Assert.Throws<OutOfMemoryException>(() =>
            TryCreate(new ManualLogSource(), _ => throw new OutOfMemoryException()));
    }

    [Fact]
    public void TheActionTimelineBindsAServiceWhoseWorkerBrieflyHoldsItsHandoffGate()
    {
        using var held = new HandoffGateHeldAfterRegistration(new StrategistServiceCycleFeature());

        try
        {
            using var runtime = AutomataServiceCycleComposition.Create(
                BepInExAutomataConfiguration.Bind(new ConfigFile()).Current,
                new ConfigGeneration(1),
                Dependencies(),
                new IAutomataServiceCycleFeature[] { held },
                new ManualLogSource());

            Assert.NotNull(runtime);
        }
        finally
        {
            held.CompositionReturned();
        }
    }

    private static AutomataServiceCycleRuntime? TryCreate(
        ManualLogSource log,
        Action<LifecycleGameActions> compose) =>
        AutomataServiceCycleComposition.TryCreate(
            BepInExAutomataConfiguration.Bind(new ConfigFile()).Current,
            new ConfigGeneration(1),
            Dependencies(),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            log,
            compose);

    private static AutomataServiceCycleHostDependencies Dependencies() =>
        new(
            () => 1,
            () => 1,
            new ServiceActionTimelineRegistry());

    private sealed class HandoffGateHeldAfterRegistration : IAutomataServiceCycleFeature, IDisposable
    {
        private static readonly TimeSpan HoldUntilCompositionReturns = TimeSpan.FromMilliseconds(500);
        private readonly IAutomataServiceCycleFeature _feature;
        private readonly ManualResetEventSlim _compositionReturned = new(false);
        private HandoffGateContention? _contention;
        private Task? _release;

        internal HandoffGateHeldAfterRegistration(IAutomataServiceCycleFeature feature) =>
            _feature = feature;

        public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
        {
            var runtime = _feature.Register(in context);
            var slot = context.Registry.GetSlot(0);
            var runner = slot.GetType()
                .GetProperty("Runner", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(slot)!;
            var contention = new HandoffGateContention(runner);
            contention.Acquire();
            _contention = contention;
            _release = Task.Run(() =>
            {
                _compositionReturned.Wait(HoldUntilCompositionReturns);
                contention.Release();
            });
            return runtime;
        }

        internal void CompositionReturned()
        {
            _compositionReturned.Set();
            _release?.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _contention?.Dispose();
            _compositionReturned.Dispose();
        }
    }
}
