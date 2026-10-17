using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class BepInExAutomataConfigurationReader
{
    internal static SuiteRuntimeConfiguration Read(BepInExAutomataConfiguration source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        return new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration
            {
                Enabled = source.Enabled.Value,
                Pace = source.Pace.Value,
            },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = source.AutoBuyMode.Value,
                StructureAffordability = source.AutoBuyAffordability.Value,
                UpgradeAffordability = source.UpgradeAffordability.Value,
                IncludeStructures = source.AutoBuyStructures.Value,
                IncludeUpgrades = source.AutoBuyUpgrades.Value,
                AutoLevelSpells = source.AutoLevelSpells.Value,
                LeaveQueueSlots = source.LeaveQueueSlots.Value,
            },
            AutoCast = new AutoCastConfiguration
            {
                Mode = source.AutoCastMode.Value,
                ToggleShortcut = source.AutoCastToggleShortcut.Value.ToString(),
                StartResourcePercent = source.AutoCastStartResourcePercent.Value,
                ManualPauseSeconds = source.AutoCastManualPauseSeconds.Value,
                FullCharge = source.AutoCastFullCharge.Value,
                RitualAware = source.AutoCastRitualAware.Value,
            },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = source.AutoConceptMode.Value,
                SlotManagement = source.AutoConceptSlotManagement.Value,
                TrainingPeriodSeconds = source.AutoConceptTrainingPeriodSeconds.Value,
                RateReservePercent = source.AutoConceptRateReservePercent.Value,
                MinimumDrainRatio = source.AutoConceptMinimumDrainRatio.Value,
            },
            AutoHarvest = new AutoHarvestConfiguration
            {
                Mode = source.AutoHarvestMode.Value,
                CollectFruitTrees = source.AutoHarvestFruitTrees.Value,
                CollectTreasureTrees = source.AutoHarvestTreasureTrees.Value,
            },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = source.AutoItemsMode.Value,
                UseScrolls = source.AutoItemsUseScrolls.Value,
                UseRelics = source.AutoItemsUseRelics.Value,
                TemporaryItemAllowlist =
                    source.AutoItemsTemporaryItemAllowlist.Value ?? string.Empty,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = source.AutoScribeMode.Value,
                Roles = source.AutoScribeRoles.Value ?? string.Empty,
            },
            Mentor = source.Mentor is null
                ? new OrbMentor.MentorConfiguration()
                : OrbMentor.MentorConfiguration.Read(source.Mentor),
            Safety = new SuiteSafetyConfiguration
            {
                EmergencyDisable = source.EmergencyDisable.Value,
            },
            Reserves = new AutomataReserveConfiguration
            {
                AbsoluteReserve = source.AbsoluteReserve.Value,
                RelativeReserveMultiplier = source.RelativeReserveMultiplier.Value,
            },
        };
    }
}
