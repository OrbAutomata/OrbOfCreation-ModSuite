using System;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Configuration;

public sealed class ServicePublicationTests
{
    [Fact]
    public void PublishingAdvancesTheOneGenerationEveryServiceReads()
    {
        using var registry = new ServiceCycleRegistry(3);
        registry.ConfigurationPublication.Publish(TestSuiteConfiguration.WithSetting(10));
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.config.a"),
            new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.config.b"),
            new RuntimeLifecycleGeneration(1));
        using var third = registry.Register(
            new SyntheticServiceDefinition("test.config.c"),
            new RuntimeLifecycleGeneration(1));

        Assert.Equal(2UL, registry.GetSlot(0).LatestConfiguration.Value);
        Assert.Equal(
            10,
            TestSuiteConfiguration.SettingOf(registry.Configuration.ReadLatest().Snapshot));

        var published = registry.ConfigurationPublication.Publish(
            TestSuiteConfiguration.WithSetting(20));

        Assert.Equal(3UL, published.Value);
        Assert.Equal(
            20,
            TestSuiteConfiguration.SettingOf(registry.Configuration.ReadLatest().Snapshot));
        for (var ordinal = 0; ordinal < 3; ordinal++)
            Assert.Equal(3UL, registry.GetSlot(ordinal).LatestConfiguration.Value);
    }

    /// <summary>
    /// A registry has its configuration from the moment it exists, on the all-defaults snapshot.
    /// </summary>
    /// <remarks>
    /// This used to be an installation step with an ordering rule and a "the suite has one" guard.
    /// Both are gone with the type: there is one configuration record, so the registry constructs it
    /// the way it constructs the world, and a service registered before anything published still
    /// reads a real snapshot rather than failing.
    /// </remarks>
    [Fact]
    public void ARegistryStartsOnTheAllDefaultsConfigurationBeforeAnythingPublishes()
    {
        using var registry = new ServiceCycleRegistry(1);

        Assert.Same(TestSuiteConfiguration.Default, registry.Configuration.ReadLatest().Snapshot);
        Assert.Equal(1UL, registry.Configuration.ReadLatest().Generation.Value);

        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.config.defaults"),
            new RuntimeLifecycleGeneration(1));

        Assert.Equal(1UL, registry.GetSlot(0).LatestConfiguration.Value);
    }

    /// <summary>
    /// A registry has its strategy from the moment it exists, on the neutral bulletin.
    /// </summary>
    /// <remarks>
    /// The third copy of the world's and the configuration's bargain, and it lands the same way:
    /// constructed with the registry rather than installed into it, so there is no ordering rule to
    /// get wrong and a service registered before any strategist exists still reads a real bulletin.
    /// </remarks>
    [Fact]
    public void ARegistryStartsOnTheNeutralBulletinBeforeAnythingPublishes()
    {
        using var registry = new ServiceCycleRegistry(1);

        Assert.Same(TestSuiteStrategy.Neutral, registry.Strategy.ReadLatest().Bulletin);
        Assert.Equal(1UL, registry.Strategy.ReadLatest().Generation.Value);

        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.strategy.defaults"),
            new RuntimeLifecycleGeneration(1));

        Assert.Equal(1UL, registry.GetSlot(0).LatestStrategy.Value);
    }

    [Fact]
    public void PublishingAStrategyAdvancesTheOneGenerationEveryServiceReads()
    {
        using var registry = new ServiceCycleRegistry(3);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.strategy.a"),
            new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.strategy.b"),
            new RuntimeLifecycleGeneration(1));
        using var third = registry.Register(
            new SyntheticServiceDefinition("test.strategy.c"),
            new RuntimeLifecycleGeneration(1));

        var published = registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(7));

        Assert.Equal(2UL, published.Value);
        Assert.Equal(
            7,
            TestSuiteStrategy.SettingOf(registry.Strategy.ReadLatest().Bulletin));
        for (var ordinal = 0; ordinal < 3; ordinal++)
            Assert.Equal(2UL, registry.GetSlot(ordinal).LatestStrategy.Value);
    }

    /// <summary>
    /// The worker is handed the bulletin its own cycle pinned, and the identity says which one.
    /// </summary>
    /// <remarks>
    /// The delivery half of the publication: a generation nothing can read is a number, not a
    /// policy. Both halves are asserted together because they are the same claim — the bulletin the
    /// evaluation saw is the one the cycle is stamped with.
    /// </remarks>
    [Fact]
    public void AWorkerIsHandedTheBulletinItsCycleWasPinnedTo()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.strategy.pinned");
        using var registration = registry.Register(
            definition,
            new RuntimeLifecycleGeneration(1));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(5));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var runner = registration.Runner;
        var frame = TestWorldCollector.ActivationFrame + 1;

        ServiceRunnerTestWait.PublishDeferredRequest(pump, runner, ref frame);
        var cycle = runner.Snapshot.InFlightCycle;
        ServiceRunnerTestWait.PumpToResponse(pump, runner, ref frame);

        Assert.Equal(5, definition.LastEvaluatedStrategySetting);
        Assert.Equal(2UL, cycle.Strategy.Value);
    }

    [Fact]
    public void ConfigurationAndStrategyAdvanceIndependently()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new SyntheticServiceDefinition("test.independent"),
            new RuntimeLifecycleGeneration(1));

        var strategyTwo = registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        registry.ConfigurationPublication.Publish(TestSuiteConfiguration.WithSetting(2));
        var strategyThree = registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(3));

        Assert.Equal(2UL, strategyTwo.Value);
        Assert.Equal(3UL, strategyThree.Value);
        Assert.Equal(2UL, registry.Configuration.ReadLatest().Generation.Value);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public async Task ConcurrentConfigurationAndStrategyReadsRemainGenerationCoherent()
    {
        using var configuration = new ServiceConfigurationPublisher(TestSuiteConfiguration.WithSetting(1));
        using var strategy = new ServiceStrategyPublisher(TestSuiteStrategy.WithSetting(1));
        using var gate = new ManualResetEventSlim(false);

        var configWriter = Task.Run(() =>
        {
            gate.Wait();
            for (var value = 2; value <= 2000; value++)
            {
                configuration.Publish(TestSuiteConfiguration.WithSetting(value));
                if (value % 8 == 0) Thread.Yield();
            }
        });
        var configReader = Task.Run(() =>
        {
            gate.Wait();
            ulong previous = 0;
            for (var read = 0; read < 10000; read++)
            {
                var publication = configuration.ReadLatest();
                Assert.True(publication.Generation.Value >= previous);
                Assert.Equal(
                    (int)publication.Generation.Value,
                    TestSuiteConfiguration.SettingOf(publication.Snapshot));
                previous = publication.Generation.Value;
                if (read % 16 == 0) Thread.Yield();
            }
        });
        var strategyWriter = Task.Run(() =>
        {
            gate.Wait();
            for (var value = 2; value <= 2000; value++)
            {
                strategy.Publish(TestSuiteStrategy.WithSetting(value));
                if (value % 8 == 0) Thread.Yield();
            }
        });
        var strategyReader = Task.Run(() =>
        {
            gate.Wait();
            ulong previous = 0;
            for (var read = 0; read < 10000; read++)
            {
                var publication = strategy.ReadLatest();
                Assert.True(publication.Generation.Value >= previous);
                Assert.Equal(
                    (int)publication.Generation.Value,
                    TestSuiteStrategy.SettingOf(publication.Bulletin));
                previous = publication.Generation.Value;
                if (read % 16 == 0) Thread.Yield();
            }
        });

        gate.Set();
        await Task.WhenAll(configWriter, configReader, strategyWriter, strategyReader);
        Assert.Equal(2000UL, configuration.ReadLatest().Generation.Value);
        Assert.Equal(
            2000,
            TestSuiteConfiguration.SettingOf(configuration.ReadLatest().Snapshot));
        Assert.Equal(2000UL, strategy.ReadLatest().Generation.Value);
        Assert.Equal(2000, TestSuiteStrategy.SettingOf(strategy.ReadLatest().Bulletin));
    }
}
