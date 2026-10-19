using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldScribeRecipe : IWorldEntity
{
    internal WorldScribeRecipe(
        Guid recipeId,
        Guid recipeTypeId,
        Guid outputConsumableId,
        bool visible,
        bool usesQuantityAsLevel)
    {
        RecipeId = recipeId;
        RecipeTypeId = recipeTypeId;
        OutputConsumableId = outputConsumableId;
        Visible = visible;
        UsesQuantityAsLevel = usesQuantityAsLevel;
    }

    public Guid EntityId => RecipeId;
    internal Guid RecipeId { get; }
    internal Guid RecipeTypeId { get; }
    internal Guid OutputConsumableId { get; }
    internal bool Visible { get; }
    internal bool UsesQuantityAsLevel { get; }
}

internal readonly struct WorldScribeQueue : IWorldEntity
{
    internal WorldScribeQueue(Guid queueId, bool isAutomatic, int used, int maximum)
    {
        QueueId = queueId;
        IsAutomatic = isAutomatic;
        Used = used;
        Maximum = maximum;
    }

    public Guid EntityId => QueueId;
    internal Guid QueueId { get; }
    internal bool IsAutomatic { get; }
    internal int Used { get; }
    internal int Maximum { get; }
}

internal readonly struct WorldScribeWork
{
    internal WorldScribeWork(
        Guid queueId,
        Guid recipeId,
        int level,
        bool isAutomatic,
        bool isExpired)
    {
        QueueId = queueId;
        RecipeId = recipeId;
        Level = level;
        IsAutomatic = isAutomatic;
        IsExpired = isExpired;
    }

    internal Guid QueueId { get; }
    internal Guid RecipeId { get; }
    internal int Level { get; }
    internal bool IsAutomatic { get; }
    internal bool IsExpired { get; }
}

internal readonly struct WorldStructureEnchantment
{
    internal WorldStructureEnchantment(Guid structureId, Guid enchantmentId, int level)
    {
        StructureId = structureId;
        EnchantmentId = enchantmentId;
        Level = level;
    }

    internal Guid StructureId { get; }
    internal Guid EnchantmentId { get; }
    internal int Level { get; }
}

internal readonly struct WorldScrollTarget
{
    internal WorldScrollTarget(Guid consumableId, Guid enchantmentId, Guid structureId)
    {
        ConsumableId = consumableId;
        EnchantmentId = enchantmentId;
        StructureId = structureId;
    }

    internal Guid ConsumableId { get; }
    internal Guid EnchantmentId { get; }
    internal Guid StructureId { get; }
}

/// <summary>
/// Completeness marker for one accepted Scroll target graph. Zero candidates is a complete fact.
/// </summary>
internal readonly struct WorldScrollTargetEvidence
{
    internal WorldScrollTargetEvidence(
        Guid consumableId,
        Guid enchantmentId,
        int candidateCount)
    {
        ConsumableId = consumableId;
        EnchantmentId = enchantmentId;
        CandidateCount = candidateCount;
    }

    internal Guid ConsumableId { get; }
    internal Guid EnchantmentId { get; }
    internal int CandidateCount { get; }
}

internal static class WorldScribeLookup
{
    internal static readonly Comparison<WorldScribeRecipe> RecipeOrder =
        static (left, right) => left.RecipeId.CompareTo(right.RecipeId);

    internal static readonly Comparison<WorldScribeQueue> QueueOrder =
        static (left, right) => left.QueueId.CompareTo(right.QueueId);

    internal static readonly Comparison<WorldScribeWork> WorkOrder =
        static (left, right) =>
        {
            var queue = left.QueueId.CompareTo(right.QueueId);
            if (queue != 0) return queue;
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            return recipe != 0 ? recipe : left.Level.CompareTo(right.Level);
        };

    internal static readonly Comparison<WorldStructureEnchantment> StructureEnchantmentOrder =
        static (left, right) =>
        {
            var structure = left.StructureId.CompareTo(right.StructureId);
            return structure != 0
                ? structure
                : left.EnchantmentId.CompareTo(right.EnchantmentId);
        };

