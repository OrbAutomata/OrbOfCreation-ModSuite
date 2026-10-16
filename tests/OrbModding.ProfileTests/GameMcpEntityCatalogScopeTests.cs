using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Which of this build's loaded types are its own internal machinery, and what the trim that
/// established them was forbidden to touch.
/// </summary>
/// <remarks>
/// The verdict is one classifier. The identity catalog it reads is four other things — handle
/// resolution, the name every reference prints, keyword resolution, and the explainer's refusal
/// arms — and a type being called machinery may not cost any of them a fact.
/// </remarks>
public sealed class GameMcpEntityCatalogScopeTests
{
    private static readonly IReadOnlyList<EntityIdentityName> BuildRows = LoadBuildRows();

    /// <summary>
    /// The verdict is on the type, and the types are this build's. A name nothing loads would be a
    /// rule about nothing, and whatever the typo meant would quietly stop being machinery.
    /// </summary>
    [Fact]
    public void EveryWithheldTypeIsATypeThisBuildActuallyLoads()
    {
        var loaded = new HashSet<string>(
            BuildRows.Select(static row => row.RuntimeType),
            StringComparer.Ordinal);

        var unknown = GameMcpEntityCatalogScope.InternalOnlyTypes
            .Where(type => !loaded.Contains(type))
            .ToArray();

        Assert.Empty(unknown);
        Assert.Equal(82, GameMcpEntityCatalogScope.InternalOnlyTypes.Count);
        Assert.Equal(
            GameMcpEntityCatalogScope.InternalOnlyTypes.Count,
            GameMcpEntityCatalogScope.InternalOnlyTypes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A type the world publishes rows for is read on those verbs, never called machinery. The two
    /// snapshot list variables are the case that proves the distinction is kept: they read as
    /// rosters and are world_list snapshot-loadouts, so they are absent from the withheld set on
    /// purpose rather than by an oversight that would have silenced them.
    /// </summary>
    [Fact]
    public void NoTypeTheWorldPublishesIsWithheld()
    {
        var projected = GameMcpEntityCatalogScope.InternalOnlyTypes
            .Where(static type =>
                GameMcpEntityCapabilityMap.TryCategoryForNativeType(type, out _))
            .ToArray();

        Assert.Empty(projected);
        Assert.False(GameMcpEntityCatalogScope.IsMachinery("AlchemySnapshotListVariable"));
        Assert.False(GameMcpEntityCatalogScope.IsMachinery("EquipmentSnapshotListVariable"));
        Assert.False(GameMcpEntityCatalogScope.IsMachinery("StructureSO"));
        Assert.False(GameMcpEntityCatalogScope.IsMachinery("AttributeSO"));
    }

    /// <summary>
    /// Every loaded id is one of two things, counted against the build rather than claimed: of
    /// 2,818 the world publishes 2,295 and this build's machinery is the other 523. Nothing falls
    /// between them, which is the fact that retired the page for what fell between them.
    /// </summary>
    /// <remarks>
    /// The two sets are disjoint by the test above and exhaustive by the sum here, so a type added
    /// to neither — a build loading something no verdict covers — fails this rather than passing
    /// unnoticed into a page nobody reads.
    /// </remarks>
    [Fact]
    public void EveryLoadedIdIsEitherPublishedOrThisBuildsMachinery()
    {
        var machinery = BuildRows.Count(row =>
            GameMcpEntityCatalogScope.IsMachinery(row.RuntimeType));
        var published = BuildRows.Count(row =>
            GameMcpEntityCapabilityMap.TryCategoryForNativeType(row.RuntimeType, out _));

        Assert.Equal(2818, BuildRows.Count);
        Assert.Equal(2295, published);
        Assert.Equal(523, machinery);
        Assert.Equal(BuildRows.Count, published + machinery);
    }

    /// <summary>
    /// Each of the fourteen types the retired page last carried is named with the door it went out
    /// of.
    /// </summary>
    /// <remarks>
    /// The sum above proves nothing is left over; it does not prove the right things left. A rule
    /// that called everything machinery would satisfy the sum and would have lost the 84 words the
    /// glossary and the stat groups carry. So the door is pinned per type. Eleven now have a world
    /// category, which means <c>world_list</c> pages them, <c>world_get</c> reads the sentence the
    /// game prints, and <c>world_search</c> finds them by name; three are machinery, which means
    /// nothing about them was ever readable and an internal asset identifier was all a row could
    /// have said. A later change that moved one type through the wrong door still sums to 2,818 and
    /// fails here.
    /// </remarks>
    [Fact]
    public void EveryTypeTheRetiredPageCarriedLeftByItsOwnDoor()
    {
        var doors = new[]
        {
            "AttributeGroupSO", "CharacterActionSO", "CharacterAttributeSO", "CharacterModifierSO",
            "CharacterTypeSO", "CombatStatusSO", "DamageTypeSO", "DisplayTypeSO", "EnchantmentSO",
            "GlyphTypeSO", "RuneStoneSO", "ConditionalTextList", "CraftingStructureSO",
            "PlayerCharacter",
        }.ToDictionary(
            static type => type,
            static type => GameMcpEntityCatalogScope.IsMachinery(type)
                ? "machinery"
                : GameMcpEntityCapabilityMap.TryCategoryForNativeType(type, out var category)
                    ? category
                    : "neither",
            StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AttributeGroupSO"] = "attribute-groups",
                ["CharacterActionSO"] = "character-actions",
                ["CharacterAttributeSO"] = "character-attributes",
                ["CharacterModifierSO"] = "character-modifiers",
                ["CharacterTypeSO"] = "character-types",
                ["CombatStatusSO"] = "status-effects",
                ["DamageTypeSO"] = "damage-types",
                ["DisplayTypeSO"] = "display-types",
                ["EnchantmentSO"] = "enchantments",
                ["GlyphTypeSO"] = "glyph-types",
                ["RuneStoneSO"] = "rune-stones",
                ["ConditionalTextList"] = "machinery",
                ["CraftingStructureSO"] = "machinery",
                ["PlayerCharacter"] = "machinery",
            },
            doors);
    }

    /// <summary>
    /// M21.2, consumer 1. A handle is a prefix unique across every loaded id, and the trim took no
    /// id out of the set it is unique across: a machinery asset still resolves from its handle, and
    /// a prefix it shares with a published id is still ambiguous rather than quietly resolving to
    /// the published one.
    /// </summary>
    [Fact]
    public void AWithheldTypesIdStillResolvesFromItsHandle()
    {
        var brewing = Guid.Parse("d76565b1-8e2b-44fe-9cf3-995d6f666305");
        var link = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");

        Assert.True(GameMcpEntityCatalogScope.IsMachinery("PrerequisiteLinkSO"));
        Assert.Equal(
            GameMcpEntityHandle.ResolutionOutcome.Resolved,
            GameMcpEntityHandle.Resolve(
                "b45055", GameMcpTestHarness.EntityCatalog, out var resolvedLink, out _));
        Assert.Equal(link, resolvedLink);
        Assert.Equal(
            GameMcpEntityHandle.ResolutionOutcome.Resolved,
            GameMcpEntityHandle.Resolve(
                "d76565", GameMcpTestHarness.EntityCatalog, out var resolvedBrewing, out _));
        Assert.Equal(brewing, resolvedBrewing);
    }

    /// <summary>
    /// M21.2, consumer 2. The name attachment reads the snapshot, never the verdict: a reference
    /// to a machinery id still prints the word the game authors for it, and a nameless one still
    /// says its asset id rather than passing for a row whose name went missing.
    /// </summary>
    [Fact]
    public void AWithheldTypesIdStillNamesItselfWhereverItIsReferenced()
    {
        var track = Guid.Parse("16b555ff-3212-4e27-9255-d2a9431602d1");
        var link = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");

        Assert.True(GameMcpEntityCatalogScope.IsMachinery("MusicTrackSO"));
        Assert.Equal(
            "Exploring the Darkness",
            GameMcpEntityHandle.Name(track, GameMcpTestHarness.EntityCatalog));
        Assert.Equal(
            "InventoryUnlocked",
            GameMcpEntityHandle.Name(link, GameMcpTestHarness.EntityCatalog));
    }

    /// <summary>
    /// M21.2, consumer 3. Keyword resolution reads the same snapshot rows. It resolves a type
    /// asset's id to its word, and it keeps doing so for a machinery type, so the trim cannot
    /// silence a word on a published row.
    /// </summary>
    [Fact]
    public void AWithheldTypesWordStillResolvesAsAKeyword()
    {
        var owner = Guid.Parse("c0000000-0000-4000-8000-000000000001");
        var withheldType = Guid.Parse("c0000000-0000-4000-8000-000000000002");
        var catalog = EntityIdentityCatalogSnapshot.Bound(5, new[]
        {
            new EntityIdentityName(owner, "GlyphSO", "Ember", "glyphEmber"),
            new EntityIdentityName(withheldType, "MusicTrackSO", "Nocturne", "trackNocturne"),
        });
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = catalog,
            EntityKeywords = PublicationTable<WorldEntityKeyword>.Create(new[]
            {
                new WorldEntityKeyword(
                    owner,
                    WorldKeywordOwnerKind.Glyph,
                    WorldKeywordSource.PrimaryType,
                    0,
                    withheldType),
            }),
        };

        Assert.True(GameMcpEntityCatalogScope.IsMachinery("MusicTrackSO"));
        Assert.Equal("Nocturne", GameMcpKeywordIndex.Build(world).Line(owner));
    }

    private static IReadOnlyList<EntityIdentityName> LoadBuildRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv");
        return File.ReadLines(path)
            .Skip(1)
            .Where(static line => line.Length > 0)
            .Select(static line => line.Split('\t'))
            .Select(static cells => new EntityIdentityName(
                Guid.Parse(cells[0]), cells[2], string.Empty, cells[1]))
            .ToArray();
    }
}
