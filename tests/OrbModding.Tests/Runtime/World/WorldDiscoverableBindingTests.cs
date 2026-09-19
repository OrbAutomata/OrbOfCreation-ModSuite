using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldDiscoverableBindingTests
{
    [Theory]
    [InlineData(typeof(AlchemyRecipeSO))]
    [InlineData(typeof(EquipmentSO))]
    [InlineData(typeof(GlyphSO))]
    [InlineData(typeof(RitualSO))]
    [InlineData(typeof(SpellRecipeSO))]
    [InlineData(typeof(TimeRuneSO))]
    public void Every_native_discoverable_category_publishes_the_same_decision_shape(Type type)
    {
        var item = Assert.IsAssignableFrom<IDiscoverable>(Activator.CreateInstance(type));
        SetDiscovered(item, false);
        var resource = new ResourceSO { quantity = new BigDouble(8, 0) };
        resource.SetGuid(Guid.NewGuid());
        item.GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(5, 0)));
        SetRequired(item, true);

        var binding = new WorldDiscoverableBinding(type, type.Name);
        var decision = binding.Read(item);

        Assert.Empty(binding.Failure);
        Assert.True(decision.Visible);
        Assert.True(decision.CanDiscover);
        Assert.False(decision.Discovered);
        Assert.True(decision.Required);
        Assert.True(decision.Affordable);
        Assert.Equal(1, decision.Costs.Count);
        var cost = decision.Costs[0];
        Assert.Equal(resource.GetGuid(), cost.ResourceId);
        Assert.Equal(0, cost.Cost.CompareTo(new BigDouble(5, 0)));
        Assert.Equal(0, cost.Amount.CompareTo(new BigDouble(8, 0)));
        Assert.True(cost.Affordable);
    }

    [Fact]
    public void Native_aggregate_affordability_is_preserved_beside_each_holding()
    {
        var item = new GlyphSO
        {
            discovered = false,
            NativeCanDiscover = false,
            NativeDiscoverVisible = false,
        };
        var resource = new ResourceSO { quantity = new BigDouble(3, 0) };
        resource.SetGuid(Guid.NewGuid());
        item.discoveryCost.costs.Add(new ResourceTuple(resource, new BigDouble(4, 0)));

        var decision = new WorldDiscoverableBinding(typeof(GlyphSO), nameof(GlyphSO)).Read(item);

        Assert.False(decision.Visible);
        Assert.False(decision.CanDiscover);
        Assert.False(decision.Affordable);
        Assert.Equal(1, decision.Costs.Count);
        Assert.False(decision.Costs[0].Affordable);
    }

    private static void SetRequired(IDiscoverable item, bool value)
    {
        switch (item)
        {
            case AlchemyRecipeSO target: target.NativeDiscoveryRequired = value; break;
            case EquipmentSO target: target.isRequiredDiscovery = value; break;
            case GlyphSO target: target.discoveryRequired = value; break;
            case RitualSO target: target.isDiscoverRequired = value; break;
            case SpellRecipeSO target: target.NativeDiscoveryRequired = value; break;
            case TimeRuneSO target: target.isDiscoverRequired = value; break;
        }
    }

    private static void SetDiscovered(IDiscoverable item, bool value)
    {
        switch (item)
        {
            case AlchemyRecipeSO target: target.discovered = value; break;
            case EquipmentSO target: target.isCreated = value; break;
            case GlyphSO target: target.discovered = value; break;
            case RitualSO target: target.discovered = value; break;
            case SpellRecipeSO target: target.discovered = value; break;
            case TimeRuneSO target: target.discovered = value; break;
        }
    }
}
