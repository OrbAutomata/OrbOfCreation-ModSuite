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

public sealed class GameMcpLoadoutTests
{
    private static readonly Guid PlayerId = Guid.Parse("fb000000-0000-0000-0000-000000000001");
    private static readonly Guid SpellId = Guid.Parse("fb000000-0000-0000-0000-000000000002");
    private static readonly Guid RecipeId = Guid.Parse("fb000000-0000-0000-0000-000000000003");
    private static readonly Guid EquipmentId = Guid.Parse("fb000000-0000-0000-0000-000000000004");
    private static readonly Guid AlchemyId = Guid.Parse("fb000000-0000-0000-0000-000000000005");
    private static readonly Guid SnapshotId = Guid.Parse("fb000000-0000-0000-0000-000000000006");

    [Fact]
    public void Tool_exposes_only_the_visible_player_and_snapshot_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_loadout");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode" },
            tool["inputSchema"]!["required"]!.Values<string>());

        // A loadout and a snapshot are both live objects the asset catalog never publishes, so the
        // wire names them the way the screen does: a position on the bar, and a section plus a
        // slot, both counted from 1.
        Assert.Null(tool["inputSchema"]!["properties"]!["uuid"]);
        Assert.Equal(1, (int)tool["inputSchema"]!["properties"]!["loadout"]!["minimum"]!);
        Assert.Equal(1, (int)tool["inputSchema"]!["properties"]!["slot"]!["minimum"]!);
        Assert.Equal(new[]
        {
            "select", "set_section", "rename", "next_icon", "next_color",
            "snapshot_save", "snapshot_load", "snapshot_clear",
        }, tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
    }

    [Fact]
    public void Mode_specific_fields_are_required_and_unrelated_fields_are_rejected()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "snapshot_load",
                    ["section"] = "equipment",
                },
            }));
        var extra = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "select",
                    ["loadout"] = 1,
                    ["name"] = "ignored",
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'slot' is missing for mode 'snapshot_load'", GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'name' is accepted only for mode 'rename'", GameMcpTestHarness.Page(extra));
    }

    [Fact]
    public void Detail_names_every_saved_entry_and_snapshot_slot()
    {
        var world = World(selected: true, populatedSnapshot: true);
        var playerResponse = Json(GameMcpWorldQuery.GetRow(Context(world, 901),
            "player-loadouts", PlayerId.ToString("D")).Freeze(), world);
        var snapshotResponse = Json(GameMcpWorldQuery.GetRow(Context(world, 901),
            "snapshot-loadouts", SnapshotId.ToString("D")).Freeze(), world);
        var player = Assert.IsType<JObject>(playerResponse["row"]);
        var snapshot = Assert.IsType<JObject>(snapshotResponse["row"]);

        // A loadout and a snapshot list are live objects the catalog never publishes, so a printed
        // handle for either resolved for no tool. The name is what the screen says and the position
        // is what the verbs take, so neither row prints an id at all.
        Assert.Null(player["uuid"]);
        Assert.Null(player["entityId"]);
        Assert.Equal("Boss setup", (string?)player["name"]);
        Assert.Equal("Beam Burst", (string?)player["sections"]!["spells"]![0]!["spell"]!["name"]);
        Assert.Equal("Aegis", (string?)player["sections"]!["equipment"]!["entries"]![0]!["name"]);
        Assert.Equal(2, (int)player["sections"]!["equipment"]!["entries"]![0]!["amount"]!);
        Assert.Equal("Clarity", (string?)player["sections"]!["alchemy"]!["entries"]![0]!["name"]);
        Assert.Null(snapshot["uuid"]);
        Assert.True((bool)snapshot["slots"]![0]!["populated"]!);
        Assert.Equal("Aegis", (string?)snapshot["slots"]![0]!["entries"]![0]!["name"]);
    }

    [Fact]
    public void A_loadout_that_saved_nothing_still_carries_every_section_as_an_empty_list()
    {
        var world = World(selected: true, populatedSnapshot: false, anySavedEntries: false);
        var response = Json(GameMcpWorldQuery.GetRow(Context(world, 902),
            "player-loadouts", PlayerId.ToString("D")).Freeze(), world);
        var sections = Assert.IsType<JObject>(response["row"]!["sections"]);

        Assert.Empty(sections["spells"]!.Values<JObject>());
        Assert.Empty(sections["equipment"]!["entries"]!.Values<JObject>());
        Assert.Empty(sections["alchemy"]!["entries"]!.Values<JObject>());
        Assert.True((bool)sections["equipment"]!["saved"]!);
    }

    /// <summary>
    /// The game's own swap gate is a state the published world already accounts for, so it reaches
    /// the wire as one. `ERR_REFUSED` is reserved for a refusal the world cannot explain, and this
    /// row explains it: the caller reads `canSelect: no` beside the sentence.
    /// </summary>
    [Fact]
    public void A_loadout_the_manager_will_not_swap_to_is_a_state_rather_than_an_unexplained_no()
    {
        var world = World(selected: false, populatedSnapshot: false, canSwitchNow: false);
        var response = Json(GameMcpWorldQuery.GetRow(Context(world, 903),
            "player-loadouts", PlayerId.ToString("D")).Freeze(), world);
        var row = Assert.IsType<JObject>(response["row"]);

        Assert.False((bool)row["canSelect"]!);
        Assert.Equal("ERR_STATE", (string?)row["reasonCode"]);
    }

    [Fact]
    public void Settled_deltas_use_the_observed_player_and_snapshot_states()
    {
        var before = World(selected: false, populatedSnapshot: true);
        var after = World(selected: true, populatedSnapshot: false);
        var select = new GameMcpCommand(1, GameMcpCommandKind.Loadout,
            9, 3, "select", PlayerId, Guid.Empty, "PlayerLoadout",
            1, string.Empty, string.Empty, false,
            frameContext: Context(before, 91));
        var clear = new GameMcpCommand(2, GameMcpCommandKind.Loadout,
            9, 3, "snapshot_clear", SnapshotId, Guid.Empty,
            "EquipmentSnapshotListVariable",
            1, string.Empty, string.Empty, false,
            frameContext: Context(before, 91));

        var selected = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(after, 92), select, GameMcpCommandResult.Committed("committed", 9, 3)), after);
        var cleared = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(after, 92), clear, GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.False((bool)selected["selected"]!["before"]!);
        Assert.True((bool)selected["selected"]!["after"]!);
        Assert.Equal("Boss setup", (string?)selected["loadout"]!["name"]);
        Assert.Null(selected["loadout"]!["uuid"]);
        // The mutation answers the way the read rows do: a loadout, a snapshot list, and a saved
        // spell are live objects the catalog never publishes, so a post-state that printed one of
        // their ids would be the last place a caller could find an address no tool accepts.
        Assert.Equal("Boss setup", (string?)selected["name"]);
        Assert.Null(selected["uuid"]);
        Assert.Null(selected["loadout"]!["sections"]!["spells"]![0]!["instanceUuid"]);
        Assert.Equal(
            "Beam Burst",
            (string?)selected["loadout"]!["sections"]!["spells"]![0]!["spell"]!["name"]);
        Assert.Null(cleared["uuid"]);
        Assert.Equal(1, (int)cleared["snapshot"]!["slot"]!);
        Assert.False((bool)cleared["snapshot"]!["populated"]!);
        Assert.Null(cleared["snapshot"]!["entries"]);
    }

    /// <summary>
    /// A player loadout and a snapshot list are both live objects the asset catalog never
    /// publishes. The wire names them the way the screen does, and a refusal says how many there
    /// actually are.
    /// </summary>
    [Fact]
    public void A_position_names_the_loadout_and_a_section_names_the_snapshot_list()
    {
        var world = World(selected: true, populatedSnapshot: true);

        Assert.True(GameMcpWorldQuery.TryPlayerLoadout(world, 1, out var player, out _));
        Assert.Equal(PlayerId, player);
        Assert.False(GameMcpWorldQuery.TryPlayerLoadout(world, 4, out _, out var absent));
        Assert.Equal("There is no loadout 4; you have loadouts 1 to 1.", absent);

        Assert.True(GameMcpWorldQuery.TrySnapshotList(world, "equipment", out var list, out _));
        Assert.Equal(SnapshotId, list);
        Assert.False(GameMcpWorldQuery.TrySnapshotList(world, "alchemy", out _, out var missing));
        Assert.Equal("The game is not showing the Alchemy snapshots right now.", missing);
    }

    /// <summary>
    /// The snapshot list rows count the way the snapshot verbs do. They used to print the internal
    /// array index, so a caller who read a row and passed its number back addressed the row above.
    /// </summary>
    [Fact]
    public void Snapshot_rows_count_slots_the_way_the_snapshot_verbs_take_them()
    {
        var world = World(selected: true, populatedSnapshot: true);
        var context = Context(world, 93);

        var slot = Assert.Single(
            Json(GameMcpWorldQuery.ListRows(context, "snapshot-slots", 0, 10).Freeze(), world)
                ["rows"]!.Values<JObject>());
        Assert.Equal(1, (int)slot["slot"]!);
        Assert.True((bool)slot["populated"]!);

        var entry = Assert.Single(
            Json(GameMcpWorldQuery.ListRows(context, "snapshot-entries", 0, 10).Freeze(), world)
                ["rows"]!.Values<JObject>());
        Assert.Equal(1, (int)entry["slot"]!);
        Assert.Equal("Aegis", (string?)entry["entry"]!["name"]);
    }

    /// <summary>
    /// Swapping loadouts re-equips the spell bar, and one select unequipped five spells with no
    /// word about it in the answer: the only trace was <c>spells: none</c> inside the selected
    /// loadout's own contents, which reads as what that loadout stores rather than as what the
    /// player now has. The bar is the player's, so the swap names it.
    /// </summary>
    [Fact]
    public void Selecting_a_loadout_reports_what_it_did_to_the_spell_bar()
    {
        var before = World(selected: false, populatedSnapshot: false, equippedSpells: 2);
        var after = World(selected: true, populatedSnapshot: false, equippedSpells: 0);
        var select = new GameMcpCommand(1, GameMcpCommandKind.Loadout,
            9, 3, "select", PlayerId, Guid.Empty, "PlayerLoadout",
            1, string.Empty, string.Empty, false,
            frameContext: Context(before, 94));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(after, 95), select, GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(2, (int)delta["spellBar"]!["equipped"]!["before"]!);
        Assert.Equal(0, (int)delta["spellBar"]!["equipped"]!["after"]!);
        Assert.Equal(
            new[] { "Beam Burst", "Beam Burst" },
            delta["spellBar"]!["unequipped"]!.Select(row => (string?)row!["name"]).ToArray());
        Assert.Null(delta["spellBar"]!["equippedNow"]);
    }

    /// <summary>
    /// A select that leaves the bar exactly as it was says nothing about it. A fact the verb did
    /// not move is not part of its post-state.
    /// </summary>
    [Fact]
    public void A_select_that_leaves_the_bar_alone_says_nothing_about_it()
    {
        var before = World(selected: false, populatedSnapshot: false, equippedSpells: 2);
        var after = World(selected: true, populatedSnapshot: false, equippedSpells: 2);
        var select = new GameMcpCommand(1, GameMcpCommandKind.Loadout,
            9, 3, "select", PlayerId, Guid.Empty, "PlayerLoadout",
            1, string.Empty, string.Empty, false,
            frameContext: Context(before, 96));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(after, 97), select, GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Null(delta["spellBar"]);
    }

    private static GameWorldState World(
        bool selected,
        bool populatedSnapshot,
        bool anySavedEntries = true,
        bool? canSwitchNow = null,
        int equippedSpells = 0)
    {
        var entries = anySavedEntries
            ? new[]
            {
                new WorldLoadoutEntry(PlayerId, WorldLoadoutEntryKind.Spell, SpellId, RecipeId, 1),
                new WorldLoadoutEntry(PlayerId, WorldLoadoutEntryKind.Equipment,
                    EquipmentId, Guid.Empty, 2),
                new WorldLoadoutEntry(PlayerId, WorldLoadoutEntryKind.Alchemy,
                    AlchemyId, Guid.Empty, 3),
            }
            : Array.Empty<WorldLoadoutEntry>();
        var snapshotEntries = populatedSnapshot
            ? new[] { new WorldSnapshotEntry(SnapshotId, 0, EquipmentId, 2) }
            : Array.Empty<WorldSnapshotEntry>();
        var identities = new[]
        {
            new EntityIdentityName(RecipeId, "SpellRecipeSO", "Beam Burst", "beam_burst"),
            new EntityIdentityName(EquipmentId, "EquipmentSO", "Aegis", "aegis"),
            new EntityIdentityName(AlchemyId, "AlchemyRecipeSO", "Clarity", "clarity"),
            new EntityIdentityName(SnapshotId, "EquipmentSnapshotListVariable",
                "Equipment Snapshots", "equipment_snapshots"),
        }.OrderBy(row => row.EntityId).ToArray();
        var slots = new WorldSpellSlot[3];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = index < equippedSpells
                ? new WorldSpellSlot(
                    index,
                    Guid.Parse("fb000000-0000-0000-0000-0000000000" + (10 + index)),
                    RecipeId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: false,
                    castReady: true,
                    chargeAvailable: false,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero)
                : new WorldSpellSlot(
                    index,
                    Guid.Empty,
                    occupied: false,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: false,
                    castReady: false,
                    chargeAvailable: false,
                    resourcesCovered: false,
                    currentCharges: 0,
                    maximumCharges: 0,
                    cooldownRemaining: BigDouble.Zero);
        }
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(slots),
            PlayerLoadouts = PublicationTable<WorldPlayerLoadout>.Create(new[]
            {
                new WorldPlayerLoadout(PlayerId, "Boss setup", selected,
                    savesEquipment: true, savesAlchemy: true, icon: 2, color: 4,
                    canSwitchNow: canSwitchNow ?? !selected),
            }),
            PlayerLoadoutEntries = PublicationTable<WorldLoadoutEntry>.Create(entries),
            SnapshotLoadouts = PublicationTable<WorldSnapshotLoadout>.Create(new[]
            {
                new WorldSnapshotLoadout(SnapshotId,
                    WorldSnapshotLoadoutKind.Equipment, slots: 1),
            }),
            SnapshotSlots = PublicationTable<WorldSnapshotSlot>.Create(new[]
            {
                new WorldSnapshotSlot(SnapshotId, 0, populatedSnapshot),
            }),
            SnapshotEntries = PublicationTable<WorldSnapshotEntry>.Create(snapshotEntries),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("loadouts", WorldCategoryOutcome.Collected,
                    2, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));

    private static GameMcpFrameContext Context(GameWorldState world, ulong generation)
    {
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(generation));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }
}
