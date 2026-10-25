using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Captures the Druidry variable's authored linear effects, their actual native distributors, and
/// each engaged plot action's operands. No level control or native modifier record is changed.
/// </summary>
internal sealed class WorldDruidryLevelCapture
{
    private readonly Type? _scriptType, _weightModType, _valueRecordType, _orderedRecordType, _actionType, _instanceType, _plotType;
    private readonly Func<object?>? _player;
    private readonly Func<object, object?>? _dial;
    private readonly Func<object, IList?>? _blocks, _scripts, _mods, _listModifiers, _listExponents;
    private readonly Func<object, int>? _prerequisites, _modifierType, _modifierOrder;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly Func<object, object?>? _weightReference, _weightAsset, _weightConversion, _weightValues;
    private readonly Func<object, bool>? _useTarget;
    private readonly Func<object, string?>? _property;
    private readonly Func<object, object?>? _scriptTarget, _scriptModifier, _scriptRecord;
    private readonly Func<object, Guid>? _modifierGuid;
    private readonly Func<object, object?>? _orderGuids;
    private readonly Func<object, IList?>? _targets;
    private readonly Func<object, object?>? _action, _resourceDrain, _authoredDrain, _currentDrain;
    private readonly Func<object, bool>? _engaged, _empty, _applied, _parallel, _ignoreYield;
    private readonly Func<object, int>? _quantity;
    private readonly Func<object, BigDouble>? _necessaryRatio, _usageRatio;
    private readonly Func<object, object?>?[] _actionRecords, _plotRecords;
    private readonly Func<object, IList?>? _actionTypes, _sizeNodes, _qualityNodes, _sizeMods, _converters;
    private readonly Func<object, bool>? _parentSize, _parentQuality, _useSpace;
    private readonly Func<object, double>? _baseTime;
    private readonly Func<object, object?>? _sizeRecord, _qualityRecord, _spaceUsage;
    private readonly NativeModifierRecordAccess? _fold;
    private readonly Func<object, object?>? _transmuterMap, _transmuterEntryValue, _transmuterList;
    private readonly Func<object, IList?>? _mapEntries;
    private readonly Func<object, int>? _transmuterKind, _extractionType;
    private readonly Func<object, bool>? _invert;
    private readonly Func<object, object?>? _extractTarget, _extractRecord, _extractModifier, _extractListRef, _listVariable, _listValue;
    private readonly Func<object, string?>? _extractProperty;
    private readonly Func<object, BigDouble>? _plainExtract;
    private readonly Func<object, int>? _converterKind;
    private readonly Func<object, double>? _converterMagnitude;
    private readonly Func<object, object?>? _converterModifier;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResourceId;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly Func<object, double>? _rarity;
    private readonly NativeModifierProgramReader _valuePrograms, _plainPrograms;
    private readonly List<NativeSource> _sources = new();
    private readonly List<WorldDruidryLevelSample> _samples = new();
    private string _failure = string.Empty;
    private readonly string _bindingFailure;
    private readonly string _currentBindingFailure;
    private string _forecastFailure = string.Empty;

