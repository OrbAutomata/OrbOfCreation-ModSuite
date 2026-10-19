using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored effect block an action applies when it completes, described by its shape rather than
/// by what it does.
/// </summary>
/// <remarks>
/// <para>
/// A consumer that needs to know an action is safe to run asks what its completion applies. That
/// question is answered from facts — which kind of block, how many modifiers, how many scripts, and
/// what the one modifier and the one script name when there is one of each — and the verdict drawn
/// from them belongs to the consumer's own policy on its own worker, not here.
/// </para>
/// <para>
/// The single-modifier and single-script terms describe the first entry of each list, and are read
/// only for the two kinds this suite knows how to read. A block of another shape still publishes its
/// counts and its type names, which is what lets a consumer say "this is not the block I audited"
/// rather than finding nothing and guessing.
/// </para>
/// <para>
/// The runtime type name travels because there is no integer to travel instead: the game distinguishes
/// block kinds by class, and a name is what a consumer can compare against without holding the type.
/// </para>
/// </remarks>
internal readonly struct WorldEffectBlock
{
    internal WorldEffectBlock(
        Guid ownerId,
        int ordinal,
        string blockTypeName,
        int prerequisiteCount,
        int modCount,
        int scriptCount,
        string modTypeName,
        string scriptTypeName,
        Guid scalingWeightId,
        Guid treasurePoolId,
        string effectTypeName,
        double effectValue,
        int filterListType,
        int filterContentCount)
    {
        OwnerId = ownerId;
        Ordinal = ordinal;
        BlockTypeName = blockTypeName;
        PrerequisiteCount = prerequisiteCount;
        ModCount = modCount;
        ScriptCount = scriptCount;
        ModTypeName = modTypeName;
        ScriptTypeName = scriptTypeName;
        ScalingWeightId = scalingWeightId;
        TreasurePoolId = treasurePoolId;
        EffectTypeName = effectTypeName;
        EffectValue = effectValue;
        FilterListType = filterListType;
        FilterContentCount = filterContentCount;
    }

    /// <summary>The entity whose completion applies the block.</summary>
    internal Guid OwnerId { get; }

    /// <summary>The block's position in its owner's list.</summary>
    internal int Ordinal { get; }

    /// <summary>The block's runtime class name, as the game names it.</summary>
    internal string BlockTypeName { get; }

    /// <summary>How many conditions gate the block.</summary>
    internal int PrerequisiteCount { get; }

    /// <summary>How many modifiers the block applies.</summary>
    internal int ModCount { get; }

    /// <summary>How many scripts the block runs.</summary>
    internal int ScriptCount { get; }

    /// <summary>The first modifier's runtime class name, empty when the block applies none.</summary>
    internal string ModTypeName { get; }

    /// <summary>The first script's runtime class name, empty when the block runs none.</summary>
    internal string ScriptTypeName { get; }

    /// <summary>
    /// Which weight the first modifier scales by, when it is one that scales by a weight.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.Empty"/> covers three things a consumer must not tell apart by this column
    /// alone — no modifier, a modifier of another kind, and one that names no weight. Which of them
    /// it is, is what <see cref="ModCount"/> and <see cref="ModTypeName"/> say.
    /// </remarks>
    internal Guid ScalingWeightId { get; }

    /// <summary>Which pool the first script draws from, when it is one that draws from a pool.</summary>
    internal Guid TreasurePoolId { get; }

    /// <summary>What the first script says it does, as the game's own string.</summary>
    internal string EffectTypeName { get; }

    /// <summary>How much of it the first script does.</summary>
    internal double EffectValue { get; }

    /// <summary>
    /// How the first script's scaling filter reads its contents, as the enum's underlying integer.
    /// </summary>
    internal int FilterListType { get; }

    /// <summary>How many entries that filter holds.</summary>
    internal int FilterContentCount { get; }
}

/// <summary>
/// Range lookup over the effect-block table, which is keyed by owner and then position.
/// </summary>
/// <remarks>
/// A block is not an entity and an owner may author several, so this is
/// <see cref="WorldPurchaseCostLookup"/>'s shape rather than <see cref="WorldLookup"/>'s: a binary
/// search for the owner's first row, then a forward walk.
/// </remarks>
internal static class WorldEffectBlockLookup
{
    internal static readonly Comparison<WorldEffectBlock> Order =
        static (left, right) =>
        {
            var byOwner = left.OwnerId.CompareTo(right.OwnerId);
            return byOwner != 0 ? byOwner : left.Ordinal.CompareTo(right.Ordinal);
        };

    internal static bool TryFindRange(
        PublicationTable<WorldEffectBlock> table,
        Guid ownerId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].OwnerId.CompareTo(ownerId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].OwnerId == ownerId) count++;
        return true;
    }
}

