using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins the resource panel's membership: the Mana orb, then each pinned list's members in the
/// order the game authored them, published whole or not at all.
/// </summary>
public sealed class WorldResourceBarReaderTests : IDisposable
{
    private static readonly Guid SpellpowerId = new("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid KnowledgeId = new("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid WoodId = new("f3333333-3333-3333-3333-333333333333");

    public WorldResourceBarReaderTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    public void TheOrbComesFirstAndEachListKeepsItsAuthoredOrder()
    {
        Seed();
        var frame = new GameWorldCycleFrame();

        var report = new WorldResourceBarReader(Resolve).Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(
            new[] { KnownEntities.Mana.Uuid, KnowledgeId, SpellpowerId, WoodId },
            Members(frame));
    }

    /// <summary>A panel missing one of its lists is withheld rather than published short.</summary>
    [Fact]
    public void AnUnresolvedListWithholdsThePanelWhole()
    {
        Seed();
        global::IdScriptableObject.RuntimeLookup.Remove(KnownEntities.ResourceBarMaterials.Uuid);
        var frame = new GameWorldCycleFrame();

        var report = new WorldResourceBarReader(Resolve).Collect(new HashSet<Guid>(), frame);

        Assert.Equal(0, frame.ResourceBar.Count);
        Assert.StartsWith("the resource panel's membership was withheld: ", report.FirstFailure);
    }

    private static Guid[] Members(GameWorldCycleFrame frame)
    {
        var members = new Guid[frame.ResourceBar.Count];
        for (var index = 0; index < members.Length; index++)
            members[index] = frame.ResourceBar[index].ResourceId;
        return members;
    }

    private static void Seed()
    {
        Register(Resource(KnownEntities.Mana.Uuid));
        List(KnownEntities.ResourceBarCore.Uuid, Resource(KnowledgeId), Resource(SpellpowerId));
        List(KnownEntities.ResourceBarMaterials.Uuid, Resource(WoodId));
    }

    private static void List(Guid id, params global::ResourceSO[] members)
    {
        var list = new global::ResourceListVariable();
        list.SetGuid(id);
        list.value.AddRange(members);
        Register(list);
    }

    private static global::ResourceSO Resource(Guid id) => new() { uuid = id.ToString() };

    private static void Register(global::IdScriptableObject entity) =>
        global::IdScriptableObject.RuntimeLookup[entity.GetGuid()] = entity;

    private static Type? Resolve(string name) =>
        typeof(global::ResourceSO).Assembly.GetType(name, throwOnError: false);
}
