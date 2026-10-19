#if SERVICE_CYCLE_PROFILE
using Newtonsoft.Json.Linq;
using static OrbAutomata.GameMcp.GameMcpOperationClass;
using static OrbAutomata.GameMcp.GameMcpToolArguments;
using static OrbAutomata.GameMcp.GameMcpToolCatalog;
using static OrbAutomata.GameMcp.GameMcpToolRoute;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldTools
{
    internal static GameMcpToolDescriptor[] Tools() => new[]
    {
        new GameMcpToolDescriptor(
            "world_overview",
            new GameMcpToolListing(
                "Published world overview",
                "Read the compact facts a strategist normally needs before choosing a detailed query, the resource panel among them: every resource it shows with held, cap, net rate per second and the time to fill or empty its tooltip prints.",
                ObjectSchema()),
            NoArguments,
            FrameRead(ReadOnly, World, Overview)),
        new GameMcpToolDescriptor(
            "world_categories",
            new GameMcpToolListing(
                "Discover world categories",
                "List every category the world collects, in one alphabetical set: its row count, whether it is available to list, and the exact reason when it is not. A collector that publishes no table of its own says so on its own row rather than being left out, so nothing the world reads is invisible here.",
                ObjectSchema()),
            NoArguments,
            FrameRead(ReadOnly, World, Categories)),
        new GameMcpToolDescriptor(
            "world_list",
            new GameMcpToolListing(
                "List exact world rows",
                "Page through one discoverable category from one immutable published world. Rows carry durable planning facts; what the game is doing at this instant is on world_get and on the action responses. Omit limit and a category of 25 rows or fewer comes back whole, with no nextOffset. Name a limit and it is an upper bound: the page also stops at a 12 KB response budget, so wide rows come back short. nextOffset is present exactly when more rows remain, and is the offset to resume from. Set affordable=true on a category whose rows carry a price to page only the rows you can buy right now, and discovered=true or false on a category whose rows carry that column to page one side of it. On a resources page, meter says which way the pair reads: meter held means amount is what you have and capacity is the ceiling it climbs toward, while meter left means amount is what is still unspent of an allowance rather than what is held, so atCapacity on such a row says none committed or some committed instead of whether it is full.",
                ObjectSchema(
                    new JObject
                    {
                        ["category"] = StringSchema("Exact name returned by world_categories."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                        ["affordable"] = BooleanSchema(
                            "Return only rows whose price is met right now. Accepted on priced "
                                + "categories; other categories refuse it rather than ignore it."),
                        ["discovered"] = BooleanSchema(
                            "Return only the discovered rows, or only the undiscovered ones. "
                                + "Accepted on the categories whose rows carry a discovered "
                                + "column; other categories refuse it rather than ignore it."),
                    },
                    "category")),
            static (arguments, builder) =>
            {
                builder.Category = RequireString(arguments, "category");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                builder.LimitFromCaller = arguments.ContainsKey("limit");
                builder.AffordableOnly = OptionalBool(arguments, "affordable", false);
                if (arguments.ContainsKey("discovered"))
                    builder.DiscoveredFilter = RequireBool(arguments, "discovered");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                    context,
                    request.Category,
                    request.Offset,
                    request.Limit,
                    request.AffordableOnly,
                    request.LimitFromCaller,
                    request.DiscoveredFilter).Freeze()))),
        new GameMcpToolDescriptor(
            "world_get",
            new GameMcpToolListing(
                "Read one id in full",
                "Everything one id says, for one id or a batch of them in a single call, from one immutable published world. A block answers what the thing is and what the player calls it, its published row — the same durable columns world_list pages for that category — and, for the categories this build evaluates, the description the game prints on its own tooltip, every decision it will accept or refuse with the reason for each, the recursive requirement graph and which judgement decides, the exact per-resource price and whether you can pay it, and what is holding it shut. A thing some effect moves — a statistic, a resource, a variable — also says what moves it: modifiedBy counts the movers per category, and world_search with modifies set to its id walks them. A keyword is a type asset, and a block on one also says what that type is worth right now: one number per modifier record it carries, named so a total its members already contain is never multiplied into one of them a second time, every source that placed a modifier on it, and how many things the keyword reaches. Ids answer in the order you asked, so nothing echoes an index back; one id failing refuses that block alone and every other block still answers. category is optional and names which table the row is read from: an id already carries its own category, so name one only to address a table whose native type belongs to more than one, or to insist on the table you meant. The description is a live read of the game's tooltip text for that one entity rather than a captured fact, so it is there only while a save is loaded and only for the categories carrying evaluated detail; with no published world the whole call refuses and names the lifecycle state instead. Detail is per id and nothing is truncated, so a 200-id batch — the ceiling — answers at 200 ids of detail.",
                WorldGetSchema()),
            static (arguments, builder) =>
            {
                if (arguments.ContainsKey("category"))
                    builder.Category = RequireString(arguments, "category");
                if (arguments.ContainsKey("uuids"))
                    builder.Uuids = ReadEntityIdArray(
                        arguments, "uuids", GameMcpWorldQuery.MaximumBatchSize);
                else
                    builder.Uuids = new[] { RequireUuid(arguments, "uuid").ToString("D") };
            },
            static (arguments, errors) =>
            {
                var hasBatch = arguments.ContainsKey("uuids");
                var hasSingle = arguments.ContainsKey("uuid");
                if (!hasBatch && !hasSingle)
                    errors.Add("world_get requires uuids (array) or uuid (singular alias)");
                else if (hasBatch && hasSingle)
                    errors.Add("world_get accepts uuid or uuids, not both");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(
                    GameMcpWorldQuery.GetRows(
                        context,
                        request.Category,
                        request.Uuids).Freeze()))),
        new GameMcpToolDescriptor(
            "world_search",
            new GameMcpToolListing(
                "Search published entities",
                "Search every stable-UUID entity category at once and answer with one uniform row: id, name, category, keywords, matchedOn. A query is matched case-insensitively as a substring — the same rule the game's own search box uses — against the entity's player-facing name, its internal asset name, its id, the keywords the game prints on its tooltip type line, and the category and native type it belongs to. matchedOn names which of those the query actually hit on this row, so a hit whose name does not visibly contain the query says where it did: name, internalName, id, keywords, category or nativeType. Hits come back most relevant first: name matches, then keyword matches, then category matches, with id order inside each band. It does NOT search descriptions: the published world captures no entity descriptions at all, so a word that appears only in an entity's description text finds nothing here; read one entity's description with world_get. Keywords are empty for the classes the game authors none for (upgrades, challenges, views, achievements, advancements, recipe books, crafting recipes) and nothing is synthesized to fill the cell. query is optional whenever at least one filter is named: a filter-only call is the whole category or the whole state, matchedOn reads - because no query was applied, and no keywordHits line is emitted. Naming neither a query nor a filter is refused. state narrows to one lifecycle word and reaches every category whose own list page carries that column: upgrades, research, attributes, alchemy-recipes, concepts, augment-glyphs, rituals, plot-nodes and challenges. A category with no lifecycle model matches no state filter, and no word is derived for it. discovered narrows to one side of the discovery verdict and reaches the categories whose own list page carries that column: spell-recipes and time-runes. Everything else discoverable spells it as its state — a ritual, a glyph and an alchemy recipe are drawn on the member their screens read, which is IsDiscovered() — so state is the filter that reaches those, and naming discovered beside one of them is refused rather than answered with a second word for one fact. run narrows to one challenge run word — idle, queued, active, passed or failed — and challenges are the only category that publishes that column, so a run filter beside any other category is refused naming it. keyword narrows to the things that wear one type asset, named by the word a row's keywords cell prints for it or by that asset's id: it is the far side of the members count a type's world_get prints, taken from the same reach, so keyword with that word and category set to a kind the members line named returns exactly the things it counted. It closes the structure subtype chain that count closes, which a query for the type's name cannot — a parent type's members are spelled with the child type's word. A word no row prints is refused, as is an id nothing wears, and each says which it was. modifies narrows to the things whose effects move one statistic, resource or variable, named by its id, a handle or the name the screen prints: it is the far side of the modifiedBy counts that thing's world_get prints, owned and not-yet-owned alike (state splits them), and each row carries an effect cell saying what it does to that thing in the tooltip's own terms — +1, x1.15, -1 while. The amount is the authored one, before any level or challenge scaling. A name nothing moves is refused. category narrows to one searchable category. An entity the published world has no row for is not here, and composite diagnostic categories are intentionally excluded; use world_list for those rows and their localized partiality evidence. A page that matched nothing says under unprojected whether any id this build loaded answers to the query at all: either the only answers are internal machinery the published world carries no row for, or no loaded id answers to it, so whether it is in this build at all is answered on the page that raised the question. A result of 25 rows or fewer comes back whole when you name no limit. limit is otherwise an upper bound: a page also stops at a 12 KB response budget, so wide rows come back short. nextOffset is present exactly when more rows remain, and is the offset to resume from. keywordHits appears when the query hit more than one keyword, and says how the whole result splits between them.",
                ObjectSchema(
                    new JObject
                    {
                        ["query"] = StringSchema(
                            "Case-insensitive text or UUID fragment. Optional when a category, " +
                            "state, run, discovered, keyword or modifies filter is named."),
                        ["category"] = StringSchema(
                            "Narrow to one category from world_categories."),
                        ["state"] = EnumSchema("locked", "available", "completed"),
                        ["discovered"] = BooleanSchema(
                            "Narrow to the discovered rows, or to the undiscovered ones. Reaches "
                                + "the categories whose rows carry a discovered column."),
                        ["run"] = EnumSchema(
                            "idle", "queued", "active", "passed", "failed"),
                        ["keyword"] = StringSchema(
                            "A type asset, named by the word a row's keywords cell prints or by " +
                            "its id. Narrows to the things that wear it — the far side of its " +
                            "members count."),
                        ["modifies"] = StringSchema(
                            "A thing an effect moves — a statistic, a resource, a variable — " +
                            "named by its id, a handle a row printed, or its screen name. " +
                            "Narrows to the things whose effects move it, owned or not, each " +
                            "with the effect in an effect cell — the far side of its modifiedBy " +
                            "counts."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                    })),
            static (arguments, builder) =>
            {
                // A query is one way to name what you want and a filter is another, so neither is
                // required on its own. Demanding a query to reach a filter taught a round to invent
                // one-letter searches whose reach nobody could state.
                if (arguments.ContainsKey("query"))
                    builder.Query = RequireString(arguments, "query");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                builder.LimitFromCaller = arguments.ContainsKey("limit");
                if (arguments.ContainsKey("category"))
                    builder.Category = RequireString(arguments, "category");
                if (arguments.ContainsKey("state"))
                {
                    builder.StateFilter = RequireOneOf(
                        arguments, "state", "locked", "available", "completed");
                }
                if (arguments.ContainsKey("run"))
                {
                    builder.RunFilter = RequireOneOf(
                        arguments, "run", "idle", "queued", "active", "passed", "failed");
                }
                if (arguments.ContainsKey("discovered"))
                    builder.DiscoveredFilter = RequireBool(arguments, "discovered");
                if (arguments.ContainsKey("keyword"))
                    builder.KeywordFilter = RequireString(arguments, "keyword");
                if (arguments.ContainsKey("modifies"))
                    builder.ModifiesFilter = RequireString(arguments, "modifies");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(
                    GameMcpWorldSearch.Search(
                        context,
                        request.Query,
                        request.Offset,
                        request.Limit,
                        request.Category,
                        request.StateFilter,
                        request.RunFilter,
                        request.KeywordFilter,
                        request.LimitFromCaller,
                        request.DiscoveredFilter,
                        request.ModifiesFilter).Freeze()))),
        new GameMcpToolDescriptor(
            "strategist",
            new GameMcpToolListing(
                "Read the strategist's bulletin",
                "Read the strategist's latest judgement of the published world: what it would work toward and what that costs. The summary names the long-term goals (finishing the world through Restoration, and cross-run capital), the top short-term goals — every gate the player can buy now, Restoration's chain first — where the run stands, and which resources the goals are waiting on. Every cost is a time measured on sustained income: trivial under 10 s, cheap under a minute, notable under an hour, expensive beyond, or out of reach with the reason; while income is still being measured a cost says so instead. section names one part in full: goals, position, demand, or facts — the exact inputs the bulletin was derived from, one record per line.",
                ObjectSchema(
                    new JObject
                    {
                        ["section"] = EnumSchema("goals", "position", "demand", "facts"),
                    })),
            static (arguments, builder) =>
            {
                if (arguments.ContainsKey("section"))
                    builder.Section = RequireOneOf(
                        arguments, "section", "goals", "position", "demand", "facts");
            },
            FrameRead(
                ReadOnly,
                GameMcpFrameData.World | GameMcpFrameData.ServiceHealth,
                static (request, context) => GameMcpStrategistQuery.Read(context, request.Section))),
    };

    private static JObject WorldGetSchema()
    {
        return ObjectSchema(
            new JObject
            {
                // The schema said "canonical D-format UUID" while every row on the surface printed a
                // six-character handle and this verb resolved it — so an agent that believed the
                // contract fanned out to recover whole UUIDs it never needed. The rule is said once
                // here, on the verb every id is carried to, and every other id argument is named
                // for what a row calls it.
                ["uuids"] = ArraySchema(
                    StringSchema(
                        "An entity id: the short handle a row printed, or the whole UUID."),
                    1,
                    GameMcpWorldQuery.MaximumBatchSize),
                ["uuid"] = StringSchema(
                    "Singular alias for one id; do not combine with uuids."),
                ["category"] = StringSchema(
                    "Optional. Exact name returned by world_categories, naming which table the " +
                    "row is read from. An id resolves its own category, so this is only needed " +
                    "to address a table whose native type belongs to more than one."),
            });
    }
}
#endif
