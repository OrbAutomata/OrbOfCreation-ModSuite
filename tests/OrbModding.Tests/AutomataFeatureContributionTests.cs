using System;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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

        Assert.Same(world, services.First());
        Assert.Same(strategist, services.Last());
        Assert.Collection(services.Skip(1).Take(services.Length - 2),
            service => Assert.IsType<AutoItemsServiceCycleFeature>(service),
            service => Assert.IsType<AutoScribeServiceCycleFeature>(service),
            service => Assert.IsType<AutoHarvestServiceCycleFeature>(service),
            service => Assert.IsType<AutoBuyServiceCycleFeature>(service),
            service => Assert.IsType<SpellLevelServiceCycleFeature>(service),
            service => Assert.IsType<AutoCastServiceCycleFeature>(service),
            service => Assert.IsType<AutoConceptServiceCycleFeature>(service),
            service => Assert.IsType<MentorServiceCycleFeature>(service));
    }
}
