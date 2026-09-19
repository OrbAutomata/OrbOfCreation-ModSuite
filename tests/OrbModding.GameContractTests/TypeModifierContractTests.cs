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

    /// <summary>
    /// Method names the runtime reaches without any call site in the assembly, so an absent caller
    /// says nothing about them.
    /// </summary>
    private static readonly HashSet<string> EntryPoints = new(StringComparer.Ordinal)
    {
        ".ctor", ".cctor", "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Update",
        "FixedUpdate", "LateUpdate", "OnValidate", "Reset", "OnApplicationQuit",
        "OnApplicationPause",
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
    /// The records this build carries but cannot read, re-derived from the pinned assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record is live when some path exists for the game to reach it, and the assembly says which:
    /// an accessor arm resolving an authored ref name onto it, a reachable getter or pull site
    /// loading it, or a <c>Register*</c> site loading it to push its modifiers into member records.
    /// This census asks the inverse question of every record at once and keeps only the ones with no
    /// path at all, which is what the suite's own liveness table must say.
    /// </para>
    /// <para>
    /// Three kinds of touch are not a read into a computation and are excluded by name: a store,
    /// which authors the record rather than consuming it; a load handed straight to
    /// <c>ModifierRecord.Clear()</c>, which is <c>ResetData</c> wiping the record on a lifecycle
    /// boundary; and a load inside a method the assembly dispatches to from nowhere. Every other
    /// touch counts, and every ambiguity counts as a read — a virtual method, a constructor, a Unity
    /// message and anything referenced anywhere all keep their record alive, because IL can prove a
    /// path exists and cannot prove one absent through a route it never sees. The census therefore
    /// fails open: it can only ever find fewer dead records than really are.
    /// </para>
    /// </remarks>
    [GameAssemblyFact]
    public void FourRecordsExistOnThisBuildWithNoPathForTheGameToReadThem()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var fields = Records
            .SelectMany(taxonomy => taxonomy.Value.Select(
                entry => (Type: taxonomy.Key, Field: entry.Split(':')[0])))
            .ToArray();
        var sites = assembly.GetFieldUseSites(fields);
        var reads = sites
            .Where(site => site.Use == "load" && site.CalledMember != "Clear")
            .ToArray();
        var dispatched = assembly.GetReferencedMethodTokens(
            reads.Select(read => read.MethodToken).Distinct().ToArray());

        var dead = fields
            .Where(field => !reads.Any(read =>
                read.FieldOwner == field.Type &&
                read.FieldName == field.Field &&
                (read.MethodIsVirtual ||
                 EntryPoints.Contains(read.MethodName) ||
                 dispatched.Contains(read.MethodToken))))
            .Select(field => field.Type + "." + field.Field)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "EquipmentTypeSO.masteryLevel",
                "PlotNodeTypeSO.totalLevel",
                "SpellTypeSO.bonusFlashRate",
                "SpellTypeSO.flashEffectMod",
            },
            dead);
    }

    /// <summary>
    /// The one arm that decides the flash pair: the router answers twenty-two ref names with twenty
    /// records, and both flash names fall through to null.
    /// </summary>
    /// <remarks>
    /// Without this, "no reachable reader" would be the whole case, and a reader could reasonably
    /// suppose an authored upgrade still reaches the record by naming it. It does not: the router is
    /// the only way an effect names a property, and <c>GetFilteredPropertyNames</c> drops a name
    /// whose accessor <c>HasNoInfo()</c> from the tooltip for the same reason.
    /// </remarks>
    [GameAssemblyFact]
    public void TheSpellTypeRouterResolvesNeitherFlashRecord()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesField(
            "SpellTypeSO", "GetValueModifierRecord", "SpellTypeSO", "power"));
        Assert.False(assembly.MethodReferencesField(
            "SpellTypeSO", "GetValueModifierRecord", "SpellTypeSO", "bonusFlashRate"));
        Assert.False(assembly.MethodReferencesField(
            "SpellTypeSO", "GetValueModifierRecord", "SpellTypeSO", "flashEffectMod"));
        Assert.True(assembly.MethodReferencesMethod(
            "UpgradeableObject+UpgradeEffectModifier", "Execute",
            "UpgradeableObject", "GetUpgradeModAccessor"));
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

    /// <summary>
    /// The spell type layer has a native oracle, and it is the whole product rather than a term of it.
    /// </summary>
    /// <remarks>
    /// The derived layer reproduces <c>GetResonantPercent</c>, and the only thing that can tell a
    /// faithful reproduction from a plausible one is the number the game itself multiplies into
    /// <c>Spell.GetPower()</c>. The exact shape is the contract: an overload taking arguments, or one
    /// answering in a narrower numeric type, would be a different question wearing the same name, and
    /// the verifier binds on this shape alone.
    /// </remarks>
    [GameAssemblyFact]
    public void TheSpellTypePowerLayerHasOneNativeOracleToAnswerTo()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var oracle = Assert.Single(assembly.GetMethods("Spell", "GetSpellTypePowerPercent"));
        Assert.Equal("public", oracle.Visibility);
        Assert.False(oracle.IsStatic);
        Assert.Equal("BigDouble", oracle.ReturnType);
        Assert.Empty(oracle.ParameterTypes);
    }
}
