using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Every record a type page can print has a disposition somebody chose: the word the game's own
/// tooltip prints, or the internal name left standing because the game prints no word for it.
/// </summary>
/// <remarks>
/// This is the whole guard. The failure it exists to stop is silent: a record added to the world's
/// census keeps its camelCase name on the wire and reads exactly like a record the game genuinely
/// has no word for, so the surface degrades one property at a time with nothing to notice. The map
/// throws instead, and this says so for all one hundred and forty-five of them at once.
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
            var word = GameMcpModifierPropertyWords.Word(kind, property);
            Assert.False(string.IsNullOrWhiteSpace(word));
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
    /// The ten records the pinned build authors no word for, named one by one. A word arriving for
    /// any of them is a game change to census, and a word quietly disappearing from the map would
    /// otherwise look identical to one of these.
    /// </summary>
    [Theory]
    [InlineData("SpellType", "bonusFlashRate")]
    [InlineData("SpellType", "flashEffectMod")]
    [InlineData("AlchemyType", "level")]
    [InlineData("EquipmentType", "experienceRateMod")]
    [InlineData("EquipmentType", "masteryLevel")]
    [InlineData("RitualType", "activeRituals")]
    [InlineData("HarvestType", "level")]
    [InlineData("PlotNodeType", "totalLevel")]
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
    /// Exactly ten stand as-is across the whole census, so a new silent record cannot be added
    /// alongside the ruled ones without this saying so.
    /// </summary>
    [Fact]
    public void Ten_of_the_hundred_and_forty_five_records_stand_under_their_internal_name()
    {
        var silent = new List<string>();
        var total = 0;
        foreach (var kind in Enum.GetValues<WorldTypeModifierOwnerKind>())
        {
            foreach (var property in WorldTypeModifierBindings.Records(kind))
            {
                total++;
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
        Assert.Equal(10, silent.Count);
    }

    /// <summary>
    /// The game itself prints one word for two ritual records, and the wire says what the game says
    /// rather than inventing a second word to tell them apart. Pinned because it is the one place a
    /// reader meets the same name twice in one block, and because a future census that "fixed" it
    /// would be publishing a word no tooltip has ever shown.
    /// </summary>
    [Fact]
    public void Two_ritual_records_share_the_one_word_the_game_authors_for_both()
    {
        var words = WorldTypeModifierBindings.Records(WorldTypeModifierOwnerKind.RitualType)
            .Select(property => GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.RitualType, property))
            .ToArray();

        Assert.Equal(2, words.Count(word => word == "Ritual Speed"));
        Assert.Equal(
            "Ritual Speed",
            GameMcpModifierPropertyWords.Word(WorldTypeModifierOwnerKind.RitualType, "speed"));
        Assert.Equal(
            "Ritual Speed",
            GameMcpModifierPropertyWords.Word(
                WorldTypeModifierOwnerKind.RitualType, "completionRateMod"));
    }
}
