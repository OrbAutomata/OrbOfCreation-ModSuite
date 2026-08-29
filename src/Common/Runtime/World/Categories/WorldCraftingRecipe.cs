using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum WorldCraftingRecipeResourceKind
{
    AuthoredInput = 0,
    GeneratedOutput = 1,
}

/// <summary>One native crafting family assigned to a concrete recipe.</summary>
internal readonly struct WorldCraftingRecipeTypeLink
{
    internal WorldCraftingRecipeTypeLink(Guid recipeId, Guid typeId)
    {
        RecipeId = recipeId;
        TypeId = typeId;
    }

    internal Guid RecipeId { get; }
    internal Guid TypeId { get; }
}

/// <summary>An authored recipe resource edge enriched from the same immutable world generation.</summary>
internal readonly struct WorldCraftingRecipeResource
{
    internal WorldCraftingRecipeResource(
        Guid recipeId,
        WorldCraftingRecipeResourceKind kind,
        Guid resourceId,
        BigDouble amount,
        bool resourceStateAvailable,
        bool visible,
        bool bandwidthResource,
        BigDouble trueQuantity,
        bool isCapped,
        BigDouble capacity,
        BigDouble usage,
        BigDouble drain)
    {
        RecipeId = recipeId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
        ResourceStateAvailable = resourceStateAvailable;
        Visible = visible;
        BandwidthResource = bandwidthResource;
        TrueQuantity = trueQuantity;
        IsCapped = isCapped;
        Capacity = capacity;
        Usage = usage;
        Drain = drain;
    }

    internal Guid RecipeId { get; }
    internal WorldCraftingRecipeResourceKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal bool ResourceStateAvailable { get; }
    internal bool Visible { get; }
    internal bool BandwidthResource { get; }
    internal BigDouble TrueQuantity { get; }
    internal bool IsCapped { get; }
    internal BigDouble Capacity { get; }
    internal BigDouble Usage { get; }
    internal BigDouble Drain { get; }
}

/// <summary>A consumable emitted by one of the recipe's native completion-effect scripts.</summary>
internal readonly struct WorldCraftingRecipeConsumableOutput
{
    internal WorldCraftingRecipeConsumableOutput(
        Guid recipeId,
        int blockIndex,
        int scriptIndex,
        Guid consumableId)
    {
        RecipeId = recipeId;
        BlockIndex = blockIndex;
        ScriptIndex = scriptIndex;
        ConsumableId = consumableId;
    }

    internal Guid RecipeId { get; }
    internal int BlockIndex { get; }
    internal int ScriptIndex { get; }
    internal Guid ConsumableId { get; }
    internal string QuantitySource => "native_effect_scaling";
}

/// <summary>The game's read-only necessary-drain verdict for one engagement-effect block.</summary>
internal readonly struct WorldCraftingRecipeDrainBlock
{
    internal WorldCraftingRecipeDrainBlock(
        Guid recipeId,
        int blockIndex,
        BigDouble necessaryRatio)
    {
        RecipeId = recipeId;
        BlockIndex = blockIndex;
        NecessaryRatio = necessaryRatio;
    }

    internal Guid RecipeId { get; }
    internal int BlockIndex { get; }
    internal BigDouble NecessaryRatio { get; }
    internal bool Blocked => NecessaryRatio.CompareTo(BigDouble.One) < 0;
    internal string ReasonCode => Blocked ? "engagement_drain_limited" : "drain_available";
}

