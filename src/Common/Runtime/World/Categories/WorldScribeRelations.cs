using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

internal sealed class WorldRelationBuffer<TRow> where TRow : struct
{
    private TRow[] _rows = new TRow[16];
    internal int Count { get; private set; }
    internal ref readonly TRow this[int index] => ref _rows[index];
    internal void Reset() => Count = 0;

    internal void Append(in TRow row)
    {
        if (Count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[Count++] = row;
    }
}

internal static class WorldScribeRelationDeriver
{
    internal static PublicationTable<TRow> Build<TRow>(
        WorldRelationBuffer<TRow> buffer,
        Comparison<TRow> comparison)
        where TRow : struct
    {
        if (buffer.Count == 0) return PublicationTable<TRow>.Empty;
        var rows = new TRow[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, comparison);
        return PublicationTable<TRow>.Create(rows, rows.Length);
    }
}

internal static class WorldScribeLookup
{
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
        frame.ScribeRecipes.Reset();
        frame.ScribeQueues.Reset();
        frame.ScribeWork.Reset();
        frame.StructureEnchantments.Reset();
        frame.ScrollTargets.Reset();
        frame.ScrollTargetEvidence.Reset();
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
        // A compiled accessor hands a native fault straight back, where MethodInfo.Invoke used to
        // deliver every one of them wrapped as TargetInvocationException. Degrading the category on
        // any of them keeps the fail-closed answer the reflective path gave — and keeps it at the
        // category, which is where the shared reader already puts it, rather than letting one
        // transient native throw out into the pass.
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category,
                ex.GetBaseException().Message);
        }
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
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
            out string reason)
        {
            bindings = null;
            try
            {
                var id = Type(resolve, "IdScriptableObject");
                var recipe = Type(resolve, "CraftingRecipeSO");
                var recipeList = Type(resolve, "CraftingRecipeListVariable");
                var recipeType = Type(resolve, "CraftingRecipeTypeSO");
                var instanceList = Type(resolve, "CraftingInstanceListVariable");
                var instance = Type(resolve, "CraftingInstance");
                var consumable = Type(resolve, "ConsumableSO");
                var structure = Type(resolve, "StructureSO");
                var enchantTable = Type(resolve, "EnchantmentSO+EnchantTable");
                var enchantInstance = Type(resolve, "EnchantmentInstance");
                var enchantment = Type(resolve, "EnchantmentSO");
                var scaling = Type(resolve, "ScalingInfo");
                var bigDouble = Type(resolve, "BigDouble");
                var block = Type(resolve, "InstantEffectBlock");
                var script = Type(resolve, "IInstantEffectScript");
                var gain = Type(resolve, "ConsumableSO+ConsumableGainEffect");
                var request = Type(resolve, "RequestTargetEffectScript");
                var options = Type(resolve, "Targeting.TargetSelectOptions");
                var selection = Type(resolve, "Targeting.BaseTargetSelection");
                var target = Type(resolve, "Targeting.TargetStructure");
                var targetable = Type(resolve, "Targeting.ITargetable");
                var enchantScript = Type(resolve, "EnchantmentSO+EnchantItemScript");

                bindings = new BindingSet(
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
                    ListValue(recipeList, recipe),
                    ListValue(instanceList, instance),
                    FieldSequence(recipe, "craftingTypes", recipeType),
                    FieldSequence(recipe, "completeEffects", block),
                    FieldValue<bool>(recipe, "useQuantityAsLevel"),
                    FieldSequence(block, "effectScripts", script),
                    FieldObject(gain, "consumable", consumable),
                    FieldValue<bool>(instanceList, "isAutoList"),
                    StaticFieldSequence(structure, "All", structure),
                    FieldObject(structure, "enchantTable", enchantTable),
                    FieldSequence(enchantTable, "enchantments", enchantInstance),
                    FieldValue<int>(recipeType, "maxStartingLevel"),
                    FieldSequence(consumable, "onUseEffects", block),
                    FieldObject(request, "targetOptions", options),
                    FieldObject(enchantScript, "enchantment", enchantment),
                    InheritedCall<Guid>(recipe, "GetGuid"),
                    InheritedCall<Guid>(recipeType, "GetGuid"),
                    InheritedCall<Guid>(consumable, "GetGuid"),
                    InheritedCall<Guid>(structure, "GetGuid"),
                    DeclaredCall<bool>(recipe, "IsVisible"),
                    InheritedCall<int>(instanceList, "GetMax"),
                    InheritedCall<Guid>(instance, "GetGuidReference"),
                    DeclaredCall<BigDouble>(instance, "GetQuantity", bigDouble),
                    DeclaredCall<bool>(instance, "IsAuto"),
                    DeclaredCall<bool>(instance, "IsExpired"),
                    InheritedCall<Guid>(enchantInstance, "GetGuidReference"),
                    InheritedCall<Guid>(enchantment, "GetGuid"),
                    DeclaredCall<int>(enchantInstance, "GetLevel"),
                    StaticCall<BigDouble>(scaling, "Basic", scaling, bigDouble),
                    DeclaredCallObject(options, "GetTargeting", selection),
                    DeclaredCallSequence(
                        target,
                        "GetRandomList",
                        typeof(List<>).MakeGenericType(targetable),
                        scaling));
                reason = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
            {
                reason = "The exact Scribe relationship binding set is unavailable: " + ex.Message;
                return false;
            }
        }

        private static Type Type(Func<string, Type?> resolve, string name) =>
            resolve(name) ?? throw new InvalidOperationException(name + " was unavailable.");

        // Discovery and compilation are paired rather than merged: the audits below are stricter
        // than a name and a type — an exact element type, a private field on a generic base, a
        // method resolved up the hierarchy — and each accessor is compiled from the member that
        // audit accepted. A member that resolves but cannot be compiled is as unavailable as one
        // that never resolved, and says so in the same sentence.
        private static Func<object, IList?> ListValue(Type listType, Type elementType) =>
            Compiled(
                NativeAccessorBinder.ReadList(GenericListValue(listType, elementType)),
                listType,
                "value");

        private static Func<object, TValue> FieldValue<TValue>(Type type, string name) =>
            Compiled(
                NativeAccessorBinder.Read<TValue>(Field(type, name, Instance, typeof(TValue))),
                type,
                name);

        private static Func<object, object?> FieldObject(Type type, string name, Type expected) =>
            Compiled(
                NativeAccessorBinder.ReadValue(Field(type, name, Instance, expected)),
                type,
                name);

        private static Func<object, IEnumerable?> FieldSequence(
            Type type,
            string name,
            Type element) =>
            Compiled(
                NativeAccessorBinder.ReadSequence(CollectionField(type, name, element)),
                type,
                name);

        private static Func<IEnumerable?> StaticFieldSequence(
            Type type,
            string name,
            Type element) =>
            Compiled(
                NativeAccessorBinder.ReadStaticSequence(
                    CollectionField(type, name, element, Static)),
                type,
                name);

        private static Func<object, TValue> InheritedCall<TValue>(Type type, string name) =>
            Compiled(
                NativeAccessorBinder.Call<TValue>(
                    MethodFromHierarchy(type, name, typeof(TValue))),
                type,
                name);

        private static Func<object, TValue> DeclaredCall<TValue>(Type type, string name) =>
            DeclaredCall<TValue>(type, name, typeof(TValue));

        private static Func<object, TValue> DeclaredCall<TValue>(
            Type type,
            string name,
            Type returnType) =>
            Compiled(
                NativeAccessorBinder.Call<TValue>(Method(type, name, returnType, Instance)),
                type,
                name);

        private static Func<object, object?> DeclaredCallObject(
            Type type,
            string name,
            Type returnType) =>
            Compiled(
                NativeAccessorBinder.CallValue(Method(type, name, returnType, Instance)),
                type,
                name);

        private static Func<object, object, IEnumerable?> DeclaredCallSequence(
            Type type,
            string name,
            Type returnType,
            Type argument) =>
            Compiled(
                NativeAccessorBinder.CallSequence(
                    Method(type, name, returnType, Instance, argument)),
                type,
                name);

        private static Func<TArgument, object?> StaticCall<TArgument>(
            Type type,
            string name,
            Type returnType,
            Type argument) =>
            Compiled(
                NativeAccessorBinder.CallStatic<TArgument>(
                    Method(type, name, returnType, Static, argument)),
                type,
                name);

        private static T Compiled<T>(T? accessor, Type type, string name)
            where T : Delegate =>
            accessor ?? throw new InvalidOperationException(
                $"{type.Name}.{name} could not be compiled into an accessor.");

        private static FieldInfo GenericListValue(Type listType, Type elementType)
        {
            for (var current = listType; current is not null; current = current.BaseType)
            {
                var field = current.GetField("value", Instance | BindingFlags.DeclaredOnly);
                if (field is not null &&
                    field.FieldType == typeof(List<>).MakeGenericType(elementType))
                    return field;
            }
            throw new InvalidOperationException(
                $"{listType.Name}.value : List<{elementType.Name}> was unavailable.");
        }

        private static FieldInfo CollectionField(
            Type type,
            string name,
            Type element,
            BindingFlags flags = Instance)
        {
            var field = type.GetField(name, flags);
            if (field is null || CollectionElement(field.FieldType) != element ||
                field.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} collection of {element.Name} was unavailable.");
            return field;
        }

        private static FieldInfo Field(
            Type type,
            string name,
            BindingFlags flags,
            Type expected,
            bool allowDictionary = false)
        {
            var field = type.GetField(name, flags);
            var typeMatches = allowDictionary
                ? field?.FieldType.IsGenericType == true &&
                  field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                  field.FieldType.GetGenericArguments()[0] == typeof(Guid)
                : field?.FieldType == expected;
            if (field is null || !typeMatches ||
                field.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} : {expected.Name} was unavailable.");
            return field;
        }

        private static MethodInfo MethodFromHierarchy(
            Type type,
            string name,
            Type returnType,
            params Type[] parameters)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var method = current.GetMethod(
                    name,
                    Instance | BindingFlags.DeclaredOnly,
                    null,
                    parameters,
                    null);
                if (method?.ReturnType == returnType && !method.IsStatic) return method;
            }
            throw new InvalidOperationException(
                $"{type.Name}.{name}({string.Join(",", Array.ConvertAll(parameters, p => p.Name))}) " +
                $": {returnType.Name} was unavailable.");
        }

        private static MethodInfo Method(
            Type type,
            string name,
            Type returnType,
            BindingFlags flags,
            params Type[] parameters)
        {
            var method = type.GetMethod(name, flags, null, parameters, null);
            if (method is null || method.ReturnType != returnType ||
                method.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} : {returnType.Name} was unavailable.");
            return method;
        }

        private static Type? CollectionElement(Type type)
        {
            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
                return type.GetGenericArguments()[0];
            foreach (var candidate in type.GetInterfaces())
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return candidate.GetGenericArguments()[0];
            return null;
        }
    }
}
