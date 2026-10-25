using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpResourceCoordinateTests
{
    [Fact]
    public void Player_facing_costs_apply_the_resources_quality_discount()
    {
        Assert.Equal("1.88e491", PlayerFacingCost(
            new BigDouble(3.1d, 523), new BigDouble(1.65d, 34)));
        Assert.Equal("1.56e508", PlayerFacingCost(
            new BigDouble(1.82d, 529), new BigDouble(1.17d, 23)));
        Assert.Equal("9.03e169", PlayerFacingCost(
            new BigDouble(9.96d, 205), new BigDouble(1.103d, 38)));
    }

    /// <summary>
    /// The four bandwidth/inverted quadrants, plus the live rows that exposed the old rule:
    /// <c>atCapacity</c> answers in the coordinate <c>amount</c> is published in, so an inverted
    /// counter is full when its displayed number reaches the ceiling. Potion Toxicity showing 0 of
    /// its tolerance is not at capacity; Stability showing its whole pool is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same rows pin what the reading is called, and both flags decide it. A row says
    /// <c>meter: left</c> when its <c>amount</c> is the very pool it spends from — inverted display
    /// and bandwidth spending, which is the twelve advancement currencies — so the number falls as
    /// the total is used. Every other row says <c>held</c>: the number climbs toward the ceiling.
    /// </para>
    /// <para>
    /// Toxicity is inverted and is not bandwidth, and it is the row that proves the rule needs both:
    /// it displays 0 of a tolerance of 10 while holding 10 to spend, so paying 8 of it moves the
    /// displayed number <em>up</em>. Called <c>left</c> off the display flag alone, the row told a
    /// reader that number falls as the total is used, which is exactly backwards.
    /// </para>
    /// <para>
    /// The used-ness pair rides only a <c>left</c> row, for the same reason: on a meter that fills,
    /// full means blocked, and <c>none committed</c> would say the opposite of it. On a <c>left</c>
    /// row a whole pool showing really is <c>none committed</c>, where a plain <c>yes</c> read as
    /// stuck while meaning the opposite.
    /// </para>
    /// </remarks>
    [Fact]
    public void ResourceCoordinatesCoverEveryBandwidthAndInvertedQuadrant()
    {
        var ordinaryId = Guid.Parse("36666666-6666-4666-8666-666666666666");
        var spellCapacityId = Guid.Parse("37777777-7777-4777-8777-777777777777");
        var potionToxicityId = Guid.Parse("38888888-8888-4888-8888-888888888888");
        var glyphUpgradesId = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var stabilityId = Guid.Parse("3aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var timeAdvancementId = Guid.Parse("3bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var arcanumId = Guid.Parse("3ccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(ordinaryId, "ResourceSO", "Knowledge", "knowledge"),
                new EntityIdentityName(spellCapacityId, "ResourceSO", "Spell Capacity", "weightSpell"),
                new EntityIdentityName(potionToxicityId, "ResourceSO", "Potion Toxicity", "potionToxicity"),
                new EntityIdentityName(glyphUpgradesId, "ResourceSO", "Glyph Upgrades", "glyphUpgrades"),
                new EntityIdentityName(stabilityId, "ResourceSO", "Stability", "stability"),
                new EntityIdentityName(timeAdvancementId, "ResourceSO", "Time Advancement", "timeAdvancement"),
                new EntityIdentityName(arcanumId, "ResourceSO", "Arcanum", "arcanum"),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                Derived(ordinaryId, held: 3, bandwidth: false, inverted: false),
                Derived(spellCapacityId, held: 3, bandwidth: true, inverted: false),
                Derived(potionToxicityId, held: 10, bandwidth: false, inverted: true),
                Derived(glyphUpgradesId, held: 10, bandwidth: true, inverted: true),
                Derived(stabilityId, held: 0, bandwidth: false, inverted: true),
                Derived(timeAdvancementId, held: 4, bandwidth: false, inverted: true),
                Derived(arcanumId, held: 10, bandwidth: false, inverted: false),
            }),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(0, WorldSpellCostKind.Immediate,
                    ordinaryId, new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "resources", WorldCategoryOutcome.Collected, 7, 0, string.Empty),
            }),
        };

        AssertCoordinates(
            world, 0, ordinaryId, display: "3", spendable: 3, cost: 50,
            meter: "held", atCapacity: false);
        AssertCoordinates(
            world, 1, spellCapacityId, display: "3", spendable: 7, cost: 100,
            meter: "held", atCapacity: false);
        // Inverted display, ordinary spending: 0 of 10 shown while 10 is spendable, and the shown
        // number rises as that 10 is spent. A meter that fills, and nowhere near blocked.
        AssertCoordinates(
            world, 2, potionToxicityId, display: "0", spendable: 10, cost: 50,
            meter: "held", atCapacity: false);
        AssertCoordinates(
            world, 3, glyphUpgradesId, display: "0", spendable: 0, cost: 100,
            meter: "left", atCapacity: "some committed");

        // The same shape at the other end: the meter is full and there is nothing left to spend,
        // which `atCapacity: yes` says and `none committed` denied.
        AssertCoordinates(
            world, 4, stabilityId, display: "10", spendable: 0, cost: 50,
            meter: "held", atCapacity: true);
        AssertCoordinates(
            world, 5, timeAdvancementId, display: "6", spendable: 4, cost: 50,
            meter: "held", atCapacity: false);
        AssertCoordinates(
            world, 6, arcanumId, display: "10", spendable: 10, cost: 50,
            meter: "held", atCapacity: true);

        var costs = Assert.IsType<JArray>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpCastProjection.ProjectEquippedSpellCosts(
                world, 0, WorldSpellCostKind.Immediate).Freeze(),
            world.EntityIdentities));
        var cost = Assert.Single(costs.Values<JObject>());
        Assert.Equal("50", (string?)cost["cost"]);
        Assert.Equal("3", (string?)cost["spendableAmount"]);
        Assert.False((bool)cost["affordable"]!);
    }

    private static void AssertCoordinates(
        GameWorldState world,
        int index,
        Guid resourceId,
        string display,
        int spendable,
        int cost,
        string meter,
        object atCapacity)
    {
        var row = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpResourceProjection.ProjectResource(world, world.Resources[index]),
            world.EntityIdentities));
        Assert.Equal(meter, (string?)row["meter"]);
        Assert.Equal(display, (string?)row["amount"]);
        Assert.Equal("10", (string?)row["capacity"]);
        Assert.Equal(atCapacity, ((JValue)row["atCapacity"]!).Value);
        Assert.Equal(new BigDouble(spendable),
            GameMcpDecisionReason.SpendableAmount(world, resourceId, BigDouble.Zero));
        Assert.Equal(new BigDouble(cost),
            GameMcpDecisionReason.PlayerFacingCost(world, resourceId, new BigDouble(100)));
    }

    private static string PlayerFacingCost(BigDouble nominal, BigDouble quality)
    {
        var resource = Guid.NewGuid();
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, BigDouble.Zero, new BigDouble(-1), true,
            BigDouble.Zero, BigDouble.Zero, quality,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, BigDouble.Zero, 0d, false,
                    BigDouble.Zero, BigDouble.Zero),
            }),
        };

        return GameMcpNumberFormatter.Format(GameMcpDecisionReason.PlayerFacingCost(
            world, resource, nominal));
    }

    private static WorldResource Derived(Guid resourceId, int held, bool bandwidth, bool inverted)
    {
        var rateInputs = default(RawResourceRateInputs);
        var modifiers = default(RawResourceModifiers);
        var traits = Traits(bandwidth, inverted);
        var reading = new RawResourceSample(
            resourceId, new BigDouble(held), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        return GameWorldStateDeriver.Derive(in reading, default(WorldFrameGlobals));
    }

    private static RawResourceTraits Traits(bool bandwidth, bool inverted) => new(
        0d, 0d, 0d,
        false, false, false,
        bandwidth, inverted, false, false,
        BigDouble.Zero, 0, 0, 0d, false, 0d,
        BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
}
