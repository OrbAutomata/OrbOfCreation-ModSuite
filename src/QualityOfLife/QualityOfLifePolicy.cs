using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>The reflective Learning passives whose native descriptions explicitly offer notification muting.</summary>
internal static class QualityOfLifePolicy
{
    internal static IReadOnlyList<KnownEntity<PassiveAbilitySOContract>> SupportedPassives { get; } =
        Array.AsReadOnly(new[]
        {
            KnownEntities.PassiveReflectiveBloomingLearning,
            KnownEntities.PassiveReflectiveBuildingLearning,
            KnownEntities.PassiveReflectiveChargedLearning,
            KnownEntities.PassiveReflectiveEnergizedLearning,
            KnownEntities.PassiveReflectiveEssenceLearning,
            KnownEntities.PassiveReflectiveMagicLearning,
            KnownEntities.PassiveReflectiveMentalLearning,
            KnownEntities.PassiveReflectiveNaturalLearning,
            KnownEntities.PassiveReflectiveRestorativeLearning,
            KnownEntities.PassiveReflectiveSpacialLearning,
        });

    internal static bool IsSupported(Guid id)
    {
        foreach (var passive in SupportedPassives)
            if (passive.Uuid == id) return true;
        return false;
    }

    internal static bool IsOperational(SuiteRuntimeConfiguration config) =>
        config.General.Enabled && !config.Safety.EmergencyDisable &&
        config.QualityOfLife.Mode == QualityOfLifeOperationMode.Active &&
        config.QualityOfLife.MutePassiveNotifications;

    internal static bool ShouldMute(in WorldPassiveAbility passive) =>
        passive.Touched && !passive.Muted && !passive.Hidden && !passive.Silent;
}
