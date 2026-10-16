#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// What the game itself calls each modifier record, so a type page says the word the tooltip says.
/// </summary>
/// <remarks>
/// <para>
/// Every modifier-bearing type class declares a <c>ModifierPropertyRef</c> per record in its static
/// constructor, and each one carries the display string the game prints beside that record's number.
/// Those strings are the whole of this table: read off the pinned assembly, joined to the record
/// field through the class's own <c>GetUpgradeModAccessorInternal</c> /
/// <c>GetValueModifierRecord</c> switch, and copied here verbatim. A record the game declares no
/// ref for keeps its internal name — an invented translation would be a word the player has never
/// seen, which is the one thing this may not produce.
/// </para>
/// <para>
/// The key is the taxonomy <em>and</em> the property, because the same field name is a different
/// word on two classes: <c>experienceRateMod</c> is "Xp Rate" on an agromancy element type and has
/// no authored word at all on an artifact type. A single property-name map would have picked one of
/// them and been wrong on the other.
/// </para>
/// <para>
/// Closed and total over the <em>live</em> records <see cref="WorldTypeModifierBindings"/> binds,
/// and a pair with no disposition throws rather than falling through to its internal name. Silence
/// has to be deliberate: a record that quietly kept its camelCase name is indistinguishable from one
/// the census missed, and only one of those is honest.
/// </para>
/// <para>
/// A record the game cannot read has no disposition at all, and asking for one throws rather than
/// answering. It is not that no word was chosen — it is that the record reaches no page to carry a
/// word on (see <see cref="WorldTypeModifierLiveness"/>), so a word here would be a name for a
/// control nothing is wired to. Four records are in that position on the pinned build.
/// </para>
/// <para>
/// One pair is worded rather than copied, and it is the only one. <c>RitualTypeSO</c>'s static
/// constructor authors the display string <c>"Ritual Speed"</c> twice — once for the ref named
/// <c>Speed</c> (backing <c>speed</c>, tooltip id <c>RitualSpeed</c>) and once for the ref named
/// <c>CompletionRate</c> (backing <c>completionRateMod</c>). Copying the game exactly makes a ritual
/// type page print one word over two different numbers, which tells a reader less than the internal
/// names would. So <c>speed</c> keeps the word — it is the ref the game itself named <c>Speed</c> —
/// and <c>completionRateMod</c> is worded from the game's own ref name instead. The screen still
/// says "Ritual Speed" for both; a reader comparing the page against it should expect that.
/// </para>
/// </remarks>
internal static class GameMcpModifierPropertyWords
{
    /// <summary>
    /// The game's word for one record, or the record's own name where the game authors none.
    /// </summary>
    internal static string Word(WorldTypeModifierOwnerKind kind, string property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (!WorldTypeModifierLiveness.IsLive(kind, property)) throw Dead(kind, property);
        return kind switch
        {
            WorldTypeModifierOwnerKind.SpellType => property switch
            {
                "augmentResonance" => "Augment Resonance",
                "bonusCritRate" => "Crit Rating",
                "bonusDoubleCastRate" => "Echo Cast Rating",
                "chargeEffectMod" => "Charge Effect",
                "chargeSpecialMod" => "Charge Special",
                "chargeTimeMod" => "Charge Time",
                "cooldownSpeed" => "Cooldown Speed",
                "cooldownTime" => "Cooldown Time",
                "costMod" => "Spell Cost",
                "critDurationMod" => "Crit Duration",
                "critEffectMod" => "Crit Effect",
                "doubleCastEffectMod" => "Echo Cast Effect",
                "drainCostMod" => "Spell Drain",
                "durationMod" => "Spell Duration",
                "elementalResonance" => "Elemental Resonance",
                "maxStacksMod" => "Max Stacks",
                "power" => "Spell Power",
                "scalingMod" => "Effect Scaling",
                "typeXpMod" => "Type Xp",
                "usageCostReduction" => "Spell Weight Reduction",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.ResourceType => property switch
            {
                "attributeCostMod" => "Attribute Cost",
                "decayRatio" => "Resource Decay Ratio",
                "decayTimeMod" => "Resource Decay Time",
                "drainMod" => "Resource Drain",
                "gainRateMod" => "Resources Gained",
                "lossPercentMod" => "Resource Loss",
                "maxQuantityMod" => "Resource Cap",
                "maxQuantityRateMod" => "Resource Capacity Rate",
                "qualityMod" => "Resource Quality",
                "rateMod" => "Resource Rate",
                "rawMaxQuantity" => "Raw Resource Cap",
                "replenishRatio" => "Resource Replenish Ratio",
                "replenishTimeMod" => "Resource Replenish Time",
                "reservationMod" => "Reservation",
                "restMod" => "Resting Rate",
                "reverberateMod" => "Resource Reverb",
                "reverberateTimeMod" => "Resource Reverb Time",
                "splashRate" => "Splash / second",
                "splashRateInterest" => "Interest / min",
                "splashRateLifetime" => "Lifetime / min",
                "splashRateMaxPercent" => "/ min",
                "splashRateMissing" => "Missing / min",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.AlchemyType => property switch
            {
                "drainCostMod" => "Drain",
                "effectLevels" => "Effect Levels",
                "experienceRate" => "Xp",
                "freeUsageSlots" => "Free Usage Slots",
                "overdriveDrainCostMod" => "Overdrive Drain",
                "overdrivePower" => "Overdrive Power",
                "overdriveSpeed" => "Overdrive Speed",
                "overdriveXpRate" => "Overdrive Xp",
                "power" => "Power",
                "special" => "Special",
                "speed" => "Speed",
                "timeReqMod" => "Time Required",
                "timeScalingMod" => "Time Scaling",

                // The game authors no word for this one. The internal name stands.
                "level" => property,
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.CraftingRecipeType => property switch
            {
                "autoPenaltyMod" => "Auto Penalty",
                "costIncrementMod" => "Cost Increment",
                "costMod" => "Cost",
                "efficiencyMod" => "Efficiency",
                "magnitudeIncrement" => "Magnitude",
                "multiPenaltyMod" => "Multi Penalty",
                "power" => "Power",
                "speed" => "Speed",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.EquipmentType => property switch
            {
                "maxTypeSlots" => "Type Slots",
                "powerMod" => "Artifact Power",

                // The game authors no word for this one. The internal name stands.
                "experienceRateMod" => property,
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.RitualType => property switch
            {
                "chainLengthBonus" => "Ritual Chain Length",
                "chainPower" => "Ritual Chain Power",
                "completionCostMod" => "Ritual Cost",

                // The game prints "Ritual Speed" here too; see the class remark.
                "completionRateMod" => "Ritual Completion Rate",
                "critDurationMod" => "Ritual Crit Duration",
                "critPower" => "Ritual Crit Power",
                "critRating" => "Ritual Crit Rating",
                "durationMod" => "Ritual Duration",
                "echoPower" => "Ritual Echo Power",
                "echoRating" => "Ritual Echo Rating",
                "power" => "Ritual Power",
                "special" => "Ritual Special",
                "speed" => "Ritual Speed",

                // The game authors no word for this one. The internal name stands.
                "activeRituals" => property,
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.StructureType => property switch
            {
                "activeCostMod" => "Active Cost",
                "attributeRankEffectMod" => "Rank Effects",
                "bonusLevels" => "Bonus Levels",
                "buildSpeedMod" => "Develop Speed",
                "costScalingMod" => "Cost Scaling",
                "drainCostMod" => "Drain",
                "echoBuildRating" => "Echo Build Rating",
                "effectLevels" => "Effect Levels",
                "passiveCostMod" => "Cost",
                "powerBuildRating" => "Power Build Rating",
                "structurePower" => "Power",
                "structurePowerScaling" => "Power Scaling",
                "structureSpeed" => "Speed",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.HarvestType => property switch
            {
                "autoGenerationMod" => "Generation",
                "drainCostMod" => "Drain Cost",
                "experienceRateMod" => "Xp Rate",
                "growthSpeedMod" => "Growth",
                "harvestSpeedMod" => "Harvest Speed",
                "maxQuantity" => "Capacity",
                "maxRestGrowth" => "Max % Growth /s",
                "power" => "Yield",
                "qualityMod" => "Quality",
                "restingGrowthSpeedMod" => "Rest",

                // The game authors no word for this one. The internal name stands.
                "level" => property,
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.PlotNodeType => property switch
            {
                "actionCostMod" => "Cost",
                "actionSpeed" => "Speed",
                "actionXpRate" => "Xp Rate",
                "growingSpeed" => "Growth Speed",
                "qualityMod" => "Quality",
                "recoverySizeMod" => "Recovery Size",
                "restingSpeed" => "Recovery Speed",
                "sizeMod" => "Size",
                "specialMod" => "Special",
                "yieldMod" => "Yield",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.ResearchType => property switch
            {
                "freeBonusLevels" => "Free Level",
                "levelRequirementAdjust" => "Requirements",
                "maxInvestmentLevel" => "Max Investment",
                "maxLevelCap" => "Max Level",
                "power" => "Effect",

                // The game authors no word for this one. The internal name stands.
                "usedBonusLevels" => property,
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.ConsumableType => property switch
            {
                "bonusLevels" => "Bonus Levels",
                "durationMod" => "Duration",
                "power" => "Power",
                "prepSpeed" => "Prep Speed",
                "special" => "Special",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.HarvestActionType => property switch
            {
                "costMod" => "Agromancy Cost",
                "growthSizeMod" => "Growth Size",
                "power" => "Agromancy Power",
                "refundRating" => "Refund Rating",
                "speed" => "Agromancy Speed",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.PassiveAbilityType => property switch
            {
                "cooldown" => "Cooldown",
                "costMod" => "Cost",
                "durationMod" => "Duration",
                "maxStacksMod" => "Max Stacks",
                "power" => "Power",
                _ => throw Unworded(kind, property),
            },
            WorldTypeModifierOwnerKind.TimeRuneType => property switch
            {
                "freeUsages" => "Free Usage",
                "masteryXpMod" => "Mastery Xp",
                "power" => "Power",
                "powerScalingMod" => "Power Scaling",

                // The game authors no word for this one. The internal name stands.
                "totalLevel" => property,
                _ => throw Unworded(kind, property),
            },
            _ => throw Unworded(kind, property),
        };
    }

    private static InvalidOperationException Dead(
        WorldTypeModifierOwnerKind kind,
        string property)
    {
        return new InvalidOperationException(
            "the type record " + kind + "." + property + " is dead on this build and has no word " +
            "because it reaches no page: nothing in the game reads it into a computation, so " +
            "nothing a player buys can move it. A word for it would be a label on a control that " +
            "is not wired to anything.");
    }

    private static InvalidOperationException Unworded(
        WorldTypeModifierOwnerKind kind,
        string property)
    {
        return new InvalidOperationException(
            "the type record " + kind + "." + property + " reached the wire with no disposition; " +
            "every record a type page can print is either given the game's own word or " +
            "deliberately left under its internal name, and a new one is a census to run rather " +
            "than a name to pass through.");
    }
}
#endif
