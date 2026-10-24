#if SERVICE_CYCLE_PROFILE
using System;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.World;
using static OrbAutomata.GameMcp.GameMcpOperationClass;
using static OrbAutomata.GameMcp.GameMcpToolArguments;
using static OrbAutomata.GameMcp.GameMcpToolCatalog;
using static OrbAutomata.GameMcp.GameMcpToolRoute;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGameplayTools
{
    internal static GameMcpToolDescriptor[] Tools() => new[]
    {
        new GameMcpToolDescriptor(
            "game_purchase",
            new GameMcpToolListing(
                "Purchase an attribute or upgrade",
                "Live-revalidate and apply one UUID-addressed attribute (native StructureSO) or upgrade purchase. The game takes the levels into its action queue and builds them afterwards, so a commit answers that it queued and how many — never a level that has not moved yet. amount is taken as far as it goes, exactly as the game's own buy button does: a press that delivers less than the ask says delivered against asked on one line and, on that same line, why the rest did not go — the game took no more this press, or the action queue had no room for it. Only a queue with no free slot at all refuses, and Auto Buy's own settings, including the queue slots it reserves for manual play, do not apply to this call.",
                ObjectSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Structure (shown in game as an attribute) or upgrade id; kind is derived."),
                        ["amount"] = IntegerSchema(1, 1000),
                    },
                    "uuid", "amount")),
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Purchase)),
        new GameMcpToolDescriptor(
            "game_cast",
            new GameMcpToolListing(
                "Cast an equipped spell",
                "Live-revalidate an equipped slot and fire it, release a charge hold, or press an active toggle spell's native cast button again to turn it off. A fire on a spell that is already running is refused rather than pressed: the game answers that press with a warning or by ending the cast, never by starting one. A committed fire means the press started a cast now. charge=true holds the cast button down the way the player does, so the spell charges instead of firing at once; release it with mode=release, and the longer it was held the more power the cast lands with. Charging needs the Charged Spells research on a spell type that scales with it, and a spell the game will not charge is refused rather than fired uncharged. Auto Cast's own settings do not apply to this call: the stand-down it takes after a manual cast never holds this press back, and the cast charges because charge=true asked for it rather than because the service prefers full charges.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("fire", "release", "toggle_off"),
                        ["slot"] = IntegerSchema(1, 256),
                        ["uuid"] = StringSchema("Spell recipe id currently occupying the slot."),
                        ["charge"] = BooleanSchema(
                            "Hold the cast button down so the spell charges; release with mode=release."),
                    },
                    "mode", "slot", "uuid"),
                    ModeRule("release", forbidden: new[] { "charge" }),
                    ModeRule("toggle_off", forbidden: new[] { "charge" }))),
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(
                    arguments, "mode", "fire", "release", "toggle_off");
                builder.SlotIndex = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "fire" && OptionalBool(arguments, "charge", false))
                    builder.SerializedValue = "charge";
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Cast)),
        new GameMcpToolDescriptor(
            "game_concept",
            new GameMcpToolListing(
                "Assign or remove a concept",
                "Apply one exact concept assignment change and return its terminal native result inline. The game's own gates decide it: a free Concept slot, a discovered recipe, and the drain the assignment costs. The read side is the concepts category: world_list category=concepts pages the forty-six the Scholar screen draws, and world_get on one carries the slot budget and this verb's own add decision. Auto Concept's own settings, the rate reserve it holds its own assignments back for included, do not apply to this call.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("add", "remove"),
                        ["uuid"] = StringSchema("Alchemy recipe id."),
                        ["amount"] = IntegerSchema(1, 1_000_000),
                    },
                    "mode", "uuid", "amount")),
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1_000_000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Concept)),
        new GameMcpToolDescriptor(
            "game_agromancy",
            new GameMcpToolListing(
                "Use the Agromancy screen",
                "Add or remove a plot action, harvest element, or element action shown on World/Agromancy, "
                    + "or set the Druidry Lv its -/+ adjuster shows (1 to Max Druidry Lv; drawn once "
                    + "Unlock Druidry Levels is bought).",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "add_plot_action", "remove_plot_action",
                            "add_element", "remove_element", "add_element_action",
                            "remove_element_action", "set_druidry_level"),
                        ["uuid"] = StringSchema("Published plot or harvest-element id."),
                        ["actionUuid"] = StringSchema(
                            "Published action UUID offered by that plot or element."),
                        ["amount"] = IntegerSchema(1, 10_000),
                        ["level"] = IntegerSchema(WorldSpellWorkbench.MinimumDialLevel, int.MaxValue),
                    },
                    "mode"),
                    ModeRule("add_plot_action", new[] { "uuid", "actionUuid", "amount" }, new[] { "level" }),
                    ModeRule("remove_plot_action", new[] { "uuid", "actionUuid", "amount" }, new[] { "level" }),
                    ModeRule("add_element", new[] { "uuid", "amount" }, new[] { "actionUuid", "level" }),
                    ModeRule("remove_element", new[] { "uuid", "amount" }, new[] { "actionUuid", "level" }),
                    ModeRule("add_element_action", new[] { "uuid", "actionUuid", "amount" }, new[] { "level" }),
                    ModeRule("remove_element_action", new[] { "uuid", "actionUuid", "amount" }, new[] { "level" }),
                    ModeRule("set_druidry_level", new[] { "level" }, new[] { "uuid", "actionUuid", "amount" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "add_plot_action", "remove_plot_action",
                    "add_element", "remove_element", "add_element_action",
                    "remove_element_action", "set_druidry_level");
                if (builder.Mode == "set_druidry_level")
                {
                    builder.Key = "druidry";
                    builder.Amount = RequiredInt(
                        arguments, "level", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
                    return;
                }
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.SecondaryUuid = builder.Mode is "add_plot_action" or
                    "remove_plot_action" or "add_element_action" or
                    "remove_element_action"
                    ? RequireUuid(arguments, "actionUuid")
                    : Guid.Empty;
                builder.Amount = RequiredInt(arguments, "amount", 1, 10_000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Harvest,
                static request => request.Mode is "add_plot_action" or "remove_plot_action"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition,
                static request => request.Mode == "set_druidry_level"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.HarvestLifecycle)),
        new GameMcpToolDescriptor(
            "game_structure",
            new GameMcpToolListing(
                "Enable or disable an attribute",
                "Apply the same native enable or disable control shown for a published StructureSO attribute.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("enable", "disable"),
                        ["uuid"] = StringSchema("Published structure id; shown in game as an attribute."),
                    },
                    "mode", "uuid"),
                    ModeRule("enable"),
                    ModeRule("disable")),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "enable", "disable");
                builder.Uuid = RequireUuid(arguments, "uuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.StructureLifecycle)),
        new GameMcpToolDescriptor(
            "game_spell_mastery",
            new GameMcpToolListing(
                "Confirm a spell's mastery",
                "Press Confirm Mastery for one spell, which raises the Mastery Lv its card shows. "
                    + "mode=all presses the native Level All Spells sweep instead, which walks the "
                    + "whole spellbook and skips only the spells it cannot afford. Spell Lv is the "
                    + "global casting dial and is never moved here; that is game_casting_dial.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("single", "all"),
                        ["uuid"] = StringSchema("Required only for single; a published spell-recipe id."),
                    },
                    "mode"),
                    ModeRule("single", new[] { "uuid" }),
                    ModeRule("all", forbidden: new[] { "uuid" }))),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "single", "all");
                builder.Uuid = builder.Mode == "single"
                    ? RequireUuid(arguments, "uuid")
                    : Guid.Empty;
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellLevel)),
        new GameMcpToolDescriptor(
            "game_casting_dial",
            new GameMcpToolListing(
                "Set a global casting dial",
                "Set the global Output Level or Reserve Level shown together on the Casting screen.",
                ObjectSchema(
                    new JObject
                    {
                        ["dial"] = EnumSchema("output", "reserve"),
                        ["value"] = IntegerSchema(
                            WorldSpellWorkbench.MinimumDialLevel, int.MaxValue),
                    },
                    "dial", "value"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Key = RequireOneOf(arguments, "dial", "output", "reserve");
                builder.Mode = builder.Key == "output" ? "set_output_level" : "set_reserve_level";
                builder.Amount = RequiredInt(
                    arguments, "value", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition)),
        new GameMcpToolDescriptor(
            "game_spell_loadout",
            new GameMcpToolListing(
                "Read staging; preview, add, remove, or move a spell",
                "Read the exact staged Spellcraft core and augment layout; preview an explicit layout's native price without changing staging; add that layout baked into a new spell; or remove or move an equipped spell. preview and add take the augments as glyphs=[{\"uuid\": \"<augment glyph id>\", \"count\": 1}], or glyphs=[] for none. remove and move name the slot on the loadout bar, counted from 1 as the screen shows it. Success returns the settled slot change.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("staged", "preview", "add", "remove", "move"),
                        ["uuid"] = StringSchema("A discovered recipe id, for preview and add."),
                        ["glyphs"] = DescribedSchema(ArraySchema(
                            ObjectSchema(new JObject
                            {
                                ["uuid"] = StringSchema("Published augment glyph id."),
                                ["count"] = IntegerSchema(1, int.MaxValue),
                            }, "uuid", "count"), 0, 64), GlyphLayoutShape),
                        ["slot"] = IntegerSchema(1, 256),
                        ["destination"] = IntegerSchema(1, 256),
                    },
                    "mode"),
                    ModeRule("staged", forbidden: new[]
                    {
                        "uuid", "glyphs", "slot", "destination",
                    }),
                    ModeRule("preview", new[] { "uuid", "glyphs" },
                        new[] { "slot", "destination" }),
                    ModeRule("add", new[] { "uuid", "glyphs" },
                        new[] { "slot", "destination" }),
                    ModeRule("remove", new[] { "slot" },
                        new[] { "uuid", "glyphs", "destination" }),
                    ModeRule("move", new[] { "slot", "destination" },
                        new[] { "uuid", "glyphs" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(
                    arguments, "mode", "staged", "preview", "add", "remove", "move");
                if (builder.Mode is "preview" or "add")
                {
                    builder.Uuid = RequireUuid(arguments, "uuid");
                    builder.UuidCounts = RequireGlyphLayout(arguments);
                }
                else if (builder.Mode is "remove" or "move")
                    builder.Amount = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "move")
                    builder.SlotIndex = RequiredInt(arguments, "destination", 1, 256);
            },
            ExecutorRead(ReadOnly, GameplayData,
                static (executor, request) => executor.PreviewSpellWorkbench(request),
                static request => request.Mode == "preview"),
            ExecutorRead(ReadOnly, None,
                static (executor, _) => executor.ReadStagedSpellWorkbench(),
                static request => request.Mode == "staged"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellWorkbench,
                static request => request.Mode == "add"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellLoadout)),
        new GameMcpToolDescriptor(
            "game_targeting",
            new GameMcpToolListing(
                "Submit or randomize the pending target",
                "Resolve the game's one current target request. Success returns the exact submitted structure.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("submit", "randomize"),
                        ["uuid"] = StringSchema("Required only for submit; an eligible named targeting candidate id."),
                    },
                    "mode"),
                    ModeRule("submit", new[] { "uuid" }),
                    ModeRule("randomize", forbidden: new[] { "uuid" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "submit", "randomize");
                if (builder.Mode == "submit") builder.Uuid = RequireUuid(arguments, "uuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Targeting)),
        new GameMcpToolDescriptor(
            "game_consumable",
            new GameMcpToolListing(
                "Use or organize consumables",
                "Use, cancel, discard, randomize, or reorder one consumable. Success returns the changed amount, flag, or slot.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "use", "cancel", "discard", "set_randomization", "move"),
                        ["uuid"] = StringSchema("Published consumable id."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                        ["enabled"] = BooleanSchema("Requested randomization state."),
                        ["list"] = EnumSchema("inventory", "hotbar"),
                        ["destination"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid"),
                    ModeRule("discard", new[] { "amount" }, new[] { "enabled", "list", "destination" }),
                    ModeRule("set_randomization", new[] { "enabled" }, new[] { "amount", "list", "destination" }),
                    ModeRule("move", new[] { "list", "destination" }, new[] { "amount", "enabled" }),
                    ModeRule("use", forbidden: new[] { "amount", "enabled", "list", "destination" }),
                    ModeRule("cancel", forbidden: new[] { "amount", "enabled", "list", "destination" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(
                    arguments,
                    "mode",
                    "use",
                    "cancel",
                    "discard",
                    "set_randomization",
                    "move");
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "discard")
                    builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
                if (builder.Mode == "set_randomization")
                    builder.SerializedValue = OptionalBool(arguments, "enabled", false)
                        ? "true"
                        : "false";
                if (builder.Mode == "move")
                {
                    builder.Key = RequireOneOf(arguments, "list", "inventory", "hotbar");
                    builder.SlotIndex = RequiredInt(arguments, "destination", 1, int.MaxValue);
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Consumable)),
        new GameMcpToolDescriptor(
            "game_craft",
            new GameMcpToolListing(
                "Craft or control one recipe",
                "Craft one exact recipe, add one automation repetition, or cancel its manual or automated instance. amount is how many crafts craft queues and defaults to 1; \"max\" queues what the screen's Mx press would. The ask is taken as far as the stock goes: a craft that queues less than the ask says queued against asked on one line and, on that same line, why the rest stopped. Success returns only the settled quantity change.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "craft", "automate", "cancel_manual", "cancel_automation"),
                        ["uuid"] = StringSchema("Published crafting recipe id."),
                        ["amount"] = new JObject
                        {
                            ["anyOf"] = new JArray(
                                IntegerSchema(1, int.MaxValue),
                                EnumSchema("max")),
                        },
                    },
                    "uuid"),
                    ModeRule("craft"),
                    ModeRule("automate", forbidden: new[] { "amount" }),
                    ModeRule("cancel_manual", forbidden: new[] { "amount" }),
                    ModeRule("cancel_automation", forbidden: new[] { "amount" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "craft", "automate",
                        "cancel_manual", "cancel_automation")
                    : "craft";
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "craft" &&
                    arguments.TryGetValue("amount", out var craftAmount) &&
                    craftAmount.Type == JTokenType.String)
                {
                    if (craftAmount.Value<string>() != "max")
                        throw new GameMcpInvalidParamsException(
                            "amount must be a whole number of crafts or \"max\"");
                    builder.SerializedValue = "max";
                    builder.Amount = 1;
                }
                else
                {
                    builder.Amount = builder.Mode == "craft"
                        ? OptionalIntInRange(arguments, "amount", 1, 1, int.MaxValue)
                        : 1;
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Crafting)),
        new GameMcpToolDescriptor(
            "game_discover",
            new GameMcpToolListing(
                "Drive a discovery tree roll",
                "Drive a Discovery Tree's roll: pay for it, pick one of the offers it rolls, confirm the pick, or reroll. Every discoverable thing comes from its tree's roll; no screen discovers one thing directly.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("offer_initiate", "offer_select", "offer_confirm", "offer_reroll"),
                        ["uuid"] = StringSchema("The published discovery tree."),
                        ["offerUuid"] = StringSchema("Required for offer_select and offer_confirm."),
                    },
                    "mode", "uuid"),
                    ModeRule("offer_initiate", new[] { "uuid" }, new[] { "offerUuid" }),
                    ModeRule("offer_reroll", new[] { "uuid" }, new[] { "offerUuid" }),
                    ModeRule("offer_select", new[] { "uuid", "offerUuid" }),
                    ModeRule("offer_confirm", new[] { "uuid", "offerUuid" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "offer_initiate", "offer_select", "offer_confirm", "offer_reroll");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.SecondaryUuid = OptionalUuid(arguments, "offerUuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.DiscoveryTreeOffer)),
        new GameMcpToolDescriptor(
            "game_equipment",
            new GameMcpToolListing(
                "Equip or unequip an artifact",
                "Equip or unequip an explicit artifact amount through the native slot, type-slot, stack, and usage-cost decision. Success returns the stack count before and after.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("equip", "unequip"),
                        ["uuid"] = StringSchema("Published equipment id."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid", "amount"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "equip", "unequip");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.EquipmentLoadout)),
        new GameMcpToolDescriptor(
            "game_alchemy",
            new GameMcpToolListing(
                "Change the ordinary Alchemy loadout",
                "Add or remove an explicit number of uses of one discovered ordinary Alchemy recipe through the native usage-capacity decision, or set the Alchemy Lv its -/+ adjuster shows (1 to Max Alchemy Level; drawn once Level Alchemy is bought). Concept assignments stay on game_concept.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("add", "remove", "set_alchemy_level"),
                        ["uuid"] = StringSchema("Published ordinary alchemy recipe id."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                        ["level"] = IntegerSchema(WorldSpellWorkbench.MinimumDialLevel, int.MaxValue),
                    },
                    "mode"),
                    ModeRule("add", new[] { "uuid", "amount" }, new[] { "level" }),
                    ModeRule("remove", new[] { "uuid", "amount" }, new[] { "level" }),
                    ModeRule("set_alchemy_level", new[] { "level" }, new[] { "uuid", "amount" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove", "set_alchemy_level");
                if (builder.Mode == "set_alchemy_level")
                {
                    builder.Key = "alchemy";
                    builder.Amount = RequiredInt(
                        arguments, "level", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
                    return;
                }
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition,
                static request => request.Mode == "set_alchemy_level"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.AlchemyLoadout)),
        new GameMcpToolDescriptor(
            "game_ritual",
            new GameMcpToolListing(
                "Select, level, activate, or cancel a ritual reward",
                "Drive the Ritual list controls. Discovering a ritual is the Devote tree roll on game_discover; cancel_duration ends a completed run's duration reward, not an active battle.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "select", "deselect", "set_level", "activate", "cancel_duration", "end"),
                        ["uuid"] = StringSchema("Published ritual id."),
                        ["level"] = IntegerSchema(
                            WorldRitualDecision.NativeMinimumStartingLevel, int.MaxValue - 1),
                    },
                    "mode", "uuid"),
                    ModeRule("select", forbidden: new[] { "level" }),
                    ModeRule("deselect", forbidden: new[] { "level" }),
                    ModeRule("set_level", new[] { "level" }),
                    ModeRule("activate", forbidden: new[] { "level" }),
                    ModeRule("cancel_duration", forbidden: new[] { "level" }),
                    ModeRule("end", forbidden: new[] { "level" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "deselect", "set_level", "activate", "cancel_duration", "end");
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "set_level")
                    builder.Amount = checked(RequiredInt(
                        arguments,
                        "level",
                        WorldRitualDecision.NativeMinimumStartingLevel,
                        int.MaxValue - 1) + 1);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.RitualLifecycle)),
        new GameMcpToolDescriptor(
            "game_level_up",
            new GameMcpToolListing(
                "Level a glyph, artifact type, resource type or Time Rune",
                "Use the native level-list controls for those four kinds: purchase buys paid levels "
                    + "and bonus applies the free levels the game grants. Research and spells keep "
                    + "their dedicated tools.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("purchase", "bonus"),
                        ["uuid"] = StringSchema("Published levelable entity id."),
                        ["amount"] = IntegerSchema(1, 1000),
                    },
                    "mode", "uuid", "amount"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "purchase", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.GenericLevel)),
        new GameMcpToolDescriptor(
            "game_loadout",
            new GameMcpToolListing(
                "Manage player loadouts and snapshots",
                "Select or edit a player loadout, or save, load, and clear visible Equipment or Alchemy snapshot slots. A loadout is named by its position on the loadout bar and a snapshot by its section and its slot, both counted from 1 as the screen shows them.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("select", "set_section", "rename", "next_icon", "next_color",
                            "snapshot_save", "snapshot_load", "snapshot_clear"),
                        ["loadout"] = IntegerSchema(1, int.MaxValue),
                        ["section"] = EnumSchema("equipment", "alchemy"),
                        ["enabled"] = BooleanSchema("Whether the selected loadout saves that section."),
                        ["name"] = StringSchema("Player-visible loadout label, at most 24 characters."),
                        ["slot"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode"),
                    ModeRule("select", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("set_section", new[] { "loadout", "section", "enabled" },
                        new[] { "name", "slot" }),
                    ModeRule("rename", new[] { "loadout", "name" },
                        new[] { "section", "enabled", "slot" }),
                    ModeRule("next_icon", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("next_color", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("snapshot_save", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" }),
                    ModeRule("snapshot_load", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" }),
                    ModeRule("snapshot_clear", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "select", "set_section",
                    "rename", "next_icon", "next_color", "snapshot_save",
                    "snapshot_load", "snapshot_clear");
                if (builder.Mode.StartsWith("snapshot_", StringComparison.Ordinal))
                {
                    builder.Key = RequireOneOf(arguments, "section", "equipment", "alchemy");
                    builder.SlotIndex = RequiredInt(arguments, "slot", 1, int.MaxValue);
                }
                else
                {
                    builder.Amount = RequiredInt(arguments, "loadout", 1, int.MaxValue);
                    if (builder.Mode == "set_section")
                    {
                        builder.Key = RequireOneOf(arguments, "section", "equipment", "alchemy");
                        builder.SerializedValue = OptionalBool(arguments, "enabled", false)
                            ? "true"
                            : "false";
                    }
                    if (builder.Mode == "rename")
                        builder.SerializedValue = RequireString(arguments, "name");
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Loadout)),
        new GameMcpToolDescriptor(
            "time_challenge",
            new GameMcpToolListing(
                "Select, queue, abandon, or reroll challenges",
                "Drive one exact native challenge decision on the game's Time tab. select presses a challenge's preferred toggle: it selects an unselected challenge and gives up a selected one, and where the selections are full and exactly one is held it gives that one up and takes this one in a single call. queue presses the row's queue toggle, which moves it between idle and queued; a queued challenge starts running at the next reset, and only a running one can be abandoned, and a challenge that has already run cannot be queued again until the next reset. reroll presses the offer screen's own new-challenges button, and the first press of a world cycle is the offer fetch: it costs no reroll, it draws this cycle's offers, it arms every challenge it drew to run at the next reset, and it unlocks the reset that refuses until the offers are fetched. Every later press costs one reroll, redraws the offers, and arms what it draws the same way. The answer names which of the two presses it was, what is armed for the reset afterwards, and whether the reset opened. state returns the reroll budget, the selections, the reset decision, and this cycle's offers once they have been fetched.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("select", "queue", "abandon", "reroll", "state"),
                        ["uuid"] = StringSchema("Required for select, queue, and abandon; a published challenge id."),
                    },
                    "mode"),
                    ModeRule("select", new[] { "uuid" }),
                    ModeRule("queue", new[] { "uuid" }),
                    ModeRule("abandon", new[] { "uuid" }),
                    ModeRule("reroll", forbidden: new[] { "uuid" }),
                    ModeRule("state", forbidden: new[] { "uuid" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "queue", "abandon", "reroll", "state");
                builder.Uuid = OptionalUuid(arguments, "uuid");
                if (builder.Mode is "select" or "queue" or "abandon" && builder.Uuid == Guid.Empty)
                    throw new GameMcpInvalidParamsException("uuid is required for " + builder.Mode);
                if (builder.Mode is "reroll" or "state" && builder.Uuid != Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "uuid is accepted only for select, queue, or abandon");
            },
            FrameRead(ReadOnly, GameplayData,
                static (_, context) => GameMcpToolExecution.Read(
                    GameMcpChallengeProjection.ChallengeStateRead(context).Freeze()),
                static request => request.Mode == "state"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.Challenge)),
        new GameMcpToolDescriptor(
            "time_prestige",
            new GameMcpToolListing(
                "Reset the persistent world",
                "Commit the irreversible native persistent reset after the world cycle and challenge choices are ready. This verb only commits: it has no read mode, and every fact the decision turns on is read with time_challenge(mode=\"state\"), whose prestigeState block carries the three Time Advancement figures, the reset count, the persistent resource, the challenges queued for the reset, the surviving rewards, and the exact reset.available verdict with its reason — so no attempt is needed to learn whether a reset can run. Read that first; then call this. Success waits for a fresh post-reset world and returns its named prestige and challenge decisions inline.",
                ObjectSchema(
                    new JObject
                    {
                        ["confirm"] = BooleanSchema("Must be true to confirm the irreversible persistent reset."),
                    },
                    "confirm"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = "reset";
                if (!OptionalBool(arguments, "confirm", false))
                    throw new GameMcpInvalidParamsException(
                        "confirm must be true to request the irreversible persistent reset");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Prestige)),
        new GameMcpToolDescriptor(
            "game_research",
            new GameMcpToolListing(
                "Develop or manage research",
                "Develop, queue, pause, resume, cancel, or apply a free bonus level to one exact research. amount is the number of levels a develop asks for and defaults to 1, and an ask beyond what this call can admit is refused with that ceiling rather than trimmed. A develop buys research time rather than a finished level, so it answers that it queued and how many; pause, resume, cancel, and bonus apply at once and answer the one state or count they moved.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("develop", "pause", "resume", "cancel", "bonus"),
                        ["uuid"] = StringSchema("Published research id."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid"),
                    ModeRule("develop"),
                    ModeRule("pause", forbidden: new[] { "amount" }),
                    ModeRule("resume", forbidden: new[] { "amount" }),
                    ModeRule("cancel", forbidden: new[] { "amount" }),
                    ModeRule("bonus", forbidden: new[] { "amount" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "develop", "pause", "resume", "cancel", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = builder.Mode == "develop"
                    ? OptionalIntInRange(arguments, "amount", 1, 1, int.MaxValue)
                    : 1;
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Research)),
    };
}
#endif
