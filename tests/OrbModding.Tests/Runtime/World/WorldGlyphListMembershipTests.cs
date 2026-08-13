using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The authored glyph populations, which are what a page is bound to and so the only fact that says
/// where the player meets a glyph.
/// </summary>
public sealed class WorldGlyphListMembershipTests : IDisposable
{
    public WorldGlyphListMembershipTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void EachPinnedListPublishesItsAuthoredMembers()
    {
        var augment = new global::GlyphSO { discoverable = true };
        var unlocker = new global::GlyphSO();
        global::GlyphSO.All.Add(augment);
        global::GlyphSO.All.Add(unlocker);
        Authored(KnownEntities.GlyphsAugmentSpell.Uuid, augment);
        Authored(KnownEntities.GlyphsCoreSpell.Uuid, unlocker);
        Authored(KnownEntities.GlyphsCoreAlchemy.Uuid, unlocker);
        Authored(KnownEntities.GlyphsEquipment.Uuid);

        var world = Collect();

        Assert.Equal(3, world.GlyphListMemberships.Count);
        Assert.True(WorldGlyphListMembershipLookup.TryFindRange(
            world.GlyphListMemberships, unlocker.GetGuid(), out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(
            new[] { KnownEntities.GlyphsCoreSpell.Uuid, KnownEntities.GlyphsCoreAlchemy.Uuid },
            new[]
            {
                world.GlyphListMemberships[start].ListId,
                world.GlyphListMemberships[start + 1].ListId,
            });
    }

    /// <summary>
    /// A glyph on more than one authored list is the ordinary case for a core unlocker, and the
    /// upgrade membership beside this one never has it — one upgrade, one screen panel.
    /// </summary>
    [Fact]
    public void AGlyphOnNoAuthoredListPublishesNoEdge()
    {
        var loose = new global::GlyphSO();
        global::GlyphSO.All.Add(loose);
        Authored(KnownEntities.GlyphsAugmentSpell.Uuid);
        Authored(KnownEntities.GlyphsCoreSpell.Uuid);
        Authored(KnownEntities.GlyphsCoreAlchemy.Uuid);
        Authored(KnownEntities.GlyphsEquipment.Uuid);

        var world = Collect();

        Assert.Equal(0, world.GlyphListMemberships.Count);
        Assert.False(WorldGlyphListMembershipLookup.TryFindRange(
            world.GlyphListMemberships, loose.GetGuid(), out _, out _));
    }

    /// <summary>
    /// Membership is published whole or withheld whole: a table missing one list's rows reads exactly
    /// like a page those glyphs are not on, so one unreadable list empties the table and says why.
    /// </summary>
    [Fact]
    public void OneUnreadableListWithholdsTheWholeTableAndNamesTheList()
    {
        var augment = new global::GlyphSO { discoverable = true };
        global::GlyphSO.All.Add(augment);
        Authored(KnownEntities.GlyphsAugmentSpell.Uuid, augment);
        Authored(KnownEntities.GlyphsCoreSpell.Uuid);
        Authored(KnownEntities.GlyphsCoreAlchemy.Uuid);

        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var report = collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(0, world.GlyphListMemberships.Count);
        Assert.Equal(
            "authored glyph list membership was withheld: pinned glyph list " +
            KnownEntities.GlyphsEquipment.Uuid.ToString("D") +
            " did not resolve to a GlyphListVariable",
            report.For("glyph lists").FirstFailure);
    }

    private static void Authored(Guid listId, params global::GlyphSO[] members)
    {
        var list = new global::GlyphListVariable { uuid = listId };
        list.value.AddRange(members);
        global::IdScriptableObject.RuntimeLookup[listId] = list;
    }

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void ClearRegistries()
    {
        global::GlyphSO.All.Clear();
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::GameManager.currentFrame = 0;
    }
}
