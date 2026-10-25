#if SERVICE_CYCLE_PROFILE
using Newtonsoft.Json.Linq;
using static OrbAutomata.GameMcp.GameMcpOperationClass;
using static OrbAutomata.GameMcp.GameMcpToolArguments;
using static OrbAutomata.GameMcp.GameMcpToolCatalog;
using static OrbAutomata.GameMcp.GameMcpToolRoute;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSuiteTools
{
    internal static GameMcpToolDescriptor[] Tools() => new[]
    {
        new GameMcpToolDescriptor(
            "suite_health",
            new GameMcpToolListing(
                "Read suite runtime health",
                "Read one compact scene/runtime/STOP/native-contract line plus feature and service names grouped by state.",
                ObjectSchema()),
            NoArguments,
            FrameRead(ReadOnly, HealthData, SuiteHealth)),
        new GameMcpToolDescriptor(
            "suite_configuration",
            new GameMcpToolListing(
                "Read committed configuration",
                "Read every writable setting as section/key and its committed value. mode=describe " +
                "adds each setting's type, the values it accepts, and what it does. section narrows " +
                "either mode to the one feature that owns it — section=AutoBuy is every Auto Buy " +
                "setting in one call — and an answer that was not narrowed names the sections it holds.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("list", "describe"),
                        ["section"] = StringSchema(
                            "Narrow to one section, as the sections arm and every row's " +
                            "section/key name spell it."),
                    })),
            static (arguments, builder) =>
            {
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "list", "describe")
                    : "list";
                if (arguments.ContainsKey("section"))
                    builder.Section = RequireString(arguments, "section");
            },
            FrameRead(ReadOnly, ConfigurationData, SuiteConfiguration)),
        new GameMcpToolDescriptor(
            "trace_health",
            new GameMcpToolListing(
                "Read trace-writer health and what world collection cost",
                "Read bounded segment, record, and byte counters, then what the collection pass behind the published world spent on each category — dearest first, with the categories that charged nothing and the ones that did not bind named. The spans are one pass, not a session distribution; individual decisions and the per-session distribution remain in trace files for offline analysis.",
                ObjectSchema()),
            NoArguments,
            FrameRead(ReadOnly, TraceData, TraceHealth)),
        new GameMcpToolDescriptor(
            "suite_check_game_math",
            new GameMcpToolListing(
                "Check the suite's math against the game",
                "Run the Mods>Runtime \"Check game math\" differential check. Every entity in every registry is compared against the game's own answer, and the reply leads with one verdict word — AGREE, DISAGREE, INCOMPLETE or INCONCLUSIVE — and the count behind it, then renders only the checks that did not agree, then one window: line naming what was compared and when. All of it runs inside this call's frame, so the game stalls for seconds and the call takes that long to answer. It is a diagnostic, not a read: run it when a number looks wrong, not on a schedule.",
                ObjectSchema()),
            NoArguments,
            ExecutorRead(
                SuiteAdministration,
                None,
                static (executor, _) => executor.RunGameMcpGameMathCheck())),
        new GameMcpToolDescriptor(
            "suite_config_set",
            new GameMcpToolListing(
                "Commit one suite setting",
                "Write one allowlisted setting through the single committed configuration-store "
                    + "publication path. The Mode settings behind the breakers are refused "
                    + "here and flipped with suite_breakers; suite_configuration still reads their "
                    + "values alongside every other setting's.",
                ObjectSchema(
                    new JObject
                    {
                        ["section"] = StringSchema(
                            "Exact section, as suite_configuration prints it."),
                        ["key"] = StringSchema("Exact key, as suite_configuration prints it."),
                        ["serializedValue"] = StringSchema("Ordinary BepInEx serialized value."),
                    },
                    "section", "key", "serializedValue"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Section = RequireString(arguments, "section");
                builder.Key = RequireString(arguments, "key");
                builder.SerializedValue = RequireRawString(arguments, "serializedValue");
            },

            // A write captures the world because one of the values it may set is only safe or unsafe
            // against a live game number: reserving the whole action queue is a number Auto Buy can
            // never queue under, and the queue's capacity is the game's to say.
            Command(
                SuiteAdministration,
                GameMcpFrameData.World | ConfigurationData,
                GameMcpCommandKind.ConfigurationSet)),
        new GameMcpToolDescriptor(
            "suite_breakers",
            new GameMcpToolListing(
                "Read or flip the suite's breakers",
                "The suite's green/gray breakers as booleans, one per registered feature. "
                    + "list returns every breaker and whether it is on; set flips exactly one and "
                    + "returns its on before/after. " + GameMcpAutomationFeatures.Descriptions() + " "
                    + "Other feature settings use suite_config_set. suite_emergency_stop overrides every breaker.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("list", "set"),
                        ["feature"] = EnumSchema(GameMcpAutomationFeatures.Names()),
                        ["on"] = BooleanSchema("Required for set: true turns the feature on."),
                    },
                    "mode"),
                    ModeRule("list", forbidden: new[] { "feature", "on" }),
                    ModeRule("set", new[] { "feature", "on" })),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "list", "set");
                if (builder.Mode == "set")
                {
                    builder.Key = RequireOneOf(
                        arguments, "feature", GameMcpAutomationFeatures.Names());
                    builder.SerializedValue = RequireBool(arguments, "on") ? "Active" : "Disabled";
                }
            },
            FrameRead(ReadOnly, GameMcpFrameData.Configuration,
                static (_, context) => GameMcpToolExecution.Read(
                    GameMcpAutomationFeatures.ProjectGameMcpAutomationFeatures(context)),
                static request => request.Mode == "list"),
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.AutomationSet)),
        new GameMcpToolDescriptor(
            "suite_emergency_stop",
            new GameMcpToolListing(
                "Engage or resume suite emergency stop",
                "Commit STOP or RESUME through the same safety configuration authority used in game.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("engage", "resume"),
                    },
                    "mode"),
                readOnly: false,
                idempotent: false),
            static (arguments, builder) =>
                builder.Mode = RequireOneOf(arguments, "mode", "engage", "resume"),
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.EmergencyStop)),
    };
}
#endif
