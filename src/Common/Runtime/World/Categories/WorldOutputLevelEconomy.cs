using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldOutputLevelSample
{
    internal WorldOutputLevelSample(int slotIndex, Guid instanceId, int baseEffectLevel,
        BigDouble recipeCostPercent, BigDouble instanceCostPercent, bool requirementsMet,
        PublicationTable<GameResourceCost> baseCosts)
    {
        SlotIndex = slotIndex; InstanceId = instanceId; BaseEffectLevel = baseEffectLevel;
        RecipeCostPercent = recipeCostPercent; InstanceCostPercent = instanceCostPercent;
        RequirementsMet = requirementsMet; BaseCosts = baseCosts;
    }
    internal int SlotIndex { get; }
    internal Guid InstanceId { get; }
    internal int BaseEffectLevel { get; }
    internal BigDouble RecipeCostPercent { get; }
    internal BigDouble InstanceCostPercent { get; }
    internal bool RequirementsMet { get; }
    internal PublicationTable<GameResourceCost> BaseCosts { get; }
}

/// <summary>The exact operands of Spell.ComputeCost, captured without changing Output Lv.</summary>
internal sealed class WorldOutputLevelInputs
{
    private readonly string _reason;
    private readonly PublicationTable<WorldOutputLevelSample> _samples;
    private readonly PublicationTable<WorldModifierProgram> _programs;
    private readonly PublicationTable<WorldModifierProgramEntry> _entries;
    private readonly BigDouble _playerCostPercent;
    private readonly GameValueModifier _requirementPenalty;

    internal WorldOutputLevelInputs(string reason, PublicationTable<WorldOutputLevelSample> samples,
        PublicationTable<WorldModifierProgram> programs, PublicationTable<WorldModifierProgramEntry> entries,
        BigDouble playerCostPercent, GameValueModifier requirementPenalty)
    {
        _reason = reason; _samples = samples; _programs = programs; _entries = entries;
        _playerCostPercent = playerCostPercent; _requirementPenalty = requirementPenalty;
    }

    internal static WorldOutputLevelInputs Missing(string reason) => new(reason,
        PublicationTable<WorldOutputLevelSample>.Empty, PublicationTable<WorldModifierProgram>.Empty,
        PublicationTable<WorldModifierProgramEntry>.Empty, default, default);

    internal WorldLevelDomainQuote Build(int currentLevel, int maximumLevel,
        PublicationTable<WorldSpellTypeResonance> resonances,
        PublicationTable<WorldSpellCost>? nativeCurrent = null)
    {
        if (_reason.Length != 0) return WorldLevelDomainQuote.Unavailable(_reason);
        if (currentLevel < WorldSpellWorkbench.MinimumDialLevel || maximumLevel < currentLevel)
            return WorldLevelDomainQuote.Unavailable("The current Output Lv or its ceiling is unavailable.");
        var current = new List<WorldLevelPrice>();
        var next = new List<WorldLevelPrice>();
        var nextComplete = true;
        var nextReason = string.Empty;
        foreach (var sample in _samples.AsSpan())
        {
            var found = false;
            var resonance = default(WorldSpellTypeResonance);
            foreach (var row in resonances.AsSpan())
                if (row.SlotIndex == sample.SlotIndex) { found = true; resonance = row; break; }
            if (!found) return WorldLevelDomainQuote.Unavailable("A loaded spell's complete type-cost layer is unavailable.");
            if (!Append(sample, currentLevel, resonance.TypeCostPercent, current))
                return WorldLevelDomainQuote.Unavailable("A loaded spell's current output cost program is unavailable.");
            if (currentLevel < maximumLevel && nextComplete &&
                !Append(sample, currentLevel + 1, resonance.TypeCostPercent, next))
            {
                nextComplete = false;
                nextReason = "A loaded spell's next output cost program is unavailable.";
                next.Clear();
            }
        }
        if (_samples.Count > 0 && (nativeCurrent is null || !MatchesNativeCurrent(current, nativeCurrent)))
            return WorldLevelDomainQuote.Unavailable("The game's loaded-spell prices have not settled to the captured Output Lv operands.");
        return new WorldLevelDomainQuote(true, string.Empty, _samples.Count > 0, currentLevel, maximumLevel,
            PublicationTable<WorldLevelPrice>.Create(current.ToArray()), PublicationTable<WorldLevelPrice>.Create(next.ToArray()),
            nextComplete, nextReason);
    }

