using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The page idiom, pinned. Every tool on this surface renders through one layout, so these are the
/// rules the whole surface inherits rather than one tool's formatting.
/// </summary>
public sealed class GameMcpTextPageTests
{
    [Fact]
    public void A_page_of_rows_says_its_keys_once_and_hoists_what_never_varies()
    {
        var page = Render(@"{
            'rows':[
              {'uuid':'006061be','name':'Constitution','category':'structures','level':2259,'queuedLevels':0},
              {'uuid':'0a1b2c3d','name':'Wit','category':'structures','level':12,'queuedLevels':0}],
            'total':180,'nextOffset':56}");

        Assert.Equal(
            new[]
            {
                "rows 2/180 next=56; all category=structures, queuedLevels=0  [id name level]",
                "006061be Constitution 2259",
                "0a1b2c3d Wit 12",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// Column separation follows the widest cell on the page rather than the reader's luck: one
    /// table, one separator, and a name with a space in it cannot be mistaken for two columns.
    /// </summary>
    [Fact]
    public void A_table_holding_a_name_with_a_space_separates_every_column_the_same_way()
    {
        var page = Render(@"{'rows':[
            {'uuid':'006061be','name':'Gather Knowledge','level':3},
            {'uuid':'0a1b2c3d','name':'Wit','level':4}]}");

        Assert.Equal(
            new[]
            {
                "rows 2  [id | name | level]",
                "006061be | Gather Knowledge | 3",
                "0a1b2c3d | Wit | 4",
            },
            page.Split('\n'));
    }

    [Fact]
    public void A_refusal_is_one_sentence_with_the_class_that_named_it()
    {
        var page = Render(@"{
            'status':'refused','reasonCode':'ERR_NOT_FOUND',
            'reason':'The spell Beam Burst you tried to cancel is not currently active.',
            'uuid':'13b37d','name':'Beam Burst'}");

        Assert.Equal(
            new[]
            {
                "refused (ERR_NOT_FOUND): The spell Beam Burst you tried to cancel is not " +
                    "currently active.",
                "uuid: 13b37d",
                "name: Beam Burst",
            },
            page.Split('\n'));
    }

    [Fact]
    public void A_decision_block_says_whether_then_why_then_the_sentence()
    {
        var page = Render(@"{
            'equip':{'available':false,'reasonCode':'ERR_LIMIT','maximumAmount':0,
                     'reason':'Every slot in this loadout is in use.'},
            'unequip':{'available':true,'maximumAmount':2}}");

        Assert.Equal(
            new[]
            {
                "equip: no (ERR_LIMIT) maximumAmount=0: Every slot in this loadout is in use.",
                "unequip: yes maximumAmount=2",
            },
            page.Split('\n'));
    }

    [Fact]
    public void A_value_beside_its_ceiling_reads_the_way_the_screen_shows_it()
    {
        var page = Render(@"{
            'spellpower':{'current':43,'maximum':45},
            'casting':{'output':{'current':259,'maximum':259},
                       'reserve':{'current':12,'maximum':259}}}");

        Assert.Equal(
            new[] { "spellpower: 43/45", "casting: output 259/259, reserve 12/259" },
            page.Split('\n'));
    }

    [Fact]
    public void An_entity_is_its_name_and_its_handle_wherever_it_appears()
    {
        var page = Render(@"{
            'belongsTo':{'spellTypes':[{'uuid':'aa11bb','name':'Time'}],
                         'coreGlyphs':[{'uuid':'cc22dd','name':'Brew'},{'uuid':'ee33ff','name':'Echo'}]},
            'owner':{'uuid':'2c20e7','name':'(unnamed 2c20e7)'}}");

        Assert.Equal(
            new[]
            {
                "belongsTo: spellTypes=[Time aa11bb], coreGlyphs=[Brew cc22dd, Echo ee33ff]",
                "owner: (unnamed 2c20e7) 2c20e7",
            },
            page.Split('\n'));
    }

    [Fact]
    public void An_envelope_around_one_object_is_not_a_fact()
    {
        var page = Render(@"{'row':{'uuid':'13b37d','name':'Gather Knowledge','masteryLevel':4}}");

        Assert.Equal(
            new[] { "uuid: 13b37d", "name: Gather Knowledge", "masteryLevel: 4" },
            page.Split('\n'));
    }

    [Fact]
    public void An_empty_requirement_tree_says_it_has_no_conditions()
    {
        var page = Render(@"{
            'masteryLevel':4,
            'requirements':{'status':'available','ownerUuid':'13b37d','tierIndex':0,
                            'operator':'AND','children':[]}}");

        Assert.Equal(
            new[] { "masteryLevel: 4", "requirements: no conditions" },
            page.Split('\n'));
    }

    /// <summary>
    /// A page that answered is a page that was available. Only a verdict with a sentence behind it
    /// earns a line.
    /// </summary>
    [Fact]
    public void A_bare_available_status_is_not_news()
    {
        Assert.Equal("level: 3", Render(@"{'status':'available','level':3}"));
        Assert.Equal(
            "unavailable (ERR_UNAVAILABLE): no save is loaded",
            Render(@"{'status':'unavailable','reasonCode':'ERR_UNAVAILABLE','reason':'no save is loaded'}"));
    }

    /// <summary>
    /// A block too big for a cell is not too big for the header. Twenty rows carrying the same
    /// refusal used to render as twenty paragraphs, because one constant nobody could fit in a
    /// column disqualified the whole page from being a table.
    /// </summary>
    [Fact]
    public void A_constant_nobody_could_fit_in_a_cell_is_still_said_once_in_the_header()
    {
        var page = Render(@"{'rows':[
            {'plot':{'uuid':'14060e','name':'Dreamberry'},'active':0,
             'add':{'status':'unavailable','reasonCode':'ERR_UNAVAILABLE',
                    'reason':'The game only checks this prerequisite when the action is started.',
                    'checkWith':'game_agromancy add_plot_action'}},
            {'plot':{'uuid':'27b41a','name':'Sunfruit'},'active':2,
             'add':{'status':'unavailable','reasonCode':'ERR_UNAVAILABLE',
                    'reason':'The game only checks this prerequisite when the action is started.',
                    'checkWith':'game_agromancy add_plot_action'}}]}");

        Assert.Equal(
            new[]
            {
                "rows 2; all add=unavailable (ERR_UNAVAILABLE) " +
                "checkWith=game_agromancy add_plot_action: The game only checks this " +
                "prerequisite when the action is started.  [plot | active]",
                "Dreamberry 14060e | 0",
                "Sunfruit 27b41a | 2",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// The relaxation is only for constants. A column that actually varies still has to fit in a
    /// cell, or the page would be a table with a fact truncated out of it.
    /// </summary>
    [Fact]
    public void A_varying_column_too_big_for_a_cell_still_costs_the_page_its_table()
    {
        var page = Render(@"{'rows':[
            {'name':'first','detail':{'a':1,'b':2,'c':{'d':3}}},
            {'name':'second','detail':{'a':9,'b':8,'c':{'d':7}}}]}");

        Assert.Equal("rows 2:", page.Split('\n')[0]);
    }

    /// <summary>
    /// A constant the header would have to count rather than say stays a column. Hoisted, it came
    /// out as `all detail=3` — three properties — and because constants are not printed per row,
    /// the content it stood for appeared nowhere on the page.
    /// </summary>
    [Fact]
    public void A_constant_the_header_could_only_count_keeps_its_content_on_the_page()
    {
        var page = Render(@"{'rows':[
            {'name':'first','detail':{'a':1,'b':2,'c':{'d':3}}},
            {'name':'second','detail':{'a':1,'b':2,'c':{'d':3}}}]}");

        Assert.DoesNotContain("all detail=", page, StringComparison.Ordinal);
        Assert.Contains("d=3", page, StringComparison.Ordinal);
    }

    private static string Render(string json) =>
        GameMcpTextPage.Render(JToken.Parse(json.Replace('\'', '"')));
}