    internal WorldDruidryLevelCapture(Func<string, Type?> resolve)
    {
        var player = resolve("Player"); var variable = resolve("NumberVariable");
        var block = resolve("PersistentEffectBlock"); var effectBlock = resolve("EffectBlock");
        _scriptType = resolve("UpgradeableObject+UpgradeEffectModifier");
        _weightModType = resolve("ScalingWeightEffectMod");
        var weightRef = resolve("ScalingWeightRef"); var weight = resolve("ScalingWeightSO");
        var conversion = resolve("ScalingConversion"); var modifier = resolve("ValueModifier");
        _valueRecordType = resolve("ValueModifierRecord"); _orderedRecordType = resolve("OrderedMultiplierRecord");
        var plainRecord = resolve("ModifierRecord"); var list = resolve("ValueModifierList");
        var action = _actionType = resolve("PlotNodeActionSO"); var instance = _instanceType = resolve("PlotNodeActionInstance");
        var plot = _plotType = resolve("PlotNodeSO"); var drain = resolve("ResourceDrain");
        var cost = resolve("ResourceCostList"); var resource = resolve("ResourceSO");
        var extractor = resolve("UpgradeableObject+ValueExtractor");
        var transmuter = resolve("UpgradeableObject+ValueTransmuter");
        var transmuterMap = resolve("UpgradeableObject+ValueTransmuterMap");
        var transmuterList = resolve("UpgradeableObject+ValueTransmuterList");
        var converter = resolve("PlotNodeActionSO+SizeConverter");
        _player = NativeAccessorBinder.StaticReference(player, "_instance", player);
        _dial = NativeAccessorBinder.Reference(player, "druidryOutputLevel", resolve("IntVariable"));
        _blocks = NativeAccessorBinder.CollectionField(variable, "persistentEffectBlocks");
        _scripts = NativeAccessorBinder.CollectionField(block, "effectScripts");
        _mods = NativeAccessorBinder.CollectionField(effectBlock, "effectMods");
        _prerequisites = NativeAccessorBinder.NestedCollectionCount(effectBlock, "prerequisites", "prerequisites");
        _weightReference = NativeAccessorBinder.Reference(_weightModType, "scalingWeightRef", weightRef);
        _weightAsset = NativeAccessorBinder.Reference(weightRef, "scalingWeight", weight);
        _weightConversion = NativeAccessorBinder.Reference(weight, "scalingEntries", conversion);
        _weightValues = NativeAccessorBinder.Reference(conversion, "values");
        _listModifiers = NativeAccessorBinder.CollectionField(list, "modifiers");
        _listExponents = NativeAccessorBinder.CollectionField(list, "exponents");
        _modifierType = NativeAccessorBinder.EnumField(modifier, "type");
        _modifierOrder = NativeAccessorBinder.Field<int>(modifier, "order");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(modifier, "adjustReal");
        _modifierGuid = NativeAccessorBinder.NestedField<Guid>(modifier, "gc", "_guid");
        _useTarget = NativeAccessorBinder.Field<bool>(_scriptType, "useTargetRef");
        _property = NativeAccessorBinder.Field<string?>(_scriptType, "propertyType");
        _scriptTarget = NativeAccessorBinder.Reference(_scriptType, "upgradeableObject");
        _scriptModifier = NativeAccessorBinder.BoxedField(_scriptType, "modifier");
        _scriptRecord = NativeAccessorBinder.CallObject(_scriptType, "GetRecord", plainRecord);
        _orderGuids = NativeAccessorBinder.Reference(_orderedRecordType, "orderGuid");
        _targets = NativeAccessorBinder.CollectionField(_orderedRecordType, "records");
        _action = NativeAccessorBinder.CallObject(instance, "GetAction", action);
        _engaged = NativeAccessorBinder.Call<bool>(instance, "IsEngaged");
        _empty = NativeAccessorBinder.Call<bool>(instance, "IsEmpty");
        _quantity = NativeAccessorBinder.Field<int>(instance, "quantity");
        _resourceDrain = NativeAccessorBinder.Reference(instance, "resourceDrain", drain);
        _authoredDrain = NativeAccessorBinder.Reference(action, "actionDrain", cost);
        _currentDrain = NativeAccessorBinder.CallObject(drain, "GetCurrentDrain", cost);
        _applied = NativeAccessorBinder.Field<bool>(drain, "isDrainApplied");
        _necessaryRatio = NativeAccessorBinder.Field<BigDouble>(drain, "necessaryRatio");
        _usageRatio = NativeAccessorBinder.Field<BigDouble>(drain, "usageRatio");
        _parallel = NativeAccessorBinder.Field<bool>(action, "parallelAction");
        _ignoreYield = NativeAccessorBinder.Field<bool>(action, "ignoreNodeYield");
        _actionRecords = new[] { NativeAccessorBinder.Reference(action, "power", _valueRecordType),
            NativeAccessorBinder.Reference(action, "speed", _valueRecordType), NativeAccessorBinder.Reference(action, "costMod", _valueRecordType),
            NativeAccessorBinder.Reference(action, "growthSizeMod", _valueRecordType) }!;
        _plotRecords = new[] { NativeAccessorBinder.Reference(plot, "yieldMod", _valueRecordType),
            NativeAccessorBinder.Reference(plot, "specialMod", _valueRecordType), NativeAccessorBinder.Reference(plot, "actionSpeed", _valueRecordType),
            NativeAccessorBinder.Reference(plot, "actionCostMod", _valueRecordType) }!;
        _actionTypes = NativeAccessorBinder.CollectionField(action, "actionTypes");
        _sizeNodes = NativeAccessorBinder.CollectionField(action, "sizeModNodes");
        _qualityNodes = NativeAccessorBinder.CollectionField(action, "qualityModNodes");
        _sizeMods = NativeAccessorBinder.CollectionField(plot, "sizeMods");
        _sizeRecord = NativeAccessorBinder.Reference(plot, "sizeMod", _valueRecordType);
        _qualityRecord = NativeAccessorBinder.Reference(plot, "qualityMod", _valueRecordType);
        _parentSize = NativeAccessorBinder.Field<bool>(action, "useParentSize");
        _parentQuality = NativeAccessorBinder.Field<bool>(action, "useParentQuality");
        _useSpace = NativeAccessorBinder.Field<bool>(action, "useSpaceUsageForTimeMult");
        _baseTime = NativeAccessorBinder.Field<double>(action, "baseTime");
        _spaceUsage = NativeAccessorBinder.Reference(plot, "spaceUsageCost", cost);
        _fold = NativeModifierRecordAccess.For(_valueRecordType);
        _transmuterMap = NativeAccessorBinder.Reference(action, "valueTransmuterMap", transmuterMap);
        _mapEntries = NativeAccessorBinder.CollectionField(transmuterMap, "valueList");
        var transmuterEntry = NativeAccessorBinder.CollectionElementType(transmuterMap, "valueList");
        _transmuterKind = NativeAccessorBinder.EnumField(transmuterEntry, "t");
        _transmuterEntryValue = NativeAccessorBinder.Reference(transmuterEntry, "v", transmuterList);
        _transmuterList = NativeAccessorBinder.Reference(transmuterList, "transmuters");
        _extractTarget = NativeAccessorBinder.Reference(extractor, "upgradeableObject");
        _extractProperty = NativeAccessorBinder.Field<string?>(extractor, "propertyType");
        _extractRecord = NativeAccessorBinder.CallObject(extractor, "GetRecord", plainRecord);
        _plainExtract = NativeAccessorBinder.Call<BigDouble>(extractor, "GetValue");
        _extractionType = NativeAccessorBinder.EnumField(transmuter, "extractionType");
        _invert = NativeAccessorBinder.Field<bool>(transmuter, "invertExtraction");
        _extractModifier = NativeAccessorBinder.BoxedField(transmuter, "extractionMod");
        _extractListRef = NativeAccessorBinder.Reference(transmuter, "extractionModList", resolve("ModifierListRef"));
        _listVariable = NativeAccessorBinder.Reference(resolve("ModifierListRef"), "variable", resolve("ModifierListVariable"));
        _listValue = NativeAccessorBinder.Reference(resolve("ModifierListVariable"), "value", list);
        _converters = NativeAccessorBinder.CollectionField(action, "sizeConverters");
        _converterKind = NativeAccessorBinder.EnumField(converter, "scalingType");
        _converterMagnitude = NativeAccessorBinder.Field<double>(converter, "magRatio");
        _converterModifier = NativeAccessorBinder.BoxedField(converter, "modifier");
        _costEntries = NativeAccessorBinder.CollectionField(cost, "costs");
        var tuple = NativeAccessorBinder.CollectionElementType(cost, "costs");
        _costResourceId = NativeAccessorBinder.ReferenceGuid(tuple, "resource");
        _costResource = NativeAccessorBinder.Reference(tuple, "resource", resource);
        _costAmount = NativeAccessorBinder.Call<BigDouble>(tuple, "GetValue");
        _rarity = NativeAccessorBinder.Field<double>(resource, "rarityValue");
        _valuePrograms = new NativeModifierProgramReader(_valueRecordType, list);
        _plainPrograms = new NativeModifierProgramReader(plainRecord, list);
        _currentBindingFailure = CurrentBound() ? string.Empty : "The actual Agromancy drain capture contracts are unavailable.";
        _bindingFailure = Bound() ? string.Empty : "The complete Agromancy prospective-level capture contracts are unavailable.";
    }