    private static bool MatchesNativeCurrent(List<WorldLevelPrice> computed,
        PublicationTable<WorldSpellCost> native)
    {
        var expected = new Dictionary<(int Slot, Guid Resource), BigDouble>();
        var actual = new Dictionary<(int Slot, Guid Resource), BigDouble>();
        foreach (var price in computed)
        {
            var key = (price.SlotIndex, price.ResourceId);
            expected.TryGetValue(key, out var amount);
            expected[key] = amount + price.Amount;
        }
        foreach (var price in native.AsSpan())
        {
            if (price.Kind != WorldSpellCostKind.Immediate) continue;
            var key = (price.SlotIndex, price.ResourceId);
            actual.TryGetValue(key, out var amount);
            actual[key] = amount + price.Amount;
        }
        if (expected.Count != actual.Count) return false;
        foreach (var pair in expected)
            if (!actual.TryGetValue(pair.Key, out var amount) || amount != pair.Value) return false;
        return true;
    }

    private bool Append(in WorldOutputLevelSample sample, int outputLevel, BigDouble typeCost,
        List<WorldLevelPrice> destination)
    {
        var level = Math.Max(outputLevel - sample.BaseEffectLevel, 1);
        // Spell.GetCostMod returns 100 * recipe% * instance% * player% * type%; ComputeCost then
        // converts that result to a fraction. Keep the multiplication order of those two methods.
        var costMod = new BigDouble(100) * OrbGameMath.AsPercent(sample.RecipeCostPercent) *
            OrbGameMath.AsPercent(sample.InstanceCostPercent) * OrbGameMath.AsPercent(_playerCostPercent) * typeCost;
        foreach (var cost in sample.BaseCosts.AsSpan())
        {
            var amount = cost.Value;
            if (level != 1 && !WorldModifierProgramMath.TryAdjustScaledList(_programs, _entries,
                sample.InstanceId, WorldModifierProgramRole.SpellLevelingStandard, new BigDouble(level - 1), amount, out amount)) return false;
            amount = OrbGameMath.RoundToTwoSigs(amount * OrbGameMath.AsPercent(costMod));
            if (!sample.RequirementsMet) amount = _requirementPenalty.Adjust(amount);
            if (BigDouble.IsNaN(amount) || BigDouble.IsInfinity(amount) || amount < BigDouble.Zero) return false;
            destination.Add(new WorldLevelPrice(sample.SlotIndex, cost.ResourceId, amount, WorldLevelPriceKind.Immediate));
        }
        return true;
    }
}

/// <summary>
/// Reads the augmented base vector once, then publishes native-free operands. Prospective level
/// transforms and spell-type resonance remain on the shared world worker.
/// </summary>
internal sealed class WorldOutputLevelCapture
{
    internal static readonly string[] ContractIds =
    {
        "level-economy.spell.type", "level-economy.recipe.type", "level-economy.glyph.type",
        "level-economy.cost.type", "level-economy.player.type", "level-economy.global.type",
        "level-economy.record.type", "level-economy.list.type", "level-economy.variable.type",
        "level-economy.modifier.type", "level-economy.spell-reference", "level-economy.spell-glyphs",
        "level-economy.spell-instance-cost", "level-economy.recipe-cost", "level-economy.recipe-base-cost",
        "level-economy.cost-level-list", "level-economy.base-effect-level", "level-economy.augment-cost",
        "level-economy.player-cost", "level-economy.variable-value", "level-economy.global-instance",
        "level-economy.requirement-penalty",
    };
    private readonly Func<object, object>? _recipe;
    private readonly Func<object, object>? _glyphs;
    private readonly Func<object, object>? _instanceCost;
    private readonly Func<object, object>? _recipeCost;
    private readonly Func<object, object>? _baseCost;
    private readonly Func<object, object>? _levelList;
    private readonly Func<object, int>? _baseLevel;
    private readonly Func<object, object, object, int, object>? _augment;
    private readonly Func<object?>? _playerCost;
    private readonly Func<object, object>? _variableValue;
    private readonly Func<object?>? _globals;
    private readonly Func<object, object>? _penalty;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, int>? _modifierOrder;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly NativeModifierRecordAccess? _records;
    private readonly NativeModifierProgramReader? _programReader;
    private readonly List<WorldOutputLevelSample> _samples = new();
    private readonly WorldRelationBuffer<WorldModifierProgram> _programs = new();
    private readonly WorldRelationBuffer<WorldModifierProgramEntry> _entries = new();
    private readonly string _bindingFailure;
    private string _failure = string.Empty;

