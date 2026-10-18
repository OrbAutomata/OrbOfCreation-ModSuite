using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
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

    private static AutomataServiceCycleRuntime? TryCreate(
        ManualLogSource log,
        Action<LifecycleGameActions> compose) =>
        AutomataServiceCycleComposition.TryCreate(
            BepInExAutomataConfiguration.Bind(new ConfigFile()).Current,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            log,
            compose);
}
