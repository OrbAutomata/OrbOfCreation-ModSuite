using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The overview's resource lines: the left panel in its own order, one line per resource it is
/// drawing, with the clocks its tooltip prints under the conditions it prints them.
/// </summary>
public sealed class GameMcpResourceBarTests
{
    private static readonly Guid Mana = KnownEntities.Mana.Uuid;
    private static readonly Guid Knowledge = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Psi = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Spark = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Ember = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Wood = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Hidden = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Elsewhere = Guid.Parse("77777777-7777-4777-8777-777777777777");

    [Fact]
    public void The_overview_reads_the_resource_panel_in_its_own_order_and_figures()
    {
        var overview = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(World()));

        Assert.Equal(
            new[]
            {
                "Mana: 133/133 +2.50/s, full",
                "Knowledge: 114/192 +0.400/s, maxed in 03:15/08:00",
                "Psi: 33.7/161 +0.633/s, maxed in 03:21/04:14, equilibrium 150",
                "Spark: 12/79.9 -1.20/s, empty in 10s",
                "Ember: 10/100 +1/s",
                "Wood: 500 +3/s",
            },
            overview["resources"]!.Values<string>().ToArray());
    }

    /// <summary>
    /// Percentage loss hides both clocks on the tooltip, so a lossy resource reads its rate and
    /// nothing after it; a resource the panel is not drawing, or one off the panel, is absent.
    /// </summary>
    [Fact]
    public void A_lossy_resource_has_no_clock_and_undrawn_resources_are_absent()
    {
        var lines = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(World()))["resources"]!
            .Values<string>().ToArray();

        Assert.Contains("Ember: 10/100 +1/s", lines);
        Assert.DoesNotContain(lines, line => line!.StartsWith("Hidden", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line!.StartsWith("Elsewhere", StringComparison.Ordinal));
    }

    private static GameMcpFrameContext World()
    {
        return GameMcpTestHarness.Context(State());
    }

    private static GameWorldState State() => new()
    {
        EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            Name(Mana, "Mana"), Name(Knowledge, "Knowledge"), Name(Psi, "Psi"), Name(Spark, "Spark"),
            Name(Ember, "Ember"), Name(Wood, "Wood"), Name(Hidden, "Hidden"), Name(Elsewhere, "Elsewhere"),
        }.OrderBy(name => name.EntityId).ToArray()),
        ResourceBar = PublicationTable<WorldResourceBarEntry>.Create(
            new[] { Mana, Knowledge, Psi, Spark, Hidden, Ember, Wood }
                .Select(id => new WorldResourceBarEntry(id))
                .ToArray()),
        Resources = PublicationTable<WorldResource>.Create(new[]
        {
            Resource(Mana, 133, 133, 2.5),
            Resource(Knowledge, 114, 192, 0.4),
            Resource(Psi, 33.7, 161, 0.633, passiveModifiers: 1, equilibrium: 150),
            Resource(Spark, 12, 79.9, -1.2),
            Resource(Ember, 10, 100, 1, lossPercent: 5),
            Resource(Wood, 500, null, 3),
            Resource(Hidden, 1, 10, 1, visible: false),
            Resource(Elsewhere, 1, 10, 1),
        }.OrderBy(resource => resource.EntityId).ToArray()),
        CollectedAtEpoch = 25,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
    };

    private static EntityIdentityName Name(Guid id, string name) =>
        new(id, "ResourceSO", name, name.ToLowerInvariant());

    private static WorldResource Resource(
        Guid id,
        double quantity,
        double? capacity,
        double rate,
        bool visible = true,
        double lossPercent = 0,
        int passiveModifiers = 0,
        double equilibrium = 0)
    {
        var rateInputs = new RawResourceRateInputs(
            new BigDouble(rate), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, 1, 0, 0, 0, 0, 0, passiveModifiers, new BigDouble(lossPercent),
            BigDouble.Zero, BigDouble.Zero, 0d);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            id, new BigDouble(quantity), new BigDouble(capacity ?? -1d), visible,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        var capped = capacity.HasValue;
        var headroom = capped
            ? BigDouble.Max(new BigDouble(capacity!.Value - quantity), BigDouble.Zero)
            : BigDouble.Zero;
        return new WorldResource(
            in reading, capped, headroom, 0d, capped && quantity >= capacity!.Value,
            new BigDouble(quantity), new BigDouble(rate))
        {
            Equilibrium = new BigDouble(equilibrium),
        };
    }
}