    internal WorldOutputLevelCapture(Func<string, Type?> resolveType)
    {
        if (!NativeBindingSet.TryBind("output-level economy", ContractIds, resolveType, null, set =>
            {
                var spell = set.Type("level-economy.spell.type", "Spell");
                var recipe = set.Type("level-economy.recipe.type", "SpellRecipeSO");
                var glyph = set.Type("level-economy.glyph.type", "GlyphSO");
                var cost = set.Type("level-economy.cost.type", "ResourceCostList");
                var player = set.Type("level-economy.player.type", "Player");
                var global = set.Type("level-economy.global.type", "GlobalValues");
                var record = set.Type("level-economy.record.type", "ValueModifierRecord");
                var list = set.Type("level-economy.list.type", "ValueModifierList");
                var variable = set.Type("level-economy.variable.type", "DoubleVariable");
                var modifier = set.Type("level-economy.modifier.type", "ValueModifier");
                return new Bindings(
                    set.Call<Func<object, object>>("level-economy.spell-reference", spell, "get_reference", recipe),
                    set.Field<Func<object, object>>("level-economy.spell-glyphs", spell, "augmentGlyphs", typeof(List<>).MakeGenericType(glyph)),
                    set.Field<Func<object, object>>("level-economy.spell-instance-cost", spell, "costMod", record),
                    set.Field<Func<object, object>>("level-economy.recipe-cost", recipe, "spellCostMod", record),
                    set.Field<Func<object, object>>("level-economy.recipe-base-cost", recipe, "baseResourceCost", cost),
                    set.Call<Func<object, object>>("level-economy.cost-level-list", recipe, "GetCostModPerLevel", list),
                    set.Call<Func<object, int>>("level-economy.base-effect-level", spell, "GetBaseEffectLevel", typeof(int)),
                    set.StaticCall<Func<object, object, object, int, object>>("level-economy.augment-cost", glyph, "AugmentCost", cost,
                        cost, typeof(List<>).MakeGenericType(glyph), list, typeof(int)),
                    set.StaticCall<Func<object?>>("level-economy.player-cost", player, "GetSpellCost", variable),
                    set.Field<Func<object, object>>("level-economy.variable-value", variable, "value", record),
                    set.StaticField<Func<object?>>("level-economy.global-instance", global, "instance", global),
                    set.Field<Func<object, object>>("level-economy.requirement-penalty", global, "insufficientReqCostMod", modifier),
                    cost, record, list, modifier);
            }, out Bindings? bound, out _bindingFailure) || bound is null) return;
        _recipe = bound.Recipe; _glyphs = bound.Glyphs; _instanceCost = bound.InstanceCost;
        _recipeCost = bound.RecipeCost; _baseCost = bound.BaseCost; _levelList = bound.LevelList;
        _baseLevel = bound.BaseLevel; _augment = bound.Augment; _playerCost = bound.PlayerCost;
        _variableValue = bound.VariableValue; _globals = bound.Globals; _penalty = bound.Penalty;
        _costEntries = NativeAccessorBinder.CollectionField(bound.CostType, "costs");
        var tuple = NativeAccessorBinder.CollectionElementType(bound.CostType, "costs");
        _resourceId = NativeAccessorBinder.ReferenceGuid(tuple, "resource");
        _costValue = NativeAccessorBinder.Call<BigDouble>(tuple, "GetValue");
        _modifierType = NativeAccessorBinder.EnumField(bound.ModifierType, "type");
        _modifierOrder = NativeAccessorBinder.Field<int>(bound.ModifierType, "order");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(bound.ModifierType, "adjustReal");
        _records = NativeModifierRecordAccess.For(bound.RecordType);
        _programReader = new NativeModifierProgramReader(bound.RecordType, bound.ListType);
        if (_records is null || !_programReader.IsAvailable || _costEntries is null || _resourceId is null ||
            _costValue is null || _modifierType is null || _modifierOrder is null || _modifierAmount is null)
            _bindingFailure = "The complete output price operands could not be bound.";
    }

