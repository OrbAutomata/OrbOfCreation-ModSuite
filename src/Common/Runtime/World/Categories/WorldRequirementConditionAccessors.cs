using System;
using System.Collections;

namespace OrbModding.Common.Runtime.World;

/// <summary>The two stored fields one list-variable class answers its membership from.</summary>
internal sealed class ListVariableAccessors
{
    private readonly Func<object, IList?>? _members;
    private readonly Func<object, bool>? _isStatic;

    private ListVariableAccessors(Func<object, IList?>? members, Func<object, bool>? isStatic)
    {
        _members = members;
        _isStatic = isStatic;
    }

    internal static ListVariableAccessors Bind(Type listType) =>
        new(
            NativeAccessorBinder.CollectionField(listType, "value"),
            NativeAccessorBinder.Field<bool>(listType, "isStatic"));

    internal bool IsBound => _members is not null && _isStatic is not null;

    internal bool IsStatic(object list) => _isStatic!(list);

    internal IList? Members(object list) => _members!(list);
}

/// <summary>
/// The compiled reads for one concrete condition class, and the kind this suite models it as.
/// </summary>
/// <remarks>
/// The three members are declared on the game's <c>BaseCondition&lt;T, TE&gt;</c>, whose <c>item</c>
/// and <c>reqType</c> are its own type parameters — so there is no single closed type to bind
/// against and the accessors are compiled against each concrete subclass instead. The two
/// composites bind their explicit child lists and retain their graph node and operator.
/// </remarks>
internal sealed class ConditionAccessors
{
    private readonly WorldRequirementConditionKind _kind;
    private readonly string _typeName;
    private readonly WorldRequirementNodeKind _nodeKind;
    private readonly WorldRequirementOperator _operator;
    private readonly Func<object, IList?>? _children;
    private readonly Func<object, Guid>? _item;
    private readonly Func<object, int>? _reqType;
    private readonly Func<object, object?>? _value;
    private readonly Func<object, double>? _baseValue;
    private readonly Func<object, int>? _perLevelType;
    private readonly Func<object, BigDouble>? _perLevelAmount;
    private readonly Func<object, int>? _perLevelOrder;
    private readonly Func<object, int>? _modPerLevelType;
    private readonly Func<object, BigDouble>? _modPerLevelAmount;
    private readonly Func<object, int>? _modPerLevelOrder;
    private readonly Func<object, object?>? _itemObject;

    private ConditionAccessors(
        WorldRequirementConditionKind kind,
        string typeName,
        WorldRequirementNodeKind nodeKind,
        WorldRequirementOperator @operator,
        Func<object, IList?>? children,
        Func<object, Guid>? item,
        Func<object, int>? reqType,
        Func<object, object?>? value,
        Func<object, double>? baseValue,
        Func<object, int>? perLevelType,
        Func<object, BigDouble>? perLevelAmount,
        Func<object, int>? perLevelOrder,
        Func<object, int>? modPerLevelType,
        Func<object, BigDouble>? modPerLevelAmount,
        Func<object, int>? modPerLevelOrder,
        Func<object, object?>? itemObject = null)
    {
        _itemObject = itemObject;
        _kind = kind;
        _typeName = typeName;
        _nodeKind = nodeKind;
        _operator = @operator;
        _children = children;
        _item = item;
        _reqType = reqType;
        _value = value;
        _baseValue = baseValue;
        _perLevelType = perLevelType;
        _perLevelAmount = perLevelAmount;
        _perLevelOrder = perLevelOrder;
        _modPerLevelType = modPerLevelType;
        _modPerLevelAmount = modPerLevelAmount;
        _modPerLevelOrder = modPerLevelOrder;
    }

    /// <summary>
    /// The list variable a <c>ListRequirement</c> compares against, as the object rather than as
    /// its identity: the membership behind the fold is only reachable through the instance.
    /// </summary>
    internal object? ReadListVariable(object condition) => _itemObject?.Invoke(condition);

    internal IList? ReadChildren(object condition) => _children?.Invoke(condition);

