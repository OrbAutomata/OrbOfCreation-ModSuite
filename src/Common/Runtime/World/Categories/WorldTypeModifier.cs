using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which taxonomy owns a type-level modifier record.</summary>
internal enum WorldTypeModifierOwnerKind
{
    SpellType = 0,
    ResourceType = 1,
    AlchemyType = 2,
    CraftingRecipeType = 3,
    EquipmentType = 4,
    RitualType = 5,
    StructureType = 6,
    HarvestType = 7,
    PlotNodeType = 8,
    ResearchType = 9,
    ConsumableType = 10,
    HarvestActionType = 11,
    PassiveAbilityType = 12,
    TimeRuneType = 13,
}

/// <summary>
/// One modifier record on one type asset: which record it is, and how many modifiers currently sit on
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A type total and a member value are one bonus, not two factors.</b> Eleven of the fourteen
/// taxonomies reach their members by <i>distribution</i>: <c>MergingModifierRecord</c> and
/// <c>OrderedMultiplierRecord</c> hold no value of their own, and when a modifier lands on one they
/// push a transformed copy into every member record registered with <c>AddRecord</c>. The member's own
/// <c>ValueModifierRecord</c> — which the suite already collects and folds — therefore already
/// contains the type's contribution. Multiplying a total derived from these rows into a member value
/// the suite already publishes counts the same bonus twice. <c>SpellTypeSO</c> is the one class where
/// it does not: all twenty-two of its records are values, and <c>Spell.GetPower()</c> multiplies the
/// type layer in as a separate factor.
/// </para>
/// <para>
/// The counts are here so a consumer can see the size of <see cref="WorldTypeModifierContribution"/>
/// before asking for its rows. They are raw dictionary counts and nothing else: a distributor's own
/// total is <c>Adjust(100)</c> over these entries, which is arithmetic and therefore belongs off the
/// Unity thread, in derivation, beside the rest of the modifier fold.
/// </para>
/// </remarks>
internal readonly struct WorldTypeModifier
{
    internal WorldTypeModifier(
        Guid typeId,
        WorldTypeModifierOwnerKind ownerKind,
        string property,
        string recordNativeType,
        int activeCount,
        int passiveCount)
    {
        TypeId = typeId;
        OwnerKind = ownerKind;
        Property = property ?? string.Empty;
        RecordNativeType = recordNativeType ?? string.Empty;
        ActiveCount = activeCount;
        PassiveCount = passiveCount;
    }

    /// <summary>The type asset carrying the record.</summary>
    internal Guid TypeId { get; }

    internal WorldTypeModifierOwnerKind OwnerKind { get; }

    /// <summary>The record's own member name, which is the key an authored effect names it by.</summary>
    internal string Property { get; }

    /// <summary>
    /// The record class. <c>ValueModifierRecord</c> holds a value of its own and is already folded
    /// where it is published; the other three hold none, so only they have a total left to derive.
    /// </summary>
    internal string RecordNativeType { get; }

    internal int ActiveCount { get; }

    internal int PassiveCount { get; }
}

/// <summary>One modifier sitting on one type asset's record, and the source that put it there.</summary>
/// <remarks>
/// The per-modifier shape is <see cref="WorldResearchRequirementAdjustment"/> unchanged, because a
/// modifier entry is a modifier entry wherever it is read from. Its own note — that it exists for
/// provenance and that the game remains the evaluator — needs one amendment here: for a distributor
/// there is no game-side evaluator to defer to, because nothing in the game asks one for a number
/// outside its tooltip. Folding these entries off-thread reproduces
/// <c>OrderedMultiplierRecord.GetTotalPercent()</c>, which is a pinned formula rather than a guess.
/// </remarks>
internal readonly struct WorldTypeModifierContribution
{
    internal WorldTypeModifierContribution(
        Guid typeId,
        string property,
        WorldResearchRequirementAdjustment contribution)
    {
        TypeId = typeId;
        Property = property ?? string.Empty;
        Contribution = contribution;
    }

    internal Guid TypeId { get; }

    internal string Property { get; }

    internal WorldResearchRequirementAdjustment Contribution { get; }
}

