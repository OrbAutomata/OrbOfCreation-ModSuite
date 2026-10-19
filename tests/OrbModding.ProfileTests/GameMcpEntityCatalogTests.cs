using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class GameMcpEntityCatalogTests
{
    /// <summary>
    /// The snapshot answers for an id progression has not revealed, and the same reference answers
    /// the next caller: it is bound once for the lifecycle rather than read again per question.
    /// </summary>
    [Fact]
    public void LiveCatalogSnapshotServesHiddenNamesWithoutReloading()
    {
        var catalog = GameMcpTestHarness.EntityCatalog;
        var block = GameMcpTestHarness.Json(GameMcpEntityCatalog.Lookup(
            catalog,
            Guid.Parse("f8a9326a-2f98-4f05-889e-3078635fd714")).Freeze());

        Assert.Null(block["status"]);
        Assert.Equal("Summon Reinforcements", (string?)block["name"]);
        Assert.Same(catalog, GameMcpTestHarness.EntityCatalog);
    }

    [Fact]
    public void OfflineCatalogAssetsAreNotEmbeddedInTheShippedPlugin()
    {
        var resources = typeof(GameMcpEntityCatalog).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(
            "OrbModSuite.GameMcp.entity-mappings.tsv", resources);
        Assert.DoesNotContain(
            "OrbModSuite.GameMcp.entity-display-names.tsv", resources);
    }

    [Fact]
    public void ToolEncodingUsesTheWorldPinnedCatalogRatherThanTheLatestLifecycle()
    {
        var uuid = Guid.NewGuid();
        var pinned = EntityIdentityCatalogSnapshot.Bound(
            41,
            new[]
            {
                new EntityIdentityName(uuid, "ResearchSO", "Pinned Name", "PinnedAsset"),
            });
        var later = EntityIdentityCatalogSnapshot.Bound(
            42,
            new[]
            {
                new EntityIdentityName(uuid, "ResearchSO", "Later Name", "LaterAsset"),
            });
        var previous = EntityIdentityCatalogPublication.Current;
        try
        {
            EntityIdentityCatalogPublication.Publish(later);

            var result = GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["uuid"] = uuid,
            }.Freeze()).WithEntityIdentities(pinned).ToProtocolResult();

            Assert.Contains("Pinned Name", result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Later", result.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            EntityIdentityCatalogPublication.Publish(previous);
        }
    }

    /// <summary>
    /// One answer, said once. The result used to carry a machine copy beside the page a caller
    /// reads, so every client decoded and truncated the same bytes twice.
    /// </summary>
    [Fact]
    public void ToolResultEmitsThePageOnceWithoutAStructuredDuplicate()
    {
        var large = new string('x', 80_000);
        var result = GameMcpToolExecution.Read(new GameMcpObjectBuilder
        {
            ["status"] = "available",
            ["large"] = large,
        }.Freeze()).ToProtocolResult();

        Assert.Null(result["structuredContent"]);
        Assert.Null(result["isError"]);
        var content = Assert.Single(result["content"]!.Values<JObject>());
        Assert.Equal("text", (string?)content["type"]);
        Assert.Equal("large: " + large, (string?)content["text"]);
    }

    [Fact]
    public void CanonicalEncoderPreservesWrittenEmptyArraysAndOmitsNullsAndEmptyObjects()
    {
        var nested = new GameMcpObjectBuilder
        {
            ["emptyArray"] = new GameMcpArrayBuilder(),
            ["nullValue"] = null,
        };
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["status"] = "available",
            ["emptyArray"] = new GameMcpArrayBuilder(),
            ["emptyObject"] = new GameMcpObjectBuilder(),
            ["emptyAfterFiltering"] = nested,
            ["nullValue"] = null,
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var result = Assert.IsType<JObject>(encoded);
        Assert.Equal(
            new[] { "status", "emptyArray", "emptyAfterFiltering" },
            result.Properties().Select(property => property.Name));
        Assert.Empty(result["emptyArray"]!);
        Assert.Empty(result["emptyAfterFiltering"]!["emptyArray"]!);
    }

    /// <summary>
    /// An asset id that is the player's word plus the word the block's own <c>category</c> states
    /// says nothing the lines beside it have not — six of one round's eleven <c>internalName</c>
    /// lines were exactly that. An id that adds a fact still ships whole.
    /// </summary>
    /// <remarks>
    /// The rule needs a category to read, so it lives where one is printed: the identity block
    /// <c>world_get</c> builds from this projection, which is the only caller left that names one.
    /// </remarks>
    [Fact]
    public void An_asset_id_that_is_only_the_name_and_the_category_is_not_printed_again()
    {
        var restating = Guid.Parse("e1a00000-0000-4000-8000-000000000001");
        var informative = Guid.Parse("e1a00000-0000-4000-8000-000000000002");
        var suffixed = Guid.Parse("e1a00000-0000-4000-8000-000000000003");
        var catalog = EntityIdentityCatalogSnapshot.Bound(77, new[]
        {
            new EntityIdentityName(restating, "RitualSO", "Strength", "StrengthRitual"),
            new EntityIdentityName(informative, "ResearchSO", "Reserve", "ReserveLevel"),
            new EntityIdentityName(suffixed, "ResearchSO", "Artistry", "ArtistryResearch"),
        });

        var blocks = new[] { restating, informative, suffixed }
            .Select(uuid => GameMcpTestHarness.Json(
                GameMcpEntityCatalog.Lookup(catalog, uuid).Freeze()))
            .ToArray();

        Assert.Equal("Strength", (string?)blocks[0]["name"]);
        Assert.Null(blocks[0]["internalName"]);
        Assert.Equal("ReserveLevel", (string?)blocks[1]["internalName"]);
        Assert.Null(blocks[2]["internalName"]);
    }

    /// <summary>
    /// An asset the game authors no word for is named by its asset name, in the one <c>name</c>
    /// cell, and the <c>internalName</c> line beside it goes: it would repeat that string
    /// character for character, which is the derivability rule the block already applies to every
    /// other id. <c>nameSource: asset</c> was a flag on a substitution; a name that is the only
    /// word there is substitutes for nothing, so there is nothing left to flag.
    /// </summary>
    /// <remarks>
    /// The fixture is the build's own nameless asset: the legacy Brewing Station, a
    /// <c>TooltipableObject</c> whose <c>displayName</c> and <c>description</c> are both authored
    /// empty. Every reference to it already printed the asset name, so this block was the surface
    /// that disagreed.
    /// </remarks>
    [Fact]
    public void LiveCatalogNamesAnUnwordedAssetByItsAssetName()
    {
        var station = Guid.Parse("d76565b1-8e2b-44fe-9cf3-995d6f666305");

        var block = GameMcpTestHarness.Json(GameMcpEntityCatalog.Lookup(
            GameMcpTestHarness.EntityCatalog, station).Freeze());

        Assert.Equal("BrewingStation", (string?)block["name"]);
        Assert.Null(block["nameSource"]);
        Assert.Null(block["hasDisplayName"]);
        Assert.Null(block["internalName"]);
        Assert.Equal("CraftingStructureSO", (string?)block["nativeType"]);
        Assert.Equal(
            "BrewingStation",
            GameMcpEntityHandle.Name(station, GameMcpTestHarness.EntityCatalog));
    }
}
