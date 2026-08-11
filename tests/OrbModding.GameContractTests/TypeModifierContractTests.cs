using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The type-level modifier surface: which records each taxonomy owns, the two type-to-type edges that
/// decide how far a type-level bonus reaches, and the one class that turns out to have no modifier
/// surface at all.
/// </summary>
/// <remarks>
/// <para>
/// The capture publishes one row per record per type asset, and the row set is the class's whole
/// record list. That makes the list itself the contract: a record added by a game update would go
/// unpublished in silence, and a record removed would show up only as a category that quietly
/// withheld itself. So the expectation here is the exact set, name and record class both, rather
/// than a floor.
/// </para>
/// <para>
/// The record class matters as much as the name. <c>ValueModifierRecord</c> holds a value of its own
/// and is already folded wherever it is published; <c>MergingModifierRecord</c> and
/// <c>OrderedMultiplierRecord</c> hold none and only distribute, so they are the ones whose total has
/// to be derived from the captured entries. A record silently changing class would move a property
/// between those two worlds without any other test noticing.
/// </para>
/// </remarks>
public sealed class TypeModifierContractTests
{
    private static readonly HashSet<string> RecordClasses = new(StringComparer.Ordinal)
    {
        "ValueModifierRecord",
        "MergingModifierRecord",
        "OrderedMultiplierRecord",
        "ModifierRecord",
    };

