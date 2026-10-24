using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One entry of an upgrade's per-level cost modifier list, as read.
/// </summary>
/// <remarks>
/// The game's <c>ValueModifierList</c> is two lists — modifiers and exponents — and the exponents are
/// not decoration: they strengthen the modifiers before any of them touches the cost. So the flag
/// travels with the entry rather than the two being flattened into one sequence.
/// </remarks>
internal readonly struct RawLevelCostModifier
{
    internal RawLevelCostModifier(
        Guid entityId,
        int modifierType,
        BigDouble amount,
        int order,
        bool isExponent)
    {
        EntityId = entityId;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
        IsExponent = isExponent;
    }

    internal Guid EntityId { get; }
    internal int ModifierType { get; }
    internal BigDouble Amount { get; }
    internal int Order { get; }
    internal bool IsExponent { get; }
}

/// <summary>
/// Optional per-level ModifierListRef source for the shared purchase-cost reader. Resolution calls
/// the game's GetValue(), a field lookup, then captures modifiers and exponents without folding them.
/// </summary>
internal sealed class WorldPurchaseLevelCostSource
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<object, object?>? _costModPerLevel;
    private readonly Func<object, object?>? _resolveList;
    private readonly Func<object, IList?>? _listModifiers;
    private readonly Func<object, IList?>? _listExponents;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly Func<object, int>? _modifierOrder;

    internal WorldPurchaseLevelCostSource(Type ownerType, string fieldName)
    {
        _costModPerLevel = NativeAccessorBinder.Reference(ownerType, fieldName);
        var refType = ownerType.GetField(fieldName, Instance)?.FieldType;
        var listType = refType?.GetMethod("GetValue", Instance, null, Type.EmptyTypes, null)?.ReturnType;
        _resolveList = NativeAccessorBinder.CallObject(refType, "GetValue", listType);
        _listModifiers = NativeAccessorBinder.CollectionField(listType, "modifiers");
        _listExponents = NativeAccessorBinder.CollectionField(listType, "exponents");

        var modifierType = NativeAccessorBinder.CollectionElementType(listType, "modifiers");
        _modifierType = NativeAccessorBinder.EnumField(modifierType, "type");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(modifierType, "adjustReal");
        _modifierOrder = NativeAccessorBinder.Field<int>(modifierType, "order");

    }

    internal bool IsBound =>
        _costModPerLevel is not null && _resolveList is not null && _listModifiers is not null &&
        _listExponents is not null && _modifierType is not null && _modifierAmount is not null &&
        _modifierOrder is not null;

    internal void Read(
        object upgrade,
        Guid entityId,
        WorldRelationBuffer<RawLevelCostModifier> modifiers)
    {
        var reference = _costModPerLevel!(upgrade);
        if (reference is null) return;

        var list = _resolveList!(reference);
        if (list is null) return;

        Append(_listModifiers!(list), entityId, isExponent: false, modifiers);
        Append(_listExponents!(list), entityId, isExponent: true, modifiers);
    }

    private void Append(
        IList? source,
        Guid entityId,
        bool isExponent,
        WorldRelationBuffer<RawLevelCostModifier> modifiers)
    {
        if (source is null) return;

        for (var index = 0; index < source.Count; index++)
        {
            var modifier = source[index];
            if (modifier is null) continue;

            modifiers.Append(new RawLevelCostModifier(
                entityId,
                _modifierType!(modifier),
                _modifierAmount!(modifier),
                _modifierOrder!(modifier),
                isExponent));
        }
    }

}