/// <summary>
/// A second walk of the plot-action registry, for the blocks each action applies on completion. It
/// claims no identities: the actions are already claimed by their own category.
/// </summary>
internal sealed class WorldEffectBlockReader : IWorldCategoryReader
{
    private readonly Type? _actionType;
    private readonly Func<IList?>? _actions;
    private readonly Type? _modType;
    private readonly Type? _scriptType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _actionId;
    private readonly Func<object, IList?>? _blocks;
    private readonly Func<object, int>? _blockPrerequisites;
    private readonly Func<object, IList?>? _mods;
    private readonly Func<object, IList?>? _scripts;
    private readonly Func<object, Guid>? _scalingWeightId;
    private readonly Func<object, Guid>? _treasurePoolId;
    private readonly Func<object, string>? _effectTypeName;
    private readonly Func<object, double>? _effectValue;
    private readonly Func<object, int>? _filterListType;
    private readonly Func<object, int>? _filterContentCount;

    /// <summary>
    /// The two kinds of modifier and script a block's shape is read past its counts for. Both are
    /// reached by name because the lists that hold them are typed as interfaces, so the element type
    /// says nothing about what the entries are.
    /// </summary>
    internal WorldEffectBlockReader(Type? actionType, Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _actionType = actionType;
        _actions = NativeAccessorBinder.StaticListAccessor(actionType, "All");
        _modType = resolveType("ScalingWeightEffectMod");
        _scriptType = resolveType("TreasurePoolInstantEffect");
        if (actionType is null || _modType is null || _scriptType is null)
        {
            _unavailable = $"the {AbsentType(actionType, _modType, _scriptType)} type was not found " +
                "on this build";
            return;
        }

        var bind = new WorldMemberBinding(actionType, "PlotNodeActionSO");
        _actionId = bind.StableUuid();
        _blocks = bind.CollectionField("completeEffects");

        var block = bind.Elements(
            bind.CollectionElementType("completeEffects"), "InstantEffectBlock");
        _blockPrerequisites = block.NestedCollectionCount("prerequisites", "prerequisites");
        _mods = block.CollectionField("effectMods");
        _scripts = block.CollectionField("effectScripts");

        var mod = bind.Elements(_modType, "ScalingWeightEffectMod");
        _scalingWeightId = mod.Through("scalingWeightRef").ReferenceGuid("scalingWeight");

        var script = bind.Elements(_scriptType, "TreasurePoolInstantEffect");
        _treasurePoolId = script.ReferenceGuid("treasurePool");
        _effectTypeName = script.Field<string>("effectType");
        _effectValue = script.Field<double>("effectValue");
        _filterListType = script.NestedEnumField("filterScaling", "listType");
        _filterContentCount = script.NestedCollectionCount("filterScaling", "listContents");

        _unavailable = bind.Failure;
    }

    private static string AbsentType(Type? action, Type? mod, Type? script) =>
        action is null ? "PlotNodeActionSO" : mod is null ? "ScalingWeightEffectMod"
            : "TreasurePoolInstantEffect";

    public string Category => "effect blocks";

    public bool IsAvailable =>
        _actionType is not null && _modType is not null && _scriptType is not null &&
        _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.EffectBlocks;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var actions = _actions?.Invoke();
        if (actions is null)
        {
            return WorldCategoryReport.Missing(
                Category, "the PlotNodeActionSO registry was unreadable");
        }

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (action is null) continue;

            try
            {
                sampled += Read(action, buffer);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                WorldSkipTally.Skip(
                    ref skipped,
                    ref firstFailure,
                    $"reading an action's effects threw: {ex.GetBaseException().Message}");
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(object action, WorldRelationBuffer<WorldEffectBlock> buffer)
    {
        var actionId = _actionId!(action);
        if (actionId == Guid.Empty) return 0;

        var blocks = _blocks!(action);
        var count = blocks?.Count ?? 0;
        var appended = 0;
        for (var index = 0; index < count; index++)
        {
            var block = blocks![index];
            if (block is null) continue;

            var mods = _mods!(block);
            var scripts = _scripts!(block);
            var mod = First(mods);
            var script = First(scripts);
            var scaling = _modType!.IsInstanceOfType(mod) ? _scalingWeightId!(mod!) : Guid.Empty;
            var pool = Guid.Empty;
            var effectTypeName = string.Empty;
            var effectValue = 0d;
            var filterListType = 0;
            var filterContentCount = 0;
            if (_scriptType!.IsInstanceOfType(script))
            {
                pool = _treasurePoolId!(script!);
                effectTypeName = _effectTypeName!(script!) ?? string.Empty;
                effectValue = _effectValue!(script!);
                filterListType = _filterListType!(script!);
                filterContentCount = _filterContentCount!(script!);
            }

            buffer.Append(new WorldEffectBlock(
                actionId,
                index,
                block.GetType().Name,
                _blockPrerequisites!(block),
                mods?.Count ?? 0,
                scripts?.Count ?? 0,
                TypeNameOf(mod),
                TypeNameOf(script),
                scaling,
                pool,
                effectTypeName,
                effectValue,
                filterListType,
                filterContentCount));
            appended++;
        }

        return appended;
    }

    private static object? First(IList? entries) => entries is { Count: > 0 } ? entries[0] : null;

    private static string TypeNameOf(object? entry) => entry?.GetType().Name ?? string.Empty;
}