/// <summary>One parent structure type and one type it confers its records on.</summary>
/// <remarks>
/// <c>StructureTypeSO.Initialize()</c> calls <c>RegisterSubType</c> for every entry, which wires the
/// parent's thirteen records into the child's thirteen. A bonus on a parent therefore reaches the
/// members of its children, so the set a type-wide bonus covers is not the parent's own membership
/// list — it is the transitive closure over this edge.
/// </remarks>
internal readonly struct WorldTypeSubtype
{
    internal WorldTypeSubtype(Guid typeId, int ordinal, Guid subTypeId)
    {
        TypeId = typeId;
        Ordinal = ordinal;
        SubTypeId = subTypeId;
    }

    internal Guid TypeId { get; }

    internal int Ordinal { get; }

    internal Guid SubTypeId { get; }
}

/// <summary>
/// Every modifier record on every type asset, bound once for the two categories that read them.
/// </summary>
/// <remarks>
/// One binding serves both readers because they walk the same fourteen registries and differ only in
/// what they take from each record: the counts, or the entries behind them. Binding twice would
/// compile the same hundred and forty-five accessors twice and give two categories two chances to
/// disagree about whether the build is readable.
/// </remarks>
internal sealed class WorldTypeModifierBindings
{
    private static readonly string[] SpellTypeRecords =
    {
        "augmentResonance", "bonusCritRate", "bonusDoubleCastRate", "bonusFlashRate",
        "chargeEffectMod", "chargeSpecialMod", "chargeTimeMod", "cooldownSpeed", "cooldownTime",
        "costMod", "critDurationMod", "critEffectMod", "doubleCastEffectMod", "drainCostMod",
        "durationMod", "elementalResonance", "flashEffectMod", "maxStacksMod", "power", "scalingMod",
        "typeXpMod", "usageCostReduction",
    };

    private static readonly string[] ResourceTypeRecords =
    {
        "attributeCostMod", "decayRatio", "decayTimeMod", "drainMod", "gainRateMod", "lossPercentMod",
        "maxQuantityMod", "maxQuantityRateMod", "qualityMod", "rateMod", "rawMaxQuantity",
        "replenishRatio", "replenishTimeMod", "reservationMod", "restMod", "reverberateMod",
        "reverberateTimeMod", "splashRate", "splashRateInterest", "splashRateLifetime",
        "splashRateMaxPercent", "splashRateMissing",
    };

    private static readonly string[] AlchemyTypeRecords =
    {
        "drainCostMod", "effectLevels", "experienceRate", "freeUsageSlots", "level",
        "overdriveDrainCostMod", "overdrivePower", "overdriveSpeed", "overdriveXpRate", "power",
        "special", "speed", "timeReqMod", "timeScalingMod",
    };

    private static readonly string[] CraftingRecipeTypeRecords =
    {
        "autoPenaltyMod", "costIncrementMod", "costMod", "efficiencyMod", "magnitudeIncrement",
        "multiPenaltyMod", "power", "speed",
    };

    private static readonly string[] EquipmentTypeRecords =
    {
        "experienceRateMod", "masteryLevel", "maxTypeSlots", "powerMod",
    };

    private static readonly string[] RitualTypeRecords =
    {
        "activeRituals", "chainLengthBonus", "chainPower", "completionCostMod", "completionRateMod",
        "critDurationMod", "critPower", "critRating", "durationMod", "echoPower", "echoRating",
        "power", "special", "speed",
    };

    private static readonly string[] StructureTypeRecords =
    {
        "activeCostMod", "attributeRankEffectMod", "bonusLevels", "buildSpeedMod", "costScalingMod",
        "drainCostMod", "echoBuildRating", "effectLevels", "passiveCostMod", "powerBuildRating",
        "structurePower", "structurePowerScaling", "structureSpeed",
    };

