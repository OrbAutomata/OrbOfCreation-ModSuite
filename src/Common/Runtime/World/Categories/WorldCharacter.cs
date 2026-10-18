using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One character as published.</summary>
internal readonly struct WorldCharacter : IWorldEntity
{
    internal WorldCharacter(Guid characterId, bool discovered, double numberSlain,
        bool floats)
    {
        CharacterId = characterId;
        Discovered = discovered;
        NumberSlain = numberSlain;
        Floats = floats;
    }

    internal Guid CharacterId { get; }

    public Guid EntityId => CharacterId;

    internal bool Discovered { get; }

    /// <summary>A double in the game too, not a count — it is large enough to need the range.</summary>
    internal double NumberSlain { get; }

    /// <summary>Whether the character floats rather than walks.</summary>
    internal bool Floats { get; }
}

internal sealed class WorldCharacterBinder : WorldPlainBinder<WorldCharacter>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, double>? _numberSlain;
    private Func<object, bool>? _floats;

    internal override string Category => "characters";

    internal override string TypeName => "CharacterSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _discovered = bind.Field<bool>("discovered");
        _numberSlain = bind.Field<double>("numberSlain");
        _floats = bind.Field<bool>("floats");
        return bind.Failure;
    }

    internal override WorldCharacter Read(object entity) =>
        new(_id!(entity), _discovered!(entity), _numberSlain!(entity),
            _floats!(entity));
}
