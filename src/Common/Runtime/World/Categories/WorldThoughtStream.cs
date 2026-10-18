using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One thought stream as published: the state it is in.</summary>
internal readonly struct WorldThoughtStream : IWorldEntity
{
    internal WorldThoughtStream(
        Guid thoughtStreamId,
        int state)
    {
        ThoughtStreamId = thoughtStreamId;
        State = state;
    }

    internal Guid ThoughtStreamId { get; }

    public Guid EntityId => ThoughtStreamId;

    internal int State { get; }
}

internal sealed class WorldThoughtStreamBinder : WorldPlainBinder<WorldThoughtStream>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _state;

    internal override string Category => "thought streams";

    internal override string TypeName => "ThoughtStreamSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _state = bind.EnumField("state");
        return bind.Failure;
    }

    internal override WorldThoughtStream Read(object entity) =>
        new(
            _id!(entity),
            _state!(entity));
}