    private static readonly string[] HarvestTypeRecords =
    {
        "autoGenerationMod", "drainCostMod", "experienceRateMod", "growthSpeedMod", "harvestSpeedMod",
        "level", "maxQuantity", "maxRestGrowth", "power", "qualityMod", "restingGrowthSpeedMod",
    };

    private static readonly string[] PlotNodeTypeRecords =
    {
        "actionCostMod", "actionSpeed", "actionXpRate", "growingSpeed", "qualityMod",
        "recoverySizeMod", "restingSpeed", "sizeMod", "specialMod", "totalLevel", "yieldMod",
    };

    private static readonly string[] ResearchTypeRecords =
    {
        "freeBonusLevels", "levelRequirementAdjust", "maxInvestmentLevel", "maxLevelCap", "power",
        "usedBonusLevels",
    };

    private static readonly string[] ConsumableTypeRecords =
    {
        "bonusLevels", "durationMod", "power", "prepSpeed", "special",
    };

    private static readonly string[] HarvestActionTypeRecords =
    {
        "costMod", "growthSizeMod", "power", "refundRating", "speed",
    };

    private static readonly string[] PassiveAbilityTypeRecords =
    {
        "cooldown", "costMod", "durationMod", "maxStacksMod", "power",
    };

    private static readonly string[] TimeRuneTypeRecords =
    {
        "freeUsages", "masteryXpMod", "power", "powerScalingMod", "totalLevel",
    };

    internal WorldTypeModifierBindings(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        Taxonomies = new[]
        {
            Bind(resolveType, WorldTypeModifierOwnerKind.SpellType, "SpellTypeSO", SpellTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.ResourceType, "ResourceTypeSO", ResourceTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.AlchemyType, "AlchemyTypeSO", AlchemyTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.CraftingRecipeType, "CraftingRecipeTypeSO", CraftingRecipeTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.EquipmentType, "EquipmentTypeSO", EquipmentTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.RitualType, "RitualTypeSO", RitualTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.StructureType, "StructureTypeSO", StructureTypeRecords, subTypeField: "subTypes"),
            Bind(resolveType, WorldTypeModifierOwnerKind.HarvestType, "HarvestTypeSO", HarvestTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.PlotNodeType, "PlotNodeTypeSO", PlotNodeTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.ResearchType, "ResearchTypeSO", ResearchTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.ConsumableType, "ConsumableTypeSO", ConsumableTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.HarvestActionType, "HarvestActionTypeSO", HarvestActionTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.PassiveAbilityType, "PassiveAbilityTypeSO", PassiveAbilityTypeRecords),
            Bind(resolveType, WorldTypeModifierOwnerKind.TimeRuneType, "TimeRuneTypeSO", TimeRuneTypeRecords),
        };

        var failures = string.Empty;
        foreach (var taxonomy in Taxonomies)
        {
            if (taxonomy.Failure.Length == 0) continue;
            failures = failures.Length == 0 ? taxonomy.Failure : failures + "; " + taxonomy.Failure;
        }

        Unavailable = failures;
    }

    internal TaxonomyBinding[] Taxonomies { get; }

    /// <summary>
    /// Empty when every record on every taxonomy bound. One failure withholds both categories: a
    /// modifier table that is silently short a class reads exactly like a class whose types carry no
    /// modifiers, and that is the reading a strategist would act on.
    /// </summary>
    internal string Unavailable { get; }

