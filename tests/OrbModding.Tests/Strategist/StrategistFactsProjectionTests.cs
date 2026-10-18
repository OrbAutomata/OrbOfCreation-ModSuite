using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Strategist;

public sealed class StrategistFactsProjectionTests
{
    private static readonly Guid UpgradeId = Guid.Parse("2f6b8c1d-4e5a-4b7c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid ResourceId = Guid.Parse("7a8b9c0d-1e2f-4a3b-8c4d-5e6f7a8b9c0d");

    /// <summary>
    /// The strategist plans around what the game offers. An upgrade whose screen membership was
    /// withheld has no screen anyone asked about, so it is not offered, the same answer the MCP gives.
    /// </summary>
    [Fact]
    public void AnUpgradeWhoseScreenCannotBeReadIsNotOffered()
    {
        var onMagic = StrategistFactsProjection.Project(
            World(new WorldUpgradeListMembership(UpgradeId, KnownEntities.UpgradesMagicScreen.Uuid)), 1);
        var withheld = StrategistFactsProjection.Project(World(), 1);

        Assert.True(onMagic.Nodes.AsSpan().ToArray().Single(node => node.Id == UpgradeId).Offered);
        Assert.DoesNotContain(withheld.Nodes.AsSpan().ToArray(), node => node.Id == UpgradeId);
    }

    /// <summary>
    /// The window spans an equipped spell's timed recharge, and only the spell's authoring says
    /// whether its recharge is timed. A spell the world publishes no authoring for is a broken
    /// publication, not an untimed spell, so the strategist refuses to judge rather than plan
    /// around a window shorter than the spell it never measured.
    /// </summary>
    [Fact]
    public void AnEquippedSpellWithoutAuthoringStopsTheCycle()
    {
        var world = World(spell: Guid.Parse("5c4d3e2f-1a0b-4c9d-8e7f-6a5b4c3d2e1f"));

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            StrategistService.TryDerive(world, 1, new StrategistIncomeWindow(), out _));

        Assert.Contains("5c4d3e2f", refusal.Message, StringComparison.Ordinal);
    }

    private static GameWorldState World(params WorldUpgradeListMembership[] memberships) =>
        World(memberships, Guid.Empty);

    private static GameWorldState World(Guid spell) =>
        World(Array.Empty<WorldUpgradeListMembership>(), spell);

    private static GameWorldState World(WorldUpgradeListMembership[] memberships, Guid spell)
    {
        var buffer = new WorldRelationBuffer<WorldUpgradeListMembership>();
        foreach (var membership in memberships) buffer.Append(membership);
        var upgrade = WorldUpgradeDeriver.Shared.Derive(WorldSamples.Upgrade(UpgradeId, maxLevel: 5));
        var costs = new[] { new WorldPurchaseCost(UpgradeId, ResourceId, new BigDouble(1.0, 0)) };
        return new GameWorldState
        {
            Upgrades = WorldTable.Create(new[] { upgrade }),
            UpgradeListMemberships = WorldUpgradeListMembershipDeriver.Build(buffer),
            Views = WorldTable.Create(new[] { new WorldView(KnownEntities.ScreenMagic.Uuid, false, false, true) }),
            Resources = WorldTable.Create(new[]
            {
                new WorldResourceDeriver(default).Derive(
                    WorldSamples.Resource(ResourceId, new BigDouble(1.0, 6), new BigDouble(-1d))),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(costs, costs.Length),
            DoubleVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(KnownEntities.TimePlayed.Uuid, new BigDouble(600), true),
            }),
            SpellSlots = spell == Guid.Empty
                ? PublicationTable<WorldSpellSlot>.Empty
                : PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    new WorldSpellSlot(0, spell, true, false, false, false, false, false, false, true,
                        true, true, 1, 1, new BigDouble(90)),
                }),
        };
    }
}