    internal static ConditionAccessors Bind(Type conditionType)
    {
        var typeName = conditionType.Name;
        var @operator = typeName switch
        {
            "AndRequirement" => WorldRequirementOperator.And,
            "OrRequirement" => WorldRequirementOperator.Or,
            _ => WorldRequirementOperator.None,
        };
        if (@operator != WorldRequirementOperator.None)
        {
            var children = NativeAccessorBinder.CollectionField(
                conditionType,
                @operator == WorldRequirementOperator.And ? "andConditions" : "orConditions");
            return new ConditionAccessors(
                WorldRequirementConditionKind.Unknown,
                typeName,
                children is null ? WorldRequirementNodeKind.Leaf : WorldRequirementNodeKind.Group,
                children is null ? WorldRequirementOperator.None : @operator,
                children,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var kind = Classify(typeName);

        var item = NativeAccessorBinder.ReferenceGuid(conditionType, "item");
        var reqType = NativeAccessorBinder.EnumField(conditionType, "reqType");
        var value = NativeAccessorBinder.Reference(conditionType, "value");
        var thresholdType = conditionType.GetField("value", WorldEntityRequirementReader.Instance)?.FieldType;

        var baseValue = NativeAccessorBinder.Field<double>(thresholdType, "baseValue");
        var perLevelType = NativeAccessorBinder.NestedEnumField(thresholdType, "perLevel", "type");
        var perLevelAmount =
            NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "perLevel", "adjustReal");
        var perLevelOrder = NativeAccessorBinder.NestedField<int>(thresholdType, "perLevel", "order");
        var modPerLevelType =
            NativeAccessorBinder.NestedEnumField(thresholdType, "modPerLevel", "type");
        var modPerLevelAmount =
            NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "modPerLevel", "adjustReal");
        var modPerLevelOrder =
            NativeAccessorBinder.NestedField<int>(thresholdType, "modPerLevel", "order");

        // A list comparison folds over the whole list, so the instance behind the identity is part
        // of this class's binding set rather than an extra the reader reaches for separately.
        var itemObject = kind == WorldRequirementConditionKind.List
            ? NativeAccessorBinder.Reference(conditionType, "item")
            : null;

        // A modelled kind whose members did not bind is not modelled after all. Collapsing the two
        // into one verdict is what keeps every consumer's fail-closed test a single comparison.
        var bound = item is not null && reqType is not null && value is not null &&
            baseValue is not null && perLevelType is not null && perLevelAmount is not null &&
            perLevelOrder is not null && modPerLevelType is not null &&
            modPerLevelAmount is not null && modPerLevelOrder is not null &&
            (kind != WorldRequirementConditionKind.List || itemObject is not null);

        return new ConditionAccessors(
            bound ? kind : WorldRequirementConditionKind.Unknown,
            typeName,
            WorldRequirementNodeKind.Leaf,
            WorldRequirementOperator.None,
            null,
            item,
            reqType,
            value,
            baseValue,
            perLevelType,
            perLevelAmount,
            perLevelOrder,
            modPerLevelType,
            modPerLevelAmount,
            modPerLevelOrder,
            itemObject: itemObject);
    }

    internal WorldEntityRequirement ReadGraph(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        int containerIndex,
        int ordinal,
        int parentOrdinal,
        int depth,
        object condition,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        BigDouble adjustment = default)
    {
        if (_nodeKind == WorldRequirementNodeKind.Group ||
            _kind == WorldRequirementConditionKind.Unknown)
        {
            return new WorldEntityRequirement(
                ownerId, ownerKind, containerIndex, ordinal, parentOrdinal, depth,
                _nodeKind, _operator, WorldRequirementConditionKind.Unknown, _typeName,
                Guid.Empty, -1, 0d, default, default,
                program, thresholdAdjustment: adjustment);
        }

        ReadScaling(condition, out var threshold, out var perLevel, out var modPerLevel);
        return new WorldEntityRequirement(
            ownerId, ownerKind, containerIndex, ordinal, parentOrdinal, depth,
            _nodeKind, _operator, _kind, _typeName, _item!(condition),
            _reqType!(condition), threshold is null ? 0d : _baseValue!(threshold),
            in perLevel, in modPerLevel,
            program, thresholdAdjustment: adjustment);
    }

    private void ReadScaling(
        object condition,
        out object? threshold,
        out WorldRequirementScaling perLevel,
        out WorldRequirementScaling modPerLevel)
    {
        threshold = _value!(condition);
        perLevel = threshold is null
            ? default
            : new WorldRequirementScaling(
                _perLevelType!(threshold), _perLevelAmount!(threshold), _perLevelOrder!(threshold));
        modPerLevel = threshold is null
            ? default
            : new WorldRequirementScaling(
                _modPerLevelType!(threshold), _modPerLevelAmount!(threshold),
                _modPerLevelOrder!(threshold));
    }

    /// <summary>
    /// The condition classes this suite has been audited against, by the name the game gives them.
    /// </summary>
    /// <remarks>
    /// A name rather than a resolved type, because the list the container holds is
    /// <c>[SerializeReference]</c> and its entries are only ever known by what they turn out to be.
    /// Composite classes are bound separately to their child lists. Everything absent from this
    /// switch and that composite branch is unknown by construction, which is the fail-closed
    /// reading.
    /// </remarks>
    private static WorldRequirementConditionKind Classify(string typeName) => typeName switch
    {
        "UpgradeRequirement" => WorldRequirementConditionKind.Upgrade,
        "ResearchRequirement" => WorldRequirementConditionKind.Research,
        "StructureRequirement" => WorldRequirementConditionKind.Structure,
        "SpellRequirement" => WorldRequirementConditionKind.Spell,
        "AlchemyRecipeRequirement" => WorldRequirementConditionKind.AlchemyRecipe,
        "RitualRequirement" => WorldRequirementConditionKind.Ritual,
        "NumberRequirement" => WorldRequirementConditionKind.Number,
        "GenericRequirement" => WorldRequirementConditionKind.Generic,
        "PrerequisiteLinkRequirement" => WorldRequirementConditionKind.PrerequisiteLink,
        "ListRequirement" => WorldRequirementConditionKind.List,
        "ResourceRequirement" => WorldRequirementConditionKind.Resource,
        _ => WorldRequirementConditionKind.Unknown,
    };
}