    private static TaxonomyBinding Bind(
        Func<string, Type?> resolveType,
        WorldTypeModifierOwnerKind kind,
        string typeName,
        string[] recordFields,
        string? subTypeField = null)
    {
        var type = resolveType(typeName);
        if (type is null)
        {
            return new TaxonomyBinding(kind, typeName,
                "the " + typeName + " type was not found on this build");
        }

        var bind = new WorldMemberBinding(type, typeName);
        var typeId = bind.Call<Guid>("GetGuid");
        var registry = NativeAccessorBinder.StaticListAccessor(type, "All");

        Func<object, IList?>? subTypes = null;
        Func<object, Guid>? subTypeId = null;
        if (subTypeField is not null)
        {
            subTypes = bind.CollectionField(subTypeField);
            subTypeId = bind
                .Elements(bind.CollectionElementType(subTypeField), typeName + "." + subTypeField + "[]")
                .Call<Guid>("GetGuid");
        }

        var failure = bind.Failure;
        if (registry is null)
        {
            failure = Join(failure, typeName + " did not expose All on this build");
        }

        var records = new RecordBinding[recordFields.Length];
        for (var index = 0; index < recordFields.Length; index++)
        {
            var field = recordFields[index];
            var access = NativeModifierAdjustmentAccess.Bind(type, field, out var recordFailure);
            if (access is null)
            {
                failure = Join(failure, typeName + "." + recordFailure);
                continue;
            }

            records[index] = new RecordBinding(field, access.RecordNativeType, access);
        }

        return failure.Length == 0
            ? new TaxonomyBinding(kind, typeName, registry!, typeId!, records, subTypes, subTypeId)
            : new TaxonomyBinding(kind, typeName, failure);
    }

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : left + "; " + right;

    internal sealed class TaxonomyBinding
    {
        internal TaxonomyBinding(WorldTypeModifierOwnerKind kind, string typeName, string failure)
        {
            Kind = kind;
            TypeName = typeName;
            Failure = failure;
            Records = Array.Empty<RecordBinding>();
        }

        internal TaxonomyBinding(
            WorldTypeModifierOwnerKind kind,
            string typeName,
            Func<IList?> registry,
            Func<object, Guid> typeId,
            RecordBinding[] records,
            Func<object, IList?>? subTypes,
            Func<object, Guid>? subTypeId)
        {
            Kind = kind;
            TypeName = typeName;
            Registry = registry;
            TypeId = typeId;
            Records = records;
            SubTypes = subTypes;
            SubTypeId = subTypeId;
            Failure = string.Empty;
        }

        internal WorldTypeModifierOwnerKind Kind { get; }
        internal string TypeName { get; }
        internal Func<IList?>? Registry { get; }
        internal Func<object, Guid>? TypeId { get; }
        internal RecordBinding[] Records { get; }
        internal Func<object, IList?>? SubTypes { get; }
        internal Func<object, Guid>? SubTypeId { get; }
        internal string Failure { get; }
    }

    internal sealed class RecordBinding
    {
        internal RecordBinding(string property, string recordNativeType, NativeModifierAdjustmentAccess access)
        {
            Property = property;
            RecordNativeType = recordNativeType;
            Access = access;
        }

        internal string Property { get; }
        internal string RecordNativeType { get; }
        internal NativeModifierAdjustmentAccess Access { get; }
    }
}

/// <summary>
/// Publishes one row per type asset record: which record, and how loaded it currently is.
/// </summary>
/// <remarks>
/// One uniform table rather than nine per-class row structs. The row shape is identical across all
/// fourteen taxonomies — a record is a record — so per-class structs would buy nine binder classes,
/// nine registrations and a nine-way fan-out in every consumer for no additional fact, and would
/// invent world categories for nine classes that have none.
/// </remarks>
internal sealed class WorldTypeModifierReader : IWorldCategoryReader
{
    private readonly WorldTypeModifierBindings _bindings;

    internal WorldTypeModifierReader(WorldTypeModifierBindings bindings) =>
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    public string Category => "type modifiers";