    internal void Begin()
    {
        _samples.Clear(); _sources.Clear(); _failure = _currentBindingFailure; _forecastFailure = _bindingFailure;
        if (_failure.Length != 0 || _forecastFailure.Length != 0) return;
        try { ReadSources(); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        { _forecastFailure = WorldFailures.ReadFault("capturing authored Druidry effects threw", exception).Text; _sources.Clear(); }
    }

    internal void Capture(object plot, object instance)
    {
        if (_failure.Length != 0) return;
        object drain;
        PublicationTable<GameResourceCost> current;
        BigDouble necessary, usage;
        try
        {
            if (instance.GetType() != _instanceType || plot.GetType() != _plotType)
                throw new InvalidOperationException("the plot action has an unaudited native runtime type");
            var native = _resourceDrain!(instance);
            var engaged = _engaged!(instance) && !_empty!(instance) && _quantity!(instance) > 0;
            if (!engaged)
            {
                if (native is not null && _applied!(native)) throw new InvalidOperationException("an inactive plot action still has applied drain");
                return;
            }
            drain = native ?? throw new InvalidOperationException("an engaged plot action has no drain state");
            var applied = _applied!(drain);
            necessary = applied ? _necessaryRatio!(drain) : BigDouble.Zero;
            usage = applied ? _usageRatio!(drain) : BigDouble.Zero;
            if (!Unit(necessary) || !Unit(usage)) throw new InvalidOperationException("an Agromancy drain utilization ratio is invalid");
            current = Costs(_currentDrain!(drain));
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        { _failure = WorldFailures.ReadFault("capturing an engaged Agromancy action's actual drain threw", exception).Text; return; }
        var index = _samples.Count;
        _samples.Add(WorldDruidryLevelSample.Actual(current, BigDouble.Min(necessary, usage),
            _forecastFailure.Length != 0 ? _forecastFailure : "The prospective Agromancy program is unavailable."));
        if (_forecastFailure.Length != 0) return;
        try
        {
            var action = _action!(instance) ?? throw new InvalidOperationException("an engaged plot action has no action");
            if (action.GetType() != _actionType) throw new InvalidOperationException("the action scaling override is unaudited");
            var actionRecords = _actionRecords.Select(get => get!(action) ?? throw new InvalidOperationException("an action scaling record is unavailable")).ToArray();
            var declaredTypes = _actionTypes!(action) ?? throw new InvalidOperationException("the action type list is unavailable");
            foreach (var source in _sources)
            {
                var applies = declaredTypes.Cast<object>().Any(type => ReferenceEquals(type, source.Target));
                if (!applies) continue;
                var targetIndex = source.Property switch { "Speed" => 1, "Cost" => 2, "GrowthSizeMod" => 3, _ => -1 };
                if (targetIndex < 0 || !Contains(source.Targets, actionRecords[targetIndex]))
                    throw new InvalidOperationException("an authored Druidry type effect has not registered its action record");
            }
            var actionValues = new[] { new DruidryScalingRecord(2, Snapshot(actionRecords[0])),
                new DruidryScalingRecord(6, Snapshot(actionRecords[1])), new DruidryScalingRecord(4, Snapshot(actionRecords[2])),
                new DruidryScalingRecord(5, Snapshot(actionRecords[2])), new DruidryScalingRecord(9, Snapshot(actionRecords[3])) };
            var plotValues = new List<DruidryScalingRecord>();
            if (!_ignoreYield!(action)) plotValues.Add(new DruidryScalingRecord(2, Snapshot(_plotRecords[0]!(plot)!)));
            plotValues.Add(new DruidryScalingRecord(8, Snapshot(_plotRecords[1]!(plot)!)));
            plotValues.Add(new DruidryScalingRecord(6, Snapshot(_plotRecords[2]!(plot)!)));
            plotValues.Add(new DruidryScalingRecord(4, Snapshot(_plotRecords[3]!(plot)!)));
            plotValues.Add(new DruidryScalingRecord(5, Snapshot(_plotRecords[3]!(plot)!)));
            var timing = CaptureTiming(action, plot);
            var sample = new WorldDruidryLevelSample(PublicationTable<DruidryScalingRecord>.Create(actionValues),
                PublicationTable<DruidryScalingRecord>.Create(plotValues.ToArray()), Transmuters(action), Converters(action),
                Costs(_authoredDrain!(action)), Costs(_currentDrain!(drain)), timing,
                _parallel!(action) ? new BigDouble(_quantity!(instance)) : BigDouble.One, BigDouble.Min(necessary, usage));
            _samples[index] = sample;
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            _samples[index] = WorldDruidryLevelSample.Actual(current, BigDouble.Min(necessary, usage),
                WorldFailures.ReadFault("capturing an engaged Agromancy action's prospective drain program threw", exception).Text);
        }
    }

    internal WorldDruidryLevelInputs Finish(bool plotsComplete)
    {
        var result = new WorldDruidryLevelInputs(
            _failure.Length > 0 ? _failure : plotsComplete ? string.Empty : "The plot action snapshot was incomplete.",
            PublicationTable<WorldDruidryLevelSample>.Create(_samples.ToArray()));
        _sources.Clear(); // Every record and target retained after capture is native-free.
        return result;
    }

    private void ReadSources()
    {
        var player = _player!() ?? throw new InvalidOperationException("Player is unavailable");
        var dial = _dial!(player) ?? throw new InvalidOperationException("Druidry Lv is unavailable");
        var blocks = _blocks!(dial) ?? throw new InvalidOperationException("the Druidry effect graph is unavailable");
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index] ?? throw new InvalidOperationException("a Druidry effect block is null");
            if (_prerequisites!(block) != 0) throw new InvalidOperationException("conditional Druidry level effects need a complete prospective prerequisite verdict");
            var mods = _mods!(block) ?? throw new InvalidOperationException("Druidry effect scaling is unavailable");
            if (mods.Count != 1 || mods[0]?.GetType() != _weightModType || !LinearWeight(mods[0]!))
                throw new InvalidOperationException("the Druidry effect block does not use the audited LinearLevelEffect program");
            var scripts = _scripts!(block) ?? throw new InvalidOperationException("Druidry effect scripts are unavailable");
            for (var scriptIndex = 0; scriptIndex < scripts.Count; scriptIndex++)
            {
                var script = scripts[scriptIndex];
                if (script is null || script.GetType() != _scriptType || _useTarget!(script))
                    throw new InvalidOperationException("an unknown or targeted Druidry effect cannot be projected");
                var property = _property!(script);
                if (property is not ("Speed" or "Cost" or "GrowthSizeMod"))
                    throw new InvalidOperationException("a Druidry effect names an unsupported property");
                var target = _scriptTarget!(script) ?? throw new InvalidOperationException("a Druidry effect target is unavailable");
                var record = _scriptRecord!(script) ?? throw new InvalidOperationException("a Druidry effect record is unavailable");
                if (record.GetType() != _orderedRecordType) throw new InvalidOperationException("a Druidry effect target is not an ordered multiplier distributor");
                var source = _sources.FirstOrDefault(row => ReferenceEquals(row.Record, record));
                if (source is null)
                {
                    source = new NativeSource(record, target, property, _targets!(record) ?? throw new InvalidOperationException("Druidry distribution targets are unavailable"));
                    _sources.Add(source);
                }
                var modifier = _scriptModifier!(script) ?? throw new InvalidOperationException("an authored Druidry modifier is unavailable");
                var id = _modifierGuid!(modifier);
                if (id == Guid.Empty) throw new InvalidOperationException("an authored Druidry modifier has no stable identity");
                if (source.Effects.Any(effect => effect.ModifierId == id)) throw new InvalidOperationException("an authored Druidry modifier identity is duplicated");
                source.Effects.Add(new DruidryEffect(id, Modifier(modifier)));
            }
        }
        foreach (var source in _sources)
        {
            var entries = new WorldRelationBuffer<WorldModifierProgramEntry>();
            _plainPrograms.CaptureEntries(Guid.Empty, WorldModifierProgramRole.PlotActionForecast, source.Record, entries);
            var keys = _orderGuids!(source.Record) as IDictionary ?? throw new InvalidOperationException("Druidry distributor order identities are unavailable");
            source.Distribution = new DruidryDistribution(WorldRelationTable.Publish(entries),
                PublicationTable<Guid>.Create(keys.Values.Cast<Guid>().ToArray()), PublicationTable<DruidryEffect>.Create(source.Effects.ToArray()));
        }
    }