    internal void Begin()
    {
        _samples.Clear(); _programs.Reset(); _entries.Reset(); _failure = _bindingFailure;
    }

    internal void Capture(object spell, int slot, Guid instanceId, bool requirementsMet)
    {
        if (_failure.Length != 0) return;
        try
        {
            if (instanceId == Guid.Empty) throw new InvalidOperationException("A loaded spell had no stable instance identity.");
            var recipe = _recipe!(spell) ?? throw new InvalidOperationException("A loaded spell recipe was unavailable.");
            var list = _levelList!(recipe) ?? throw new InvalidOperationException("A loaded spell level-cost program was unavailable.");
            var baseLevel = _baseLevel!(spell);
            var cost = _augment!(_baseCost!(recipe), _glyphs!(spell), list, baseLevel);
            var values = _costEntries!(cost) ?? throw new InvalidOperationException("Augmented base costs were unavailable.");
            var rows = new GameResourceCost[values.Count];
            for (var index = 0; index < rows.Length; index++)
            {
                var value = values[index] ?? throw new InvalidOperationException("An augmented cost entry was null.");
                var resource = _resourceId!(value);
                if (resource == Guid.Empty) throw new InvalidOperationException("An augmented cost had no stable resource identity.");
                rows[index] = new GameResourceCost(resource, _costValue!(value));
            }
            _programReader!.CaptureList(instanceId, WorldModifierProgramRole.SpellLevelingStandard, list, _programs, _entries);
            var recipeRecord = _recipeCost!(recipe) ?? throw new InvalidOperationException("A spell recipe cost record was unavailable.");
            var instanceRecord = _instanceCost!(spell) ?? throw new InvalidOperationException("A spell instance cost record was unavailable.");
            _samples.Add(new WorldOutputLevelSample(slot, instanceId, baseLevel, _records!.Fold(recipeRecord),
                _records.Fold(instanceRecord), requirementsMet, PublicationTable<GameResourceCost>.Create(rows)));
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        { _failure = WorldFailures.ReadFault("capturing loaded-spell output prices threw", exception).Text; }
    }

    internal WorldOutputLevelInputs Finish(bool slotsComplete)
    {
        if (!slotsComplete && _failure.Length == 0) _failure = "The equipped spell snapshot was incomplete.";
        var player = BigDouble.Zero;
        var penalty = default(GameValueModifier);
        if (_failure.Length == 0)
        {
            try
            {
                var variable = _playerCost!() ?? throw new InvalidOperationException("Player spell cost is unavailable.");
                var record = _variableValue!(variable) ?? throw new InvalidOperationException("The player spell-cost record was unavailable.");
                player = _records!.Fold(record);
                var global = _globals!() ?? throw new InvalidOperationException("GlobalValues.instance is unavailable.");
                var value = _penalty!(global);
                penalty = new GameValueModifier((GameValueModifierType)_modifierType!(value), _modifierAmount!(value), _modifierOrder!(value));
            }
            catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
            { _failure = WorldFailures.ReadFault("capturing shared output price operands threw", exception).Text; }
        }
        return new WorldOutputLevelInputs(_failure, PublicationTable<WorldOutputLevelSample>.Create(_samples.ToArray()),
            WorldRelationTable.Build(_programs, WorldModifierProgramMath.Order),
            WorldRelationTable.Build(_entries, WorldModifierProgramMath.EntryOrder), player, penalty);
    }

    private sealed record Bindings(Func<object, object> Recipe, Func<object, object> Glyphs,
        Func<object, object> InstanceCost, Func<object, object> RecipeCost, Func<object, object> BaseCost,
        Func<object, object> LevelList, Func<object, int> BaseLevel, Func<object, object, object, int, object> Augment,
        Func<object?> PlayerCost, Func<object, object> VariableValue, Func<object?> Globals, Func<object, object> Penalty,
        Type CostType, Type RecordType, Type ListType, Type ModifierType);
}
