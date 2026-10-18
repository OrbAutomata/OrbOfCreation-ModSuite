using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum WorldSpellAuthoredCostKind
{
    Immediate = 0,
    Usage = 1,
    HoldDrain = 2,
}

internal enum WorldSpellRelationKind
{
    SpellType = 0,
    CoreGlyph = 1,
    RecipeBook = 2,

    /// <summary>
    /// <c>SpellRecipeSO.notSpellTypes</c>. The name reads as an exclusion and is not one:
    /// <c>Spell.GetAllSpellTypes()</c> is <c>GetNotSpellTypes().Concat(augmentedSpellTypes)</c>, so
    /// these types are part of the set the game resonates over. It is empty on all sixty-five recipes
    /// on the audited build, which is a reading rather than a rule, so it is bound like any other.
    /// </summary>
    NotSpellType = 3,
}

/// <summary>Authored cast/cooldown scalars for one spell recipe.</summary>
internal readonly struct WorldSpellRecipeAuthoring : IWorldEntity
{
    internal WorldSpellRecipeAuthoring(
        Guid recipeId,
        int castType,
        double rechargeDuration,
        double rechargeMultiplier,
        int rechargeProcessorType,
        double maximumChannelBase,
        double repeatInstantEffectRateBase,
        bool combatOnly = false)
    {
        RecipeId = recipeId;
        CastType = castType;
        RechargeDuration = rechargeDuration;
        RechargeMultiplier = rechargeMultiplier;
        RechargeProcessorType = rechargeProcessorType;
        MaximumChannelBase = maximumChannelBase;
        RepeatInstantEffectRateBase = repeatInstantEffectRateBase;
        CombatOnly = combatOnly;
    }

    internal Guid RecipeId { get; }
    public Guid EntityId => RecipeId;
    internal int CastType { get; }
    internal double RechargeDuration { get; }
    internal double RechargeMultiplier { get; }
    internal int RechargeProcessorType { get; }
    internal double MaximumChannelBase { get; }
    internal double RepeatInstantEffectRateBase { get; }

    /// <summary>
    /// Every script the cast runs is a <c>Combat.CombatEffect</c>, so outside a ritual the whole cast
    /// fizzles: <c>BattleManager.RegisterCombatEffects</c> drops combat effects unless
    /// <c>IsInCombat()</c>, and the game's tooltip reads "Fizzled. Not in Combat.". The suite calls
    /// such a spell an attack spell.
    /// </summary>
    internal bool CombatOnly { get; }
}

