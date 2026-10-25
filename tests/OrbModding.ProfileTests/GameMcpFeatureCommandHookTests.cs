using System;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpFeatureCommandHookTests
{
    [Fact]
    public void TwoRuntimesClaimingPurchaseRefuseDispatcherConstruction()
    {
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        var probe = new ServiceCycleProfileProbe();
        var actions = new LifecycleGameActions();
        using var configuration = new ServiceConfigurationPublisher(new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        });
        var feature = new AutoBuyServiceCycleFeature(new AutoBuyFeatureDependencies(
            () => 7, () => AutoBuyCandidateKinds.None, null,
            new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutoBuyFeatureContribution.FeatureId),
                    "Auto Buy", true, FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"), 7)),
            new AutoBuyRefusalBundleWriter(() => throw new InvalidOperationException("unused")),
            _ => { }, _ => { }));
        var context = new AutomataServiceCycleFeatureContext(
            registry, new ConfigGeneration(1), 7, actions, probe);
        var runtime = feature.Register(in context);
        using var host = new AutomataServiceCycleHost(registry, () => 1, null, null, probe);
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(() =>
                new GameMcpCommandDispatcher(host, configuration, actions,
                    new[] { runtime, runtime }));
            Assert.Contains("Purchase", failure.Message);
            Assert.Contains("more than one", failure.Message);
        }
        finally
        {
            runtime.DisposeRegistration();
        }
    }
}
