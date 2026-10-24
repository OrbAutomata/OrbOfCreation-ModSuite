#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>Typed access to one immutable world table, including any category membership filter.</summary>
internal abstract class GameMcpWorldTable
{
    protected GameMcpWorldTable(string propertyName, string rowTypeName, string identityMode)
    {
        PropertyName = propertyName;
        RowTypeName = rowTypeName;
        IdentityMode = identityMode;
    }

    internal string PropertyName { get; }
    internal string RowTypeName { get; }
    internal string IdentityMode { get; }
    internal abstract int Count(GameWorldState world);
    internal abstract object Row(GameWorldState world, int index);
    internal abstract bool TryIdentity(object row, out Guid identity);
}

internal sealed class GameMcpEntityTable<TRow> : GameMcpWorldTable
    where TRow : struct, IWorldEntity
{
    private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

    /// <summary>
    /// Which of the table's rows this category holds, where one native class backs two player
    /// concepts and each half is a category of its own. Null holds the whole table.
    /// </summary>
    private readonly Func<TRow, bool>? _admits;

    internal GameMcpEntityTable(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table,
        Func<TRow, bool>? admits = null)
        : base(propertyName, typeof(TRow).Name, "stable_entity_uuid")
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _admits = admits;
    }

    internal override int Count(GameWorldState world)
    {
        var table = _table(world);
        if (_admits is null) return table.Count;
        var held = 0;
        for (var index = 0; index < table.Count; index++)
            if (_admits(table[index])) held++;
        return held;
    }

    internal override object Row(GameWorldState world, int index)
    {
        var table = _table(world);
        if (_admits is null) return table[index];
        var held = 0;
        for (var position = 0; position < table.Count; position++)
        {
            if (!_admits(table[position])) continue;
            if (held == index) return table[position];
            held++;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal override bool TryIdentity(object row, out Guid identity)
    {
        if (row is TRow typed)
        {
            identity = typed.EntityId;
            return identity != Guid.Empty;
        }
        identity = Guid.Empty;
        return false;
    }
}

internal sealed class GameMcpCompositeTable<TRow> : GameMcpWorldTable
    where TRow : struct
{
    private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

    internal GameMcpCompositeTable(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        : base(propertyName, typeof(TRow).Name, "composite_guid_fields")
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    internal override int Count(GameWorldState world) => _table(world).Count;
    internal override object Row(GameWorldState world, int index) => _table(world)[index];
    internal override bool TryIdentity(object row, out Guid identity)
    {
        identity = Guid.Empty;
        return false;
    }
}
#endif
