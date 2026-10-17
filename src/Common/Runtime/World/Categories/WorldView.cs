using System;
using System.Collections;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One view as published: whether it is active, whether it is always active, the discovery tree
/// whose roll it draws, and the views drawn as its tabs.
/// </summary>
internal readonly struct WorldView : IWorldEntity
{
    internal WorldView(
        Guid viewId,
        bool active,
        bool alwaysActive,
        bool available,
        Guid discoveryTreeId = default,
        Guid[]? subViewIds = null)
    {
        ViewId = viewId;
        Active = active;
        AlwaysActive = alwaysActive;
        Available = available;
        DiscoveryTreeId = discoveryTreeId;
        SubViewIds = subViewIds is null || subViewIds.Length == 0
            ? PublicationTable<Guid>.Empty
            : PublicationTable<Guid>.Create(subViewIds);
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

    /// <summary>
    /// <c>ViewSO.subViews</c>: the views this one draws as its tabs. A tab's button, and the
    /// new-content mark on it, is drawn only while its own view is available, so a view is on screen
    /// only while it and every view above it are available.
    /// </summary>
    internal PublicationTable<Guid> SubViewIds { get; }
}

internal sealed class WorldViewBinder : WorldPlainBinder<WorldView>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _active;
    private Func<object, bool>? _alwaysActive;
    private Func<object, bool>? _available;
    private Func<object, Guid>? _discoveryTree;
    private Func<object, IList?>? _subViews;

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
        _subViews = bind.Through("subViews").CollectionField("value");
        return bind.Failure;
    }

    internal override WorldView Read(object entity) =>
        new(
            _id!(entity),
            _active!(entity),
            _alwaysActive!(entity),
            _available!(entity),
            _discoveryTree!(entity),
            ReadSubViews(entity));

    private Guid[] ReadSubViews(object entity)
    {
        var members = _subViews!(entity);
        if (members is null || members.Count == 0) return Array.Empty<Guid>();
        var result = new Guid[members.Count];
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index] ??
                throw new InvalidOperationException("ViewSO.subViews contained a null view");
            result[index] = _id!(member);
        }
        return result;
    }
}