/// <summary>
/// One actual <c>CraftingRecipeSO</c>, including all authored input/output edges and the current
/// native visibility, purchase, capacity, and engagement-drain verdicts.
/// </summary>
internal readonly struct WorldCraftingRecipe : IWorldEntity
{
    internal WorldCraftingRecipe(
        in RawCraftingRecipeSample reading,
        PublicationTable<WorldCraftingRecipeTypeLink> types,
        PublicationTable<WorldCraftingRecipeResource> resources,
        PublicationTable<WorldCraftingRecipeConsumableOutput> consumableOutputs,
        PublicationTable<WorldCraftingRecipeDrainBlock> drainBlocks)
    {
        Reading = reading;
        Types = types;
        Resources = resources;
        ConsumableOutputs = consumableOutputs;
        DrainBlocks = drainBlocks;
    }

    internal RawCraftingRecipeSample Reading { get; }
    public Guid EntityId => Reading.RecipeId;
    internal PublicationTable<WorldCraftingRecipeTypeLink> Types { get; }
    internal PublicationTable<WorldCraftingRecipeResource> Resources { get; }
    internal PublicationTable<WorldCraftingRecipeConsumableOutput> ConsumableOutputs { get; }
    internal PublicationTable<WorldCraftingRecipeDrainBlock> DrainBlocks { get; }
}

/// <summary>Fixed-size native verdicts captured on the Unity thread.</summary>
internal readonly struct RawCraftingRecipeSample
{
    internal RawCraftingRecipeSample(
        Guid recipeId,
        bool visible,
        bool canBuyAtStartingQuantity,
        BigDouble startingQuantity,
        bool useQuantityAsLevel,
        double timeToComplete,
        bool outputWithinCapacity,
        int typeCount,
        int authoredInputCount,
        int generatedOutputCount,
        int consumableOutputCount,
        int engagementEffectCount,
        int completionEffectCount)
    {
        RecipeId = recipeId;
        Visible = visible;
        CanBuyAtStartingQuantity = canBuyAtStartingQuantity;
        StartingQuantity = startingQuantity;
        UseQuantityAsLevel = useQuantityAsLevel;
        TimeToComplete = timeToComplete;
        OutputWithinCapacity = outputWithinCapacity;
        TypeCount = typeCount;
        AuthoredInputCount = authoredInputCount;
        GeneratedOutputCount = generatedOutputCount;
        ConsumableOutputCount = consumableOutputCount;
        EngagementEffectCount = engagementEffectCount;
        CompletionEffectCount = completionEffectCount;
    }

    internal Guid RecipeId { get; }
    internal bool Visible { get; }
    internal string VisibilityReasonCode => Visible ? "visible" : "hidden_or_undiscovered";
    internal bool CanBuyAtStartingQuantity { get; }
    internal string NativePurchaseReasonCode =>
        CanBuyAtStartingQuantity ? "can_buy" : "native_can_buy_refused";
    internal BigDouble StartingQuantity { get; }
    internal bool UseQuantityAsLevel { get; }
    internal double TimeToComplete { get; }
    internal bool OutputWithinCapacity { get; }
    internal string OutputCapacityReasonCode =>
        OutputWithinCapacity ? "output_capacity_available" : "output_capacity_blocked";
    internal int TypeCount { get; }
    internal int AuthoredInputCount { get; }
    internal int GeneratedOutputCount { get; }
    internal int ConsumableOutputCount { get; }
    internal int EngagementEffectCount { get; }
    internal int CompletionEffectCount { get; }
}

