using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OrbModding.Common;

/// <summary>
/// One assembly, one plugin GUID, one BepInEx configuration file — and therefore exactly one schema
/// plan. <see cref="ConfigurationSchemaTransaction"/> stores a single
/// <c>[OrbModding] ConfigurationSchemaVersion</c> marker per file, so three plans binding into one
/// file would fight over one number: the first writes its version and every later plan reads a
/// version it does not recognise. Retired per-plugin files carry different names and are never read.
/// </summary>
internal static class SuiteConfigurationSchema
{
    internal const int CurrentVersion = 7;
    internal static readonly ConfigurationKey AutoCastShortcut =
        new("AutoCast", "ToggleShortcut");
    internal static readonly ConfigurationKey DifferentialVerificationShortcut =
        new("Diagnostics", "VerifyGameMathShortcut");
    internal static readonly ConfigurationKey MentorOperationsPerFrame =
        new("Performance", "OperationsPerFrame");
    internal static readonly ConfigurationKey CpuBudgetMilliseconds =
        new("Performance", "CpuBudgetMilliseconds");
    internal static readonly ConfigurationKey AutoBuyMaxCandidatesPerScan =
        new("AutoBuy", "MaxCandidatesPerScan");
    internal static readonly ConfigurationKey MaxLoggedRejections =
        new("Diagnostics", "MaxLoggedRejections");
    internal static readonly ConfigurationKey EnableOperationalLogging =
        new("Diagnostics", "EnableOperationalLogging");
    internal static readonly ConfigurationKey DecisionLogLevel =
        new("Diagnostics", "DecisionLogLevel");
    internal static readonly ConfigurationKey MentorDetailedLogging =
        new("Diagnostics", "DetailedLogging");
    internal static readonly ConfigurationKey DevelopmentEventProbe =
        new("Development", "EventProbe");
    internal static readonly ConfigurationKey AutoBuyEvaluationIntervalSeconds =
        new("AutoBuy", "EvaluationIntervalSeconds");
    internal static readonly ConfigurationKey AutoCastEvaluationIntervalSeconds =
        new("AutoCast", "EvaluationIntervalSeconds");
    internal static readonly ConfigurationKey AutoHarvestEvaluationIntervalSeconds =
        new("AutoHarvest", "EvaluationIntervalSeconds");
    internal static readonly ConfigurationKey AutoConceptFallbackEvaluationIntervalSeconds =
        new("AutoConcept", "FallbackEvaluationIntervalSeconds");
    internal static readonly ConfigurationKey AutoBuyAllowedUuids =
        new("AutoBuy", "AllowedUuids");
    internal static readonly ConfigurationKey AutoBuyBlockedUuids =
        new("AutoBuy", "BlockedUuids");
    internal static readonly ConfigurationKey AutoConceptAllowedUuids =
        new("AutoConcept", "AllowedUuids");
    internal static readonly ConfigurationKey AutoConceptBlockedUuids =
        new("AutoConcept", "BlockedUuids");
    internal static readonly ConfigurationKey AutoBuyPurchaseGrouping =
        new("AutoBuy", "PurchaseGrouping");
    internal static readonly ConfigurationKey AutoBuyFixedGroupSize =
        new("AutoBuy", "FixedGroupSize");
    internal static readonly ConfigurationKey AutoBuyBatchSizingMode =
        new("AutoBuy", "BatchSizingMode");
    internal static readonly ConfigurationKey AutoBuyMaxPurchasesPerBatch =
        new("AutoBuy", "MaxPurchasesPerBatch");
    internal static readonly ConfigurationKey AutoBuyPrioritizeCostAndQualityStructures =
        new("AutoBuy", "PrioritizeCostAndQualityStructures");
    internal static readonly ConfigurationKey AutoConceptPerConceptQuantityCap =
        new("AutoConcept", "PerConceptQuantityCap");
    internal static readonly ConfigurationKey AutoConceptTrainingPeriodSeconds =
        new("AutoConcept", "TrainingPeriodSeconds");
    internal static readonly ConfigurationKey AutoConceptMinimumResourcePercent =
        new("AutoConcept", "MinimumResourcePercent");