    private bool LinearWeight(object mod)
    {
        var reference = _weightReference!(mod);
        var asset = reference is null ? null : _weightAsset!(reference);
        var conversion = asset is null ? null : _weightConversion!(asset);
        var entries = conversion is null ? null : _weightValues!(conversion) as IDictionary;
        if (entries is null || entries.Count != 2) return false;
        var seen = 0;
        foreach (DictionaryEntry entry in entries)
        {
            var kind = Convert.ToInt32(entry.Key); var list = entry.Value;
            if (list is null || _listExponents!(list)?.Count != 0) return false;
            var modifiers = _listModifiers!(list);
            if (modifiers is null || modifiers.Count != 1 || modifiers[0] is null) return false;
            var value = Modifier(modifiers[0]!);
            if (value.Type != GameValueModifierType.MultiDiminishing || value.Order != 0) return false;
            if (kind == 0 && value.Amount == new BigDouble(1) && (seen & 1) == 0) seen |= 1;
            else if (kind == 2 && value.Amount == new BigDouble(.01) && (seen & 2) == 0) seen |= 2;
            else return false;
        }
        return seen == 3;
    }

    private DruidryValueRecord Snapshot(object record)
    {
        if (record.GetType() != _valueRecordType) throw new InvalidOperationException("a prospective extraction is not a value record");
        var programs = new WorldRelationBuffer<WorldModifierProgram>();
        var entries = new WorldRelationBuffer<WorldModifierProgramEntry>();
        _valuePrograms.CaptureRecord(Guid.Empty, WorldModifierProgramRole.PlotActionForecast, record, programs, entries);
        var distributions = _sources.Where(source => Contains(source.Targets, record)).Select(source => source.Distribution).ToArray();
        return new DruidryValueRecord(programs[0], WorldRelationTable.Publish(entries), PublicationTable<DruidryDistribution>.Create(distributions));
    }

