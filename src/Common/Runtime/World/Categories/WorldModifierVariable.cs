using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One global modifier as published — a modifier the game keeps in a registry and entities point at
/// by reference rather than owning.
/// </summary>
/// <remarks>
/// <para>
/// This is the registry behind every <c>ValueModifierRef</c>. A structure's <c>costPerQuantity</c>
/// does not hold a modifier; it holds a reference to one of these, and so do the equivalent fields on
/// several other entity kinds. Collecting the registry once means an entity row can carry a modifier
/// identity — a Guid — and the deriver resolves it here, instead of every entity carrying a copy of a
/// value that is shared by construction.
/// </para>
/// <para>
/// The three published fields are the whole of the game's <c>ValueModifier</c> arithmetic:
/// <c>type</c> selects which operation applies, <c>adjustReal</c> is its magnitude, and <c>order</c>
/// decides which modifiers merge with each other before any of them is applied. See
/// <see cref="GameValueModifier"/>, which they map onto one-for-one.
/// </para>
/// </remarks>
internal readonly struct WorldModifierVariable : IWorldEntity
{
    internal WorldModifierVariable(Guid variableId, int modifierType, BigDouble amount, int order)
    {
        VariableId = variableId;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
    }

    internal Guid VariableId { get; }

    public Guid EntityId => VariableId;

    /// <summary>
    /// The game's <c>ValueModifierType</c> as its underlying integer, never as a copied enum — see
    /// <see cref="NativeAccessorBinder.EnumField"/> for why the suite does not mirror game enums.
    /// </summary>
    internal int ModifierType { get; }

    /// <summary>The modifier's magnitude — the original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    internal int Order { get; }
}

/// <summary>The global modifier registry: <c>ValueModifierVariable.All</c>.</summary>
internal sealed class WorldModifierVariableBinder : WorldPlainBinder<WorldModifierVariable>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _modifierType;
    private Func<object, BigDouble>? _amount;
    private Func<object, int>? _order;

    internal override string Category => "modifier variables";

    internal override string TypeName => "ValueModifierVariable";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();

        // value is a ValueModifier struct held inline, so these are nested field reads rather than a
        // second entity. GetValue() would return the same field, but reading it directly keeps the
        // rule that collection never calls an accessor it has not proven to be a plain read.
        _modifierType = bind.NestedEnumField("value", "type");
        _amount = bind.NestedField<BigDouble>("value", "adjustReal");
        _order = bind.NestedField<int>("value", "order");
        return bind.Failure;
    }

    internal override WorldModifierVariable Read(object entity) =>
        new(_id!(entity), _modifierType!(entity), _amount!(entity), _order!(entity));
}
