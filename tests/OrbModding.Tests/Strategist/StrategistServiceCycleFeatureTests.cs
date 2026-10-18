using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using Xunit;

namespace OrbModding.Tests.Strategist;

public sealed class StrategistServiceCycleFeatureTests
{
    [Fact]
    public void TheSharedHostStartsWithTheStrategistRegistered()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var frame = 0L;
        var log = new ManualLogSource();

        using var runtime = AutomataServiceCycleComposition.TryCreate(
            configuration.Current,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => ++frame,
                () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            log);

        Assert.True(runtime is not null, string.Join("\n", log.Entries));
        for (var index = 0; index < 3; index++)
            runtime.Tick(0);
    }
}
