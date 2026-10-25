#if SERVICE_CYCLE_PROFILE
using System;
using Newtonsoft.Json.Linq;
using static OrbAutomata.GameMcp.GameMcpOperationClass;
using static OrbAutomata.GameMcp.GameMcpToolArguments;
using static OrbAutomata.GameMcp.GameMcpToolCatalog;
using static OrbAutomata.GameMcp.GameMcpToolRoute;

namespace OrbAutomata.GameMcp;

internal static class GameMcpScreenTools
{
    internal static GameMcpToolDescriptor[] Tools() => new[]
    {
        new GameMcpToolDescriptor(
            "game_screenshot",
            new GameMcpToolListing(
                "Capture the game framebuffer",
                "Return a PNG as inline MCP image content. Reading one costs ceil(width/28) x ceil(height/28) tokens, so pixels are its whole price and format and compression never enter it. Every capture arrives 896 pixels wide — 32 patch columns, the narrowest width at which every class of on-screen text stays readable. Set save=true to also write a generated name in the trace folder.",
                ObjectSchema(new JObject
                {
                    ["save"] = BooleanSchema("Also save to the trace folder."),
                }),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
                builder.SaveCapture = OptionalBool(arguments, "save", false),

            // A screenshot is a capture the server performs, not a read of published state, whether or
            // not the caller also asks for it on disk. One classification keeps one status word.
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.Screenshot)),
        new GameMcpToolDescriptor(
            "game_continue",
            new GameMcpToolListing(
                "Continue the selected save",
                "On the Start scene only, invoke the game's audited native Continue action for the already selected save.",
                ObjectSchema(),
                readOnly: false,
                idempotent: false),
            NoArguments,
            Command(UiState, UiData, GameMcpCommandKind.ContinueRun)),
        new GameMcpToolDescriptor(
            "game_return_to_menu",
            new GameMcpToolListing(
                "Return to the Start screen",
                "While playing, open the panel that holds the game's Back to Main Menu button if it is shut, invoke that button including its authored manual-save event, and acknowledge after the native screen transition starts but before scene teardown.",
                ObjectSchema(),
                readOnly: false,
                idempotent: false),
            NoArguments,
            Command(Gameplay, GameplayData, GameMcpCommandKind.ReturnToMenu)),
        new GameMcpToolDescriptor(
            "game_modal",
            new GameMcpToolListing(
                "Use the game's panels",
                "Open one of the panels this screen's chrome puts up — the ones game_screen_elements lists as opens: modal — or dismiss the one unambiguous open native modal through its visible close control.",
                ObjectSchema(new JObject
                {
                    ["mode"] = EnumSchema("dismiss", "open"),
                    ["title"] = StringSchema(
                        "The panel to open, named exactly as game_screen_elements prints it. " +
                        "Required by open, ignored by dismiss."),
                }, "mode"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "dismiss", "open");
                if (string.Equals(builder.Mode, "open", StringComparison.Ordinal))
                    builder.Title = RequireString(arguments, "title");
            },
            Command(UiState, UiData, GameMcpCommandKind.Modal)),
        new GameMcpToolDescriptor(
            "game_screen_catalog",
            new GameMcpToolListing(
                "Discover navigable screens",
                "List the live screens with the active one marked and its current subtab strips grouped beneath it. Screens come back under screens[]; a navigate answers with activeScreen.",
                ObjectSchema()),
            NoArguments,
            ExecutorRead(ReadOnly, None, static (executor, _) => executor.CaptureScreenCatalog())),
        new GameMcpToolDescriptor(
            "game_navigate",
            new GameMcpToolListing(
                "Navigate the live screen catalog",
                "UI-only, no gameplay/save mutation. Select one catalog screen, an optional subtab of that screen, and an optional tile that page draws; the answer is the arrived screen in words. game_screenshot is the only tool that captures the framebuffer.",
                ObjectSchema(
                    new JObject
                    {
                        ["screen"] = StringSchema("Exact player-facing top-level screen name."),
                        ["subtab"] = StringSchema("Optional exact player-facing subtab name. A nested strip is one path, parent first: Spellbook/Loadout."),
                        ["uuid"] = StringSchema("Optional tile to pick: an Agromancy plot on World/Agromancy, or a spell on Magic/Spellbook/Spells, which opens its card."),
                    },
                    "screen"),
                readOnly: false,
                idempotent: false,
                classification: "UI-only, no gameplay/save mutation"),
            static (arguments, builder) =>
            {
                builder.Tab = ParseNavigationSelector(RequireSelector(arguments, "screen"));
                if (arguments.TryGetValue("subtab", out _))
                    builder.Subtab = ParseNavigationSelector(RequireSelector(arguments, "subtab"));
                builder.Uuid = OptionalUuid(arguments, "uuid");
            },
            Command(UiState, UiData, GameMcpCommandKind.Navigation)),
        new GameMcpToolDescriptor(
            "game_screen_elements",
            new GameMcpToolListing(
                "List the screen's hoverable elements",
                "Page through the elements the player can hover right now — the current screen, its persistent chrome, and any open modal — as an address book of paths, names, ids and slots, with none of their text: this is the only verb that mints a path, and game_tooltip reads what one of them says. Elements are addressed by sibling-indexed native path, and every path printed here is the shortest tail of that path no other live element answers to — it lengthens one segment at a time only where uniqueness requires it, so it names exactly one element and is always a valid path argument as printed, with no ancestry to prepend. A row is one panel of the screen. When every element of a panel is the same component with a different index the panel says that component once as pathComponent and each element's path is only its own [index], which joins onto the component with no separator. A panel holding exactly one element is that element, said directly rather than as a group of one. An element about a game entity carries that entity's id, including the casting-bar and passive buttons, whose id is the recipe asset the live instance was built from — the same asset whose name the button prints. An element about nothing but a control carries no id. A button showing a spell the loadout holds in exactly one slot also carries that slot, which is the number every cast verb takes; the bracket index in a path is a Unity sibling ordinal and is never it. offset, limit, total and nextOffset all count panels, and a factored screen is small enough that one call usually returns all of them. A closed modal stays instantiated and is not listed. nextOffset is present exactly when more rows remain, and is the offset to resume from.",
                ObjectSchema(new JObject
                {
                    ["offset"] = IntegerSchema(0, int.MaxValue),
                    ["limit"] = IntegerSchema(1, 200),
                })),
            static (arguments, builder) =>
            {
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldList.DefaultLimit);
            },

            // The tooltip catalog joins the spell a casting-bar button is about to the loadout slot the
            // cast verbs address it by, and the loadout is a published world fact. With no world there
            // is no join and those rows carry no slot; the catalog still answers, because what the
            // player can hover is a screen fact rather than a save one.
            Command(ReadOnly, World, GameMcpCommandKind.TooltipCatalog)),
        new GameMcpToolDescriptor(
            "game_tooltip",
            new GameMcpToolListing(
                "Read one element's tooltip text",
                "Read the tooltip text the game prints for one element of the current screen, or for one entity, as compact plain screen text. Address it by path or by uuid, exactly one of the two. A uuid needs no catalog call first: an id from a search, a list, an action response or a tooltip's link line reads the screen's words about that thing where one element on this screen is about it, and that entity's own tooltip where none is, unless the game hides that entity from the player — several elements are refused naming their paths, because two buttons for one thing are separate objects and may print different text. A path is the only address for the elements bound to no entity — the badges, the multiplier control, the help elements and the suite's own controls — and the only way to pick between duplicates. Paths are volatile screen-state handles, so refresh game_screen_elements after navigation or mutation; a uuid is not, and stays good across screens.",
                ObjectSchema(
                    new JObject
                    {
                        ["path"] = StringSchema(
                            "A path as game_screen_elements returned it, joined onto what its " +
                            "row did not repeat: a row under a pathComponent is addressed by " +
                            "that component and its own index joined, never by the bare index. " +
                            "Any longer tail of the same path is accepted, including the whole " +
                            "path."),
                        ["uuid"] = StringSchema(
                            "A published entity id, as any row or link line of this surface " +
                            "prints it. The one element on this screen about that entity " +
                            "answers, the entity's own tooltip answers where none is, and " +
                            "several refuse naming their paths."),
                    }),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Path = OptionalString(arguments, "path");
                builder.Uuid = OptionalUuid(arguments, "uuid");
            },

            // Two address forms, exactly one per call. A path names a place on the screen and a uuid
            // names a thing; sending both would be two questions in one call, and answering either
            // silently would be a guess about which the caller meant.
            static (arguments, errors) =>
            {
                var hasPath = arguments.ContainsKey("path");
                var hasEntity = arguments.ContainsKey("uuid");
                if (!hasPath && !hasEntity)
                    errors.Add("game_tooltip requires path (a screen address) or uuid (a published entity)");
                else if (hasPath && hasEntity)
                    errors.Add("game_tooltip accepts path or uuid, not both");
            },

            // A uuid no element on this screen shows answers with the entity's own tooltip unless the
            // world marks the entity hidden, so a uuid read takes the world. A path read reads no world
            // at all and keeps taking none — what the player can hover is a screen fact.
            Command(ReadOnly, World, GameMcpCommandKind.TooltipRead,
                static request => request.Uuid != Guid.Empty),
            Command(ReadOnly, None, GameMcpCommandKind.TooltipRead)),
        new GameMcpToolDescriptor(
            "game_probe",
            new GameMcpToolListing(
                "Run an allowlisted native value probe",
                "Read runtime/lifecycle, navigation, or live action-queue facts absent from the published world.",
                ObjectSchema(
                    new JObject
                    {
                        ["probe"] = EnumSchema("runtime", "action_queue_room", "navigation"),
                    },
                    "probe")),
            static (arguments, builder) =>
                builder.Probe = RequireOneOf(
                    arguments, "probe", "runtime", "action_queue_room", "navigation"),
            Command(ReadOnly, None, GameMcpCommandKind.Probe)),
    };

    private static GameMcpNavigationSelector ParseNavigationSelector(string label) => new(label);
}
#endif
