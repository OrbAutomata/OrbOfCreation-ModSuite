#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Projects values from the immutable configuration publication through the finite writable MCP
/// schema. Schema metadata is bound once from BepInEx; request-time values never read mutable
/// ConfigEntry objects and never enumerate or reflect over configuration properties.
/// </summary>
/// <remarks>
/// This is the one place a published value becomes text, so it is the one place a spelling is
/// decided — the read, the narrowed read, <c>mode=describe</c> and the committed write's
/// <c>{before, after}</c> pair all say what it says. A boolean is written the way every other
/// boolean on this wire reads — <c>yes</c> and <c>no</c> — rather than the way BepInEx writes it
/// into its own TOML file: the caller reading this surface reads the wire's vocabulary, and the
/// file's <c>true</c>/<c>false</c> is a fact about the file, restored on the way back in by
/// <see cref="GameMcpConfigurationValuePolicy.NativeSerializedValue"/>.
/// </remarks>
internal static class GameMcpConfigurationSchema
{
    internal static string SerializePublishedValue(
        SuiteRuntimeConfiguration configuration,
        string section,
        string key)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        object value = (section, key) switch
        {
            ("General", "Enabled") => configuration.General.Enabled,
            ("General", "Mode") => configuration.Mentor.Mode,
            ("AutoBuy", "Mode") => configuration.AutoBuy.Mode,
            ("AutoBuy", "AffordabilityMode") => configuration.AutoBuy.StructureAffordability,
            ("AutoBuy", "UpgradeAffordabilityMode") => configuration.AutoBuy.UpgradeAffordability,
            ("AutoBuy", "IncludeStructures") => configuration.AutoBuy.IncludeStructures,
            ("AutoBuy", "IncludeUpgrades") => configuration.AutoBuy.IncludeUpgrades,
            ("AutoBuy", "AutoLevelSpells") => configuration.AutoBuy.AutoLevelSpells,
            ("AutoBuy", "LeaveQueueSlots") => configuration.AutoBuy.LeaveQueueSlots,
            ("AutoCast", "Mode") => configuration.AutoCast.Mode,
            ("AutoCast", "StartResourcePercent") => configuration.AutoCast.StartResourcePercent,
            ("AutoCast", "ManualPauseSeconds") => configuration.AutoCast.ManualPauseSeconds,
            ("AutoCast", "FullCharge") => configuration.AutoCast.FullCharge,
            ("AutoConcept", "Mode") => configuration.AutoConcept.Mode,
            ("AutoConcept", "SlotManagementMode") => configuration.AutoConcept.SlotManagement,
            ("AutoConcept", "TrainingPeriodSeconds") => configuration.AutoConcept.TrainingPeriodSeconds,
            ("AutoConcept", "RateReservePercent") => configuration.AutoConcept.RateReservePercent,
            ("AutoConcept", "MinimumResourcePercent") => configuration.AutoConcept.MinimumResourcePercent,
            ("AutoConcept", "MinimumDrainRatio") => configuration.AutoConcept.MinimumDrainRatio,
            ("AutoHarvest", "Mode") => configuration.AutoHarvest.Mode,
            ("AutoHarvest", "CollectFruitTrees") => configuration.AutoHarvest.CollectFruitTrees,
            ("AutoHarvest", "CollectTreasureTrees") => configuration.AutoHarvest.CollectTreasureTrees,
            ("AutoItems", "Mode") => configuration.AutoItems.Mode,
            ("AutoItems", "UseScrolls") => configuration.AutoItems.UseScrolls,
            ("AutoItems", "UseRelics") => configuration.AutoItems.UseRelics,
            ("AutoItems", "TemporaryItemAllowlist") => configuration.AutoItems.TemporaryItemAllowlist,
            ("AutoScribe", "Mode") => configuration.AutoScribe.Mode,
            ("AutoScribe", "Roles") => configuration.AutoScribe.Roles,
            ("Reserves", "AbsoluteReserve") => configuration.Reserves.AbsoluteReserve,
            ("Reserves", "RelativeReserveMultiplier") => configuration.Reserves.RelativeReserveMultiplier,
            _ => throw new InvalidOperationException(
                "the static MCP writable schema has no published value mapping for " +
                section + "/" + key),
        };
        return value switch
        {
            bool boolean => boolean ? "yes" : "no",
            float single => single.ToString("R", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
#endif