    internal static readonly Comparison<WorldScrollTarget> ScrollTargetOrder =
        static (left, right) =>
        {
            var item = left.ConsumableId.CompareTo(right.ConsumableId);
            if (item != 0) return item;
            var enchantment = left.EnchantmentId.CompareTo(right.EnchantmentId);
            return enchantment != 0
                ? enchantment
                : left.StructureId.CompareTo(right.StructureId);
        };

    internal static readonly Comparison<WorldScrollTargetEvidence> TargetEvidenceOrder =
        static (left, right) =>
        {
            var item = left.ConsumableId.CompareTo(right.ConsumableId);
            return item != 0
                ? item
                : left.EnchantmentId.CompareTo(right.EnchantmentId);
        };

    internal static bool TryGetRecipe(
        PublicationTable<WorldScribeRecipe> recipes,
        Guid recipeId,
        out WorldScribeRecipe recipe)
    {
        for (var index = 0; index < recipes.Count; index++)
        {
            if (recipes[index].RecipeId != recipeId) continue;
            recipe = recipes[index];
            return true;
        }
        recipe = default;
        return false;
    }

    internal static bool TryGetTargetEvidence(
        PublicationTable<WorldScrollTargetEvidence> evidence,
        Guid consumableId,
        Guid enchantmentId,
        out int candidateCount)
    {
        for (var index = 0; index < evidence.Count; index++)
        {
            var row = evidence[index];
            if (row.ConsumableId != consumableId || row.EnchantmentId != enchantmentId)
                continue;
            candidateCount = row.CandidateCount;
            return true;
        }
        candidateCount = 0;
        return false;
    }

    internal static int EnchantmentLevel(
        PublicationTable<WorldStructureEnchantment> enchantments,
        Guid structureId,
        Guid enchantmentId)
    {
        for (var index = 0; index < enchantments.Count; index++)
        {
            var row = enchantments[index];
            if (row.StructureId == structureId && row.EnchantmentId == enchantmentId)
                return row.Level;
        }
        return 0;
    }
}

/// <summary>
/// Captures Scribe relationships through one constructor-bound exact schema. The warm path only
/// invokes retained metadata; member discovery cannot fail halfway through a collection.
/// </summary>
internal sealed class WorldScribeRelationReader : IWorldCategoryReader
{
    private readonly BindingSet? _native;
    private readonly string _unavailable;

