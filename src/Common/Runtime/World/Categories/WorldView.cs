using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One view as published: whether it is active, whether it is always active, and the discovery tree
/// whose roll it draws.
/// </summary>
internal readonly struct WorldView : IWorldEntity
{
    internal WorldView(
        Guid viewId,
        bool active,
        bool alwaysActive,
        bool available,
        Guid discoveryTreeId = default)
    {
        ViewId = viewId;
        Active = active;
        AlwaysActive = alwaysActive;
        Available = available;
        DiscoveryTreeId = discoveryTreeId;
    }

    internal Guid ViewId { get; }

    public Guid EntityId => ViewId;

    internal bool Active { get; }

    internal bool AlwaysActive { get; }

    /// <summary>The game's composed progression answer for this view.</summary>
    internal bool Available { get; }

    /// <summary>
    /// <c>ViewSO.discoveryTree</c>: the tree this page rolls, or <see cref="Guid.Empty"/> on a page
    /// that rolls none. <c>ViewSO.InternalHasActiveTutorial()</c> raises the tab's new-content mark
    /// off this edge while the tree owes a required discovery.
    /// </summary>
    internal Guid DiscoveryTreeId { get; }
}

internal sealed class WorldViewBinder : WorldPlainBinder<WorldView>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _active;
    private Func<object, bool>? _alwaysActive;
    private Func<object, bool>? _available;
    private Func<object, Guid>? _discoveryTree;

    internal override string Category => "views";

    internal override string TypeName => "ViewSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _active = bind.Field<bool>("active");
        _alwaysActive = bind.Field<bool>("alwaysActive");
        _available = bind.Call<bool>("IsAvailable");
        _discoveryTree = bind.ReferenceGuid("discoveryTree");
        return bind.Failure;
    }

    internal override WorldView Read(object entity) =>
        new(
            _id!(entity),
            _active!(entity),
            _alwaysActive!(entity),
            _available!(entity),
            _discoveryTree!(entity));
}
