using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// The resource each harvest element owns. Walks the element registry, because that is the only path
/// to a resource the game deliberately keeps out of <c>ResourceSO.All</c>.
/// </summary>
internal sealed class WorldHarvestResourceBinder
    : WorldRowBinder<RawHarvestResourceSample, WorldHarvestResource>
{
    private Func<object, Guid>? _elementId;
    private WorldResourceMembers? _resource;

    internal override string Category => "harvest resources";

    internal override string TypeName => "HarvestElementSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _elementId = bind.Call<Guid>("GetGuid");

        // Rooted at the element's private resource, so the whole resource member list applies without
        // a second copy of it.
        _resource = new WorldResourceMembers(bind.Through("harvestResource"));
        return bind.Failure;
    }

    internal override RawHarvestResourceSample Read(object entity) =>
        new(_elementId!(entity), _resource!.Read(entity));
}

/// <summary>
/// The resource a harvest element owns, read through the element that owns it.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate category rather than more rows in the resource table because it is a separate
/// population. <c>HarvestElementSO.ResetData()</c> creates the resource with
/// <c>ScriptableObject.CreateInstance&lt;ResourceSO&gt;()</c>, never calls <c>RegisterObject()</c> on
/// it, and sets <c>excludeFromGlobals = true</c> — so it is absent from <c>ResourceSO.All</c> and
/// from every global aggregate, by the game's own decision. Folding it into the resource table would
/// publish it as something the game treats it as not being.
/// </para>
/// <para>
/// The identity is the resource's own, because a row stands under the entity it describes and the
/// element's own row already stands under the element's. That identity is fresh each session: an instance created at
/// runtime takes the <c>Guid.NewGuid()</c> its field initialiser produces, with no serialized value to
/// restore. It is therefore a key within one snapshot's lifetime and not a name anything may persist —
/// <see cref="ElementId"/> is the stable way to refer to one of these.
/// </para>
/// </remarks>
internal readonly struct RawHarvestResourceSample : IWorldEntity
{
    internal RawHarvestResourceSample(Guid elementId, in RawResourceSample resource)
    {
        ElementId = elementId;
        Resource = resource;
    }

    /// <summary>The element that owns this resource — the stable identity of the pair.</summary>
    internal Guid ElementId { get; }

    /// <summary>The resource itself, read with exactly the member list every other resource uses.</summary>
    internal RawResourceSample Resource { get; }

    public Guid EntityId => Resource.ResourceId;
}

/// <summary>
/// One harvest element's own resource as published: the element it belongs to, and the resource row
/// derived exactly as every other resource row is.
/// </summary>
internal readonly struct WorldHarvestResource : IWorldEntity
{
    internal WorldHarvestResource(Guid elementId, in WorldResource resource)
    {
        ElementId = elementId;
        Resource = resource;
    }

    /// <summary>The owning element. Stable across sessions, unlike <see cref="EntityId"/>.</summary>
    internal Guid ElementId { get; }

    internal WorldResource Resource { get; }

    public Guid EntityId => Resource.EntityId;
}