    private PublicationTable<DruidryTransmuter> Transmuters(object action)
    {
        var map = _transmuterMap!(action) ?? throw new InvalidOperationException("the action transmuter map is unavailable");
        var values = _mapEntries!(map) ?? throw new InvalidOperationException("the ordered action transmuter entries are unavailable");
        var result = new List<DruidryTransmuter>();
        foreach (var entry in values)
        {
            if (entry is null) throw new InvalidOperationException("an action transmuter entry is null");
            var kind = _transmuterKind!(entry);
            var valueList = _transmuterEntryValue!(entry);
            var list = valueList is null ? null : _transmuterList!(valueList) as IList;
            if (list is null) throw new InvalidOperationException("an action transmuter list is unavailable");
            foreach (var value in list)
            {
                if (value is null) throw new InvalidOperationException("an action transmuter is null");
                var empty = _extractTarget!(value) is null;
                DruidryValueRecord? record = null; var plain = BigDouble.Zero;
                if (!empty)
                {
                    if (string.IsNullOrEmpty(_extractProperty!(value))) throw new InvalidOperationException("a transmuter property has not been initialized");
                    var native = _extractRecord!(value);
                    if (native is not null && native.GetType() == _valueRecordType) record = Snapshot(native);
                    else plain = _plainExtract!(value); // ModifierAccessor reads its plainValue in this branch.
                }
                var modifier = _extractModifier!(value) ?? throw new InvalidOperationException("a transmuter modifier is unavailable");
                var reference = _extractListRef!(value); var variable = reference is null ? null : _listVariable!(reference);
                var modifiers = variable is null ? null : _listValue!(variable);
                result.Add(new DruidryTransmuter(kind, _extractionType!(value), _invert!(value), empty,
                    record, plain, Modifier(modifier), ModifierList(modifiers)));
            }
        }
        return PublicationTable<DruidryTransmuter>.Create(result.ToArray());
    }

