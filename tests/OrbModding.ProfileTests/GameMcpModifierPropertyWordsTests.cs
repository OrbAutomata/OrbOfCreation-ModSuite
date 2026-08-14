using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Every record a type page can print has a disposition somebody chose: the word the game's own
/// tooltip prints, or the internal name left standing because the game prints no word for it. A
/// record no type page can print has none at all.
/// </summary>
/// <remarks>
/// This is the whole guard. The failure it exists to stop is silent: a record added to the world's
/// census keeps its camelCase name on the wire and reads exactly like a record the game genuinely
/// has no word for, so the surface degrades one property at a time with nothing to notice. The map
/// throws instead, and this says so for all one hundred and forty-five of them at once — one hundred
/// and forty-one with a disposition, and four dead ones the map refuses to name.
/// </remarks>
public sealed class GameMcpModifierPropertyWordsTests
{
    /// <summary>
    /// The census the two type-modifier collectors walk is the exact set a page can print, so the
    /// map is checked against it rather than against a second hand-kept list.
    /// </summary>
    /// <remarks>
    /// The taxonomy travels as its name because the enum is internal to the world model, and a
    /// public theory parameter may not be. The name is also what a failure has to read as.
    /// </remarks>
    public static TheoryData<string> Taxonomies()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Enum.GetNames<WorldTypeModifierOwnerKind>()) data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(Taxonomies))]
    public void Every_record_a_type_page_can_print_has_a_deliberate_disposition(string taxonomy)
    {
        var kind = Enum.Parse<WorldTypeModifierOwnerKind>(taxonomy);
        var records = WorldTypeModifierBindings.Records(kind);

        Assert.NotEmpty(records);
        foreach (var property in records)
        {
            if (!WorldTypeModifierLiveness.IsLive(kind, property)) continue;
            var word = GameMcpModifierPropertyWords.Word(kind, property);
            Assert.False(string.IsNullOrWhiteSpace(word));
        }
    }

    /// <summary>
    /// A record the game cannot read has no word, and asking for one says why rather than handing
    /// back a name.
    /// </summary>
    /// <remarks>
    /// The two tables have to agree in both directions. A dead record with a word would be a label
    /// waiting for a page it never reaches, and a live record without one is the silent camelCase
    /// failure the census exists to stop; keeping the word table total over exactly the live records
    /// is what makes each of those a test failure rather than a wire surprise.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Taxonomies))]
    public void A_record_the_game_cannot_read_has_no_word_because_it_reaches_no_page(
        string taxonomy)
    {
        var kind = Enum.Parse<WorldTypeModifierOwnerKind>(taxonomy);
        foreach (var property in WorldTypeModifierBindings.Records(kind))
        {
            if (WorldTypeModifierLiveness.IsLive(kind, property)) continue;
            var thrown = Assert.Throws<InvalidOperationException>(
                () => GameMcpModifierPropertyWords.Word(kind, property));
            Assert.Contains(
                kind + "." + property + " is dead on this build",
                thrown.Message,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A record the census does not carry has no disposition, and the map says so rather than
    /// handing back a camelCase name that would read as a deliberate silence.
    /// </summary>
    [Fact]
    public void A_record_nobody_has_ruled_on_throws_rather_than_keeping_its_internal_name()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.StructureType, "structureCharisma"));

        Assert.Contains("StructureType.structureCharisma", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same field name is a different disposition on two classes, which is why the map is keyed
    /// by the taxonomy as well. A single property-name table would have had to pick one of these.
    /// </summary>
    [Fact]
    public void One_field_name_can_be_a_game_word_on_one_class_and_silent_on_another()
    {
        Assert.Equal(
            "Xp Rate",
            GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.HarvestType, "experienceRateMod"));
        Assert.Equal(
            "experienceRateMod",
            GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.EquipmentType, "experienceRateMod"));
    }

    /// <summary>
    /// The six live records the pinned build authors no word for, named one by one. A word arriving
    /// for any of them is a game change to census, and a word quietly disappearing from the map
    /// would otherwise look identical to one of these.
    /// </summary>
    [Theory]
    [InlineData("AlchemyType", "level")]
    [InlineData("EquipmentType", "experienceRateMod")]
    [InlineData("RitualType", "activeRituals")]
    [InlineData("HarvestType", "level")]
    [InlineData("ResearchType", "usedBonusLevels")]
    [InlineData("TimeRuneType", "totalLevel")]
    public void A_record_the_game_words_nowhere_keeps_its_internal_name(
        string taxonomy,
        string property)
    {
        Assert.Equal(
            property,
            GameMcpModifierPropertyWords.Word(
                Enum.Parse<WorldTypeModifierOwnerKind>(taxonomy), property));
    }

    /// <summary>
    /// Of the hundred and forty-five records the world binds, four are dead and six of the
    /// remaining hundred and forty-one stand as-is, so neither a new silent record nor a record
    /// quietly losing its path can be added alongside the ruled ones without this saying so.
    /// </summary>
    [Fact]
    public void Four_records_are_dead_and_six_live_ones_stand_under_their_internal_name()
    {
        var dead = new List<string>();
        var silent = new List<string>();
        var total = 0;
        foreach (var kind in Enum.GetValues<WorldTypeModifierOwnerKind>())
        {
            foreach (var property in WorldTypeModifierBindings.Records(kind))
            {
                total++;
                if (!WorldTypeModifierLiveness.IsLive(kind, property))
                {
                    dead.Add(kind + "." + property);
                    continue;
                }

                if (string.Equals(
                        GameMcpModifierPropertyWords.Word(kind, property),
                        property,
                        StringComparison.Ordinal))
                {
                    silent.Add(kind + "." + property);
                }
            }
        }

        Assert.Equal(145, total);
        Assert.Equal(
            new[]
            {
                "SpellType.bonusFlashRate",
                "SpellType.flashEffectMod",
                "EquipmentType.masteryLevel",
                "PlotNodeType.totalLevel",
            },
            dead);
        Assert.Equal(6, silent.Count);
    }

    /// <summary>
    /// The game authors "Ritual Speed" for two different ritual records, so copying it exactly makes
    /// one type page print one word over two numbers. The record the game itself named <c>Speed</c>
    /// keeps the word; the other is worded from the game's own ref name. Pinned because it is the one
    /// invented word on the wire, and because a page that shows one word twice cannot be told apart
    /// from a page that lost a record.
    /// </summary>
    [Fact]
    public void The_one_word_the_game_authors_twice_becomes_two_a_page_can_tell_apart()
    {
        var words = WorldTypeModifierBindings.Records(WorldTypeModifierOwnerKind.RitualType)
            .Select(property => GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.RitualType, property))
            .ToArray();

        Assert.Equal(words.Length, words.Distinct().Count());
        Assert.Equal(
            "Ritual Speed",
            GameMcpModifierPropertyWords.Word(WorldTypeModifierOwnerKind.RitualType, "speed"));
        Assert.Equal(
            "Ritual Completion Rate",
            GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.RitualType, "completionRateMod"));
    }
}
