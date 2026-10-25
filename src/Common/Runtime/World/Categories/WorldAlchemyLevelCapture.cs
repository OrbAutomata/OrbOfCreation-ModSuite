using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Captures only ordinary recipes whose native selected-level variable is the Alchemy dial.</summary>
internal sealed class WorldAlchemyLevelCapture
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type? _instanceType;
    private readonly Type? _recipeType;
    private readonly Func<object, object?>? _instanceRecipe;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, Guid>? _coreId;
    private readonly Func<object, int>? _quantity;
    private readonly Func<object, int>? _queued;
    private readonly Func<object?>? _dial;
    private readonly Func<object, Guid>? _variableId;
    private readonly Func<object, object?>? _core;
    private readonly Func<object, object?>? _selectedVariable;
    private readonly Func<object, BigDouble>? _selectedLevel;
    private readonly Func<object, BigDouble>? _drain;
    private readonly Func<object, BigDouble>? _speed;
    private readonly Func<object, BigDouble>? _free;
    private readonly Func<object, BigDouble>? _overSpeed;
    private readonly Func<object, BigDouble>? _overDrain;
    private readonly Func<object, int>? _advancement;
    private readonly Func<object, object?>? _scaling;
    private readonly Func<object, GameValueModifier>? _requirementSpeed;
    private readonly Func<object, GameValueModifier>? _requirementCost;
    private readonly Func<object, object?>? _completion;
    private readonly Func<object, object?>? _level;
    private readonly Func<object, object?>? _resolveList;
    private readonly Func<object, object?>? _authoredDrain;
    private readonly Func<object, object?>? _bandwidth;
    private readonly Func<object, object?>? _resourceDrain;
    private readonly Func<object, bool>? _applied;
    private readonly Func<object, BigDouble>? _necessaryRatio;
    private readonly Func<object, BigDouble>? _usageRatio;
    private readonly Func<object, object?>? _currentDrain;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, BigDouble>? _amount;
    private readonly NativeModifierProgramReader _programReader;
    private readonly NativeInstanceScalingProgramReader _scalingPrograms;
    private readonly HashSet<Guid> _seenScalings = new();
    private readonly List<WorldAlchemyLevelSample> _samples = new();
    private readonly WorldRelationBuffer<WorldModifierProgram> _programs = new();
    private readonly WorldRelationBuffer<WorldModifierProgramEntry> _entries = new();
    private readonly HashSet<Guid> _seen = new();
    private string _failure = string.Empty;
    private string _nextFailure = string.Empty;

    internal WorldAlchemyLevelCapture(Func<string, Type?> resolve)
    {
        var recipe = resolve("AlchemyRecipeSO");
        var core = resolve("AlchemyTypeSO");
        var instance = _instanceType = resolve("AlchemyInstance");
        _recipeType = recipe;
        _instanceRecipe = NativeAccessorBinder.CallObject(instance, "get_reference", recipe);
        _recipeId = NativeAccessorBinder.StableUuid(recipe);
        _coreId = NativeAccessorBinder.CallReferenceGuid(recipe, "GetCoreType");
        _quantity = NativeAccessorBinder.Field<int>(instance, "quantity");
        _queued = NativeAccessorBinder.Call<int>(instance, "GetQueuedQuantity");
        var player = resolve("Player");
        var integer = resolve("IntVariable");
        var listRef = resolve("ModifierListRef");
        var drain = resolve("ResourceDrain");
        var cost = resolve("ResourceCostList");
        var getDial = player?.GetMethod("GetAlchemyOutputLevel", Static, null, Type.EmptyTypes, null);
        _dial = getDial?.ReturnType == integer ? NativeAccessorBinder.CallStaticValue(getDial) : null;
        _variableId = NativeAccessorBinder.StableUuid(integer);
        _core = NativeAccessorBinder.CallObject(recipe, "GetCoreType", core);
        _selectedVariable = NativeAccessorBinder.Reference(core, "selectedLevel");
        _selectedLevel = NativeAccessorBinder.ModifierRecord(integer, "value");
        _drain = NativeAccessorBinder.ModifierRecord(recipe, "drainCostMod");
        _speed = NativeAccessorBinder.ModifierRecord(recipe, "speed");
        _free = NativeAccessorBinder.ModifierRecord(recipe, "freeUsageSlots");
        _overSpeed = NativeAccessorBinder.ModifierRecord(recipe, "overdriveSpeed");
        _overDrain = NativeAccessorBinder.ModifierRecord(recipe, "overdriveDrainCostMod");
        _advancement = NativeAccessorBinder.Field<int>(recipe, "advancementLevel");
        _scaling = NativeAccessorBinder.Reference(recipe, "instanceScaling");
        _scalingPrograms = new NativeInstanceScalingProgramReader(resolve);
        _requirementSpeed = Penalty(core, "reqSpeedPenalty");
        _requirementCost = Penalty(core, "reqCostPenalty");
        _completion = NativeAccessorBinder.Reference(recipe, "completionCostAdvanceMod");
        _level = NativeAccessorBinder.Reference(recipe, "drainCostLevelMod");
        _resolveList = NativeAccessorBinder.CallObject(listRef, "GetValue", resolve("ValueModifierList"));
        _authoredDrain = NativeAccessorBinder.Reference(recipe, "drainCost");
        _bandwidth = NativeAccessorBinder.Reference(recipe, "bandwidthCost");
        _resourceDrain = NativeAccessorBinder.Reference(instance, "resourceDrain");
        _applied = NativeAccessorBinder.Field<bool>(drain, "isDrainApplied");
        _necessaryRatio = NativeAccessorBinder.Field<BigDouble>(drain, "necessaryRatio");
        _usageRatio = NativeAccessorBinder.Field<BigDouble>(drain, "usageRatio");
        _currentDrain = NativeAccessorBinder.CallObject(drain, "GetCurrentDrain", cost);
        _costEntries = NativeAccessorBinder.CollectionField(cost, "costs");
        var entry = NativeAccessorBinder.CollectionElementType(cost, "costs");
        _resourceId = NativeAccessorBinder.ReferenceGuid(entry, "resource");
        _amount = NativeAccessorBinder.Field<BigDouble>(entry, "valueBig");
        _programReader = new NativeModifierProgramReader(resolve("ValueModifierRecord"), resolve("ValueModifierList"));
    }

    internal void Begin()
    {
        _samples.Clear(); _programs.Reset(); _entries.Reset(); _seen.Clear(); _seenScalings.Clear();
        _failure = CurrentAvailable ? string.Empty : "The ordinary Alchemy current-drain contracts are unavailable.";
        _nextFailure = ForecastAvailable ? string.Empty : "The ordinary Alchemy prospective-level contracts are unavailable.";
    }

    internal void CaptureActive(IList? active)
    {
        try { CaptureActiveCore(active); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            _failure = WorldFailures.ReadFault("reading ordinary Alchemy active level economy threw", exception).Text;
        }
    }

    private void CaptureActiveCore(IList? active)
    {
        if (_failure.Length != 0) return;
        if (active is null) { _failure = "The ordinary Alchemy active list is unreadable."; return; }
        for (var position = 0; position < active.Count; position++)
        {
            var instance = active[position];
            if (instance is null || instance.GetType() != _instanceType)
            { _failure = "An ordinary Alchemy active slot has an unexpected native type."; return; }
            var quantity = _quantity!(instance);
            if (quantity != _queued!(instance))
            { _failure = "An ordinary Alchemy quantity change has not settled."; return; }
            if (quantity <= 0) continue;
            var recipe = _instanceRecipe!(instance);
            if (recipe is null || recipe.GetType() != _recipeType)
            { _failure = "An ordinary Alchemy active slot has no readable recipe."; return; }
            if (!WorldAlchemyLoadoutReader.IsOrdinaryCore(_coreId!(recipe))) continue;
            Capture(_recipeId!(recipe), recipe, instance, position, quantity);
        }
    }

    private void Capture(Guid recipeId, object recipe, object instance, int position, int quantity)
    {
        if (_failure.Length != 0 || quantity <= 0) return;
        try
        {
            var core = _core!(recipe) ?? throw new InvalidOperationException("an ordinary recipe has no core type");
            var selected = _selectedVariable!(core);
            if (selected is null) return;
            var dial = _dial!() ?? throw new InvalidOperationException("the Alchemy dial is unavailable");
            var id = _variableId!(selected);
            var dialId = _variableId!(dial);
            if (id == Guid.Empty || dialId == Guid.Empty)
                throw new InvalidOperationException("an Alchemy level variable has no stable identity");
            if (id != dialId) return;
            if (!_seen.Add(recipeId)) throw new InvalidOperationException("an ordinary recipe has duplicate active entries");
            var resourceDrain = _resourceDrain!(instance) ?? throw new InvalidOperationException("an active ordinary recipe has no drain state");
            var current = _currentDrain!(resourceDrain) ?? throw new InvalidOperationException("an ordinary recipe's current drain is unavailable");
            var applied = _applied!(resourceDrain);
            var ratio = applied ? BigDouble.Min(_necessaryRatio!(resourceDrain), _usageRatio!(resourceDrain)) : BigDouble.Zero;
            var currentCosts = Costs(position, current);
            var sampleIndex = _samples.Count;
            // Current attribution is complete before any optional forecast binding/operand is read.
            _samples.Add(new WorldAlchemyLevelSample(recipeId, position, quantity, 0, 0,
                default, default, default, default, 0, default, default, default, ratio, applied,
                PublicationTable<WorldLevelPrice>.Empty, currentCosts, PublicationTable<WorldLevelPrice>.Empty));
            if (_nextFailure.Length != 0) return;
            try
            {
                var authored = _authoredDrain!(recipe) ?? throw new InvalidOperationException("an ordinary recipe's authored drain is unavailable");
                var scaling = _scaling!(recipe) ?? throw new InvalidOperationException("an ordinary recipe has no instance scaling");
                var scalingBasis = _scalingPrograms.Capture(scaling, _seenScalings, _programs, _entries);
                var bandwidth = _bandwidth!(recipe) ?? throw new InvalidOperationException("an ordinary recipe's bandwidth list is unavailable");
                CaptureList(recipeId, WorldModifierProgramRole.ConceptCompletionCost, _completion!(recipe));
                CaptureList(recipeId, WorldModifierProgramRole.ConceptDrainLevel, _level!(recipe));
                _samples[sampleIndex] = new WorldAlchemyLevelSample(recipeId, position, quantity, _selectedLevel!(selected).ToInt(),
                    _advancement!(recipe), _drain!(recipe), _speed!(recipe), _requirementSpeed!(core), _requirementCost!(core),
                    _free!(recipe).ToInt(), _overSpeed!(recipe), _overDrain!(recipe), scalingBasis,
                    ratio, applied, Costs(position, authored), currentCosts, Costs(position, bandwidth));
            }
            catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
            {
                _nextFailure = WorldFailures.ReadFault("reading ordinary Alchemy prospective level economy threw", exception, recipeId).Text;
            }
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            _failure = WorldFailures.ReadFault("reading ordinary Alchemy current drain threw", exception, recipeId).Text;
        }
    }

    internal WorldAlchemyLevelInputs Finish() => _failure.Length != 0
        ? WorldAlchemyLevelInputs.Unavailable(_failure)
        : new WorldAlchemyLevelInputs(string.Empty, PublicationTable<WorldAlchemyLevelSample>.Create(_samples.ToArray()),
            WorldRelationTable.Build(_programs, WorldModifierProgramMath.Order),
            WorldRelationTable.Build(_entries, WorldModifierProgramMath.EntryOrder), _nextFailure);

    private void CaptureList(Guid owner, WorldModifierProgramRole role, object? reference)
    {
        if (reference is null) throw new InvalidOperationException("an ordinary Alchemy modifier-list reference was null");
        var list = _resolveList!(reference) ?? throw new InvalidOperationException("ModifierListRef.GetValue returned null");
        _programReader.CaptureList(owner, role, list, _programs, _entries);
    }

    private PublicationTable<WorldLevelPrice> Costs(int position, object source)
    {
        var entries = _costEntries!(source) ?? throw new InvalidOperationException("an Alchemy drain cost list was null");
        var rows = new WorldLevelPrice[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i] ?? throw new InvalidOperationException("an Alchemy drain entry was null");
            var id = _resourceId!(entry);
            if (id == Guid.Empty) throw new InvalidOperationException("an Alchemy drain resource has no stable identity");
            rows[i] = new WorldLevelPrice(position, id, _amount!(entry), WorldLevelPriceKind.Drain);
        }
        return PublicationTable<WorldLevelPrice>.Create(rows);
    }

    private static Func<object, GameValueModifier>? Penalty(Type? owner, string field)
    {
        var type = NativeAccessorBinder.NestedEnumField(owner, field, "type");
        var amount = NativeAccessorBinder.NestedField<BigDouble>(owner, field, "adjustReal");
        var order = NativeAccessorBinder.NestedField<int>(owner, field, "order");
        return type is null || amount is null || order is null ? null :
            source => new GameValueModifier((GameValueModifierType)type(source), amount(source), order(source));
    }

    private bool CurrentAvailable => _instanceType is not null && _recipeType is not null &&
        _instanceRecipe is not null && _recipeId is not null && _coreId is not null &&
        _quantity is not null && _queued is not null && _dial is not null && _variableId is not null &&
        _core is not null && _selectedVariable is not null && _resourceDrain is not null &&
        _applied is not null && _necessaryRatio is not null && _usageRatio is not null &&
        _currentDrain is not null && _costEntries is not null && _resourceId is not null && _amount is not null;

    private bool ForecastAvailable => _selectedLevel is not null && _drain is not null && _speed is not null &&
        _free is not null && _overSpeed is not null && _overDrain is not null && _advancement is not null &&
        _scaling is not null && _scalingPrograms.IsAvailable && _requirementSpeed is not null &&
        _requirementCost is not null && _completion is not null && _level is not null && _resolveList is not null &&
        _authoredDrain is not null && _bandwidth is not null && _programReader.IsAvailable;
}
