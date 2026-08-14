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
                "  elements 1",
                "  [id | name | path]",
                "  d0d000 | Resonance | PassiveAbilityItem(Clone)[0]",
            }),
            Render(Panel(
                "PassiveList[0]",
                Row("PassiveAbilityItem(Clone)[0]", "Resonance", Resonance))));
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

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Encoded(GameMcpObjectBuilder row) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(row.Freeze(), Catalog));

    private static GameMcpObjectBuilder Row(string path, string name, Guid entityId) =>
        GameMcpTooltipPanelRow.Project(path, name, entityId, World(
            (0, Cube), (3, Propagation), (6, BeamBurst)));

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

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Cube, "SpellRecipeSO", "Ralochs's Cube", "ralochsCube"),
            new EntityIdentityName(BeamBurst, "SpellRecipeSO", "Beam Burst", "beamBurst"),
            new EntityIdentityName(Propagation, "SpellRecipeSO", "Propagation", "propagation"),
            new EntityIdentityName(
                Resonance, "PassiveAbilitySO", "Resonance", "resonancePassive"),
        });
}