internal readonly struct WorldSpellAuthoredCost
{
    internal WorldSpellAuthoredCost(
        Guid recipeId, WorldSpellAuthoredCostKind kind, int ordinal, Guid resourceId, BigDouble amount)
    {
        RecipeId = recipeId;
        Kind = kind;
        Ordinal = ordinal;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid RecipeId { get; }
    internal WorldSpellAuthoredCostKind Kind { get; }
    internal int Ordinal { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct WorldSpellRelation
{
    internal WorldSpellRelation(Guid recipeId, WorldSpellRelationKind kind, int ordinal, Guid targetId)
    {
        RecipeId = recipeId;
        Kind = kind;
        Ordinal = ordinal;
        TargetId = targetId;
    }

    internal Guid RecipeId { get; }
    internal WorldSpellRelationKind Kind { get; }
    internal int Ordinal { get; }
    internal Guid TargetId { get; }
}

internal sealed class WorldSpellGraphReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _spellType;
    private readonly Func<IList?>? _spells;
    private readonly Func<object, int>? _castType;
    private readonly Func<object, double>? _rechargeDuration;
    private readonly Func<object, double>? _rechargeMultiplier;
    private readonly Func<object, int>? _rechargeType;
    private readonly Func<object, double>? _maximumChannel;
    private readonly Func<object, double>? _repeatRate;
    private readonly Type? _combatEffectType;
    private readonly Func<object, IList?>? _onCastBlocks;
    private readonly Func<object, IList?>? _onCastScripts;
    private readonly Func<object, IList?>? _toggledBlocks;
    private readonly Func<object, int>? _toggledScriptCount;
    private readonly Func<object, object?>[] _costLists = new Func<object, object?>[3];
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResource;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly Func<object, IList?>[] _relations = new Func<object, IList?>[4];
    private readonly Func<object, Guid>?[] _relationIdentities = new Func<object, Guid>?[4];
    private readonly string _unavailable;

    internal WorldSpellGraphReader(Type? spellType, Type? combatEffectType)
    {
        _spellType = spellType;
        _combatEffectType = combatEffectType;
        if (spellType is null)
        {
            _unavailable = "the SpellRecipeSO type was not found on this build";
            return;
        }

        _spells = NativeAccessorBinder.StaticListAccessor(spellType, "All");
        _castType = NativeAccessorBinder.EnumField(spellType, "castType");
        _rechargeDuration = NativeAccessorBinder.NestedField<double>(spellType, "baseRecharge", "duration");
        _rechargeMultiplier = NativeAccessorBinder.NestedField<double>(spellType, "baseRecharge", "mult");
        _rechargeType = NativeAccessorBinder.NestedEnumField(spellType, "baseRecharge", "type");
        _maximumChannel = NativeAccessorBinder.NestedField<double>(spellType, "maxChannel", "baseValue");
        _repeatRate = NativeAccessorBinder.NestedField<double>(
            spellType, "repeatInstantEffectRate", "baseValue");
        _onCastBlocks = NativeAccessorBinder.CollectionField(spellType, "onCastEffects");
        _onCastScripts = NativeAccessorBinder.CollectionField(
            NativeAccessorBinder.CollectionElementType(spellType, "onCastEffects"), "effectScripts");
        _toggledBlocks = NativeAccessorBinder.CollectionField(spellType, "toggledEffects");
        _toggledScriptCount = NativeAccessorBinder.CollectionCount(
            NativeAccessorBinder.CollectionElementType(spellType, "toggledEffects"), "effectScripts");

        _costLists[0] = NativeAccessorBinder.Reference(spellType, "baseResourceCost")!;
        _costLists[1] = NativeAccessorBinder.Reference(spellType, "baseUsageCost")!;
        _costLists[2] = NativeAccessorBinder.Reference(spellType, "holdDrain")!;
        var costListType = spellType.GetField("baseResourceCost", Instance)?.FieldType;
        var costEntryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _costEntries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _costResource = NativeAccessorBinder.ReferenceGuid(costEntryType, "resource");
        _costAmount = NativeAccessorBinder.Field<BigDouble>(costEntryType, "valueBig");

        _relations[0] = NativeAccessorBinder.CollectionField(spellType, "spellTypes")!;
        _relations[1] = NativeAccessorBinder.CollectionField(spellType, "coreRecipe")!;
        var recipeBooksType = spellType.GetField("recipeBookList", Instance)?.FieldType;
        var recipeBooks = NativeAccessorBinder.Reference(spellType, "recipeBookList");
        var recipeBookEntries = NativeAccessorBinder.CollectionField(recipeBooksType, "recipeBooks");
        _relations[2] = recipeBooks is null || recipeBookEntries is null
            ? null!
            : source =>
            {
                var list = recipeBooks(source);
                return list is null ? null : recipeBookEntries(list);
            };
        _relations[3] = NativeAccessorBinder.CollectionField(spellType, "notSpellTypes")!;
        _relationIdentities[0] = NativeAccessorBinder.StableUuid(
            NativeAccessorBinder.CollectionElementType(spellType, "spellTypes"));
        _relationIdentities[1] = NativeAccessorBinder.StableUuid(
            NativeAccessorBinder.CollectionElementType(spellType, "coreRecipe"));
        _relationIdentities[2] = NativeAccessorBinder.StableUuid(
            NativeAccessorBinder.CollectionElementType(recipeBooksType, "recipeBooks"));
        _relationIdentities[3] = NativeAccessorBinder.StableUuid(
            NativeAccessorBinder.CollectionElementType(spellType, "notSpellTypes"));

        _unavailable = IsBound()
            ? string.Empty
            : "SpellRecipeSO did not expose the authored spell graph on this build";
    }

    public string Category => "spell authored graph";
    public bool IsAvailable => _spellType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.SpellRecipeAuthoring.Reset();
        frame.SpellAuthoredCosts.Reset();
        frame.SpellRelations.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var spells = _spells?.Invoke();
        if (spells is null)
            return WorldCategoryReport.Missing(Category, "the SpellRecipeSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < spells.Count; index++)
        {
            var spell = spells[index];
            if (spell is null || index >= frame.SpellRecipes.Count)
            {
                skipped++;
                if (firstFailure.Length == 0) firstFailure = "spell registry identity snapshot was incomplete";
                continue;
            }

            try
            {
                // Reuse the identity already read by the primary registry reader. Both readers walk
                // SpellRecipeSO.All in its authored order, so this removes the second GetGuid call.
                var recipeId = frame.SpellRecipes[index].EntityId;
                frame.SpellRecipeAuthoring.Append(new WorldSpellRecipeAuthoring(
                    recipeId, _castType!(spell), _rechargeDuration!(spell),
                    _rechargeMultiplier!(spell), _rechargeType!(spell), _maximumChannel!(spell),
                    _repeatRate!(spell), CombatOnly(spell)));

                for (var kind = 0; kind < _costLists.Length; kind++)
                {
                    var list = _costLists[kind](spell);
                    var entries = list is null ? null : _costEntries!(list);
                    for (var ordinal = 0; ordinal < (entries?.Count ?? 0); ordinal++)
                    {
                        var entry = entries![ordinal];
                        if (entry is null) continue;
                        frame.SpellAuthoredCosts.Append(new WorldSpellAuthoredCost(
                            recipeId, (WorldSpellAuthoredCostKind)kind, ordinal,
                            _costResource!(entry), _costAmount!(entry)));
                    }
                }

                for (var kind = 0; kind < _relations.Length; kind++)
                {
                    var entries = _relations[kind](spell);
                    for (var ordinal = 0; ordinal < (entries?.Count ?? 0); ordinal++)
                    {
                        var entry = entries![ordinal];
                        if (entry is null) continue;
                        frame.SpellRelations.Append(new WorldSpellRelation(
                            recipeId, (WorldSpellRelationKind)kind, ordinal,
                            _relationIdentities[kind]!(entry)));
                    }
                }

                sampled++;
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = $"reading a spell graph row threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private bool CombatOnly(object spell)
    {
        var toggled = _toggledBlocks!(spell);
        for (var block = 0; block < (toggled?.Count ?? 0); block++)
        {
            if (toggled![block] is { } owner && _toggledScriptCount!(owner) > 0) return false;
        }

        var combat = 0;
        var blocks = _onCastBlocks!(spell);
        for (var block = 0; block < (blocks?.Count ?? 0); block++)
        {
            var scripts = blocks![block] is { } owner ? _onCastScripts!(owner) : null;
            for (var index = 0; index < (scripts?.Count ?? 0); index++)
            {
                var script = scripts![index];
                if (script is null) continue;
                if (!_combatEffectType!.IsInstanceOfType(script)) return false;
                combat++;
            }
        }

        return combat > 0;
    }

    private bool IsBound() =>
        _combatEffectType is not null && _onCastBlocks is not null && _onCastScripts is not null &&
        _toggledBlocks is not null && _toggledScriptCount is not null &&
        _castType is not null && _rechargeDuration is not null && _rechargeMultiplier is not null &&
        _rechargeType is not null && _maximumChannel is not null && _repeatRate is not null &&
        Array.TrueForAll(_costLists, static one => one is not null) &&
        Array.TrueForAll(_relations, static one => one is not null) &&
        Array.TrueForAll(_relationIdentities, static one => one is not null) &&
        _costEntries is not null && _costResource is not null && _costAmount is not null;
}

/// <summary>
/// Range lookups over the authored spell graph. Every table is sorted by recipe first, so one
/// recipe's rows are contiguous and a reader takes a slice rather than a scan per row.
/// </summary>
internal static class WorldSpellGraphLookup
{
    internal static bool TryFindAuthoring(
        PublicationTable<WorldSpellRecipeAuthoring> table,
        Guid recipeId,
        out WorldSpellRecipeAuthoring authoring)
    {
        var rows = table.AsSpan();
        var start = LowerBound(rows, recipeId, static (in WorldSpellRecipeAuthoring row) => row.RecipeId);
        if (start < rows.Length && rows[start].RecipeId == recipeId)
        {
            authoring = rows[start];
            return true;
        }
        authoring = default;
        return false;
    }

    internal static bool TryFindCosts(
        PublicationTable<WorldSpellAuthoredCost> table,
        Guid recipeId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        start = LowerBound(rows, recipeId, static (in WorldSpellAuthoredCost row) => row.RecipeId);
        count = 0;
        while (start + count < rows.Length && rows[start + count].RecipeId == recipeId) count++;
        return count > 0;
    }

    internal static bool TryFindRelations(
        PublicationTable<WorldSpellRelation> table,
        Guid recipeId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        start = LowerBound(rows, recipeId, static (in WorldSpellRelation row) => row.RecipeId);
        count = 0;
        while (start + count < rows.Length && rows[start + count].RecipeId == recipeId) count++;
        return count > 0;
    }

    private delegate Guid RecipeOf<T>(in T row) where T : struct;

    private static int LowerBound<T>(ReadOnlySpan<T> rows, Guid recipeId, RecipeOf<T> recipeOf)
        where T : struct
    {
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (recipeOf(in rows[middle]).CompareTo(recipeId) < 0) low = middle + 1;
            else high = middle - 1;
        }
        return low;
    }
}

/// <summary>Sorts one relation buffer into its published table. Shared by every relation category.</summary>
internal static class WorldRelationTableDeriver
{
    internal static PublicationTable<T> Build<T>(WorldRelationBuffer<T> buffer, Comparison<T> comparison)
        where T : struct
    {
        if (buffer.Count == 0) return PublicationTable<T>.Empty;
        var rows = new T[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, comparison);
        return PublicationTable<T>.Create(rows, rows.Length);
    }
}