    internal static ConfigurationSchemaPlan Plan { get; } = new(CurrentVersion, new[]
    {
        new ConfigurationMigrationStep(
            0,
            1,
            Array.Empty<ConfigurationKey>(),
            static _ => { }),
        // Nothing in the file changes shape here. The version moves so that one launch — the launch
        // that reads a file written before the differential verification chord moved off Mentor's
        // toggle key — can tell that a persisted shortcut is an inherited default rather than a
        // choice, and rebind it. Values are left where they are: the shortcut is bound outside this
        // transaction, and a migration step may only write keys the transaction itself binds.
        new ConfigurationMigrationStep(
            1,
            2,
            Array.Empty<ConfigurationKey>(),
            static _ => { }),
        // Version 3 removes native input races. Values are rewritten only when they match a default
        // inherited from an older schema. A player-selected chord is preserved even though the
        // differential verifier no longer listens to any key.
        new ConfigurationMigrationStep(
            2,
            3,
            new[] { AutoCastShortcut },
            MigrateInheritedShortcuts),
        // Mentor was the last consumer of the legacy main-thread work admission and CPU-time
        // budget. ServiceCycle owns bounded planning and action dispatch, so retaining these values
        // would advertise controls that no runtime reads.
        new ConfigurationMigrationStep(
            3,
            4,
            new[] { MentorOperationsPerFrame, CpuBudgetMilliseconds },
            static context =>
            {
                context.DiscardObsolete(
                    MentorOperationsPerFrame,
                    "Mentor service-cycle dispatch no longer uses operations-per-frame admission.");
                context.DiscardObsolete(
                    CpuBudgetMilliseconds,
                    "ServiceCycle replaced the legacy shared CPU-time budget.");
            }),
        // Schema 5 removes preferences that only described the retired scan/budget/logging engines.
        // Maintained diagnostics are explicit Runtime actions and unconditional warnings/errors.
        new ConfigurationMigrationStep(
            4,
            5,
            new[]
            {
                AutoBuyMaxCandidatesPerScan,
                MaxLoggedRejections,
                EnableOperationalLogging,
                DecisionLogLevel,
                MentorDetailedLogging,
                DevelopmentEventProbe,
                DifferentialVerificationShortcut,
            },
            static context =>
            {
                context.DiscardObsolete(
                    AutoBuyMaxCandidatesPerScan,
                    "ServiceCycle evaluates the complete audited Auto Buy snapshot without a per-scan candidate cap.");
                context.DiscardObsolete(
                    MaxLoggedRejections,
                    "The retired rejection logger was the only consumer of this limit.");
                context.DiscardObsolete(
                    EnableOperationalLogging,
                    "Runtime bug reports and the decision journal replace legacy operational narration.");
                context.DiscardObsolete(
                    DecisionLogLevel,
                    "Runtime observation controls replace the legacy decision-log level.");
                context.DiscardObsolete(
                    MentorDetailedLogging,
                    "Mentor ServiceCycle never consumed the legacy detailed-logging preference.");
                context.DiscardObsolete(
                    DevelopmentEventProbe,
                    "ServiceCycle trace and journal evidence replace the unused mastery event probe.");
                context.DiscardObsolete(
                    DifferentialVerificationShortcut,
                    "Differential verification is an explicit Mods Runtime action.");
            }),
        // Schema 6 establishes one suite cadence. Service evaluation follows world/configuration
        // publications, so the cadence, filtering, grouping, batching, priority, and concept-cap
        // preferences below no longer describe runtime behavior and are deleted. Every serialized
        // 300-second training period (the former default) becomes 30 seconds, whether that value was
        // inherited or deliberately saved; every other serialized training period is preserved.
        new ConfigurationMigrationStep(
            5,
            6,
            new[]
            {
                AutoBuyEvaluationIntervalSeconds,
                AutoCastEvaluationIntervalSeconds,
                AutoHarvestEvaluationIntervalSeconds,
                AutoConceptFallbackEvaluationIntervalSeconds,
                AutoBuyAllowedUuids,
                AutoBuyBlockedUuids,
                AutoConceptAllowedUuids,
                AutoConceptBlockedUuids,
                AutoBuyPurchaseGrouping,
                AutoBuyFixedGroupSize,
                AutoBuyBatchSizingMode,
                AutoBuyMaxPurchasesPerBatch,
                AutoBuyPrioritizeCostAndQualityStructures,
                AutoConceptPerConceptQuantityCap,
                AutoConceptTrainingPeriodSeconds,
            },
            static context =>
            {
                context.DiscardObsolete(
                    AutoBuyEvaluationIntervalSeconds,
                    "Auto Buy evaluates after each world or configuration publication.");
                context.DiscardObsolete(
                    AutoCastEvaluationIntervalSeconds,
                    "Auto Cast evaluates after each world or configuration publication.");
                context.DiscardObsolete(
                    AutoHarvestEvaluationIntervalSeconds,
                    "Auto Harvest evaluates after each world or configuration publication.");
                context.DiscardObsolete(
                    AutoConceptFallbackEvaluationIntervalSeconds,
                    "Auto Concept evaluates after each world or configuration publication.");
                context.DiscardObsolete(
                    AutoBuyAllowedUuids,
                    "Auto Buy no longer filters candidates through a configured allow list.");
                context.DiscardObsolete(
                    AutoBuyBlockedUuids,
                    "Auto Buy no longer filters candidates through a configured block list.");
                context.DiscardObsolete(
                    AutoConceptAllowedUuids,
                    "Auto Concept no longer filters candidates through a configured allow list.");
                context.DiscardObsolete(
                    AutoConceptBlockedUuids,
                    "Auto Concept no longer filters candidates through a configured block list.");
                context.DiscardObsolete(
                    AutoBuyPurchaseGrouping,
                    "Auto Buy always groups structures by live Bulk Development and upgrades singly.");
                context.DiscardObsolete(
                    AutoBuyFixedGroupSize,
                    "The retired fixed purchase-grouping mode was this setting's only consumer.");
                context.DiscardObsolete(
                    AutoBuyBatchSizingMode,
                    "Auto Buy always plans candidates until the live queue reserve is reached.");
                context.DiscardObsolete(
                    AutoBuyMaxPurchasesPerBatch,
                    "The retired fixed batch-sizing mode was this setting's only consumer.");
                context.DiscardObsolete(
                    AutoBuyPrioritizeCostAndQualityStructures,
                    "Auto Buy now uses cost ratio and stable identity without a structure-effect priority tier.");
                context.DiscardObsolete(
                    AutoConceptPerConceptQuantityCap,
                    "Auto Concept always uses the recipe's native mastery maximum.");

                if (context.TryGet(AutoConceptTrainingPeriodSeconds, out var serialized) &&
                    int.TryParse(
                        serialized,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var seconds) &&
                    seconds == 300)
                {
                    context.Map(
                        AutoConceptTrainingPeriodSeconds,
                        AutoConceptTrainingPeriodSeconds,
                        "30",
                        "Rewrote the serialized Auto Concept training period from 300 seconds to 30 seconds.");
                }
                else if (context.TryGet(AutoConceptTrainingPeriodSeconds, out serialized))
                {
                    context.Preserve(AutoConceptTrainingPeriodSeconds, serialized);
                }
            }),
        // A late-game Psi pool sits far below a cap that grows faster than it fills, so a fill
        // floor held every concept back while income dwarfed the drain. The income reserve and the
        // pull-back on zero or negative income already keep a drained resource safe.
        new ConfigurationMigrationStep(
            6,
            CurrentVersion,
            new[] { AutoConceptMinimumResourcePercent },
            static context => context.DiscardObsolete(
                AutoConceptMinimumResourcePercent,
                "Auto Concept no longer requires a drained resource to fill a share of its cap.")),
    });

