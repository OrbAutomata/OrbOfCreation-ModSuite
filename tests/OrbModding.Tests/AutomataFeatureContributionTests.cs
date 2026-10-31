using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataFeatureContributionTests
{
    [Fact]
    public void ContributedMetadataAndControlsReachTheRegisteredStatusAndConfigurationPage()
    {
        var file = new ConfigFile();
        var config = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        config.AttachMentor(mentor);
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        var store = new AutomataConfigurationStore(config, statuses.ObserveConfiguration);
        var controls = AutomationFeatureControlRegistry.Create(store, statuses, new SpellLevelCapabilityState(), mentor);

        Assert.Equal(AutomataFeatureCatalog.All.Count, AutomataFeatureCatalog.All.Select(feature => feature.Id).Distinct().Count());
        Assert.Equal(AutomataFeatureCatalog.All.Count(feature => feature.PageLabel is not null), controls.Features.Count);
        foreach (var feature in AutomataFeatureCatalog.All)
        {
            var reporter = statuses.Get(feature.Id);
            Assert.Equal(feature.Id, reporter.Key.FeatureId);
            Assert.Equal(feature.DisplayName, reporter.DisplayName);
            if (feature.ServiceId is not null)
                Assert.Equal(feature.TraceName, AutomataServiceCycleTraceRoster.DisplayName(new ServiceId(feature.ServiceId)));
            if (feature.PageLabel is null) continue;
            Assert.True(controls.TryGet(feature.PageLabel, out var control));
            Assert.Equal(feature.Id, control.FeatureId);
            Assert.Equal(feature.DisplayName, control.DisplayName);
            Assert.Equal(reporter.Current, control.Status);
            var wasOn = reporter.Current.ConfiguredEnabled;
            control.Toggle();
            Assert.Equal(!wasOn, reporter.Current.ConfiguredEnabled);
            Assert.Equal(reporter.Current, control.Status);
            control.Toggle();
            Assert.Equal(wasOn, reporter.Current.ConfiguredEnabled);

            if (feature.ConfigurationSection is null) continue;
            var mode = file.Single(entry => entry.Key.Section == feature.ConfigurationSection && entry.Key.Key == "Mode").Value;
            var metadata = Assert.Single(mode.Description.Tags.OfType<ModConfigMetadata>());
            Assert.Equal(feature.PageLabel, metadata.DisplaySection);
            Assert.Equal(feature.DisplayName, metadata.DisplayName);
            Assert.Equal(feature.PageOrder, metadata.SectionOrder);
        }
    }

    [Fact]
    public void RootCompositionConstructsEveryContributedServiceBetweenItsInfrastructureOwners()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        using var ownership = new AutomataActionFamilyOwnership(new ActionFamilyOwnershipRegistry());
        var context = new AutomataFeatureComposition
        {
            ReadLifecycleEpoch = () => 1,
            RegistryResolver = TypedRegistryResolver.Shared,
            Ownership = ownership,
            Configuration = new AutomataConfigurationStore(config, statuses.ObserveConfiguration),
            Statuses = statuses,
            SpellLevelCapability = new SpellLevelCapabilityState(),
            ManualCastPause = new AutoCastManualPauseState(),
            AuditedBaselineId = GameAssemblyAudit.MacV1052BaselineId,
            RuntimeDiagnostics = new RuntimeDiagnosticsRegistry(),
            RefusalBundles = new AutoBuyRefusalBundleWriter(() => throw new InvalidOperationException("No action runs during construction.")),
            ReportInvariant = _ => { },
            Warn = _ => { },
            Info = _ => { },
        };
        var world = new AutomataWorldCollectionFeature(() => 1, () => 1, new AutomataWorldDemand());
        var strategist = new StrategistServiceCycleFeature();

        var services = AutomataFeatureCatalog.CreateServices(context, world, strategist);
        var serviceIds = AutomataFeatureCatalog.All.Where(feature => feature.ServiceId is not null)
            .Select(feature => feature.ServiceId).ToArray();

        Assert.Same(world, services.First());
        Assert.Same(strategist, services.Last());
        Assert.Equal(serviceIds.Length + 2, services.Length);
        using var registry = new ServiceCycleRegistry(serviceIds.Length,
            config.Current, new ConfigGeneration(1), new LifecycleGeneration(1));
        using var actions = new LifecycleGameActions();
        var featureContext = new AutomataServiceCycleFeatureContext(registry,
            new ConfigGeneration(1), 1, actions);
        var runtimes = new List<IAutomataServiceCycleFeatureRuntime>();
        try
        {
            foreach (var service in services.Skip(1).Take(services.Length - 2))
                runtimes.Add(service.Register(in featureContext));

            Assert.Equal(serviceIds,
                Enumerable.Range(0, registry.OrdinalCount).Select(ordinal => registry.GetServiceId(ordinal).Value));
        }
        finally
        {
            for (var index = runtimes.Count - 1; index >= 0; index--)
                runtimes[index].DisposeRegistration();
        }
    }
}
