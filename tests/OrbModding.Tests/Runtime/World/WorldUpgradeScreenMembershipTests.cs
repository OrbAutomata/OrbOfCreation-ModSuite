using System;
using System.IO;
using System.Linq;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The screen an upgrade row names comes from the authored list it sits on, so the set of authored
/// lists is what the answer is complete against.
/// </summary>
public sealed class WorldUpgradeScreenMembershipTests
{
    /// <summary>
    /// Every <c>UpgradeListVariable</c> the pinned build authors is pinned here, so a build that
    /// adds a tenth upgrade panel fails loudly instead of quietly wording its rows as if the panel
    /// did not exist.
    /// </summary>
    /// <remarks>
    /// This is the check that replaces a per-list member count. A count drifting is not a defect —
    /// the membership is read live, so a Scholar screen that gains an upgrade is simply read with
    /// one more row. A whole authored list appearing that nothing pins <em>is</em> a defect: its
    /// members would fall through to the catch-all and read as if no screen showed them.
    /// </remarks>
    [Fact]
    public void EveryAuthoredUpgradeListIsPinnedSoANewScreenCannotArriveUnnoticed()
    {
        var authored = File
            .ReadAllLines(Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv"))
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 3 && parts[2] == "UpgradeListVariable")
            .Select(parts => Guid.Parse(parts[0]))
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(9, authored.Length);
        Assert.Equal(authored, WorldUpgradeScreenLists.All.OrderBy(id => id).ToArray());
        Assert.Equal(
            WorldUpgradeScreenLists.All.Length,
            WorldUpgradeScreenLists.All.Distinct().Count());
    }

    /// <summary>
    /// The published table is ordered by upgrade, which is what lets one row's lists be found
    /// without walking the whole table, and it is the deriver that guarantees it.
    /// </summary>
    [Fact]
    public void MembershipIsPublishedGroupedByTheUpgradeThatOwnsIt()
    {
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var buffer = new WorldRelationBuffer<WorldUpgradeListMembership>();
        buffer.Append(new WorldUpgradeListMembership(second, WorldUpgradeScreenLists.All[0]));
        buffer.Append(new WorldUpgradeListMembership(first, WorldUpgradeScreenLists.All[1]));
        buffer.Append(new WorldUpgradeListMembership(first, WorldUpgradeScreenLists.All[0]));

        var table = WorldUpgradeListMembershipDeriver.Build(buffer);

        Assert.True(WorldUpgradeListMembershipLookup.TryFindRange(table, first, out var start, out var count));
        Assert.Equal(0, start);
        Assert.Equal(2, count);
        Assert.True(WorldUpgradeListMembershipLookup.TryFindRange(table, second, out var only, out var one));
        Assert.Equal(2, only);
        Assert.Equal(1, one);
        Assert.False(
            WorldUpgradeListMembershipLookup.TryFindRange(table, Guid.NewGuid(), out _, out var none));
        Assert.Equal(0, none);
    }
}