    private PublicationTable<GamePlotSizeConverter> Converters(object action)
    {
        var values = _converters!(action) ?? throw new InvalidOperationException("the size converter list is unavailable");
        var rows = new List<GamePlotSizeConverter>();
        foreach (var converter in values)
        {
            if (converter is null) throw new InvalidOperationException("a size converter is null");
            rows.Add(new GamePlotSizeConverter(_converterKind!(converter), _converterMagnitude!(converter), Modifier(_converterModifier!(converter)!)));
        }
        return PublicationTable<GamePlotSizeConverter>.Create(rows.ToArray());
    }

    private DruidryTiming CaptureTiming(object action, object plot)
    {
        var sizes = _sizeNodes!(action) ?? throw new InvalidOperationException("action size inputs are unavailable");
        var qualities = _qualityNodes!(action) ?? throw new InvalidOperationException("action quality inputs are unavailable");
        var factors = new List<DruidryQuickFactor>();
        foreach (var node in sizes) factors.Add(Size(node ?? throw new InvalidOperationException("a size input is null")));
        foreach (var node in qualities)
            factors.Add(new DruidryQuickFactor(TimingValue(_qualityRecord!(node!)), new BigDouble(100)));
        var parentSize = _parentSize!(action) ? Size(plot) : new DruidryQuickFactor(new BigDouble(100), new BigDouble(100));
        var parentQuality = _parentQuality!(action) ? TimingValue(_qualityRecord!(plot)) : new BigDouble(100);
        var space = new List<DruidryRarityPrice>();
        if (_useSpace!(action))
        {
            var cost = _spaceUsage!(plot) ?? throw new InvalidOperationException("the plot's space usage price is unavailable");
            var entries = _costEntries!(cost) ?? throw new InvalidOperationException("space usage price entries are unavailable");
            foreach (var entry in entries)
            {
                if (entry is null) throw new InvalidOperationException("a space usage entry is null");
                var resource = _costResource!(entry);
                space.Add(new DruidryRarityPrice(resource is null ? 0d : _rarity!(resource), _costAmount!(entry)));
            }
        }
        return new DruidryTiming(PublicationTable<DruidryQuickFactor>.Create(factors.ToArray()), parentSize, parentQuality,
            _baseTime!(action), _useSpace!(action), PublicationTable<DruidryRarityPrice>.Create(space.ToArray()));
    }

