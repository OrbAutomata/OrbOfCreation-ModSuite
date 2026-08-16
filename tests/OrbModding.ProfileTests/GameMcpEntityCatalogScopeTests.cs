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
/// What <c>entity_catalog</c> lists, and what the trim was forbidden to touch.
/// </summary>
/// <remarks>
/// The listing is one page of one verb. The identity catalog behind it is four other things —
/// handle resolution, the name every reference prints, keyword resolution, and the explainer's
/// refusal arms — and a type leaving the page may not cost any of them a fact.
/// </remarks>
public sealed class GameMcpEntityCatalogScopeTests
{
    private static readonly IReadOnlyList<EntityIdentityName> BuildRows = LoadBuildRows();

    /// <summary>
    /// The verdict is on the type, and the types are this build's. A name nothing loads would be a
    /// rule about nothing, and the page would quietly keep listing whatever the typo meant.
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
    /// A type the world publishes rows for is off the page because those verbs carry it, never
    /// because it was called machinery. The two snapshot list variables are the case that proves
    /// the distinction is kept: they read as rosters and are world_list snapshot-loadouts, so they
    /// are absent from the withheld set on purpose and leave the listing by the other door.
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
        Assert.False(GameMcpEntityCatalogScope.Lists("AlchemySnapshotListVariable"));
        Assert.False(GameMcpEntityCatalogScope.Lists("EquipmentSnapshotListVariable"));
        Assert.False(GameMcpEntityCatalogScope.Lists("StructureSO"));
        Assert.False(GameMcpEntityCatalogScope.Lists("AttributeSO"));
    }

    /// <summary>
    /// The page is the remainder and nothing else, counted against the build rather than claimed —
    /// and the remainder is now nothing. Of 2,818 loaded ids the world publishes 2,295 and this
    /// build's machinery is 523, so no row is left for the page to have anything to say about.
    /// </summary>
    [Fact]
    public void TheListingIsTheRemainderTheWorldPublishesNoRowFor()
    {
        var listed = BuildRows.Count(row => GameMcpEntityCatalogScope.Lists(row.RuntimeType));
        var machinery = BuildRows.Count(row =>
            GameMcpEntityCatalogScope.IsMachinery(row.RuntimeType));
        var published = BuildRows.Count(row =>
            GameMcpEntityCapabilityMap.TryCategoryForNativeType(row.RuntimeType, out _));

        Assert.Equal(2818, BuildRows.Count);
        Assert.Equal(2295, published);
        Assert.Equal(523, machinery);
        Assert.Equal(0, listed);
        Assert.Equal(BuildRows.Count, published + machinery + listed);
    }

    /// <summary>
    /// Nothing is left, and each of the fourteen types that used to be left is named with the door
    /// it went out of.
    /// </summary>
    /// <remarks>
    /// Emptiness on its own is the weak claim: a rule that withheld everything would satisfy it and
    /// would have lost the 84 words the glossary and the stat groups carry. So the door is pinned
    /// per type. Eleven now have a world category, which means <c>world_list</c> pages them,
    /// <c>world_get</c> reads the sentence the game prints, and <c>world_search</c> finds them by
    /// name; three are machinery, which means nothing about them was ever readable and the page was
    /// printing an internal asset identifier. A later change that moved one type through the wrong
    /// door would keep the page empty and fail here.
    /// </remarks>
    [Fact]
    public void TheRemainderIsEmptyAndEveryTypeLeftByItsOwnDoor()
    {
        var kept = BuildRows
            .Where(row => GameMcpEntityCatalogScope.Lists(row.RuntimeType))
            .GroupBy(static row => row.RuntimeType, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count());

        Assert.Equal(new Dictionary<string, int>(StringComparer.Ordinal), kept);

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
                    : "still-listed",
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
    /// The page itself, not the rule: the two queries that used to answer — one with animation
    /// assets, one with the combat glossary — both answer with nothing, by two different doors.
    /// </summary>
    [Fact]
    public void ThePageWithholdsMachineryAndLeavesTheGlossaryToTheWorld()
    {
        var machinery = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog, "AnimationEffectSO", 0, 20).Freeze());
        var glossary = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog, "CombatStatusSO", 0, 20).Freeze());

        Assert.Equal(0, (int)machinery["total"]!);
        Assert.Empty(machinery["rows"]!);
        Assert.True(GameMcpEntityCatalogScope.IsMachinery("AnimationEffectSO"));

        Assert.Equal(0, (int)glossary["total"]!);
        Assert.Empty(glossary["rows"]!);
        Assert.False(GameMcpEntityCatalogScope.IsMachinery("CombatStatusSO"));
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType("CombatStatusSO", out _));
    }

    /// <summary>
    /// The other half of the page's own rule. A statistic is a published row with a name, a value,
    /// a display type and a group, and world_list, world_search and world_get all carry it; this
    /// page listing the same id under its identity alone said nothing those three had not, so it
    /// stops. The asset is still exactly as findable — by the word the screen prints, on the verb
    /// that has the facts.
    /// </summary>
    [Fact]
    public void ThePageLeavesTheWorldsOwnRowsToTheVerbsThatCarryThem()
    {
        var published = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog, "Hidden Component", 0, 20).Freeze());

        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType("AttributeSO", out _));
        Assert.Null(published["status"]);
        Assert.Equal(0, (int)published["total"]!);
        Assert.Empty(published["rows"]!);
    }

    /// <summary>
    /// No row on this page carries a <c>category</c> cell, and the page still projects a row for a
    /// type nobody has ruled on. Every row it can return is one the published world has no category
    /// for, so the column could only ever read the same constant, and a column that cannot vary is
    /// a byte per row spent saying what the verb's contract says once.
    /// </summary>
    /// <remarks>
    /// The fixture is synthetic on purpose. This build loads no unruled type any more, so a
    /// projection test written against it would be a test that could only pass by returning
    /// nothing — and it would stop guarding the projector the day a build loads something new. The
    /// two rows here are exactly that day: a type with no verdict is listed rather than dropped in
    /// silence, and the row it gets carries identity and native type and no category.
    /// </remarks>
    [Fact]
    public void TheRemainderRowsCarryNoConstantCategoryColumn()
    {
        var unruled = Guid.Parse("c1000000-0000-4000-8000-000000000001");
        var alsoUnruled = Guid.Parse("c2000000-0000-4000-8000-000000000001");
        var catalog = EntityIdentityCatalogSnapshot.Bound(5, new[]
        {
            new EntityIdentityName(unruled, "SomethingNewSO", "Fresh Thing", "FreshThingAsset"),
            new EntityIdentityName(alsoUnruled, "SomethingNewSO", "Other Thing", "OtherThingAsset"),
            new EntityIdentityName(
                Guid.Parse("c3000000-0000-4000-8000-000000000001"),
                "AnimationEffectSO",
                "Sparkle",
                "SomethingNewSparkle"),
        });

        Assert.True(GameMcpEntityCatalogScope.Lists("SomethingNewSO"));

        var page = GameMcpTestHarness.Json(
            GameMcpEntityCatalog.Search(catalog, "SomethingNew", 0, 20).Freeze());

        var rows = page["rows"]!.Values<JObject>().ToArray();
        Assert.Equal(2, (int)page["total"]!);
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Null(row!["category"]));
        Assert.All(rows, row => Assert.Equal("SomethingNewSO", (string?)row!["nativeType"]));
        Assert.Equal(new[] { "Fresh Thing", "Other Thing" }, rows
            .Select(static row => (string?)row!["name"])
            .ToArray());
    }

    /// <summary>
    /// M21.2, consumer 1. A handle is a prefix unique across every loaded id, and the trim took no
    /// id out of the set it is unique across: the withheld asset still resolves from its handle,
    /// and a prefix it shares with a listed id is still ambiguous rather than quietly resolving to
    /// the survivor.
    /// </summary>
    [Fact]
    public void AWithheldTypesIdStillResolvesFromItsHandle()
    {
        var brewing = Guid.Parse("d76565b1-8e2b-44fe-9cf3-995d6f666305");
        var link = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");

        Assert.False(GameMcpEntityCatalogScope.Lists("PrerequisiteLinkSO"));
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
    /// M21.2, consumer 2. The name attachment reads the snapshot, never the page: a reference to a
    /// withheld id still prints the word the game authors for it, and a nameless one still says its
    /// asset id rather than passing for a row whose name went missing.
    /// </summary>
    [Fact]
    public void AWithheldTypesIdStillNamesItselfWhereverItIsReferenced()
    {
        var track = Guid.Parse("16b555ff-3212-4e27-9255-d2a9431602d1");
        var link = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");

        Assert.False(GameMcpEntityCatalogScope.Lists("MusicTrackSO"));
        Assert.Equal(
            "Exploring the Darkness",
            GameMcpEntityHandle.Name(track, GameMcpTestHarness.EntityCatalog));
        Assert.Equal(
            "InventoryUnlocked",
            GameMcpEntityHandle.Name(link, GameMcpTestHarness.EntityCatalog));
    }

    /// <summary>
    /// M21.2, consumer 3. Keyword resolution reads the same snapshot rows. It resolves a type
    /// asset's id to its word, and it would keep doing so for a withheld type, so the trim cannot
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

        Assert.False(GameMcpEntityCatalogScope.Lists("MusicTrackSO"));
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
