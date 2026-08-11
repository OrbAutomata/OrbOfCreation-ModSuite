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
    /// <summary>
    /// The count, then what the rows share, then the columns: three header lines a reader can rely
    /// on, and a column set that is complete whatever the page's rows happen to hold.
    /// </summary>
    [Fact]
    public void A_page_of_rows_names_every_column_it_has_and_then_says_them_once_per_row()
    {
        var page = Render(@"{
            'rows':[
              {'uuid':'006061be','name':'Constitution','category':'structures','level':2259,'queuedLevels':0},
              {'uuid':'0a1b2c3d','name':'Wit','category':'structures','level':12,'queuedLevels':0}],
            'total':180,'nextOffset':56}");

        Assert.Equal(
            new[]
            {
                "rows 2/180 next=56",
                "[id | name | category | level | queuedLevels]",
                "006061be | Constitution | structures | 2259 | 0",
                "0a1b2c3d | Wit | structures | 12 | 0",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// One table dialect for the whole surface. A page whose cells happen to hold no space used to
    /// separate its columns with one, so two categories read in one batch came back in two
    /// grammars and the safe one was the one that got lucky with its names.
    /// </summary>
    [Fact]
    public void Every_table_separates_its_columns_the_same_way()
    {
        var spaced = Render(@"{'rows':[
            {'uuid':'006061be','name':'Gather Knowledge','level':3},
            {'uuid':'0a1b2c3d','name':'Wit','level':4}]}");
        var unspaced = Render(@"{'rows':[
            {'uuid':'526b34','name':'Harmony','reachedLevel':200},
            {'uuid':'461dbd','name':'Fabrication','reachedLevel':200}]}");

        Assert.Equal(
            new[]
            {
                "rows 2",
                "[id | name | level]",
                "006061be | Gather Knowledge | 3",
                "0a1b2c3d | Wit | 4",
            },
            spaced.Split('\n'));
        Assert.Equal(
            new[]
            {
                "rows 2",
                "[id | name | reachedLevel]",
                "526b34 | Harmony | 200",
                "461dbd | Fabrication | 200",
            },
            unspaced.Split('\n'));
    }

    /// <summary>
    /// The round-9 defect, in the shape it was found in: two adjacent reads of one unchanged
    /// category came back three columns wide and four columns wide, because the five rituals on
    /// page one happened to share a level the next twenty-seven did not. What a page shows is a
    /// fact about the category; only the share line is allowed to notice the coincidence.
    /// </summary>
    [Fact]
    public void A_column_every_row_agrees_on_is_still_a_column()
    {
        var first = Render(@"{'rows':[
            {'uuid':'526b34','name':'Harmony','reachedLevel':200,'selectedLevel':200},
            {'uuid':'461dbd','name':'Fabrication','reachedLevel':200,'selectedLevel':7}],
            'total':32,'nextOffset':2}");
        var second = Render(@"{'rows':[
            {'uuid':'7c1a90','name':'Consciousness','reachedLevel':69,'selectedLevel':69},
            {'uuid':'8d2b01','name':'Fundamentals','reachedLevel':2,'selectedLevel':2}],
            'total':32}");

        Assert.Equal(
            new[]
            {
                "rows 2/32 next=2",
                "[id | name | reachedLevel | selectedLevel]",
                "526b34 | Harmony | 200 | 200",
                "461dbd | Fabrication | 200 | 7",
            },
            first.Split('\n'));
        Assert.Equal(
            "[id | name | reachedLevel | selectedLevel]",
            second.Split('\n')[1]);
    }

    /// <summary>
    /// The share line adds; it never subtracts, so it has to be worth its own bytes. Six rows that
    /// repeat a long word earn it, and two rows that repeat a short one do not — which is the page
    /// size the old header was widest on.
    /// </summary>
    [Fact]
    public void The_share_line_is_said_only_when_it_is_shorter_than_the_repetition_it_names()
    {
        var wide = Render(@"{'rows':[
            {'uuid':'00246c','name':'Gather Space','affordable':'already_maxed','available':false},
            {'uuid':'00246d','name':'Gather Time','affordable':'already_maxed','available':false},
            {'uuid':'00246e','name':'Gather Wit','affordable':'already_maxed','available':false},
            {'uuid':'00246f','name':'Gather Will','affordable':'already_maxed','available':false},
            {'uuid':'002470','name':'Gather Void','affordable':'already_maxed','available':false},
            {'uuid':'002471','name':'Gather Vim','affordable':'already_maxed','available':false}],
            'total':229,'nextOffset':6}");
        var narrow = Render(@"{'rows':[
            {'uuid':'00246c','name':'Gather Space','level':1,'queuedLevels':0},
            {'uuid':'00246d','name':'Gather Time','level':4,'queuedLevels':0}],
            'total':229}");

        Assert.Equal(
            new[]
            {
                "rows 6/229 next=6",
                "these 6 share: affordable=already_maxed, available=no",
                "[id | name | affordable | available]",
                "00246c | Gather Space | already_maxed | no",
                "00246d | Gather Time | already_maxed | no",
                "00246e | Gather Wit | already_maxed | no",
                "00246f | Gather Will | already_maxed | no",
                "002470 | Gather Void | already_maxed | no",
                "002471 | Gather Vim | already_maxed | no",
            },
            wide.Split('\n'));
        Assert.DoesNotContain("share:", narrow, StringComparison.Ordinal);
        Assert.Equal("[id | name | level | queuedLevels]", narrow.Split('\n')[1]);
    }

    /// <summary>
    /// A reader splits the share line on <c>, </c> and the first <c>=</c>, so nothing on it may
    /// carry a comma. The refusal sentence that broke this rule appeared on every page of three
    /// categories and grew the header a phantom field; it keeps its cell, where its own column
    /// boundary says where it ends. No producer emits that sentence into a row any more — cells
    /// carry words — but the renderer takes whatever it is handed, so the rule is pinned on the
    /// shape that broke it.
    /// </summary>
    [Fact]
    public void A_page_constant_holding_a_comma_stays_out_of_the_share_line()
    {
        const string Row =
            "'available':false,'affordable':'already_maxed'," +
            "'reason':'The game keeps this locked, and says nothing about what would unlock it.'";
        var page = Render(@"{'rows':[
            {'uuid':'00246c','name':'Gather Space'," + Row + @"},
            {'uuid':'00246d','name':'Gather Time'," + Row + @"},
            {'uuid':'00246e','name':'Gather Wit'," + Row + @"},
            {'uuid':'00246f','name':'Gather Will'," + Row + @"},
            {'uuid':'002470','name':'Gather Void'," + Row + @"},
            {'uuid':'002471','name':'Gather Vim'," + Row + @"}],
            'total':229,'nextOffset':6}");
        var share = Array.Find(
            page.Split('\n'),
            line => line.StartsWith("these ", StringComparison.Ordinal));

        Assert.Equal("these 6 share: available=no, affordable=already_maxed", share);
        Assert.Contains(
            "00246c | Gather Space | no | already_maxed | " +
            "The game keeps this locked, and says nothing about what would unlock it.",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The order the columns are named in is a fact about the category, not about which row the
    /// page happened to start with. Round 9 paged one tooltip catalog and got
    /// <c>[id | name | path]</c> then <c>[path | name | id]</c>, because page two's first row was
    /// the one carrying no id.
    /// </summary>
    [Fact]
    public void Two_pages_of_one_category_name_their_columns_in_one_order()
    {
        var declaring = @"'columns':['uuid','name','path'],";
        var first = Render("{" + declaring + @"'rows':[
            {'uuid':'aa11bb','name':'Arcane Glyphs','path':'LeftSide[1]'},
            {'name':'Scroll Bar','path':'ScrollContainer[1]'}],'total':80,'nextOffset':2}");
        var second = Render("{" + declaring + @"'rows':[
            {'name':'Viewport','path':'Viewport[0]'},
            {'uuid':'cc22dd','name':'Levelable List','path':'LevelableList[0]'}],'total':80}");
        var undeclared = Render(@"{'rows':[
            {'name':'Viewport','path':'Viewport[0]'},
            {'uuid':'cc22dd','name':'Levelable List','path':'LevelableList[0]'}],'total':80}");

        Assert.Equal("[id | name | path]", first.Split('\n')[1]);
        Assert.Equal("[id | name | path]", second.Split('\n')[1]);
        Assert.Equal("[id | name | path]", undeclared.Split('\n')[1]);
        Assert.Equal("- | Viewport | Viewport[0]", second.Split('\n')[2]);
    }

    /// <summary>
    /// One shape for a page that matched nothing: the count it would have had, and the columns it
    /// would have shown. A sentence in a second grammar told a reader neither how many rows the
    /// category holds nor what a row of it looks like.
    /// </summary>
    [Fact]
    public void An_empty_page_is_the_same_table_with_no_rows()
    {
        Assert.Equal(
            new[] { "rows 0/180", "[id | name | level]" },
            Render(@"{'columns':['uuid','name','level'],'rows':[],'total':180}").Split('\n'));
        Assert.Equal("rows 0/180", Render(@"{'rows':[],'total':180}"));
        Assert.Equal("spells: none", Render(@"{'spells':[]}"));
    }

    /// <summary>
    /// A key with nothing after it is not a value. It read as a truncated line, as an empty string
    /// and as the token that followed it, all at once — so the page says absence the one way it
    /// already says it.
    /// </summary>
    [Fact]
    public void A_value_the_game_published_as_nothing_says_so_rather_than_ending_a_line_in_equals()
    {
        var page = Render(@"{'rows':[
            {'owner':'InstantEffectBlock','ordinal':0,'effectTypeName':''},
            {'owner':'DelayedEffectBlock','ordinal':1,'effectTypeName':''}],'total':27}");

        Assert.Equal(
            new[]
            {
                "rows 2/27",
                "[owner | ordinal | effectTypeName]",
                "InstantEffectBlock | 0 | -",
                "DelayedEffectBlock | 1 | -",
            },
            page.Split('\n'));
        Assert.Equal("effectTypeName: -", Render(@"{'effectTypeName':''}"));
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
    /// A block too big for a cell is not too big for a cell every row fills the same way. Twenty
    /// rows carrying the same refusal used to render as twenty paragraphs, because one constant
    /// nobody could fit in a column disqualified the whole page from being a table; the length
    /// relaxation that fixed it survives the column staying where it belongs. The agromancy page
    /// this was found on now says two words instead, so the fixture is the retired shape and the
    /// rule is what still has to hold for any long page constant.
    /// </summary>
    [Fact]
    public void A_constant_nobody_could_fit_in_a_cell_keeps_its_column_and_its_content()
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

        const string Refusal = "unavailable (ERR_UNAVAILABLE) " +
            "checkWith=game_agromancy add_plot_action: The game only checks this " +
            "prerequisite when the action is started.";

        Assert.Equal(
            new[]
            {
                "rows 2",
                "these 2 share: add=" + Refusal,
                "[plot | active | add]",
                "Dreamberry 14060e | 0 | " + Refusal,
                "Sunfruit 27b41a | 2 | " + Refusal,
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

    /// <summary>
    /// A count belongs to the rows it counted. A second list beside a page's rows used to be handed
    /// the page's own total and resume offset, so a complete two-item list of what could not be read
    /// came back as a page of a set it has nothing to do with.
    /// </summary>
    [Fact]
    public void A_second_list_beside_a_page_does_not_borrow_the_page_count()
    {
        var page = Render(@"{
            'unavailableCategories':[{'category':'glyphs'},{'category':'rituals'}],
            'rows':[{'uuid':'006061','name':'Constitution','level':2}],
            'total':174,'nextOffset':30}");

        Assert.Equal(
            new[]
            {
                "unavailableCategories 2",
                "[category]",
                "glyphs",
                "rituals",
                "rows 1/174 next=30",
                "[id | name | level]",
                "006061 | Constitution | 2",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A press that landed says so. The status word is dropped because the page under it proves the
    /// answer, and an action the game exposes no read for has no page under it — so the one word it
    /// had left was dropped too and a committed press rendered as the literal `(empty)`.
    /// </summary>
    [Fact]
    public void A_committed_action_with_nothing_to_show_still_says_it_worked()
    {
        Assert.Equal("committed", Render(@"{'status':'committed'}"));
        Assert.Equal("available", Render(@"{'status':'available'}"));

        // And it is still dropped wherever the page beneath it says the same thing.
        Assert.Equal("level: 1 -> 2", Render(@"{'status':'committed','level':{'before':1,'after':2}}"));
    }

    private static string Render(string json) =>
        GameMcpTextPage.Render(JToken.Parse(json.Replace('\'', '"')));
}