    private DruidryQuickFactor Size(object plot)
    {
        var sizes = _sizeMods!(plot) ?? throw new InvalidOperationException("a plot's size queue is unavailable");
        return new DruidryQuickFactor(TimingValue(_sizeRecord!(plot)), sizes.Count == 0 ? new BigDouble(100) : (BigDouble)sizes[0]!);
    }
    private BigDouble TimingValue(object? record)
    {
        if (record is null) throw new InvalidOperationException("a plot timing record is unavailable");
        if (_sources.Any(source => Contains(source.Targets, record)))
            throw new InvalidOperationException("a Druidry effect changes a plot size or quality timing record without a complete prospective timing program");
        return _fold!.Fold(record);
    }

    private PublicationTable<GameResourceCost> Costs(object? cost)
    {
        if (cost is null) throw new InvalidOperationException("a drain cost list is unavailable");
        var entries = _costEntries!(cost) ?? throw new InvalidOperationException("drain entries are unavailable");
        var rows = new List<GameResourceCost>();
        foreach (var entry in entries)
        {
            if (entry is null) throw new InvalidOperationException("a drain resource entry is null");
            var id = _costResourceId!(entry);
            if (id == Guid.Empty) throw new InvalidOperationException("a drain resource has no stable identity");
            rows.Add(new GameResourceCost(id, _costAmount!(entry)));
        }
        return PublicationTable<GameResourceCost>.Create(rows.ToArray());
    }
    private GamePlotModifierList ModifierList(object? list)
    {
        if (list is null) return GamePlotModifierListDefaults.Empty;
        return new GamePlotModifierList(Modifiers(_listModifiers!(list)), Modifiers(_listExponents!(list)));
    }
    private PublicationTable<GameValueModifier> Modifiers(IList? values)
    {
        if (values is null) throw new InvalidOperationException("a modifier list is unavailable");
        var result = new List<GameValueModifier>();
        foreach (var value in values) result.Add(Modifier(value ?? throw new InvalidOperationException("a modifier list entry is null")));
        return PublicationTable<GameValueModifier>.Create(result.ToArray());
    }
    private GameValueModifier Modifier(object value)
    {
        var kind = _modifierType!(value); var amount = _modifierAmount!(value);
        if (kind < 0 || kind > 4 || BigDouble.IsNaN(amount) || BigDouble.IsInfinity(amount))
            throw new InvalidOperationException("a Druidry forecast operand has an unknown or non-finite modifier");
        return new GameValueModifier((GameValueModifierType)kind, amount, _modifierOrder!(value));
    }
    private static bool Contains(IList values, object value) => values.Cast<object>().Any(item => ReferenceEquals(item, value));
    private static bool Unit(BigDouble value) => !BigDouble.IsNaN(value) && !BigDouble.IsInfinity(value) && value >= 0 && value <= 1;
    private bool CurrentBound() => _engaged is not null && _empty is not null && _quantity is not null &&
        _resourceDrain is not null && _applied is not null && _necessaryRatio is not null && _usageRatio is not null &&
        _currentDrain is not null && _costEntries is not null && _costResourceId is not null && _costAmount is not null;
    private bool Bound() => _scriptType is not null && _weightModType is not null && _valueRecordType is not null && _orderedRecordType is not null &&
        _player is not null && _dial is not null && _blocks is not null && _scripts is not null && _mods is not null && _prerequisites is not null &&
        _weightReference is not null && _weightAsset is not null && _weightConversion is not null && _weightValues is not null && _listModifiers is not null && _listExponents is not null && _modifierType is not null && _modifierOrder is not null &&
        _modifierAmount is not null && _modifierGuid is not null && _useTarget is not null && _property is not null && _scriptTarget is not null &&
        _scriptModifier is not null && _scriptRecord is not null && _orderGuids is not null && _targets is not null && _action is not null && _engaged is not null &&
        _empty is not null && _quantity is not null && _resourceDrain is not null && _authoredDrain is not null && _currentDrain is not null && _applied is not null &&
        _necessaryRatio is not null && _usageRatio is not null && _parallel is not null && _ignoreYield is not null && _actionRecords.All(get => get is not null) &&
        _plotRecords.All(get => get is not null) && _actionTypes is not null && _sizeNodes is not null && _qualityNodes is not null && _sizeMods is not null &&
        _sizeRecord is not null && _qualityRecord is not null && _parentSize is not null && _parentQuality is not null && _baseTime is not null && _useSpace is not null &&
        _spaceUsage is not null && _fold is not null && _transmuterMap is not null && _mapEntries is not null && _transmuterKind is not null && _transmuterEntryValue is not null && _transmuterList is not null && _extractTarget is not null &&
        _extractProperty is not null && _extractRecord is not null && _plainExtract is not null && _extractionType is not null && _invert is not null &&
        _extractModifier is not null && _extractListRef is not null && _listVariable is not null && _listValue is not null && _converters is not null &&
        _converterKind is not null && _converterMagnitude is not null && _converterModifier is not null && _costEntries is not null && _costResourceId is not null &&
        _costResource is not null && _costAmount is not null && _rarity is not null && _valuePrograms.IsAvailable && _plainPrograms.CanCaptureEntries;

    private sealed class NativeSource
    {
        internal NativeSource(object record, object target, string property, IList targets)
        { Record = record; Target = target; Property = property; Targets = targets; }
        internal object Record { get; }
        internal object Target { get; }
        internal string Property { get; }
        internal IList Targets { get; }
        internal List<DruidryEffect> Effects { get; } = new();
        internal DruidryDistribution Distribution { get; set; }
    }
}