    public bool IsAvailable => _bindings.Unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        frame.TypeModifiers.Reset();
        frame.TypeSubtypes.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _bindings.Unavailable);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        foreach (var taxonomy in _bindings.Taxonomies)
        {
            var types = taxonomy.Registry!();
            if (types is null)
            {
                WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                    "the " + taxonomy.TypeName + " registry was unreadable");
                continue;
            }

            for (var index = 0; index < types.Count; index++)
            {
                var asset = types[index];
                if (asset is null)
                {
                    WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                        "a " + taxonomy.TypeName + " registry entry was null");
                    continue;
                }

                try
                {
                    var typeId = taxonomy.TypeId!(asset);
                    if (typeId == Guid.Empty)
                    {
                        WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                            "a " + taxonomy.TypeName + " entry had no stable identity");
                        continue;
                    }

                    foreach (var record in taxonomy.Records)
                    {
                        if (!record.Access.TryCount(asset, out var passive, out var active))
                        {
                            WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                                taxonomy.TypeName + "." + record.Property + " held no record");
                            continue;
                        }

                        frame.TypeModifiers.Append(new WorldTypeModifier(
                            typeId, taxonomy.Kind, record.Property, record.RecordNativeType,
                            active, passive));
                    }

                    if (taxonomy.SubTypes is not null)
                    {
                        var subTypes = taxonomy.SubTypes(asset);
                        for (var ordinal = 0; ordinal < (subTypes?.Count ?? 0); ordinal++)
                        {
                            var subType = subTypes![ordinal];
                            if (subType is null) continue;
                            var subTypeId = taxonomy.SubTypeId!(subType);
                            if (subTypeId == Guid.Empty) continue;
                            frame.TypeSubtypes.Append(
                                new WorldTypeSubtype(typeId, ordinal, subTypeId));
                        }
                    }

                    sampled++;
                }
                catch (Exception exception)
                {
                    WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                        "reading a " + taxonomy.TypeName + " modifier row threw: " +
                        exception.GetBaseException().Message);
                }
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }
}

/// <summary>Publishes one row per modifier currently sitting on a type asset's record.</summary>
/// <remarks>
/// Variable-size with no upper bound the audited build can state: the two dictionaries are runtime
/// state written by whatever effect fired, so how many entries a mid-game save carries is not
/// knowable from IL or from the serialized assets. <see cref="WorldTypeModifier"/> carries the counts
/// for exactly that reason — a consumer can size this table before walking it.
/// </remarks>
internal sealed class WorldTypeModifierContributionReader : IWorldCategoryReader
{
    private readonly WorldTypeModifierBindings _bindings;

    internal WorldTypeModifierContributionReader(WorldTypeModifierBindings bindings) =>
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    public string Category => "type modifier contributions";

    public bool IsAvailable => _bindings.Unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        frame.TypeModifierContributions.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _bindings.Unavailable);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        foreach (var taxonomy in _bindings.Taxonomies)
        {
            var types = taxonomy.Registry!();
            if (types is null)
            {
                WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                    "the " + taxonomy.TypeName + " registry was unreadable");
                continue;
            }

            for (var index = 0; index < types.Count; index++)
            {
                var asset = types[index];
                if (asset is null)
                {
                    WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                        "a " + taxonomy.TypeName + " registry entry was null");
                    continue;
                }

                try
                {
                    var typeId = taxonomy.TypeId!(asset);
                    if (typeId == Guid.Empty)
                    {
                        WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                            "a " + taxonomy.TypeName + " entry had no stable identity");
                        continue;
                    }

                    foreach (var record in taxonomy.Records)
                    {
                        var contributions = record.Access.ReadInto(asset);
                        for (var entry = 0; entry < contributions.Length; entry++)
                        {
                            frame.TypeModifierContributions.Append(new WorldTypeModifierContribution(
                                typeId, record.Property, contributions[entry]));
                        }
                    }

                    sampled++;
                }
                catch (Exception exception)
                {
                    WorldTypeModifierWalk.Skip(ref skipped, ref firstFailure,
                        "reading a " + taxonomy.TypeName + " contribution row threw: " +
                        exception.GetBaseException().Message);
                }
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }
}

internal static class WorldTypeModifierWalk
{
    internal static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
