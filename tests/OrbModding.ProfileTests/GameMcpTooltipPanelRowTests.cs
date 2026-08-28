using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The casting-bar and passive panels read like every other panel on the page, and the slot they
/// carry is the loadout's answer rather than the sibling index in their path.
/// </summary>
public sealed class GameMcpTooltipPanelRowTests
{
    private static readonly Guid Cube = Guid.Parse("d0a00000-0000-4000-8000-000000000001");
    private static readonly Guid BeamBurst = Guid.Parse("d0b00000-0000-4000-8000-000000000001");
    private static readonly Guid Propagation = Guid.Parse("d0c00000-0000-4000-8000-000000000001");
    private static readonly Guid Resonance = Guid.Parse("d0d00000-0000-4000-8000-000000000001");
    private static readonly Guid Ferocity = Guid.Parse("d0e00000-0000-4000-8000-000000000001");

    /// <summary>
    /// The whole panel, line for line, against round eleven's <c>[path | name]</c>. Beam Burst sits
    /// in slot 7 behind an empty slot 6, which is the pair that made the path's <c>(Clone)[6]</c>
    /// read as a slot number and cost a refused cast.
    /// </summary>
    [Fact]
    public void The_casting_bar_panel_names_the_spell_and_the_slot_the_cast_verb_takes()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "scene: Main",
                "pathRoot: Canvas[0]/ContentArea[2]",
                "rows 1/1:",
                "  pathPrefix: MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                "  elements 3",
                "  [id | name | path | slot]",
                "  d0a000 | Ralochs's Cube | SpellButtonBottomBar(Clone)[0] | 1",
                "  d0c000 | Propagation | SpellButtonBottomBar(Clone)[3] | 4",
                "  d0b000 | Beam Burst | SpellButtonBottomBar(Clone)[6] | 7",
            }),
            Render(Panel(
                "MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                Row("SpellButtonBottomBar(Clone)[0]", "Ralochs's Cube", Cube),
                Row("SpellButtonBottomBar(Clone)[3]", "Propagation", Propagation),
                Row("SpellButtonBottomBar(Clone)[6]", "Beam Burst", BeamBurst))));
    }

    /// <summary>
    /// A passive wears no slot: the loadout has positions for spells and none for passives, so the
    /// panel carries the three columns its siblings carry and no fourth.
    /// </summary>
    [Fact]
    public void The_passive_panel_names_what_it_shows_and_claims_no_slot()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "scene: Main",
                "pathRoot: Canvas[0]/ContentArea[2]",
                "rows 1/1:",
                "  pathPrefix: PassiveList[0]",
                "  pathComponent: PassiveAbilityItem(Clone)",
                "  elements 2",
                "  [id | name | path]",
                "  d0d000 | Resonance | [0]",
                "  d0e000 | Ferocity | [1]",
            }),
            Render(Folded(
                "PassiveList[0]",
                ("PassiveAbilityItem(Clone)[0]", "Resonance", Resonance),
                ("PassiveAbilityItem(Clone)[1]", "Ferocity", Ferocity))));
    }

    /// <summary>
    /// A control the game assigns no entity keeps exactly the two columns it always had, beside
    /// rows that do carry an id.
    /// </summary>
    [Fact]
    public void A_row_about_no_entity_carries_no_id_and_no_slot()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "scene: Main",
                "pathRoot: Canvas[0]/ContentArea[2]",
                "rows 1/1:",
                "  pathPrefix: MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                "  elements 2",
                "  [id | name | path | slot]",
                "  d0a000 | Ralochs's Cube | SpellButtonBottomBar(Clone)[0] | 1",
                "  - | Cast settings | SettingsButton[1] | -",
            }),
            Render(Panel(
                "MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                Row("SpellButtonBottomBar(Clone)[0]", "Ralochs's Cube", Cube),
                Row("SettingsButton[1]", "Cast settings", Guid.Empty))));
    }

    /// <summary>
    /// Two slots holding one recipe is two answers to "which slot is this button", so every button
    /// showing that recipe says neither. Naming one would send a cast to the position the reader
    /// was not looking at, which is the exact failure the slot column exists to end.
    /// </summary>
    [Fact]
    public void A_recipe_two_slots_hold_leaves_both_buttons_without_a_slot()
    {
        var world = World((0, Cube), (1, Cube), (6, BeamBurst));

        Assert.False(GameMcpTooltipPanelRow.TrySoleSpellSlot(world, Cube, out _));
        Assert.True(GameMcpTooltipPanelRow.TrySoleSpellSlot(world, BeamBurst, out var beam));
        Assert.Equal(6, beam);

        var row = Encoded(GameMcpTooltipPanelRow.Project("Button[0]", "Ralochs's Cube", Cube, world));
        Assert.Null(row["slot"]);
        Assert.Equal(GameMcpEntityHandle.Format(Cube), (string?)row["uuid"]);
    }

    /// <summary>
    /// A spell shown where the loadout does not hold it — the spellbook, a recipe page — is a spell
    /// with no position, and so is every row on a screen read with no world published at all.
    /// </summary>
    [Fact]
    public void A_spell_no_slot_holds_carries_its_id_and_no_slot()
    {
        var world = World((0, Cube));
        Assert.False(GameMcpTooltipPanelRow.TrySoleSpellSlot(world, BeamBurst, out _));

        foreach (var state in new GameWorldState?[] { world, null })
        {
            var row = Encoded(
                GameMcpTooltipPanelRow.Project("Button[0]", "Beam Burst", BeamBurst, state));
            Assert.Null(row["slot"]);
            Assert.Equal(GameMcpEntityHandle.Format(BeamBurst), (string?)row["uuid"]);
        }
    }

    /// <summary>
    /// An empty slot holds no recipe, so it can never be the answer for one: the game's zero Guid
    /// is not an address, and a row that joined on it would claim the empty position.
    /// </summary>
    [Fact]
    public void An_empty_slot_is_never_the_answer_for_a_recipe()
    {
        var world = World((0, Cube), (5, Guid.Empty));

        Assert.False(GameMcpTooltipPanelRow.TrySoleSpellSlot(world, Guid.Empty, out _));
        Assert.True(GameMcpTooltipPanelRow.TrySoleSpellSlot(world, Cube, out var cube));
        Assert.Equal(0, cube);
    }

    /// <summary>
    /// One panel is one component repeated with a different index, and the component was re-typed
    /// per row inside a table that already names what its rows share — 1,210 bytes of one round.
    /// The panel names it once and each row keeps its own bracket index, brackets included: a bare
    /// number in a path column beside a slot column is the confusion the slot column exists to end.
    /// </summary>
    [Fact]
    public void A_panel_of_one_component_names_it_once_and_leaves_each_row_its_index()
    {
        Assert.True(GameMcpTooltipPanelRow.TrySharedComponent(
            new[]
            {
                "SpellButtonBottomBar(Clone)[0]",
                "SpellButtonBottomBar(Clone)[3]",
                "SpellButtonBottomBar(Clone)[6]",
            },
            out var component));
        Assert.Equal("SpellButtonBottomBar(Clone)", component);

        Assert.Equal(
            string.Join('\n', new[]
            {
                "scene: Main",
                "pathRoot: Canvas[0]/ContentArea[2]",
                "rows 1/1:",
                "  pathPrefix: MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                "  pathComponent: SpellButtonBottomBar(Clone)",
                "  elements 3",
                "  [id | name | path | slot]",
                "  d0a000 | Ralochs's Cube | [0] | 1",
                "  d0c000 | Propagation | [3] | 4",
                "  d0b000 | Beam Burst | [6] | 7",
            }),
            Render(Folded(
                "MainContentContainer[2]/CastingBar[3]/SmallSpellList[0]",
                ("SpellButtonBottomBar(Clone)[0]", "Ralochs's Cube", Cube),
                ("SpellButtonBottomBar(Clone)[3]", "Propagation", Propagation),
                ("SpellButtonBottomBar(Clone)[6]", "Beam Burst", BeamBurst))));
    }

    /// <summary>
    /// The fold is per panel and it turns on the rows really being one component. A panel mixing
    /// components keeps every row's own segment, and a panel where saying the component once costs
    /// more than repeating it keeps them too.
    /// </summary>
    [Fact]
    public void A_panel_of_mixed_or_cheap_components_keeps_every_row_its_own_segment()
    {
        Assert.False(GameMcpTooltipPanelRow.TrySharedComponent(
            new[] { "SpellButtonBottomBar(Clone)[0]", "SettingsButton[1]" }, out _));

        // Two rows of `Row` buy back four characters and the line costs fifteen.
        Assert.False(GameMcpTooltipPanelRow.TrySharedComponent(
            new[] { "Row[0]", "Row[1]" }, out _));

        // A segment with no bracketed index is not an instance of anything.
        Assert.False(GameMcpTooltipPanelRow.TrySharedComponent(
            new[] { "Header", "Header" }, out _));

        // One element is a panel with nothing to share.
        Assert.False(GameMcpTooltipPanelRow.TrySharedComponent(
            new[] { "SpellButtonBottomBar(Clone)[0]" }, out _));
    }

    /// <summary>
    /// Every row is addressed by the shortest tail of its path no other live element answers to,
    /// whether it carries a uuid or not: the address only has to name one element, and the
    /// Canvas-rooted chain around it was 23% of one round's whole screen-elements wire.
    /// </summary>
    [Fact]
    public void A_row_is_addressed_by_the_shortest_tail_that_names_one_element()
    {
        var live = new[]
        {
            "Canvas[0]/ContentArea[2]/ScreenContent[2]/Panel[0]/NumberVarPlain[0]",
            "Canvas[0]/ContentArea[2]/ScreenContent[2]/Other[1]/Label[0]",
        };

        Assert.Equal("NumberVarPlain[0]", GameMcpTooltipPanelRow.ShortestUnique(live[0], live));
        Assert.Equal("Label[0]", GameMcpTooltipPanelRow.ShortestUnique(live[1], live));
    }

    /// <summary>
    /// It lengthens one segment at a time and only where uniqueness requires it, so the printed
    /// address is always a valid <c>path</c> argument and never longer than the whole chain.
    /// </summary>
    [Fact]
    public void An_address_two_elements_answer_to_grows_by_one_segment_at_a_time()
    {
        var colliding = new[]
        {
            "Canvas[0]/ContentArea[2]/ScreenContent[2]/Panel[0]/NumberVarPlain[0]",
            "Canvas[0]/ContentArea[2]/ScreenContent[2]/Other[1]/NumberVarPlain[0]",
        };

        Assert.Equal(
            "Panel[0]/NumberVarPlain[0]",
            GameMcpTooltipPanelRow.ShortestUnique(colliding[0], colliding));
        Assert.Equal(
            "Other[1]/NumberVarPlain[0]",
            GameMcpTooltipPanelRow.ShortestUnique(colliding[1], colliding));

        // Nothing distinguishes two identical paths, so the address is the whole chain rather than
        // a shorter one that would be a guess.
        var twins = new[] { colliding[0], colliding[0] };
        Assert.Equal(colliding[0], GameMcpTooltipPanelRow.ShortestUnique(colliding[0], twins));
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Encoded(GameMcpObjectBuilder row) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(row.Freeze(), Catalog));

    private static GameMcpObjectBuilder Row(string path, string name, Guid entityId) =>
        GameMcpTooltipPanelRow.Project(path, name, entityId, World(
            (0, Cube), (3, Propagation), (6, BeamBurst)));

    /// <summary>
    /// The same document with the gadget's shared-component fold applied, so the page under test is
    /// the page the verb emits rather than one assembled to match it.
    /// </summary>
    private static JObject Folded(
        string prefix,
        params (string Path, string Name, Guid Entity)[] elements)
    {
        var segments = new string[elements.Length];
        for (var index = 0; index < elements.Length; index++) segments[index] = elements[index].Path;
        Assert.True(GameMcpTooltipPanelRow.TrySharedComponent(segments, out var component));

        var members = new GameMcpArrayBuilder();
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            var row = Row(element.Path, element.Name, element.Entity);
            row["path"] = element.Path.Substring(component.Length);
            members.Add(row);
        }
        var rows = new GameMcpArrayBuilder();
        rows.Add(new GameMcpObjectBuilder
        {
            ["pathPrefix"] = prefix,
            ["pathComponent"] = component,
            ["elements"] = members,
        });
        return Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["scene"] = "Main",
                ["pathRoot"] = "Canvas[0]/ContentArea[2]",
                ["total"] = 1,
                ["rows"] = rows,
            }.Freeze(),
            Catalog));
    }

    /// <summary>The catalog document one panel produces, exactly as the gadget assembles it.</summary>
    private static JObject Panel(string prefix, params GameMcpObjectBuilder[] elements)
    {
        var rows = new GameMcpArrayBuilder();
        var members = new GameMcpArrayBuilder();
        for (var index = 0; index < elements.Length; index++) members.Add(elements[index]);
        rows.Add(new GameMcpObjectBuilder
        {
            ["pathPrefix"] = prefix,
            ["elements"] = members,
        });
        return Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["scene"] = "Main",
                ["pathRoot"] = "Canvas[0]/ContentArea[2]",
                ["total"] = 1,
                ["rows"] = rows,
            }.Freeze(),
            Catalog));
    }

    /// <summary>One published loadout: which recipe each occupied position holds.</summary>
    private static GameWorldState World(params (int Slot, Guid Recipe)[] slots)
    {
        var rows = new WorldSpellSlot[slots.Length];
        for (var index = 0; index < slots.Length; index++)
        {
            rows[index] = new WorldSpellSlot(
                slotIndex: slots[index].Slot,
                spellRecipeId: slots[index].Recipe,
                occupied: slots[index].Recipe != Guid.Empty,
                casting: false,
                readyingCast: false,
                attuning: false,
                channeled: false,
                toggled: false,
                chargeable: true,
                castReady: true,
                chargeAvailable: true,
                resourcesCovered: true,
                currentCharges: 1,
                maximumCharges: 1,
                cooldownRemaining: BigDouble.Zero);
        }

        return new GameWorldState
        {
            EntityIdentities = Catalog,
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(rows),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    /// <summary>
    /// The duplicate pair, which is the common case rather than the corner: the Magic screen draws
    /// every equipped spell twice, once in its own list and once in the casting bar, and the two
    /// buttons are separate objects that may print different text. The refusal hands back the
    /// addresses instead of picking, and each one is the shortest form that resolves.
    /// </summary>
    [Fact]
    public void One_entity_on_two_buttons_refuses_and_names_both_addresses()
    {
        var address = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Cube, Guid.Empty, Cube },
            new[]
            {
                "Canvas[0]/ContentArea[2]/ArcaneCasting[0]/SpellList[0]/SpellButton(Clone)[0]",
                "Canvas[0]/ContentArea[2]/BottomBar[1]/PinnedObjects[0]",
                "Canvas[0]/ContentArea[2]/CastingBar[3]/SmallSpellList[0]/" +
                "SpellButtonBottomBar(Clone)[0]",
            },
            Cube,
            loaded: true,
            publishedScreen: string.Empty);

        Assert.False(address.Resolved);
        Assert.Equal("ambiguous_element", address.Code);
        Assert.Equal(
            "2 elements on this screen show this entity, and two elements about one thing may " +
            "print different text; name one of the paths listed here with path.",
            address.Reason);
        Assert.Equal(
            new[] { "SpellButton(Clone)[0]", "SpellButtonBottomBar(Clone)[0]" },
            address.Paths);
    }

    /// <summary>
    /// One element about the entity is the whole answer, and an element bound to nothing is never
    /// it: chrome carries no id and stays reachable by path alone.
    /// </summary>
    [Fact]
    public void One_element_about_the_entity_answers_and_chrome_never_does()
    {
        var address = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Guid.Empty, BeamBurst, Cube },
            new[] { "Canvas[0]/Chrome[0]", "Canvas[0]/List[1]/Row[0]", "Canvas[0]/List[1]/Row[1]" },
            BeamBurst,
            loaded: true,
            publishedScreen: string.Empty);

        Assert.True(address.Resolved);
        Assert.Equal(1, address.Element);
        Assert.Empty(address.Paths);

        var chrome = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Guid.Empty, BeamBurst, Cube },
            new[] { "Canvas[0]/Chrome[0]", "Canvas[0]/List[1]/Row[0]", "Canvas[0]/List[1]/Row[1]" },
            Guid.Empty,
            loaded: true,
            publishedScreen: string.Empty);
        Assert.False(chrome.Resolved);
        Assert.Equal("not_on_screen", chrome.Code);
    }

    /// <summary>
    /// A real entity this screen does not draw is a different answer from an id nothing in the
    /// build carries, and the first one names where to go: the published <c>screen</c> column when
    /// the world has one for this id, and the screen catalog when it does not.
    /// </summary>
    [Fact]
    public void An_entity_this_screen_does_not_draw_is_told_where_it_is_drawn()
    {
        var elsewhere = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Cube },
            new[] { "Canvas[0]/List[1]/Row[0]" },
            BeamBurst,
            loaded: true,
            publishedScreen: "Magic/Augments");

        Assert.False(elsewhere.Resolved);
        Assert.Equal("not_on_screen", elsewhere.Code);
        Assert.Equal(
            "Nothing this screen draws is about this entity; the world publishes it on " +
            "Magic/Augments, so navigate there and read it again.",
            elsewhere.Reason);
        Assert.Empty(elsewhere.Paths);

        var unpublished = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Cube },
            new[] { "Canvas[0]/List[1]/Row[0]" },
            BeamBurst,
            loaded: true,
            publishedScreen: string.Empty);
        Assert.Equal(
            "Nothing this screen draws is about this entity; page game_screen_catalog for the " +
            "screens this build offers and navigate to the one that draws it.",
            unpublished.Reason);
    }

    /// <summary>
    /// An id no loaded entity carries names nothing anywhere, which is not the same no as an id
    /// this screen happens not to draw — and the sentence has to send the caller somewhere else.
    /// </summary>
    [Fact]
    public void An_id_nothing_in_this_build_carries_says_so_rather_than_blaming_the_screen()
    {
        var address = GameMcpTooltipPanelRow.AddressEntity(
            new[] { Cube },
            new[] { "Canvas[0]/List[1]/Row[0]" },
            BeamBurst,
            loaded: false,
            publishedScreen: string.Empty);

        Assert.False(address.Resolved);
        Assert.Equal("unknown_uuid", address.Code);
        Assert.Equal(
            "No entity in this build carries this id, so no element on any screen is about it; " +
            "check the id you sent, or find the thing with world_search.",
            address.Reason);
    }

    /// <summary>
    /// Both new codes are classified. An unclassified code falls to <c>ERR_REFUSED</c>, which means
    /// "the game refused and said nothing else" and is false about either of these.
    /// </summary>
    [Fact]
    public void Both_uuid_addressing_refusals_are_classified()
    {
        Assert.Equal("ERR_INPUT", GameMcpDecisionReason.Class("ambiguous_element"));
        Assert.Equal("ERR_NOT_FOUND", GameMcpDecisionReason.Class("not_on_screen"));
    }

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Cube, "SpellRecipeSO", "Ralochs's Cube", "ralochsCube"),
            new EntityIdentityName(BeamBurst, "SpellRecipeSO", "Beam Burst", "beamBurst"),
            new EntityIdentityName(Propagation, "SpellRecipeSO", "Propagation", "propagation"),
            new EntityIdentityName(
                Resonance, "PassiveAbilitySO", "Resonance", "resonancePassive"),
            new EntityIdentityName(
                Ferocity, "PassiveAbilitySO", "Ferocity", "ferocityPassive"),
        });
}