    private static readonly Dictionary<string, string[]> Records = new(StringComparer.Ordinal)
    {
        ["SpellTypeSO"] = new[]
        {
            "augmentResonance:ValueModifierRecord", "bonusCritRate:ValueModifierRecord",
            "bonusDoubleCastRate:ValueModifierRecord", "bonusFlashRate:ValueModifierRecord",
            "chargeEffectMod:ValueModifierRecord", "chargeSpecialMod:ValueModifierRecord",
            "chargeTimeMod:ValueModifierRecord", "cooldownSpeed:ValueModifierRecord",
            "cooldownTime:ValueModifierRecord", "costMod:ValueModifierRecord",
            "critDurationMod:ValueModifierRecord", "critEffectMod:ValueModifierRecord",
            "doubleCastEffectMod:ValueModifierRecord", "drainCostMod:ValueModifierRecord",
            "durationMod:ValueModifierRecord", "elementalResonance:ValueModifierRecord",
            "flashEffectMod:ValueModifierRecord", "maxStacksMod:ValueModifierRecord",
            "power:ValueModifierRecord", "scalingMod:ValueModifierRecord",
            "typeXpMod:ValueModifierRecord", "usageCostReduction:ValueModifierRecord",
        },
        ["ResourceTypeSO"] = new[]
        {
            "attributeCostMod:OrderedMultiplierRecord", "decayRatio:MergingModifierRecord",
            "decayTimeMod:OrderedMultiplierRecord", "drainMod:OrderedMultiplierRecord",
            "gainRateMod:OrderedMultiplierRecord", "lossPercentMod:OrderedMultiplierRecord",
            "maxQuantityMod:OrderedMultiplierRecord", "maxQuantityRateMod:OrderedMultiplierRecord",
            "qualityMod:OrderedMultiplierRecord", "rateMod:OrderedMultiplierRecord",
            "rawMaxQuantity:MergingModifierRecord", "replenishRatio:MergingModifierRecord",
            "replenishTimeMod:OrderedMultiplierRecord", "reservationMod:OrderedMultiplierRecord",
            "restMod:OrderedMultiplierRecord", "reverberateMod:OrderedMultiplierRecord",
            "reverberateTimeMod:OrderedMultiplierRecord", "splashRate:MergingModifierRecord",
            "splashRateInterest:MergingModifierRecord", "splashRateLifetime:MergingModifierRecord",
            "splashRateMaxPercent:MergingModifierRecord", "splashRateMissing:MergingModifierRecord",
        },
        ["AlchemyTypeSO"] = new[]
        {
            "drainCostMod:OrderedMultiplierRecord", "effectLevels:MergingModifierRecord",
            "experienceRate:OrderedMultiplierRecord", "freeUsageSlots:MergingModifierRecord",
            "level:ValueModifierRecord", "overdriveDrainCostMod:OrderedMultiplierRecord",
            "overdrivePower:OrderedMultiplierRecord", "overdriveSpeed:OrderedMultiplierRecord",
            "overdriveXpRate:OrderedMultiplierRecord", "power:OrderedMultiplierRecord",
            "special:MergingModifierRecord", "speed:OrderedMultiplierRecord",
            "timeReqMod:OrderedMultiplierRecord", "timeScalingMod:OrderedMultiplierRecord",
        },
        ["CraftingRecipeTypeSO"] = new[]
        {
            "autoPenaltyMod:OrderedMultiplierRecord", "costIncrementMod:OrderedMultiplierRecord",
            "costMod:OrderedMultiplierRecord", "efficiencyMod:OrderedMultiplierRecord",
            "magnitudeIncrement:ValueModifierRecord", "multiPenaltyMod:OrderedMultiplierRecord",
            "power:OrderedMultiplierRecord", "speed:OrderedMultiplierRecord",
        },
        ["EquipmentTypeSO"] = new[]
        {
            "experienceRateMod:OrderedMultiplierRecord", "masteryLevel:ValueModifierRecord",
            "maxTypeSlots:ValueModifierRecord", "powerMod:MergingModifierRecord",
        },
        ["RitualTypeSO"] = new[]
        {
            "activeRituals:ValueModifierRecord", "chainLengthBonus:MergingModifierRecord",
            "chainPower:OrderedMultiplierRecord", "completionCostMod:OrderedMultiplierRecord",
            "completionRateMod:OrderedMultiplierRecord", "critDurationMod:OrderedMultiplierRecord",
            "critPower:OrderedMultiplierRecord", "critRating:MergingModifierRecord",
            "durationMod:OrderedMultiplierRecord", "echoPower:OrderedMultiplierRecord",
            "echoRating:MergingModifierRecord", "power:OrderedMultiplierRecord",
            "special:OrderedMultiplierRecord", "speed:OrderedMultiplierRecord",
        },
        ["StructureTypeSO"] = new[]
        {
            "activeCostMod:OrderedMultiplierRecord",
            "attributeRankEffectMod:OrderedMultiplierRecord", "bonusLevels:MergingModifierRecord",
            "buildSpeedMod:OrderedMultiplierRecord", "costScalingMod:OrderedMultiplierRecord",
            "drainCostMod:OrderedMultiplierRecord", "echoBuildRating:MergingModifierRecord",
            "effectLevels:MergingModifierRecord", "passiveCostMod:OrderedMultiplierRecord",
            "powerBuildRating:MergingModifierRecord", "structurePower:OrderedMultiplierRecord",
            "structurePowerScaling:MergingModifierRecord", "structureSpeed:OrderedMultiplierRecord",
        },
        ["HarvestTypeSO"] = new[]
        {
            "autoGenerationMod:OrderedMultiplierRecord", "drainCostMod:OrderedMultiplierRecord",
            "experienceRateMod:OrderedMultiplierRecord", "growthSpeedMod:OrderedMultiplierRecord",
            "harvestSpeedMod:OrderedMultiplierRecord", "level:ValueModifierRecord",
            "maxQuantity:OrderedMultiplierRecord", "maxRestGrowth:MergingModifierRecord",
            "power:OrderedMultiplierRecord", "qualityMod:OrderedMultiplierRecord",
            "restingGrowthSpeedMod:OrderedMultiplierRecord",
        },
        ["PlotNodeTypeSO"] = new[]
        {
            "actionCostMod:OrderedMultiplierRecord", "actionSpeed:OrderedMultiplierRecord",
            "actionXpRate:OrderedMultiplierRecord", "growingSpeed:OrderedMultiplierRecord",
            "qualityMod:OrderedMultiplierRecord", "recoverySizeMod:OrderedMultiplierRecord",
            "restingSpeed:OrderedMultiplierRecord", "sizeMod:OrderedMultiplierRecord",
            "specialMod:OrderedMultiplierRecord", "totalLevel:ValueModifierRecord",
            "yieldMod:OrderedMultiplierRecord",
        },
        ["ResearchTypeSO"] = new[]
        {
            "freeBonusLevels:ValueModifierRecord", "levelRequirementAdjust:ModifierRecord",
            "maxInvestmentLevel:ValueModifierRecord", "maxLevelCap:MergingModifierRecord",
            "power:OrderedMultiplierRecord", "usedBonusLevels:ValueModifierRecord",
        },
        ["ConsumableTypeSO"] = new[]
        {
            "bonusLevels:MergingModifierRecord", "durationMod:OrderedMultiplierRecord",
            "power:OrderedMultiplierRecord", "prepSpeed:OrderedMultiplierRecord",
            "special:OrderedMultiplierRecord",
        },
        ["HarvestActionTypeSO"] = new[]
        {
            "costMod:OrderedMultiplierRecord", "growthSizeMod:OrderedMultiplierRecord",
            "power:OrderedMultiplierRecord", "refundRating:MergingModifierRecord",
            "speed:OrderedMultiplierRecord",
        },
        ["PassiveAbilityTypeSO"] = new[]
        {
            "cooldown:OrderedMultiplierRecord", "costMod:OrderedMultiplierRecord",
            "durationMod:OrderedMultiplierRecord", "maxStacksMod:MergingModifierRecord",
            "power:OrderedMultiplierRecord",
        },
        ["TimeRuneTypeSO"] = new[]
        {
            "freeUsages:MergingModifierRecord", "masteryXpMod:OrderedMultiplierRecord",
            "power:OrderedMultiplierRecord", "powerScalingMod:OrderedMultiplierRecord",
            "totalLevel:ValueModifierRecord",
        },
    };