internal readonly struct RawCraftingRecipeResource
{
    internal RawCraftingRecipeResource(
        Guid recipeId,
        WorldCraftingRecipeResourceKind kind,
        Guid resourceId,
        BigDouble amount)
    {
        RecipeId = recipeId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid RecipeId { get; }
    internal WorldCraftingRecipeResourceKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

/// <summary>
/// Lifecycle-bound live-state reader for concrete recipes. Reflection discovery is confined to
/// construction; authored relationships are captured by the paired structural reader, while each
/// ordinary collection invokes only compiled live-state delegates and copies values.
/// </summary>
internal sealed class WorldCraftingRecipeReader : IWorldCategoryReader
{
    private readonly Type? _nativeType;
    private readonly Type? _recipeTypeType;
    private readonly Type? _resourceTupleType;
    private readonly Type? _instantBlockType;
    private readonly Type? _instantScriptType;
    private readonly Type? _consumableGainType;
    private readonly Func<IList?>? _all;
    private readonly Func<object, Guid>? _id;
    private readonly Func<object, bool>? _visible;
    private readonly Func<object, BigDouble>? _startingQuantity;
    private readonly Func<object, BigDouble, bool>? _canBuyAt;
    private readonly Func<object, bool>? _useQuantityAsLevel;
    private readonly Func<object, double>? _timeToComplete;
    private readonly Func<object, bool>? _outputWithinCapacity;
    private readonly Func<object, IList?>? _types;
    private readonly Func<object, Guid>? _typeId;
    private readonly Func<object, IList?>? _inputs;
    private readonly Func<object, IList?>? _outputs;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, BigDouble>? _resourceAmount;
    private readonly Func<object, IList?>? _engagementEffects;
    private readonly Type? _engagementEffectType;
    private readonly Type? _effectBlockType;
    private readonly Func<object, BigDouble>? _necessaryDrainRatio;
    private readonly Func<object, IList?>? _completeEffects;
    private readonly Func<object, IList?>? _effectScripts;
    private readonly Func<object, Guid>? _outputConsumableId;
    private readonly string _unavailable;
    private readonly List<RecipeSource> _sources = new();

    internal WorldCraftingRecipeReader(Func<string, Type?> resolveType)
    {
        _nativeType = resolveType("CraftingRecipeSO");
        if (_nativeType is null)
        {
            _unavailable = "the CraftingRecipeSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(_nativeType, "CraftingRecipeSO");
        _all = NativeAccessorBinder.StaticListAccessor(_nativeType, "All");
        _id = bind.Call<Guid>("GetGuid");
        _visible = bind.Call<bool>("IsVisible");
        _startingQuantity = bind.Call<BigDouble>("GetStartingQuantity");
        _canBuyAt = bind.Call<BigDouble, bool>("CanBuyAt");
        _useQuantityAsLevel = bind.Field<bool>("useQuantityAsLevel");
        _timeToComplete = bind.Field<double>("timeToComplete");
        _outputWithinCapacity = bind.Through("generatedResources").Call<bool>("IsWithinCapacity");

        _types = bind.CollectionField("craftingTypes");
        _recipeTypeType = bind.CollectionElementType("craftingTypes");
        _typeId = bind.Elements(_recipeTypeType, "CraftingRecipeSO.craftingTypes[]")
            .Call<Guid>("GetGuid");

        _inputs = bind.Through("recipeCost").CollectionField("costs");
        _outputs = bind.Through("generatedResources").CollectionField("costs");
        var inputCostType = _nativeType.GetField("recipeCost")?.FieldType;
        var outputCostType = _nativeType.GetField("generatedResources")?.FieldType;
        _resourceTupleType = NativeAccessorBinder.CollectionElementType(inputCostType, "costs");
        var outputTupleType = NativeAccessorBinder.CollectionElementType(outputCostType, "costs");
        var tuple = bind.Elements(_resourceTupleType, "ResourceCostList.costs[]");
        _resourceId = tuple.ReferenceGuid("resource");
        _resourceAmount = tuple.Field<BigDouble>("valueBig");

        _engagementEffects = bind.CollectionField("engagementEffects");
        _engagementEffectType = bind.CollectionElementType("engagementEffects");
        _effectBlockType = resolveType("EffectBlock");
        _necessaryDrainRatio = bind
            .Elements(_effectBlockType, "EffectBlock")
            .Call<BigDouble>("GetEffectNecessaryDrainRatio");

        _completeEffects = bind.CollectionField("completeEffects");
        _instantBlockType = bind.CollectionElementType("completeEffects");
        var block = bind.Elements(_instantBlockType, "CraftingRecipeSO.completeEffects[]");
        _effectScripts = block.CollectionField("effectScripts");
        _instantScriptType = block.CollectionElementType("effectScripts");
        _consumableGainType = resolveType("ConsumableSO+ConsumableGainEffect");
        _outputConsumableId = bind
            .Elements(_consumableGainType, "ConsumableSO.ConsumableGainEffect")
            .ReferenceGuid("consumable");

        var failure = bind.Failure;
        if (_all is null) failure = Append(failure, "CraftingRecipeSO.All was unavailable");
        if (inputCostType != outputCostType || _resourceTupleType != outputTupleType)
            failure = Append(failure, "recipeCost and generatedResources did not share one ResourceTuple type");
        if (_consumableGainType is null)
            failure = Append(failure, "ConsumableSO+ConsumableGainEffect was unavailable");
        if (_effectBlockType is null || _engagementEffectType is null ||
            !_effectBlockType.IsAssignableFrom(_engagementEffectType))
        {
            failure = Append(
                failure,
                "CraftingRecipeSO.engagementEffects[] was not assignable to EffectBlock");
        }
        _unavailable = failure;
    }

    public string Category => "crafting recipe state";
    public bool IsAvailable => _nativeType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.CraftingRecipes.Reset();
        frame.CraftingRecipeDrainBlocks.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < _sources.Count; index++)
        {
            var source = _sources[index];
            try
            {
                var startingQuantity = _startingQuantity!(source.Entity);
                if (startingQuantity.CompareTo(BigDouble.Zero) <= 0)
                    throw new InvalidOperationException("GetStartingQuantity returned a non-positive value");
                AppendDrainBlocks(
                    source.RecipeId,
                    source.EngagementEffects,
                    frame.CraftingRecipeDrainBlocks);
                frame.CraftingRecipes.Append(new RawCraftingRecipeSample(
                    source.RecipeId,
                    _visible!(source.Entity),
                    _canBuyAt!(source.Entity, startingQuantity),
                    startingQuantity,
                    source.UseQuantityAsLevel,
                    source.TimeToComplete,
                    _outputWithinCapacity!(source.Entity),
                    source.TypeCount,
                    source.AuthoredInputCount,
                    source.GeneratedOutputCount,
                    source.ConsumableOutputCount,
                    source.EngagementEffects.Length,
                    source.CompletionEffectCount));
                sampled++;
            }
            catch (Exception exception)
            {
                Skip(
                    ref skipped,
                    ref firstFailure,
                    "reading live CraftingRecipeSO state threw: " +
                    exception.GetBaseException().Message);
            }
        }

        return new WorldCategoryReport(
            Category,
            WorldCategoryOutcome.Collected,
            sampled,
            skipped,
            firstFailure);
    }

    internal WorldCategoryReport CollectAuthoring(
        HashSet<Guid> claimed,
        GameWorldCycleFrame frame)
    {
        frame.CraftingRecipeTypeLinks.Reset();
        frame.CraftingRecipeResources.Reset();
        frame.CraftingRecipeConsumableOutputs.Reset();
        _sources.Clear();
        if (!IsAvailable) return WorldCategoryReport.Missing("crafting recipes", _unavailable);

        var entities = _all!();
        if (entities is null)
            return WorldCategoryReport.Missing(
                "crafting recipes",
                "the CraftingRecipeSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null || entity.GetType() != _nativeType)
            {
                Skip(ref skipped, ref firstFailure, "a registry entry had an unexpected native type");
                continue;
            }

            try
            {
                var id = _id!(entity);
                if (id == Guid.Empty || !claimed.Add(id))
                {
                    Skip(ref skipped, ref firstFailure, "a crafting recipe had an empty or duplicate identity");
                    continue;
                }

                var types = RequireList(_types!(entity), "craftingTypes");
                var inputs = RequireList(_inputs!(entity), "recipeCost.costs");
                var outputs = RequireList(_outputs!(entity), "generatedResources.costs");
                var engagementEffects = RequireList(_engagementEffects!(entity), "engagementEffects");
                var completeEffects = RequireList(_completeEffects!(entity), "completeEffects");
                ValidateTypes(types);
                ValidateResources(inputs);
                ValidateResources(outputs);

                AppendTypes(id, types, frame.CraftingRecipeTypeLinks);
                AppendResources(
                    id,
                    WorldCraftingRecipeResourceKind.AuthoredInput,
                    inputs,
                    frame.CraftingRecipeResources);
                AppendResources(
                    id,
                    WorldCraftingRecipeResourceKind.GeneratedOutput,
                    outputs,
                    frame.CraftingRecipeResources);
                var cachedEngagementEffects = CaptureEngagementEffects(engagementEffects);
                var consumableOutputCount = AppendConsumableOutputs(
                    id,
                    completeEffects,
                    frame.CraftingRecipeConsumableOutputs);

                _sources.Add(new RecipeSource(
                    entity,
                    id,
                    _useQuantityAsLevel!(entity),
                    _timeToComplete!(entity),
                    types.Count,
                    inputs.Count,
                    outputs.Count,
                    consumableOutputCount,
                    cachedEngagementEffects,
                    completeEffects.Count));
                sampled++;
            }
            catch (Exception exception)
            {
                Skip(
                    ref skipped,
                    ref firstFailure,
                    "reading a CraftingRecipeSO threw: " + exception.GetBaseException().Message);
            }
        }

        return new WorldCategoryReport(
            "crafting recipes",
            WorldCategoryOutcome.Collected,
            sampled,
            skipped,
            firstFailure);
    }

    private void ValidateTypes(IList types)
    {
        if (types.Count == 0) throw new InvalidOperationException("craftingTypes was empty");
        for (var index = 0; index < types.Count; index++)
        {
            var entry = types[index];
            if (entry is null || entry.GetType() != _recipeTypeType || _typeId!(entry) == Guid.Empty)
                throw new InvalidOperationException("craftingTypes contained an unidentified entry");
        }
    }

    private void ValidateResources(IList resources)
    {
        for (var index = 0; index < resources.Count; index++)
        {
            var entry = resources[index];
            if (entry is null || entry.GetType() != _resourceTupleType ||
                _resourceId!(entry) == Guid.Empty)
                throw new InvalidOperationException("a recipe resource entry was unidentified");
        }
    }

    private void AppendTypes(
        Guid recipeId,
        IList types,
        WorldRelationBuffer<WorldCraftingRecipeTypeLink> destination)
    {
        for (var index = 0; index < types.Count; index++)
            destination.Append(new WorldCraftingRecipeTypeLink(recipeId, _typeId!(types[index]!)));
    }

    private void AppendResources(
        Guid recipeId,
        WorldCraftingRecipeResourceKind kind,
        IList resources,
        WorldRelationBuffer<RawCraftingRecipeResource> destination)
    {
        for (var index = 0; index < resources.Count; index++)
        {
            var entry = resources[index]!;
            destination.Append(new RawCraftingRecipeResource(
                recipeId,
                kind,
                _resourceId!(entry),
                _resourceAmount!(entry)));
        }
    }

    private object[] CaptureEngagementEffects(IList blocks)
    {
        var result = new object[blocks.Count];
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block is null || block.GetType() != _engagementEffectType)
                throw new InvalidOperationException("engagementEffects contained an unexpected type");
            result[index] = block;
        }
        return result;
    }

    private void AppendDrainBlocks(
        Guid recipeId,
        object[] blocks,
        WorldRelationBuffer<WorldCraftingRecipeDrainBlock> destination)
    {
        for (var index = 0; index < blocks.Length; index++)
        {
            destination.Append(new WorldCraftingRecipeDrainBlock(
                recipeId,
                index,
                _necessaryDrainRatio!(blocks[index])));
        }
    }

    private int AppendConsumableOutputs(
        Guid recipeId,
        IList blocks,
        WorldRelationBuffer<WorldCraftingRecipeConsumableOutput> destination)
    {
        var count = 0;
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            if (block is null || block.GetType() != _instantBlockType)
                throw new InvalidOperationException("completeEffects contained an unexpected type");
            var scripts = RequireList(_effectScripts!(block), "completeEffects.effectScripts");
            for (var scriptIndex = 0; scriptIndex < scripts.Count; scriptIndex++)
            {
                var script = scripts[scriptIndex];
                if (script is null || !_instantScriptType!.IsInstanceOfType(script))
                    throw new InvalidOperationException("effectScripts contained an unexpected type");
                if (script.GetType() != _consumableGainType) continue;
                var outputId = _outputConsumableId!(script);
                if (outputId == Guid.Empty)
                    throw new InvalidOperationException("a ConsumableGainEffect output was unidentified");
                destination.Append(new WorldCraftingRecipeConsumableOutput(
                    recipeId,
                    blockIndex,
                    scriptIndex,
                    outputId));
                count++;
            }
        }
        return count;
    }

    private static IList RequireList(IList? value, string field) =>
        value ?? throw new InvalidOperationException(field + " was null");

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }

    private static string Append(string first, string second) =>
        first.Length == 0 ? second : first + "; " + second;

    private readonly struct RecipeSource
    {
        internal RecipeSource(
            object entity,
            Guid recipeId,
            bool useQuantityAsLevel,
            double timeToComplete,
            int typeCount,
            int authoredInputCount,
            int generatedOutputCount,
            int consumableOutputCount,
            object[] engagementEffects,
            int completionEffectCount)
        {
            Entity = entity;
            RecipeId = recipeId;
            UseQuantityAsLevel = useQuantityAsLevel;
            TimeToComplete = timeToComplete;
            TypeCount = typeCount;
            AuthoredInputCount = authoredInputCount;
            GeneratedOutputCount = generatedOutputCount;
            ConsumableOutputCount = consumableOutputCount;
            EngagementEffects = engagementEffects;
            CompletionEffectCount = completionEffectCount;
        }

        internal object Entity { get; }
        internal Guid RecipeId { get; }
        internal bool UseQuantityAsLevel { get; }
        internal double TimeToComplete { get; }
        internal int TypeCount { get; }
        internal int AuthoredInputCount { get; }
        internal int GeneratedOutputCount { get; }
        internal int ConsumableOutputCount { get; }
        internal object[] EngagementEffects { get; }
        internal int CompletionEffectCount { get; }
    }
}

