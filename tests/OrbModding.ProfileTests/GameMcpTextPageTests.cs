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
    /// on, and a column set that is complete whatever the page's rows happen to hold. Complete
    /// across both lines — a column the share line states is named there, with its value, and never
    /// again on the rows beneath it.
    /// </summary>
    [Fact]
    public void A_page_of_rows_names_every_column_once_and_never_twice()
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
                "these 2 share: category=structures, queuedLevels=0",
                "[id | name | level]",
                "006061be | Constitution | 2259",
                "0a1b2c3d | Wit | 12",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// The T3 contract, both ways at once, on the shape a live round found it in: the worth block
    /// printed <c>these 32 share: effect=raw, order=0</c> and then <c>| raw | 0</c> on all 32 rows,
    /// while the one column that varied — who each contribution came from — was the column it left
    /// out. A declared-constant column leaves the rows; a varying column is never omitted.
    /// </summary>
    [Fact]
    public void A_share_line_takes_the_constant_columns_and_never_the_varying_one()
    {
        var page = Render(@"{'sources':[
            {'source':'Alchemic Study','amount':322,'effect':'raw','order':0},
            {'source':'Refined Practice','amount':331,'effect':'raw','order':0},
            {'source':'Tempered Insight','amount':347,'effect':'raw','order':0},
            {'source':'Deep Reading','amount':350,'effect':'raw','order':0}]}");

        Assert.Equal(
            new[]
            {
                "sources 4",
                "these 4 share: effect=raw, order=0",
                "[source | amount]",
                "Alchemic Study | 322",
                "Refined Practice | 331",
                "Tempered Insight | 347",
                "Deep Reading | 350",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// Two pages of one category still name one column set: what the share line says plus what the
    /// header says is the declaration, in declaration order, on every page. A narrower header is
    /// never a column silently dropped — the line directly above it says which column went and what
    /// it read there.
    /// </summary>
    [Fact]
    public void The_share_line_and_the_header_together_name_the_whole_declared_set()
    {
        const string Declaring = "'columns':['uuid','name','state','run','level'],";
        var first = Render("{" + Declaring + @"'rows':[
            {'uuid':'011677','name':'Specialization: Storm','state':'available','run':'idle','level':1},
            {'uuid':'0f75c0','name':'Focus: Inventory','state':'available','run':'idle','level':1},
            {'uuid':'1a2b3c','name':'Weakened Scholar','state':'available','run':'idle','level':2},
            {'uuid':'2b3c4d','name':'Speed: Scholar','state':'available','run':'idle','level':3}],
            'total':98,'nextOffset':4}");
        var second = Render("{" + Declaring + @"'rows':[
            {'uuid':'3c4d5e','name':'Speed: Workshop','state':'locked','run':'idle','level':0},
            {'uuid':'4d5e6f','name':'Increased Difficulty','state':'available','run':'queued','level':2},
            {'uuid':'5e6f70','name':'Speed: Rituals','state':'locked','run':'idle','level':0},
            {'uuid':'6f7081','name':'Weakened Zeal','state':'available','run':'queued','level':1}],
            'total':98}");

        Assert.Equal("these 4 share: state=available, run=idle", first.Split('\n')[1]);
        Assert.Equal("[id | name | level]", first.Split('\n')[2]);
        Assert.Equal("[id | name | state | run | level]", second.Split('\n')[1]);
        Assert.DoesNotContain("share:", second, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page whose every column is constant keeps its table. Rows with nothing left in them are
    /// not rows, and a share line that emptied them would have said the page had four of something
    /// while showing four blank lines.
    /// </summary>
    [Fact]
    public void A_page_whose_every_column_is_constant_keeps_its_table()
    {
        var page = Render(@"{'rows':[
            {'category':'upgrades','affordable':'already_maxed'},
            {'category':'upgrades','affordable':'already_maxed'},
            {'category':'upgrades','affordable':'already_maxed'},
            {'category':'upgrades','affordable':'already_maxed'}]}");

        Assert.Equal(
            new[]
            {
                "rows 4",
                "[category | affordable]",
                "upgrades | already_maxed",
                "upgrades | already_maxed",
                "upgrades | already_maxed",
                "upgrades | already_maxed",
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
    /// A coincidence too small to be worth a line is not one. Two rituals happening to share a
    /// level is a column the share line would spend more characters naming than the rows spend
    /// repeating, so the page keeps its whole header and says the value twice — which is also what
    /// keeps a two-row page and a twenty-row page of one category looking alike.
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
    /// The share line has to be worth its own bytes, measured against what it takes off the page:
    /// the column's label out of the header and its value out of every row. Six rows that repeat a
    /// long word earn it, and two rows that repeat a short one do not.
    /// </summary>
    [Fact]
    public void The_share_line_is_said_only_when_it_is_shorter_than_what_it_takes_off_the_page()
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
                "[id | name]",
                "00246c | Gather Space",
                "00246d | Gather Time",
                "00246e | Gather Wit",
                "00246f | Gather Will",
                "002470 | Gather Void",
                "002471 | Gather Vim",
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
        Assert.Equal("[id | name | reason]", page.Split('\n')[2]);
        Assert.Contains(
            "00246c | Gather Space | " +
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
            {'uuid':'aa11bb','name':'Arcane AugmentGlyphs','path':'LeftSide[1]'},
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
        Assert.Equal("spells: -", Render(@"{'spells':[]}"));
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
    /// A block too big for a cell is not too big for a share line. Twenty rows carrying the same
    /// refusal used to render as twenty paragraphs, because one constant nobody could fit in a
    /// column disqualified the whole page from being a table; the length relaxation that fixed it
    /// now pays for itself, because the long constant is said once instead of once per row. The
    /// agromancy page this was found on now says two words instead, so the fixture is the retired
    /// shape and the rule is what still has to hold for any long page constant.
    /// </summary>
    [Fact]
    public void A_constant_nobody_could_fit_in_a_cell_is_said_once_on_the_share_line()
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
                "[plot | active]",
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

    /// <summary>
    /// An arrow is how this page says something moved, so a pair whose halves are equal spends one
    /// on a move that did not happen. The value is the fact; the arrow is kept for the moves.
    /// </summary>
    [Fact]
    public void A_value_that_did_not_move_is_stated_once_rather_than_pointed_at_itself()
    {
        Assert.Equal(
            "feature: auto_buy\non: no",
            Render(@"{'feature':'auto_buy','on':{'before':false,'after':false}}"));
        Assert.Equal(
            "feature: auto_buy\non: no -> yes",
            Render(@"{'feature':'auto_buy','on':{'before':false,'after':true}}"));
        Assert.Equal(
            "uuid: cd5465\nlevel: 20",
            Render(@"{'uuid':'cd5465','level':{'before':20,'after':20}}"));
    }

    /// <summary>
    /// The page is not JSON and never wears JSON's escapes. A description the game authors with a
    /// real paragraph break used to arrive as the two literal characters <c>\</c> and <c>n</c>,
    /// because every scalar was serialized to JSON and then unquoted — while the same text through
    /// <c>game_tooltip</c>, which never took that detour, rendered correctly.
    /// </summary>
    [Fact]
    public void A_multi_paragraph_value_renders_as_paragraphs_and_never_as_escape_characters()
    {
        var page = GameMcpTextPage.Render(new JObject
        {
            ["name"] = "Raise Druidry Lv",
            ["description"] =
                "Increases the maximum speed for harvesting and maximum size for weaving.\n\n" +
                "This makes you faster but significantly less efficient.",
        });

        Assert.DoesNotContain("\\n", page);
        Assert.Equal(
            new[]
            {
                "name: Raise Druidry Lv",
                "description:",
                "  Increases the maximum speed for harvesting and maximum size for weaving.",
                string.Empty,
                "  This makes you faster but significantly less efficient.",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A one-line value keeps its one line: the paragraph rule must not push every description onto
    /// a key line of its own.
    /// </summary>
    [Fact]
    public void A_single_paragraph_value_stays_on_the_key_line()
    {
        var page = GameMcpTextPage.Render(new JObject
        {
            ["description"] = "Binds mana to unseen energies.",
        });

        Assert.Equal("description: Binds mana to unseen energies.", page);
    }

    /// <summary>
    /// A canned refusal sentence is said once per decision, so a list repeating one refusal down
    /// its rows says it on the first. The class rides every occurrence, because that is what a
    /// caller branches on; the sentence explains the class, and a reader who met it four lines up
    /// learns nothing from meeting it again. A live round paid for nine distinct sentences
    /// fifty-nine times.
    /// </summary>
    [Fact]
    public void A_canned_refusal_sentence_is_said_once_down_a_list_of_rows()
    {
        const string Locked = "The game keeps this locked, and says nothing about what would " +
            "unlock it.";
        var page = Render(@"{'results':[
            {'uuid':'01273b','name':'Fortunate',
             'purchase':{'available':false,'reasonCode':'ERR_LOCKED','reason':'" + Locked + @"'}},
            {'uuid':'02e0cd','name':'Formation',
             'purchase':{'available':false,'reasonCode':'ERR_LOCKED','reason':'" + Locked + @"'}},
            {'uuid':'03f1de','name':'Psionic',
             'purchase':{'available':false,'reasonCode':'ERR_LOCKED','reason':'" + Locked + @"'}}]}");

        Assert.Equal(
            new[]
            {
                "results 3:",
                "  uuid: 01273b",
                "  name: Fortunate",
                "  purchase: no (ERR_LOCKED): " + Locked,
                string.Empty,
                "  uuid: 02e0cd",
                "  name: Formation",
                "  purchase: no (ERR_LOCKED)",
                string.Empty,
                "  uuid: 03f1de",
                "  name: Psionic",
                "  purchase: no (ERR_LOCKED)",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// Two different decisions that happen to share a reason code each keep their sentence.
    /// </summary>
    /// <remarks>
    /// The saving is worth having down a list of rows, where the reader has the sentence four lines
    /// up in the same shape. Across two named decisions it is a different trade: a post-reset answer
    /// explained why <c>reset</c> was refused and then rendered <c>reroll</c> as a bare class, and
    /// nothing on the page said the two nos were the same no — so the second read as a second,
    /// unexplained wall. The sentence is said once where it is said, not once per response.
    /// </remarks>
    [Fact]
    public void Two_decisions_sharing_a_reason_code_each_keep_their_sentence()
    {
        const string Locked = "A world cycle has to finish before either of these is offered.";
        var page = Render(@"{'prestigeState':{
            'reset':{'available':false,'reasonCode':'ERR_STATE','reason':'" + Locked + @"'},
            'reroll':{'available':false,'reasonCode':'ERR_STATE','reason':'" + Locked + @"'}}}");

        Assert.Equal(
            new[]
            {
                "reset: no (ERR_STATE): " + Locked,
                "reroll: no (ERR_STATE): " + Locked,
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A response holding one refusal is byte for byte what it always was, and a refusal with no
    /// class to carry it keeps its sentence however often it is said — the class is what makes the
    /// short form readable, so without one there is no short form.
    /// </summary>
    [Fact]
    public void A_lone_refusal_and_a_classless_one_keep_their_whole_sentence()
    {
        Assert.Equal(
            "refused (ERR_STATE): This is already running.",
            Render(@"{'status':'refused','reasonCode':'ERR_STATE','reason':'This is already running.'}"));
        Assert.Equal(
            new[] { "equip: no: Nothing says why.", "unequip: no: Nothing says why." },
            Render(@"{
                'equip':{'available':false,'reason':'Nothing says why.'},
                'unequip':{'available':false,'reason':'Nothing says why.'}}").Split('\n'));
    }

    /// <summary>
    /// One entity page has one shape however many ids the call named. A block whose fields are all
    /// flat satisfied every test for a one-row table, so the same plot node came back as an
    /// indented block inside a two-id batch and as a <c>[row]</c> header over one comma-joined
    /// <c>key=value</c> line when asked for alone. <c>results</c> holds documents, and a document is
    /// never a row. What every document in a batch says the same way is said once above them —
    /// which is the block grammar unchanged, not a second shape: every line is still
    /// <c>key: value</c> at the block's own indent.
    /// </summary>
    [Fact]
    public void One_id_and_the_same_id_in_a_batch_render_the_same_block()
    {
        const string Oak = @"{'uuid':'2163ef','name':'Oak Tree','category':'plot-nodes',
            'row':{'state':'available','remainingQuantity':12,'masteryLevel':4}}";
        var alone = Render(@"{'results':[" + Oak + "]}");
        var batched = Render(@"{'results':[" + Oak + @",
            {'uuid':'f05fdf','name':'Water Spring','category':'plot-nodes',
             'row':{'state':'locked','remainingQuantity':0,'masteryLevel':0}}]}");

        Assert.Equal(
            new[]
            {
                "results 1:",
                "  uuid: 2163ef",
                "  name: Oak Tree",
                "  category: plot-nodes",
                "  row: state=available, remainingQuantity=12, masteryLevel=4",
            },
            alone.Split('\n'));
        Assert.Equal(
            new[]
            {
                "results 2:",
                "  these 2 share:",
                "    category: plot-nodes",
                string.Empty,
                "  uuid: 2163ef",
                "  name: Oak Tree",
                "  row: state=available, remainingQuantity=12, masteryLevel=4",
                string.Empty,
                "  uuid: f05fdf",
                "  name: Water Spring",
                "  row: state=locked, remainingQuantity=0, masteryLevel=0",
            },
            batched.Split('\n'));
    }

    /// <summary>
    /// The hoist a table already had, on the shape a detail read answers in. A live round's
    /// three-glyph <c>world_get</c> spent 46% of its 1,736 bytes printing the same eighteen lines
    /// three times, because the share line only ever existed on the table path and a batch of
    /// entities is not a table. Nothing leaves the page: what every block says identically is said
    /// once, and each block keeps everything of its own.
    /// </summary>
    [Fact]
    public void A_batch_says_what_every_block_shares_once_and_keeps_what_each_owns()
    {
        const string Shared =
            @"'paidLevel':0,'bonusLevel':0,'totalLevel':0,'category':'glyphs','state':'locked'";
        var page = Render(
            @"{'results':[
                {'uuid':'aaa111','name':'Quick'," + Shared + @"},
                {'uuid':'bbb222','name':'Bright'," + Shared + @"},
                {'uuid':'ccc333','name':'Scholar'," + Shared + "}]}");

        Assert.Equal(
            new[]
            {
                "results 3:",
                "  these 3 share:",
                "    paidLevel: 0",
                "    bonusLevel: 0",
                "    totalLevel: 0",
                "    category: glyphs",
                "    state: locked",
                string.Empty,
                "  uuid: aaa111",
                "  name: Quick",
                string.Empty,
                "  uuid: bbb222",
                "  name: Bright",
                string.Empty,
                "  uuid: ccc333",
                "  name: Scholar",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A hoist that does not shorten the page is not a hoist. Two blocks agreeing on one short line
    /// cost more in the heading than the second copy of the line costs, so the blocks keep it.
    /// </summary>
    [Fact]
    public void A_shared_line_too_short_to_pay_for_its_heading_stays_in_the_blocks()
    {
        var page = Render(
            @"{'results':[
                {'uuid':'aaa111','name':'Quick','level':0},
                {'uuid':'bbb222','name':'Bright','level':0}]}");

        Assert.Equal(
            new[]
            {
                "results 2:",
                "  uuid: aaa111",
                "  name: Quick",
                "  level: 0",
                string.Empty,
                "  uuid: bbb222",
                "  name: Bright",
                "  level: 0",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A header with a body under it belongs to the block that owns the body: hoisting the header
    /// away would leave the body attached to nothing, so a line with anything indented beneath it
    /// is never a candidate however many blocks repeat it.
    /// </summary>
    [Fact]
    public void A_line_with_a_body_under_it_is_never_hoisted_away_from_it()
    {
        const string Blockers = @"'blockers':{'cap':{'blocked':true},'leeway':{'blocked':false}}";
        var page = Render(
            @"{'results':[
                {'uuid':'aaa111','category':'glyphs','state':'locked'," + Blockers + @"},
                {'uuid':'bbb222','category':'glyphs','state':'locked'," + Blockers + "}]}");

        Assert.Equal(
            new[]
            {
                "results 2:",
                "  these 2 share:",
                "    category: glyphs",
                "    state: locked",
                string.Empty,
                "  uuid: aaa111",
                "  blockers:",
                "    cap: blocked=yes",
                "    leeway: blocked=no",
                string.Empty,
                "  uuid: bbb222",
                "  blockers:",
                "    cap: blocked=yes",
                "    leeway: blocked=no",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A price with one resource in it is said on the line that names it. The table around it was
    /// three lines of frame for one row, and a live round paid that frame 106 times: 11,540 bytes
    /// of table to deliver 4,613 bytes of price.
    /// </summary>
    [Fact]
    public void A_price_with_one_resource_in_it_is_said_on_one_line()
    {
        var page = Render(@"{'purchase':{'available':true,'affordable':true,'costs':[
            {'cost':'6','spendableAmount':'131','affordable':true,
             'resource':{'uuid':'9dd2cf','name':'Artifact Upgrades'}}]}}");

        Assert.Equal(
            new[]
            {
                "available: yes",
                "affordable: yes",
                "cost: 6 of 131 Artifact Upgrades 9dd2cf",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// The per-row affordability word is <c>spendableAmount &gt;= cost</c> on its own row and the
    /// block above it already answers the whole purchase, so it is worth a reader's attention only
    /// where it says no — and that is where it is said, in the word the column used.
    /// </summary>
    [Fact]
    public void A_price_that_falls_short_says_so_and_one_that_does_not_stays_quiet()
    {
        var page = Render(@"{'purchase':{'costs':[
            {'cost':'400','spendableAmount':'12','affordable':false,
             'resource':{'uuid':'9dd2cf','name':'Artifact Upgrades'}}]}}");

        Assert.Equal(
            new[] { "cost: 400 of 12 Artifact Upgrades 9dd2cf affordable=no" },
            page.Split('\n'));
    }

    /// <summary>
    /// Every other list of one pays the same frame — a count, a header, and one row to deliver one
    /// row — and a round paid it nineteen more times. Inlined, every word the header carried is
    /// still there as its own key.
    /// </summary>
    [Fact]
    public void A_list_holding_one_row_is_said_on_the_line_that_names_it()
    {
        var page = Render(@"{'members':[{'kind':'structures','count':2}],
            'sources':[{'amount':'40','effect':'raw','order':0}]}");

        Assert.Equal(
            new[]
            {
                "members: kind=structures, count=2",
                "sources: amount=40, effect=raw, order=0",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// A row carrying the move a mutation made is still a row of one. The pair renders in three
    /// words the way this page renders every other move, so the removal's usage budget stops paying
    /// for a count, a header and a column line to deliver one line.
    /// </summary>
    [Fact]
    public void A_list_of_one_whose_row_carries_a_move_says_the_move_inline()
    {
        Assert.Equal(
            new[]
            {
                "usageBudget: resource=Spell Capacity fdcbb8, headroom=7 -> 9, used=24, " +
                "maximum=31",
            },
            Render(@"{'usageBudget':[{'resource':{'uuid':'fdcbb8','name':'Spell Capacity'},
                'headroom':{'before':7,'after':9},'used':24,'maximum':31}]}").Split('\n'));
    }

    /// <summary>
    /// A list of one has no line budget. The budget turns a long nested object into a block a
    /// reader can scan; the block a one-row table falls back to is every character of the inline
    /// form plus a count, a header and a column line around it, so refusing the long line only ever
    /// bought more bytes saying the same thing.
    /// </summary>
    [Fact]
    public void A_row_of_one_longer_than_the_inline_budget_is_still_one_line()
    {
        Assert.Equal(
            new[]
            {
                "costs: resource=Time Advancement Of Considerable Length 487a14, " +
                "amount=123456789, held=987654321, shortfall=12345, perSecond=42",
            },
            Render(@"{'costs':[{'resource':{'uuid':'487a14',
                'name':'Time Advancement Of Considerable Length'},'amount':123456789,
                'held':987654321,'shortfall':12345,'perSecond':42}]}").Split('\n'));
    }

    /// <summary>
    /// A second row is what columns are for, and a page keeps its table however few rows it holds:
    /// the count and the declared columns are what a paged read is read by, and one row today is
    /// not a promise about tomorrow's.
    /// </summary>
    [Fact]
    public void A_second_row_keeps_the_table_and_so_does_a_page_of_one()
    {
        Assert.Equal(
            new[]
            {
                "members 2",
                "[kind | count]",
                "structures | 2",
                "upgrades | 5",
            },
            Render(@"{'members':[{'kind':'structures','count':2},
                {'kind':'upgrades','count':5}]}").Split('\n'));

        Assert.Equal(
            new[]
            {
                "rows 1/9",
                "[kind | count]",
                "structures | 2",
            },
            Render(@"{'total':9,'rows':[{'kind':'structures','count':2}]}").Split('\n'));
    }

    /// <summary>
    /// Two resources is what columns are for, so the table stays exactly as it was.
    /// </summary>
    [Fact]
    public void A_price_naming_more_than_one_resource_keeps_its_table()
    {
        var page = Render(@"{'costs':[
            {'cost':'6','spendableAmount':'131','affordable':true,
             'resource':{'uuid':'9dd2cf','name':'Artifact Upgrades'}},
            {'cost':'9','spendableAmount':'2','affordable':false,
             'resource':{'uuid':'11ab00','name':'Arcana'}}]}");

        Assert.Equal(
            new[]
            {
                "costs 2",
                "[cost | spendableAmount | affordable | resource]",
                "6 | 131 | yes | Artifact Upgrades 9dd2cf",
                "9 | 2 | no | Arcana 11ab00",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// The share line only ever fired on a column that was constant to the last row, so a
    /// ninety-eight-row page spent 1,076 bytes printing one word ninety-five times and the three
    /// rows that mattered had to be found by eye. The majority is named once and the rows that are
    /// not it are named beside it, by the handle the page addresses them with.
    /// </summary>
    [Fact]
    public void A_column_nearly_every_row_agrees_on_names_the_majority_and_then_the_exceptions()
    {
        var page = Render(@"{'rows':[
            {'uuid':'011677','state':'available'},
            {'uuid':'050187','state':'available'},
            {'uuid':'0cb332','state':'available'},
            {'uuid':'8191b9','state':'completed'},
            {'uuid':'41aad0','state':'available'},
            {'uuid':'8412b4','state':'available'},
            {'uuid':'96074f','state':'available'}]}");

        Assert.Equal(
            new[]
            {
                "rows 7",
                "6 of 7 share: state=available; 8191b9 completed",
                "[id]",
                "011677",
                "050187",
                "0cb332",
                "8191b9",
                "41aad0",
                "8412b4",
                "96074f",
            },
            page.Split('\n'));
    }

    /// <summary>
    /// The hoist is measured against the page it would produce rather than against a formula about
    /// it. On a short table the share line costs more than the column it lifts, and the eighteen of
    /// one round's twenty-seven hoists that lost bytes were all of them tables of four rows or
    /// fewer.
    /// </summary>
    [Fact]
    public void A_table_too_short_for_the_share_line_to_pay_keeps_its_columns()
    {
        var page = Render(@"{'rows':[
            {'uuid':'a1','keywords':'-','matchedOn':'-'},
            {'uuid':'a2','keywords':'-','matchedOn':'-'}]}");

        Assert.Equal(
            new[]
            {
                "rows 2",
                "[id | keywords | matchedOn]",
                "a1 | - | -",
                "a2 | - | -",
            },
            page.Split('\n'));
    }

    private static string Render(string json) =>
        GameMcpTextPage.Render(JToken.Parse(json.Replace('\'', '"')));
}