    [InlineData("SpellTypeSO")]
    [InlineData("ResourceTypeSO")]
    [InlineData("AlchemyTypeSO")]
    [InlineData("CraftingRecipeTypeSO")]
    [InlineData("EquipmentTypeSO")]
    [InlineData("RitualTypeSO")]
    [InlineData("StructureTypeSO")]
    [InlineData("HarvestTypeSO")]
    [InlineData("PlotNodeTypeSO")]
    [InlineData("ResearchTypeSO")]
    [InlineData("ConsumableTypeSO")]
    [InlineData("HarvestActionTypeSO")]
    [InlineData("PassiveAbilityTypeSO")]
    [InlineData("TimeRuneTypeSO")]
    [GameAssemblyTheory]
    public void EveryTaxonomyDeclaresExactlyTheModifierRecordsTheCaptureWalks(string taxonomy)
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType(taxonomy), $"{taxonomy} is no longer declared.");

        var declared = assembly.GetFields(taxonomy)
            .Where(field => !field.IsStatic && RecordClasses.Contains(field.FieldType))
            .Select(field => field.Name + ":" + field.FieldType)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            Records[taxonomy].OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
            declared);
    }

    /// <summary>
    /// The seven challenge types have no modifier surface, and the base chain is why.
    /// </summary>
    /// <remarks>
    /// A wordless type asset looks like a hidden bonus carrier, and this is the fact that retires the
    /// suspicion for good: <c>ChallengeTypeSO</c> derives straight from <c>IdScriptableObject</c>
    /// rather than from <c>UpgradeableObject</c>, so there is no modifier machinery on it to hide
    /// anything in. What it does carry is the draft's weighting, which is published instead.
    /// </remarks>
    [GameAssemblyFact]
    public void ChallengeTypesCarryTheDraftsWeightingAndNoModifierSurface()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("IdScriptableObject", assembly.GetBaseType("ChallengeTypeSO"));

        var fields = assembly.GetFields("ChallengeTypeSO")
            .Where(field => !field.IsStatic)
            .Select(field => field.Name + ":" + field.FieldType)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "displayEffect:DisplayEffectSO",
                "excludeFromRandomSelection:System.Boolean",
                "restrictedInstances:System.Boolean",
                "weight:System.Double",
            },
            fields);
        Assert.DoesNotContain(
            assembly.GetFields("ChallengeTypeSO"),
            field => RecordClasses.Contains(field.FieldType));
    }

    /// <summary>
    /// The structure subtype chain is a type-to-type edge, and it is what carries a parent's records
    /// down to a child's members.
    /// </summary>
    /// <remarks>
    /// Without this edge, "a type-wide bonus reaches exactly that type's own members" would be a
    /// reasonable and wrong assumption for structures. <c>Initialize()</c> calls
    /// <c>RegisterSubType</c> per entry, and that method is what wires the parent's thirteen records
    /// into the child's thirteen.
    /// </remarks>
    [GameAssemblyFact]
    public void TheStructureSubtypeChainIsATypeToTypeEdgeTheParentRegisters()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.List`1<StructureTypeSO>",
            assembly.GetFieldType("StructureTypeSO", "subTypes"));
        Assert.NotEmpty(assembly.GetMethods("StructureTypeSO", "RegisterSubType"));
        Assert.Contains(
            assembly.GetMethods("StructureTypeSO", "RegisterSubType"),
            method => method.ParameterTypes.SequenceEqual(new[] { "StructureTypeSO" }));
    }

    /// <summary>
    /// The set a spell resonates over is the recipe's list plus the live, glyph-rewritten one.
    /// </summary>
    /// <remarks>
    /// <c>Spell.GetAllSpellTypes()</c> concatenates the two, so binding only the authored
    /// <c>spellTypes</c> would answer the wrong question the moment a glyph is equipped. The
    /// recipe-side member is private, which is a fact about how it must be read rather than a reason
    /// not to read it.
    /// </remarks>
    [GameAssemblyFact]
    public void TheEffectiveSpellTypeSetIsTheRecipeListAndTheLiveAugmentedList()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.List`1<SpellTypeSO>",
            assembly.GetFieldType("Spell", "augmentedSpellTypes"));
        Assert.Equal(
            "System.Collections.Generic.List`1<SpellTypeSO>",
            assembly.GetFieldType("SpellRecipeSO", "notSpellTypes"));
        Assert.Equal("private", assembly.GetField("SpellRecipeSO", "notSpellTypes").Visibility);
        Assert.NotEmpty(assembly.GetMethods("SpellRecipeSO", "GetNotSpellTypes"));
        Assert.NotEmpty(assembly.GetMethods("Spell", "GetAllSpellTypes"));
    }
}