    private static void MigrateInheritedShortcuts(ConfigurationMigrationContext context)
    {
        if (context.TryGet(AutoCastShortcut, out var autoCast))
        {
            if (IsChord(autoCast, KeyCode.X, KeyCode.LeftAlt))
            {
                context.Map(
                    AutoCastShortcut,
                    AutoCastShortcut,
                    KeyCode.F8.ToString(),
                    "Rebound the inherited Auto Cast shortcut from native Inventory key X to F8.");
            }
            else
            {
                context.Preserve(AutoCastShortcut, autoCast);
            }
        }

    }

    internal static bool IsChord(
        string serialized,
        KeyCode mainKey,
        params KeyCode[] modifiers)
    {
        if (serialized is null) return false;
        var parts = serialized.Split('+');
        if (parts.Length != modifiers.Length + 1 ||
            !Enum.TryParse(parts[0].Trim(), ignoreCase: true, out KeyCode parsedMain) ||
            parsedMain != mainKey)
            return false;
        var remaining = new HashSet<KeyCode>(modifiers);
        for (var index = 1; index < parts.Length; index++)
        {
            if (!Enum.TryParse(parts[index].Trim(), ignoreCase: true, out KeyCode modifier) ||
                !remaining.Remove(modifier))
                return false;
        }
        return remaining.Count == 0;
    }
}