    internal WorldScribeRelationReader(Func<string, Type?> resolve)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        if (BindingSet.TryCreate(resolve, out var native, out var reason))
        {
            _native = native;
            _unavailable = string.Empty;
        }
        else
        {
            _unavailable = reason;
        }
    }

    public string Category => "scribe relations";
    public bool IsAvailable => _native is not null;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        ResetRows(frame);
        if (_native is not { } native)
            return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var sampled = ReadRecipes(native, frame);
            sampled += ReadQueues(native, frame);
            sampled += ReadEnchantments(native, frame);
            sampled += ReadTargets(native, frame);
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                skipped: 0,
                firstFailure: string.Empty);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            ResetRows(frame);
            return WorldCategoryReport.Missing(
                Category,
                ex.GetBaseException().Message);
        }
    }

    private static void ResetRows(GameWorldCycleFrame frame)
    {
        frame.ScribeRecipes.Reset();
        frame.ScribeQueues.Reset();
        frame.ScribeWork.Reset();
        frame.StructureEnchantments.Reset();
        frame.ScrollTargets.Reset();
        frame.ScrollTargetEvidence.Reset();
    }

    private static int ReadRecipes(BindingSet native, GameWorldCycleFrame frame)
    {
        var registry = Resolve(
            native,
            KnownEntities.ScribeCraftingRecipes.Uuid,
            native.RecipeListType);
        var recipes = RequireList(
            native.RecipeListValue(registry),
            "ScribeCraftingRecipes.value");
        var sampled = 0;
        foreach (var value in recipes)
        {
            var recipe = RequireExact(value, native.RecipeType, "Scribe recipe");
            var recipeId = native.RecipeIdentity(recipe);
            var types = RequireEnumerable(
                native.RecipeTypes(recipe),
                "CraftingRecipeSO.craftingTypes");
            var typeCount = 0;
            var typeId = Guid.Empty;
            foreach (var valueType in types)
            {
                var exactType = RequireExact(valueType, native.RecipeTypeType, "recipe type");
                typeCount++;
                typeId = native.RecipeTypeIdentity(exactType);
            }

            var outputCount = 0;
            var outputId = Guid.Empty;
            foreach (var blockValue in RequireEnumerable(
                         native.CompleteEffects(recipe),
                         "CraftingRecipeSO.completeEffects"))
            {
                var block = RequireExact(blockValue, native.InstantBlockType, "complete effect block");
                foreach (var scriptValue in RequireEnumerable(
                             native.EffectScripts(block),
                             "InstantEffectBlock.effectScripts"))
                {
                    if (scriptValue is null || !native.InstantScriptType.IsInstanceOfType(scriptValue))
                        throw new InvalidOperationException(
                            $"Scribe recipe {EntityIdentityFormatter.Format(recipeId)} contained a non-IInstantEffectScript output.");
                    if (scriptValue.GetType() != native.ConsumableGainType) continue;
                    var output = RequireExact(
                        native.GainConsumable(scriptValue),
                        native.ConsumableType,
                        "ConsumableGainEffect.consumable");
                    outputCount++;
                    outputId = native.ConsumableIdentity(output);
                }
            }
            if (typeCount != 1 || outputCount != 1)
                throw new InvalidOperationException(
                    $"Scribe recipe {EntityIdentityFormatter.Format(recipeId)} had {typeCount} recipe types and " +
                    $"{outputCount} ConsumableGainEffect outputs; exactly one of each is required.");

            frame.ScribeRecipes.Append(new WorldScribeRecipe(
                recipeId,
                typeId,
                outputId,
                native.RecipeVisible(recipe),
                native.UseQuantityAsLevel(recipe)));
            sampled++;
        }
        return sampled;
    }

    private static int ReadQueues(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        foreach (var queueId in new[]
        {
            KnownEntities.ActiveScribeInstances.Uuid,
            KnownEntities.AutoScribeInstances.Uuid,
        })
        {
            var queue = Resolve(native, queueId, native.InstanceListType);
            var values = RequireList(
                native.InstanceListValue(queue),
                "CraftingInstance list value");
            var isAutomatic = native.AutoList(queue);
            if (isAutomatic != (queueId == KnownEntities.AutoScribeInstances.Uuid))
                throw new InvalidOperationException(
                    $"Scribe queue {EntityIdentityFormatter.Format(queueId)} contradicted its native automation role.");
            frame.ScribeQueues.Append(new WorldScribeQueue(
                queueId,
                isAutomatic,
                CountNonNull(values),
                native.ListMaximum(queue)));
            sampled++;
            foreach (var value in values)
            {
                if (value is null) continue;
                var instance = RequireExact(value, native.InstanceType, "CraftingInstance");
                var instanceAutomatic = native.InstanceAutomatic(instance);
                if (instanceAutomatic != isAutomatic)
                    throw new InvalidOperationException(
                        $"CraftingInstance.IsAuto() contradicted containing Scribe queue {EntityIdentityFormatter.Format(queueId)}.");
                frame.ScribeWork.Append(new WorldScribeWork(
                    queueId,
                    native.InstanceRecipe(instance),
                    Level(native.InstanceQuantity(instance)),
                    instanceAutomatic,
                    native.InstanceExpired(instance)));
                sampled++;
            }
        }
        return sampled;
    }

    private static int ReadEnchantments(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        foreach (var value in RequireEnumerable(
                     native.StructureAll(),
                     "StructureSO.All"))
        {
            var structure = RequireExact(value, native.StructureType, "StructureSO");
            var structureId = native.StructureIdentity(structure);
            var table = RequireExact(
                native.EnchantTable(structure),
                native.EnchantTableType,
                "EnchantmentSO.EnchantTable");
            foreach (var entryValue in RequireEnumerable(
                         native.Enchantments(table),
                         "EnchantmentSO.EnchantTable.enchantments"))
            {
                var entry = RequireExact(
                    entryValue,
                    native.EnchantmentInstanceType,
                    "EnchantmentInstance");
                frame.StructureEnchantments.Append(new WorldStructureEnchantment(
                    structureId,
                    native.EnchantmentInstanceIdentity(entry),
                    native.EnchantmentLevel(entry)));
                sampled++;
            }
        }
        return sampled;
    }

    private static int ReadTargets(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        for (var index = 0; index < TargetRoles.Length; index++)
        {
            var role = TargetRoles[index];
            var consumable = Resolve(native, role.ScrollId, native.ConsumableType);
            var targeting = ResolveTargeting(native, consumable, role.EnchantmentId);
            var recipeType = Resolve(
                native,
                KnownEntities.ScribeCrafting.Uuid,
                native.RecipeTypeType);
            var level = Math.Max(1, native.MaximumStartingLevel(recipeType));
            var scaling = Require(
                native.ScalingBasic(new BigDouble(level, 0)),
                "ScalingInfo.Basic");
            if (scaling.GetType() != native.ScalingType)
                throw new InvalidOperationException("ScalingInfo.Basic(BigDouble) changed return type.");
            var candidates = RequireEnumerable(
                native.GetRandomList(targeting, scaling),
                "Targeting.TargetStructure.GetRandomList");
            var count = 0;
            foreach (var candidateValue in candidates)
            {
                var candidate = RequireExact(candidateValue, native.StructureType, "Scroll target");
                frame.ScrollTargets.Append(new WorldScrollTarget(
                    role.ScrollId,
                    role.EnchantmentId,
                    native.StructureIdentity(candidate)));
                count++;
                sampled++;
            }
            frame.ScrollTargetEvidence.Append(new WorldScrollTargetEvidence(
                role.ScrollId,
                role.EnchantmentId,
                count));
            sampled++;
        }
        return sampled;
    }

    private static object ResolveTargeting(
        BindingSet native,
        object consumable,
        Guid expectedEnchantment)
    {
        object? options = null;
        var requestCount = 0;
        var enchantCount = 0;
        var enchantment = Guid.Empty;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects(consumable),
                     "ConsumableSO.onUseEffects"))
        {
            var block = RequireExact(blockValue, native.InstantBlockType, "on-use effect block");
            foreach (var scriptValue in RequireEnumerable(
                         native.EffectScripts(block),
                         "InstantEffectBlock.effectScripts"))
            {
                if (scriptValue is null || !native.InstantScriptType.IsInstanceOfType(scriptValue))
                    throw new InvalidOperationException(
                        "Scroll on-use effects contained a non-IInstantEffectScript value.");
                if (scriptValue.GetType() == native.RequestType)
                {
                    requestCount++;
                    options = native.TargetOptions(scriptValue);
                }
                else if (scriptValue.GetType() == native.EnchantScriptType)
                {
                    enchantCount++;
                    var enchant = RequireExact(
                        native.EnchantScriptEnchantment(scriptValue),
                        native.EnchantmentType,
                        "EnchantItemScript.enchantment");
                    enchantment = native.EnchantmentIdentity(enchant);
                }
            }
        }
        if (requestCount != 1 || enchantCount != 1 || enchantment != expectedEnchantment)
            throw new InvalidOperationException(
                $"Scroll {EntityIdentityFormatter.Format(native.ConsumableIdentity(consumable))} had {requestCount} target " +
                $"requests, {enchantCount} enchant effects, and enchantment {EntityIdentityFormatter.Format(enchantment)}; " +
                $"expected exactly one of each and {EntityIdentityFormatter.Format(expectedEnchantment)}.");
        var exactOptions = RequireExact(options, native.OptionsType, "TargetSelectOptions");
        var targeting = native.GetTargeting(exactOptions);
        return RequireExact(targeting, native.TargetStructureType, "TargetStructure");
    }

    private static object Resolve(BindingSet native, Guid id, Type exactType)
    {
        var source = native.Registry.Read();
        if (!source.IsReady || source.Registry is null || !source.Registry.Contains(id))
            throw new InvalidOperationException(
                $"The identity registry did not contain {exactType.Name} {EntityIdentityFormatter.Format(id)}.");
        return RequireExact(source.Registry[id], exactType, exactType.Name);
    }

    private static int CountNonNull(IList values)
    {
        var count = 0;
        foreach (var value in values)
            if (value is not null) count++;
        return count;
    }

    private static int Level(BigDouble value)
    {
        var scalar = value.ToDouble();
        if (double.IsFinite(scalar) && scalar >= 0 && scalar <= int.MaxValue)
            return (int)Math.Floor(scalar);
        throw new InvalidOperationException("A Scribe level was not a finite non-negative integer.");
    }

    private static object Require(object? value, string contract) =>
        value ?? throw new InvalidOperationException(contract + " returned null.");

    private static object RequireExact(object? value, Type type, string contract) =>
        value is not null && value.GetType() == type
            ? value
            : throw new InvalidOperationException(contract + " was not the exact audited type.");

    private static IEnumerable RequireEnumerable(object? value, string contract) =>
        value as IEnumerable ??
        throw new InvalidOperationException(contract + " was not enumerable.");

    private static IList RequireList(object? value, string contract) =>
        value as IList ??
        throw new InvalidOperationException(contract + " was not a list.");

    private readonly record struct TargetRole(Guid ScrollId, Guid EnchantmentId);

    private static readonly TargetRole[] TargetRoles =
    {
        new(KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
        new(KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
        new(KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
        new(KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
        new(KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
        new(KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
        new(KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
        new(KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
    };

    private sealed class BindingSet
    {
        internal static readonly string[] ContractIds =
        {
            "resource-bar.identity.type-capture",
            "crafting-instance.recipe.type-action",
            "scribe-relations.recipe-list.type",
            "crafting-instance.recipe-type.type-action",
            "crafting-instance.queue.type-action",
            "auto-scribe.crafting-instance.type",
            "consumable.type",
            "structure-lifecycle.structure.type-action",
            "scribe-relations.enchant-table.type",
            "scribe-relations.enchantment-instance.type",
            "enchantment-so.tooltipable-base",
            "auto-items.scaling-info.type",
            "auto-items.instant-effect-block.type",
            "scribe-relations.instant-effect-script.type",
            "auto-scribe.consumable-gain.type",
            "scribe-relations.request-target.type",
            "scribe-relations.target-select-options.type",
            "scribe-relations.base-target-selection.type",
            "auto-items.target-structure.type",
            "scribe-relations.targetable.type",
            "auto-scribe.enchant-script.type",
            "auto-scribe.list.value-capture",
            "auto-scribe.recipe.crafting-types",
            "auto-scribe.recipe.complete-effects",
            "auto-scribe.recipe.levelled",
            "auto-harvest.effect-scripts",
            "auto-scribe.consumable-gain.output",
            "auto-scribe.list.auto-flag",
            "structure.all",
            "auto-scribe.structure.enchant-table",
            "auto-scribe.enchant-table.entries",
            "crafting-recipe-type.max-starting-level",
            "auto-scribe.consumable.on-use-effects",
            "scribe-relations.request-target.options",
            "auto-scribe.enchant-script.enchantment",
            "id-scriptable-object.get-guid",
            "crafting-recipe.visible",
            "auto-scribe.list.get-max",
            "auto-scribe.ref-instance.guid-reference",
            "auto-scribe.instance.quantity",
            "auto-scribe.instance.auto",
            "auto-scribe.instance.expired",
            "auto-scribe.enchantment-instance.level",
            "scribe-relations.scaling-info.basic",
            "scribe-relations.target-options.get-targeting",
            "scribe-relations.target-structure.get-random-list",
        };

        private BindingSet(
            Type recipeType,
            Type recipeListType,
            Type recipeTypeType,
            Type instanceListType,
            Type instanceType,
            Type consumableType,
            Type structureType,
            Type enchantTableType,
            Type enchantmentInstanceType,
            Type enchantmentType,
            Type scalingType,
            Type instantBlockType,
            Type instantScriptType,
            Type consumableGainType,
            Type requestType,
            Type optionsType,
            Type targetStructureType,
            Type enchantScriptType,
            RuntimeIdentityRegistryBinding registry,
            Func<object, IList?> recipeListValue,
            Func<object, IList?> instanceListValue,
            Func<object, IEnumerable?> recipeTypes,
            Func<object, IEnumerable?> completeEffects,
            Func<object, bool> useQuantityAsLevel,
            Func<object, IEnumerable?> effectScripts,
            Func<object, object?> gainConsumable,
            Func<object, bool> autoList,
            Func<IEnumerable?> structureAll,
            Func<object, object?> enchantTable,
            Func<object, IEnumerable?> enchantments,
            Func<object, int> maximumStartingLevel,
            Func<object, IEnumerable?> onUseEffects,
            Func<object, object?> targetOptions,
            Func<object, object?> enchantScriptEnchantment,
            Func<object, Guid> recipeIdentity,
            Func<object, Guid> recipeTypeIdentity,
            Func<object, Guid> consumableIdentity,
            Func<object, Guid> structureIdentity,
            Func<object, bool> recipeVisible,
            Func<object, int> listMaximum,
            Func<object, Guid> instanceRecipe,
            Func<object, BigDouble> instanceQuantity,
            Func<object, bool> instanceAutomatic,
            Func<object, bool> instanceExpired,
            Func<object, Guid> enchantmentInstanceIdentity,
            Func<object, Guid> enchantmentIdentity,
            Func<object, int> enchantmentLevel,
            Func<BigDouble, object?> scalingBasic,
            Func<object, object?> getTargeting,
            Func<object, object, IEnumerable?> getRandomList)
        {
            RecipeType = recipeType;
            RecipeListType = recipeListType;
            RecipeTypeType = recipeTypeType;
            InstanceListType = instanceListType;
            InstanceType = instanceType;
            ConsumableType = consumableType;
            StructureType = structureType;
            EnchantTableType = enchantTableType;
            EnchantmentInstanceType = enchantmentInstanceType;
            EnchantmentType = enchantmentType;
            ScalingType = scalingType;
            InstantBlockType = instantBlockType;
            InstantScriptType = instantScriptType;
            ConsumableGainType = consumableGainType;
            RequestType = requestType;
            OptionsType = optionsType;
            TargetStructureType = targetStructureType;
            EnchantScriptType = enchantScriptType;
            Registry = registry;
            RecipeListValue = recipeListValue;
            InstanceListValue = instanceListValue;
            RecipeTypes = recipeTypes;
            CompleteEffects = completeEffects;
            UseQuantityAsLevel = useQuantityAsLevel;
            EffectScripts = effectScripts;
            GainConsumable = gainConsumable;
            AutoList = autoList;
            StructureAll = structureAll;
            EnchantTable = enchantTable;
            Enchantments = enchantments;
            MaximumStartingLevel = maximumStartingLevel;
            OnUseEffects = onUseEffects;
            TargetOptions = targetOptions;
            EnchantScriptEnchantment = enchantScriptEnchantment;
            RecipeIdentity = recipeIdentity;
            RecipeTypeIdentity = recipeTypeIdentity;
            ConsumableIdentity = consumableIdentity;
            StructureIdentity = structureIdentity;
            RecipeVisible = recipeVisible;
            ListMaximum = listMaximum;
            InstanceRecipe = instanceRecipe;
            InstanceQuantity = instanceQuantity;
            InstanceAutomatic = instanceAutomatic;
            InstanceExpired = instanceExpired;
            EnchantmentInstanceIdentity = enchantmentInstanceIdentity;
            EnchantmentIdentity = enchantmentIdentity;
            EnchantmentLevel = enchantmentLevel;
            ScalingBasic = scalingBasic;
            GetTargeting = getTargeting;
            GetRandomList = getRandomList;
        }

        internal Type RecipeType { get; }
        internal Type RecipeListType { get; }
        internal Type RecipeTypeType { get; }
        internal Type InstanceListType { get; }
        internal Type InstanceType { get; }
        internal Type ConsumableType { get; }
        internal Type StructureType { get; }
        internal Type EnchantTableType { get; }
        internal Type EnchantmentInstanceType { get; }
        internal Type EnchantmentType { get; }
        internal Type ScalingType { get; }
        internal Type InstantBlockType { get; }
        internal Type InstantScriptType { get; }
        internal Type ConsumableGainType { get; }
        internal Type RequestType { get; }
        internal Type OptionsType { get; }
        internal Type TargetStructureType { get; }
        internal Type EnchantScriptType { get; }
        internal RuntimeIdentityRegistryBinding Registry { get; }
        internal Func<object, IList?> RecipeListValue { get; }
        internal Func<object, IList?> InstanceListValue { get; }
        internal Func<object, IEnumerable?> RecipeTypes { get; }
        internal Func<object, IEnumerable?> CompleteEffects { get; }
        internal Func<object, bool> UseQuantityAsLevel { get; }
        internal Func<object, IEnumerable?> EffectScripts { get; }
        internal Func<object, object?> GainConsumable { get; }
        internal Func<object, bool> AutoList { get; }
        internal Func<IEnumerable?> StructureAll { get; }
        internal Func<object, object?> EnchantTable { get; }
        internal Func<object, IEnumerable?> Enchantments { get; }
        internal Func<object, int> MaximumStartingLevel { get; }
        internal Func<object, IEnumerable?> OnUseEffects { get; }
        internal Func<object, object?> TargetOptions { get; }
        internal Func<object, object?> EnchantScriptEnchantment { get; }
        internal Func<object, Guid> RecipeIdentity { get; }
        internal Func<object, Guid> RecipeTypeIdentity { get; }
        internal Func<object, Guid> ConsumableIdentity { get; }
        internal Func<object, Guid> StructureIdentity { get; }
        internal Func<object, bool> RecipeVisible { get; }
        internal Func<object, int> ListMaximum { get; }
        internal Func<object, Guid> InstanceRecipe { get; }
        internal Func<object, BigDouble> InstanceQuantity { get; }
        internal Func<object, bool> InstanceAutomatic { get; }
        internal Func<object, bool> InstanceExpired { get; }
        internal Func<object, Guid> EnchantmentInstanceIdentity { get; }
        internal Func<object, Guid> EnchantmentIdentity { get; }
        internal Func<object, int> EnchantmentLevel { get; }
        internal Func<BigDouble, object?> ScalingBasic { get; }
        internal Func<object, object?> GetTargeting { get; }
        internal Func<object, object, IEnumerable?> GetRandomList { get; }

        internal static bool TryCreate(
            Func<string, Type?> resolve,
            out BindingSet? bindings,
            out string reason) =>
            NativeBindingSet.TryBind("Scribe relationship", ContractIds, resolve, null, set =>
            {
                var id = set.Type("resource-bar.identity.type-capture", "IdScriptableObject");
                var recipe = set.Type("crafting-instance.recipe.type-action", "CraftingRecipeSO");
                var recipeList = set.Type("scribe-relations.recipe-list.type", "CraftingRecipeListVariable");
                var recipeType = set.Type("crafting-instance.recipe-type.type-action", "CraftingRecipeTypeSO");
                var instanceList = set.Type("crafting-instance.queue.type-action", "CraftingInstanceListVariable");
                var instance = set.Type("auto-scribe.crafting-instance.type", "CraftingInstance");
                var consumable = set.Type("consumable.type", "ConsumableSO");
                var structure = set.Type("structure-lifecycle.structure.type-action", "StructureSO");
                var enchantTable = set.Type("scribe-relations.enchant-table.type", "EnchantmentSO+EnchantTable");
                var enchantInstance = set.Type("scribe-relations.enchantment-instance.type", "EnchantmentInstance");
                var enchantment = set.Type("enchantment-so.tooltipable-base", "EnchantmentSO");
                var scaling = set.Type("auto-items.scaling-info.type", "ScalingInfo");
                var block = set.Type("auto-items.instant-effect-block.type", "InstantEffectBlock");
                var script = set.Type("scribe-relations.instant-effect-script.type", "IInstantEffectScript");
                var gain = set.Type("auto-scribe.consumable-gain.type", "ConsumableSO+ConsumableGainEffect");
                var request = set.Type("scribe-relations.request-target.type", "RequestTargetEffectScript");
                var options = set.Type("scribe-relations.target-select-options.type", "Targeting.TargetSelectOptions");
                var selection = set.Type(
                    "scribe-relations.base-target-selection.type", "Targeting.BaseTargetSelection");
                var target = set.Type("auto-items.target-structure.type", "Targeting.TargetStructure");
                var targetable = set.Type("scribe-relations.targetable.type", "Targeting.ITargetable");
                var enchantScript = set.Type("auto-scribe.enchant-script.type", "EnchantmentSO+EnchantItemScript");

                return new BindingSet(
                    recipe,
                    recipeList,
                    recipeType,
                    instanceList,
                    instance,
                    consumable,
                    structure,
                    enchantTable,
                    enchantInstance,
                    enchantment,
                    scaling,
                    block,
                    script,
                    gain,
                    request,
                    options,
                    target,
                    enchantScript,
                    new RuntimeIdentityRegistryBinding(
                        () => id, requireStableIdentityContract: false),
                    set.Field<Func<object, IList?>>(
                        "auto-scribe.list.value-capture", recipeList, "value", ListOf(recipe)),
                    set.Field<Func<object, IList?>>(
                        "auto-scribe.list.value-capture", instanceList, "value", ListOf(instance)),
                    set.Field<Func<object, IEnumerable?>>(
                        "auto-scribe.recipe.crafting-types", recipe, "craftingTypes", ListOf(recipeType)),
                    set.Field<Func<object, IEnumerable?>>(
                        "auto-scribe.recipe.complete-effects", recipe, "completeEffects", ListOf(block)),
                    set.Field<Func<object, bool>>(
                        "auto-scribe.recipe.levelled", recipe, "useQuantityAsLevel", typeof(bool)),
                    set.Field<Func<object, IEnumerable?>>(
                        "auto-harvest.effect-scripts", block, "effectScripts", ListOf(script)),
                    set.Field<Func<object, object?>>(
                        "auto-scribe.consumable-gain.output", gain, "consumable", consumable),
                    set.Field<Func<object, bool>>(
                        "auto-scribe.list.auto-flag", instanceList, "isAutoList", typeof(bool)),
                    set.StaticField<Func<IEnumerable?>>("structure.all", structure, "All", ListOf(structure)),
                    set.Field<Func<object, object?>>(
                        "auto-scribe.structure.enchant-table", structure, "enchantTable", enchantTable),
                    set.Field<Func<object, IEnumerable?>>(
                        "auto-scribe.enchant-table.entries", enchantTable, "enchantments", ListOf(enchantInstance)),
                    set.Field<Func<object, int>>(
                        "crafting-recipe-type.max-starting-level", recipeType, "maxStartingLevel", typeof(int)),
                    set.Field<Func<object, IEnumerable?>>(
                        "auto-scribe.consumable.on-use-effects", consumable, "onUseEffects", ListOf(block)),
                    set.Field<Func<object, object?>>(
                        "scribe-relations.request-target.options", request, "targetOptions", options),
                    set.Field<Func<object, object?>>(
                        "auto-scribe.enchant-script.enchantment", enchantScript, "enchantment", enchantment),
                    set.StableUuid("id-scriptable-object.get-guid", recipe),
                    set.StableUuid("id-scriptable-object.get-guid", recipeType),
                    set.StableUuid("id-scriptable-object.get-guid", consumable),
                    set.StableUuid("id-scriptable-object.get-guid", structure),
                    set.Call<Func<object, bool>>("crafting-recipe.visible", recipe, "IsVisible", typeof(bool)),
                    set.Call<Func<object, int>>("auto-scribe.list.get-max", instanceList, "GetMax", typeof(int)),
                    set.Call<Func<object, Guid>>(
                        "auto-scribe.ref-instance.guid-reference", instance, "GetGuidReference", typeof(Guid)),
                    set.Call<Func<object, BigDouble>>(
                        "auto-scribe.instance.quantity", instance, "GetQuantity", typeof(BigDouble)),
                    set.Call<Func<object, bool>>("auto-scribe.instance.auto", instance, "IsAuto", typeof(bool)),
                    set.Call<Func<object, bool>>("auto-scribe.instance.expired", instance, "IsExpired", typeof(bool)),
                    set.Call<Func<object, Guid>>(
                        "auto-scribe.ref-instance.guid-reference", enchantInstance, "GetGuidReference", typeof(Guid)),
                    set.StableUuid("id-scriptable-object.get-guid", enchantment),
                    set.Call<Func<object, int>>(
                        "auto-scribe.enchantment-instance.level", enchantInstance, "GetLevel", typeof(int)),
                    set.StaticCall<Func<BigDouble, object?>>(
                        "scribe-relations.scaling-info.basic", scaling, "Basic", scaling, typeof(BigDouble)),
                    set.Call<Func<object, object?>>(
                        "scribe-relations.target-options.get-targeting", options, "GetTargeting", selection),
                    set.Call<Func<object, object, IEnumerable?>>(
                        "scribe-relations.target-structure.get-random-list",
                        target,
                        "GetRandomList",
                        ListOf(targetable),
                        scaling));
            }, out bindings, out reason);

        private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
    }
}
