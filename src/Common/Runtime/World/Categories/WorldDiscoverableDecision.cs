using System;
using System.Collections;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One exact native discovery price beside the holding that would pay it now.</summary>
internal readonly struct WorldDiscoverableCost
{
    internal WorldDiscoverableCost(Guid resourceId, BigDouble cost, BigDouble amount)
    {
        ResourceId = resourceId;
        Cost = cost;
        Amount = amount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
    internal BigDouble Amount { get; }
    internal BigDouble AvailableAmount => Amount;
    internal bool Affordable => Amount.CompareTo(Cost) >= 0;
}

/// <summary>
/// The player-facing decision facts shared by every native <c>IDiscoverable</c> implementer.
/// The game evaluates visibility, prerequisites, and aggregate affordability; the suite publishes
/// those verdicts without recreating them.
/// </summary>
internal readonly struct WorldDiscoverableDecision
{
    private readonly PublicationTable<WorldDiscoverableCost>? _costs;
    private readonly PublicationTable<Guid>? _glyphRecipe;
    private readonly PublicationTable<Guid>? _resourceRecipe;

    internal WorldDiscoverableDecision(
        bool visible,
        bool canDiscover,
        bool discovered,
        bool required,
        bool affordable,
        PublicationTable<WorldDiscoverableCost> costs,
        PublicationTable<Guid>? glyphRecipe = null,
        PublicationTable<Guid>? resourceRecipe = null)
    {
        Visible = visible;
        CanDiscover = canDiscover;
        Discovered = discovered;
        Required = required;
        Affordable = affordable;
        _costs = costs ?? throw new ArgumentNullException(nameof(costs));
        _glyphRecipe = glyphRecipe ?? PublicationTable<Guid>.Empty;
        _resourceRecipe = resourceRecipe ?? PublicationTable<Guid>.Empty;
    }

    internal bool Visible { get; }
    internal bool CanDiscover { get; }
    internal bool Discovered { get; }
    internal bool Required { get; }
    internal bool Affordable { get; }
    internal PublicationTable<WorldDiscoverableCost> Costs =>
        _costs ?? PublicationTable<WorldDiscoverableCost>.Empty;
    /// <summary>The exact authored glyph identities used by the native compose resolver.</summary>
    internal PublicationTable<Guid> GlyphRecipe =>
        _glyphRecipe ?? PublicationTable<Guid>.Empty;
    /// <summary>The exact authored resource identities used by the native compose resolver.</summary>
    internal PublicationTable<Guid> ResourceRecipe =>
        _resourceRecipe ?? PublicationTable<Guid>.Empty;
}

/// <summary>
/// One compiled read boundary for the <c>IDiscoverable</c> contract. Concrete world binders reuse it
/// during their existing category traversal, so discovery publication adds no second registry scan.
/// </summary>
internal sealed class WorldDiscoverableBinding
{
    private readonly Func<object, bool>? _visible;
    private readonly Func<object, bool>? _canDiscover;
    private readonly Func<object, bool>? _discovered;
    private readonly Func<object, bool>? _required;
    private readonly Func<object, object?>? _cost;
    private readonly Func<object, bool>? _affordable;
    private readonly Func<object, IList?>? _entries;
    private readonly Func<object, object?>? _resource;
    private readonly Func<object, BigDouble>? _value;
    private readonly Func<object, Guid>? _resourceIdentity;
    private readonly Func<object, BigDouble>? _resourceAmount;
    private readonly Func<object, IList?>? _glyphRecipe;
    private readonly Func<object, IList?>? _resourceRecipe;
    private readonly Func<object, Guid>? _glyphIdentity;

    internal WorldDiscoverableBinding(Type concreteType, string concreteTypeName)
    {
        if (concreteType is null) throw new ArgumentNullException(nameof(concreteType));
        var discoverable = ExactInterface(concreteType, "IDiscoverable");
        if (discoverable is null)
        {
            Failure = concreteTypeName + " did not implement the exact IDiscoverable contract";
            return;
        }

        var item = new WorldMemberBinding(discoverable, "IDiscoverable");
        _visible = item.Call<bool>("IsDiscoverVisible");
        _canDiscover = item.Call<bool>("CanDiscover");
        _discovered = item.Call<bool>("IsDiscovered");
        _required = item.Call<bool>("IsDiscoverRequired");
        var glyphRecipeMethod = discoverable.GetMethod("GetGlyphRecipe");
        var glyphRecipeType = glyphRecipeMethod?.ReturnType is { IsGenericType: true } glyphList
            ? glyphList.GetGenericArguments()[0]
            : null;
        _glyphRecipe = item.CallList("GetGlyphRecipe", glyphRecipeType);
        var resourceRecipeMethod = discoverable.GetMethod("GetResourceRecipe");
        var resourceRecipeType = resourceRecipeMethod?.ReturnType is { IsGenericType: true } resourceList
            ? resourceList.GetGenericArguments()[0]
            : null;
        _resourceRecipe = item.CallList("GetResourceRecipe", resourceRecipeType);
        var costMethod = discoverable.GetMethod("GetDiscoverCost");
        var costType = costMethod?.ReturnType;
        _cost = item.CallObject("GetDiscoverCost", costType);

        var cost = new WorldMemberBinding(costType!, "ResourceCostList");
        _affordable = cost.Call<bool>("HasEnough");
        var entriesMethod = costType?.GetMethod("GetEntries");
        var entriesType = entriesMethod?.ReturnType;
        var tupleType = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _entries = cost.CallList("GetEntries", tupleType);

        var tuple = new WorldMemberBinding(tupleType!, "ResourceTuple");
        var resourceField = tupleType?.GetField("resource");
        var resourceType = resourceField?.FieldType;
        _resource = tuple.Reference("resource", resourceType);
        _value = tuple.Call<BigDouble>("GetValue");

        var resource = new WorldMemberBinding(resourceType!, "ResourceSO");
        _resourceIdentity = resource.Call<Guid>("GetGuid");
        _resourceAmount = resource.Call<BigDouble>("GetQuantity");
        var glyph = new WorldMemberBinding(glyphRecipeType!, "GlyphSO");
        _glyphIdentity = glyph.Call<Guid>("GetGuid");

        Failure = Join(
            item.Failure,
            cost.Failure,
            tuple.Failure,
            resource.Failure,
            glyph.Failure);
    }

    internal string Failure { get; }

    internal WorldDiscoverableDecision Read(object entity)
    {
        var cost = _cost!(entity) ??
            throw new InvalidOperationException("IDiscoverable.GetDiscoverCost() returned null");
        var entries = _entries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries() returned null");
        var glyphValues = _glyphRecipe!(entity) ??
            throw new InvalidOperationException("IDiscoverable.GetGlyphRecipe() returned null");
        var resourceValues = _resourceRecipe!(entity) ??
            throw new InvalidOperationException("IDiscoverable.GetResourceRecipe() returned null");
        var glyphRecipe = ReadRecipe(glyphValues, "glyph", _glyphIdentity!);
        var resourceRecipe = ReadRecipe(resourceValues, "resource", _resourceIdentity!);
        var costs = new WorldDiscoverableCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("discovery cost entry " + index + " was null");
            var resource = _resource!(entry) ??
                throw new InvalidOperationException("discovery cost entry " + index + " had no resource");
            costs[index] = new WorldDiscoverableCost(
                _resourceIdentity!(resource),
                _value!(entry),
                _resourceAmount!(resource));
        }
        return new WorldDiscoverableDecision(
            _visible!(entity),
            _canDiscover!(entity),
            _discovered!(entity),
            _required!(entity),
            _affordable!(cost),
            PublicationTable<WorldDiscoverableCost>.Create(costs),
            glyphRecipe,
            resourceRecipe);
    }

    private static PublicationTable<Guid> ReadRecipe(
        IList values,
        string kind,
        Func<object, Guid> readIdentity)
    {
        var result = new Guid[values.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var component = values[index] ??
                throw new InvalidOperationException(
                    "IDiscoverable " + kind + " recipe entry " + index + " was null");
            result[index] = readIdentity(component);
            if (result[index] == Guid.Empty)
                throw new InvalidOperationException(
                    "IDiscoverable " + kind + " recipe entry " + index + " had no identity");
        }
        return PublicationTable<Guid>.Create(result);
    }

    private static Type? ExactInterface(Type concrete, string name)
    {
        Type? match = null;
        foreach (var candidate in concrete.GetInterfaces())
        {
            if (!string.Equals(candidate.FullName, name, StringComparison.Ordinal)) continue;
            if (match is not null) return null;
            match = candidate;
        }
        return match;
    }

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}
