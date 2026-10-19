using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// A category whose asset publishes its identity and its authored <c>description</c> and nothing
/// else: the vocabulary a reader meets as a word on some other row and asks the meaning of.
/// </summary>
/// <remarks>
/// Each category keeps its own row type, because consumers rely on an enchantment and a rune stone
/// never being the same table. What they share is the reading, so it lives here once.
/// </remarks>
internal sealed class WorldDescriptionBinder<TRow> : WorldPlainBinder<TRow>
    where TRow : struct, IWorldEntity
{
    private readonly Func<Guid, string, TRow> _row;
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal WorldDescriptionBinder(string category, string typeName, Func<Guid, string, TRow> row)
    {
        Category = category;
        TypeName = typeName;
        _row = row;
    }

    internal override string Category { get; }

    internal override string TypeName { get; }

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override TRow Read(object entity) =>
        _row(_id!(entity), _description!(entity));
}
