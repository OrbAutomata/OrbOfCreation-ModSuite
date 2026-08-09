using System;
using System.Collections.Generic;
using System.IO;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The wire hands out an id handle and takes one back. These pin both halves of that contract: the
/// handle is one constant width computed once against the pinned build's whole id set, and every
/// id argument reads a whole UUID or any prefix that names exactly one published entity.
/// </summary>
public sealed class GameMcpEntityHandleTests
{
    /// <summary>
    /// The build-time count, made durable. The suite pins the game build, so the handle length is a
    /// constant rather than a runtime recompute — and this is what makes that constant honest: a
    /// build whose published id set moved fails here instead of shipping a colliding handle.
    /// </summary>
    [Fact]
    public void The_handle_length_is_the_shortest_prefix_unique_across_every_published_id()
    {
        var ids = PublishedIds();

        Assert.True(ids.Count > 2500, "the published id set should cover the whole pinned build");
        Assert.Equal(ids.Count, Distinct(ids, GameMcpEntityHandle.Length));
        Assert.True(
            GameMcpEntityHandle.Length == 6 ||
            Distinct(ids, GameMcpEntityHandle.Length - 1) < ids.Count,
            "the handle is the shortest unique prefix, floored at six");
    }

    [Fact]
    public void Every_handle_is_the_same_width()
    {
        var first = GameMcpEntityHandle.Format(
            Guid.Parse("075fcca6-8c12-4b2a-b690-94deaa2c54d3"));
        var second = GameMcpEntityHandle.Format(
            Guid.Parse("2c20e7a8-4356-4b21-81ed-b057e2e2c3b7"));

        Assert.Equal("075fcc", first);
        Assert.Equal("2c20e7", second);
        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public void A_whole_uuid_is_always_accepted()
    {
        var outcome = GameMcpEntityHandle.Resolve(
            "075fcca6-8c12-4b2a-b690-94deaa2c54d3",
            EntityIdentityCatalogSnapshot.Unbound(0),
            out var uuid,
            out _);

        Assert.Equal(GameMcpEntityHandle.ResolutionOutcome.Resolved, outcome);
        Assert.Equal(Guid.Parse("075fcca6-8c12-4b2a-b690-94deaa2c54d3"), uuid);
    }

    [Fact]
    public void An_unambiguous_prefix_names_its_one_entity()
    {
        var wanted = Guid.Parse("075fcca6-8c12-4b2a-b690-94deaa2c54d3");
        var catalog = Catalog(wanted, Guid.Parse("2c20e7a8-4356-4b21-81ed-b057e2e2c3b7"));

        var outcome = GameMcpEntityHandle.Resolve("075fcc", catalog, out var uuid, out _);

        Assert.Equal(GameMcpEntityHandle.ResolutionOutcome.Resolved, outcome);
        Assert.Equal(wanted, uuid);
    }

    [Fact]
    public void An_ambiguous_prefix_answers_with_the_ids_it_matched()
    {
        var catalog = Catalog(
            Guid.Parse("075fcca6-8c12-4b2a-b690-94deaa2c54d3"),
            Guid.Parse("075f0000-8c12-4b2a-b690-94deaa2c54d3"));

        var outcome = GameMcpEntityHandle.Resolve("075f", catalog, out var uuid, out var candidates);

        Assert.Equal(GameMcpEntityHandle.ResolutionOutcome.Ambiguous, outcome);
        Assert.Equal(Guid.Empty, uuid);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void A_prefix_no_published_id_starts_with_is_not_guessed_at()
    {
        var catalog = Catalog(Guid.Parse("075fcca6-8c12-4b2a-b690-94deaa2c54d3"));

        var outcome = GameMcpEntityHandle.Resolve("ffffff", catalog, out _, out _);

        Assert.Equal(GameMcpEntityHandle.ResolutionOutcome.NotFound, outcome);
    }

    private static EntityIdentityCatalogSnapshot Catalog(params Guid[] ids)
    {
        var rows = new EntityIdentityName[ids.Length];
        for (var index = 0; index < ids.Length; index++)
            rows[index] = new EntityIdentityName(ids[index], "TestSO", "Test", "test");
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return EntityIdentityCatalogSnapshot.Bound(1, rows);
    }

    private static int Distinct(IReadOnlyList<string> ids, int length)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++) seen.Add(ids[index].Substring(0, length));
        return seen.Count;
    }

    private static IReadOnlyList<string> PublishedIds()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv");
        var lines = File.ReadAllLines(path);
        var result = new List<string>(lines.Length);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0) continue;
            var tab = line.IndexOf('\t');
            result.Add(tab < 0 ? line : line.Substring(0, tab));
        }
        return result;
    }
}