/// <summary>
/// Captures each recipe's authored graph once per lifecycle and refreshes the paired reader's native
/// references. Ordinary world passes retain the graph buffers and invoke only live recipe verdicts.
/// </summary>
internal sealed class WorldCraftingRecipeAuthoringReader : IWorldCategoryReader
{
    private readonly WorldCraftingRecipeReader _state;

    internal WorldCraftingRecipeAuthoringReader(WorldCraftingRecipeReader state) =>
        _state = state ?? throw new ArgumentNullException(nameof(state));

    public string Category => "crafting recipes";
    public bool IsAvailable => _state.IsAvailable;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame) =>
        _state.CollectAuthoring(claimed, frame);
}

internal static class WorldCraftingRecipeDeriver
{
    internal static PublicationTable<WorldCraftingRecipe> Build(
        WorldRelationBuffer<RawCraftingRecipeSample> recipes,
        WorldRelationBuffer<WorldCraftingRecipeTypeLink> types,
        WorldRelationBuffer<RawCraftingRecipeResource> resources,
        WorldRelationBuffer<WorldCraftingRecipeConsumableOutput> consumableOutputs,
        WorldRelationBuffer<WorldCraftingRecipeDrainBlock> drainBlocks,
        PublicationTable<WorldResource> worldResources)
    {
        if (recipes.Count == 0) return PublicationTable<WorldCraftingRecipe>.Empty;

        var samples = Copy(recipes);
        Array.Sort(samples, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        var typeRows = Copy(types);
        Array.Sort(typeRows, static (left, right) =>
        {
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            return recipe != 0 ? recipe : left.TypeId.CompareTo(right.TypeId);
        });
        var resourceRows = Enrich(resources, worldResources);
        Array.Sort(resourceRows, static (left, right) =>
        {
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            if (recipe != 0) return recipe;
            var kind = left.Kind.CompareTo(right.Kind);
            return kind != 0 ? kind : left.ResourceId.CompareTo(right.ResourceId);
        });
        var outputRows = Copy(consumableOutputs);
        Array.Sort(outputRows, static (left, right) =>
        {
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            if (recipe != 0) return recipe;
            var block = left.BlockIndex.CompareTo(right.BlockIndex);
            return block != 0 ? block : left.ScriptIndex.CompareTo(right.ScriptIndex);
        });
        var drainRows = Copy(drainBlocks);
        Array.Sort(drainRows, static (left, right) =>
        {
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            return recipe != 0 ? recipe : left.BlockIndex.CompareTo(right.BlockIndex);
        });

        var result = new WorldCraftingRecipe[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            var id = samples[index].RecipeId;
            result[index] = new WorldCraftingRecipe(
                in samples[index],
                Slice(typeRows, id, static row => row.RecipeId),
                Slice(resourceRows, id, static row => row.RecipeId),
                Slice(outputRows, id, static row => row.RecipeId),
                Slice(drainRows, id, static row => row.RecipeId));
        }
        return PublicationTable<WorldCraftingRecipe>.Create(result, result.Length);
    }

    private static WorldCraftingRecipeResource[] Enrich(
        WorldRelationBuffer<RawCraftingRecipeResource> rows,
        PublicationTable<WorldResource> resources)
    {
        var result = new WorldCraftingRecipeResource[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            ref readonly var row = ref rows[index];
            if (WorldLookup.TryFind(resources, row.ResourceId, out var resource))
            {
                result[index] = new WorldCraftingRecipeResource(
                    row.RecipeId,
                    row.Kind,
                    row.ResourceId,
                    row.Amount,
                    resourceStateAvailable: true,
                    resource.Reading.Visible,
                    resource.Reading.Traits.BandwidthResource,
                    resource.TrueQuantity,
                    resource.IsCapped,
                    resource.Reading.Capacity,
                    resource.Reading.Usage,
                    resource.Reading.Drain);
            }
            else
            {
                result[index] = new WorldCraftingRecipeResource(
                    row.RecipeId,
                    row.Kind,
                    row.ResourceId,
                    row.Amount,
                    resourceStateAvailable: false,
                    visible: false,
                    bandwidthResource: false,
                    BigDouble.Zero,
                    isCapped: false,
                    BigDouble.Zero,
                    BigDouble.Zero,
                    BigDouble.Zero);
            }
        }
        return result;
    }

    private static TRow[] Copy<TRow>(WorldRelationBuffer<TRow> buffer) where TRow : struct
    {
        var result = new TRow[buffer.Count];
        for (var index = 0; index < result.Length; index++) result[index] = buffer[index];
        return result;
    }

    private static PublicationTable<TRow> Slice<TRow>(
        TRow[] rows,
        Guid recipeId,
        Func<TRow, Guid> id)
        where TRow : struct
    {
        var count = 0;
        for (var index = 0; index < rows.Length; index++)
            if (id(rows[index]) == recipeId) count++;
        if (count == 0) return PublicationTable<TRow>.Empty;

        var result = new TRow[count];
        var destination = 0;
        for (var index = 0; index < rows.Length; index++)
            if (id(rows[index]) == recipeId) result[destination++] = rows[index];
        return PublicationTable<TRow>.Create(result, result.Length);
    }
}
