using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpConfigurationTests
{
    [Fact]
    public void QueryReturnsOnlyTheWritableCatalog()
    {
        var writable = new GameMcpWritableSettingDescriptor(
            "AutoCast",
            "Mode",
            "Mode",
            string.Empty,
            new GameMcpConfigurationConstraint(
                "exact_parse_and_domain",
                string.Empty,
                string.Empty));
        var context = GameMcpTestHarness.Context(writable: new[] { writable });
        var listed = GameMcpAcceptanceFixture.CallText("suite_configuration", context: context);
        var described = GameMcpAcceptanceFixture.CallText(
            "suite_configuration", new JObject { ["mode"] = "describe" }, context);

        // The ordinary read is one line per setting under the grouping words those settings use,
        // and nothing else: what a setting does and what it takes are the same words on every call,
        // so they live in the tool's own documentation.
        Assert.Equal("sections: AutoCast\nAutoCast/Mode: Disabled", listed);
        Assert.Contains("AutoCast/Mode", described);
        Assert.Contains("type", described);
        Assert.Contains("description", described);
    }

    [Fact]
    public void WritableSchemaIsStaticAndValuesComeFromThePinnedPublication()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var schema = configuration.CreateGameMcpWritableSchema();
        var entries = file.Select(pair => pair.Value)
            .ToDictionary(
                entry => (entry.Definition.Section, entry.Definition.Key));

        Assert.Equal(39, schema.Length);
        Assert.Equal(39, schema.Select(item => (item.Section, item.Key)).Distinct().Count());
        foreach (var descriptor in schema)
        {
            var entry = entries[(descriptor.Section, descriptor.Key)];

            // The published text round-trips back to exactly the text the entry holds. It is not
            // always the same string: a boolean is spelled the wire's way — `yes`/`no`, as every
            // other boolean on this surface reads — and the file keeps TOML's `true`/`false`. The
            // round trip is the invariant, because it is what makes a value read here writable back
            // without the file changing shape.
            Assert.Equal(
                entry.GetSerializedValue(),
                GameMcpConfigurationValuePolicy.NativeSerializedValue(
                    entry.SettingType,
                    GameMcpConfigurationSchema.SerializePublishedValue(
                        configuration.Current,
                        descriptor.Section,
                        descriptor.Key)));
        }

        var pinned = configuration.Current;
        configuration.AutoCastMode.Value = AutoCastOperationMode.Active;
        var result = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            GameMcpTestHarness.Context(
                configurationGeneration: 12,
                writable: schema,
                configuration: pinned),
            describe: false));

        Assert.Null(result["configurationGeneration"]);
        Assert.Null(result["configuration"]);
        Assert.DoesNotContain(
            result.DescendantsAndSelf().OfType<JProperty>(),
            property => property.Name == "equalityContract");
        Assert.Equal("Disabled", (string?)result["AutoCast/Mode"]);
        Assert.Equal("Active", configuration.AutoCastMode.GetSerializedValue());
        Assert.Same(schema, GameMcpTestHarness.Context(writable: schema).WritableConfiguration);
    }

    /// <summary>
    /// Closing the second write door onto the registered breakers takes nothing away from the read:
    /// their values stay in the one catalog <c>suite_configuration</c> pages, beside every other
    /// setting, so a caller can still see what a breaker is set to without pressing anything.
    /// </summary>
    [Fact]
    public void TheBreakerSettingsStayReadableAlongsideEveryOtherSetting()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());

        // The mentor's breaker rides on its own binding and reaches this catalog only
        // once that binding is attached — exactly as it does in a composed process.
        configuration.AttachMentor(MentorConfig.Bind(new ConfigFile()));
        var schema = configuration.CreateGameMcpWritableSchema();

        Assert.All(GameMcpAutomationFeatures.All, feature =>
            Assert.Single(
                schema,
                descriptor => descriptor.Section == feature.Section &&
                    descriptor.Key == feature.Key));

        var listed = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            GameMcpTestHarness.Context(
                writable: schema,
                configuration: configuration.Current),
            describe: false));

        Assert.Equal("Disabled", (string?)listed["AutoHarvest/Mode"]);
        Assert.Equal("Disabled", (string?)listed["Mentor/Mode"]);
    }

    /// <summary>
    /// Orb Mentor's breaker is addressed by the feature it belongs to. It is the one setting whose
    /// wire address is not the address the config file holds it at: the file keeps its
    /// <c>[General] Mode</c> line, because renaming that section would move a player's existing one
    /// and silently reset the breaker, while the wire stops filing it under OrbAutomata's own
    /// <c>General</c>.
    /// </summary>
    [Fact]
    public void Orb_Mentors_switch_is_addressed_by_the_mod_it_belongs_to()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        configuration.AttachMentor(mentor);
        var schema = configuration.CreateGameMcpWritableSchema();

        Assert.Single(
            schema,
            item => item.Section == "Mentor" && item.Key == "Mode");
        Assert.DoesNotContain(
            schema,
            item => item.Section == "General" && item.Key == "Mode");

        // OrbAutomata's own master switch is the other setting in that section and does not move.
        Assert.Single(
            schema,
            item => item.Section == "General" && item.Key == "Enabled");

        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        Assert.Equal(
            AutomataConfigurationWrite.Committed,
            store.SetGameMcp(
                "Mentor", "Mode", "Active", store.CurrentGeneration, out var reason, out _));
        Assert.Equal(string.Empty, reason);

        // The write lands on the line the file has always held, under the section the file has
        // always spelled it — the wire address never reaches disk.
        file.Save();
        Assert.True(file.TryGetPersisted("General", "Mode", out var stored));
        Assert.Equal("Active", stored);
        Assert.False(file.TryGetPersisted("Mentor", "Mode", out _));

        Assert.Equal(
            "Active",
            GameMcpConfigurationSchema.SerializePublishedValue(
                configuration.Current, "Mentor", "Mode"));
        Assert.Equal(
            mentor.Mode.GetSerializedValue(),
            GameMcpConfigurationValuePolicy.NativeSerializedValue(
                mentor.Mode.SettingType,
                GameMcpConfigurationSchema.SerializePublishedValue(
                    configuration.Current, "Mentor", "Mode")));

        var listed = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            GameMcpTestHarness.Context(
                writable: schema,
                configuration: configuration.Current),
            describe: false));

        Assert.Equal("Active", (string?)listed["Mentor/Mode"]);
        Assert.Null(listed["General/Mode"]);
        Assert.Equal("yes", (string?)listed["General/Enabled"]);
    }

    /// <summary>
    /// Being told the suite has no such setting would be true and useless.
    /// </summary>
    [Fact]
    public void The_address_the_wire_used_to_answer_to_says_where_the_setting_went()
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        configuration.AttachMentor(MentorConfig.Bind(file));
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        Assert.Equal(
            AutomataConfigurationWrite.Refused,
            store.SetGameMcp(
                "General", "Mode", "Active", store.CurrentGeneration, out var retired, out _));
        Assert.Equal(
            "setting General/Mode is now addressed as Mentor/Mode, under the mod it belongs to; " +
            "the setting itself is unchanged",
            retired);
        Assert.Equal(
            GameMcpDecisionReason.ClassInput,
            GameMcpDecisionReason.Class("configuration_write_rejected"));

        // A name nothing has ever answered to is still told it is not one, not sent somewhere.
        Assert.Equal(
            AutomataConfigurationWrite.Refused,
            store.SetGameMcp(
                "Mentor", "Enabled", "true", store.CurrentGeneration, out var unknown, out _));
        Assert.StartsWith(
            "setting Mentor/Enabled is not in the perf-debug MCP allowlist",
            unknown,
            StringComparison.Ordinal);

        // The other setting in that section is OrbAutomata's own and still writes there.
        Assert.Equal(
            AutomataConfigurationWrite.Committed,
            store.SetGameMcp(
                "General", "Enabled", "no", store.CurrentGeneration, out _, out _));
        Assert.Equal(
            "no",
            GameMcpConfigurationSchema.SerializePublishedValue(
                configuration.Current, "General", "Enabled"));
    }

    /// <summary>
    /// "Show me everything Auto Buy has" is one call. The narrowing word is the section every row
    /// already wears, so the rows come back spelled exactly as the whole catalog spells them and go
    /// straight into a write without being rejoined.
    /// </summary>
    [Fact]
    public void One_call_answers_every_setting_one_feature_owns()
    {
        var narrowed = GameMcpAcceptanceFixture.CallText(
            "suite_configuration",
            new JObject { ["section"] = "AutoBuy" },
            BoundConfigurationContext());

        Assert.Equal(
            "AutoBuy/Mode: Active\n" +
            "AutoBuy/AffordabilityMode: Excess100\n" +
            "AutoBuy/UpgradeAffordabilityMode: Excess100\n" +
            "AutoBuy/IncludeStructures: yes\n" +
            "AutoBuy/IncludeUpgrades: yes\n" +
            "AutoBuy/AutoLevelSpells: yes\n" +
            "AutoBuy/FollowStrategistGoal: no\n" +
            "AutoBuy/LeaveQueueSlots: 1",
            narrowed);
    }

    /// <summary>
    /// The narrowed answer is the same rows the whole catalog carries, minus the ones belonging to
    /// other features — never a second spelling of them.
    /// </summary>
    [Fact]
    public void A_narrowed_answer_is_the_whole_catalog_minus_the_other_features()
    {
        var context = BoundConfigurationContext();
        var whole = GameMcpAcceptanceFixture.CallText("suite_configuration", context: context);
        var narrowed = GameMcpAcceptanceFixture.CallText(
            "suite_configuration", new JObject { ["section"] = "AutoBuy" }, context);

        Assert.Equal(
            narrowed.Split('\n'),
            whole.Split('\n').Where(line => line.StartsWith("AutoBuy/", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The word a caller narrows by is read off the surface, not guessed at: an answer nobody
    /// narrowed leads with the sections it holds, in the order the writable schema declares them.
    /// </summary>
    [Fact]
    public void An_answer_nobody_narrowed_names_the_sections_it_holds()
    {
        var context = BoundConfigurationContext();

        Assert.Equal(
            "sections: General, AutoBuy, AutoCast, AutoConcept, AutoHarvest, AutoItems, " +
            "AutoScribe, Reserves, Mentor, AutoLevels, QualityOfLife, GameFixes",
            GameMcpAcceptanceFixture.CallText("suite_configuration", context: context)
                .Split('\n')[0]);
        Assert.Equal(
            "sections: General, AutoBuy, AutoCast, AutoConcept, AutoHarvest, AutoItems, " +
            "AutoScribe, Reserves, Mentor, AutoLevels, QualityOfLife, GameFixes",
            GameMcpAcceptanceFixture.CallText(
                    "suite_configuration", new JObject { ["mode"] = "describe" }, context)
                .Split('\n')[0]);
        Assert.DoesNotContain(
            "sections:",
            GameMcpAcceptanceFixture.CallText(
                "suite_configuration", new JObject { ["section"] = "Reserves" }, context));
    }

    /// <summary>
    /// The longest answer is the one worth narrowing, so the section narrows <c>describe</c> too —
    /// one filter over one set, rendered each mode's own way rather than an argument that works on
    /// one of them.
    /// </summary>
    [Fact]
    public void The_section_narrows_the_described_answer_as_well()
    {
        var described = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            BoundConfigurationContext(),
            describe: true,
            section: "Reserves"));
        var settings = described["settings"]!.Values<JObject>().ToArray();

        Assert.Equal(
            new[] { "Reserves/AbsoluteReserve", "Reserves/RelativeReserveMultiplier" },
            settings.Select(setting => (string?)setting!["setting"]));
        Assert.All(settings, setting => Assert.NotNull(setting!["description"]));
        Assert.Null(described["sections"]);
    }

    /// <summary>
    /// A section nobody files settings under is refused by naming the ones that exist, so the fix
    /// is on the page that refused rather than a round trip away.
    /// </summary>
    [Fact]
    public void An_unknown_section_is_refused_by_naming_the_real_ones()
    {
        var refusal = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            BoundConfigurationContext(),
            describe: false,
            section: "autobuy_settings"));

        Assert.Equal("unavailable", (string?)refusal["status"]);
        Assert.Equal(GameMcpDecisionReason.ClassInput, (string?)refusal["reasonCode"]);
        Assert.Equal(
            "unknown section 'autobuy_settings'; the sections are General, AutoBuy, AutoCast, " +
            "AutoConcept, AutoHarvest, AutoItems, AutoScribe, Reserves, Mentor, AutoLevels, QualityOfLife, GameFixes",
            (string?)refusal["reason"]);
        Assert.Null(refusal["sections"]);

        // The caller's own filter word, which is one class of no wherever it is passed.
        Assert.Equal(
            GameMcpDecisionReason.ClassInput,
            GameMcpDecisionReason.Class("unknown_section"));
    }

    /// <summary>
    /// The section is matched the way an enum value a caller writes is matched, so the one spelling
    /// difference between "AutoBuy" and how a caller types it is not a refusal — and the rows still
    /// come back in the surface's own spelling.
    /// </summary>
    [Fact]
    public void The_section_is_matched_however_the_caller_cased_it()
    {
        var context = BoundConfigurationContext();

        Assert.Equal(
            GameMcpAcceptanceFixture.CallText(
                "suite_configuration", new JObject { ["section"] = "AutoBuy" }, context),
            GameMcpAcceptanceFixture.CallText(
                "suite_configuration", new JObject { ["section"] = "autobuy" }, context));
    }

    /// <summary>
    /// The read arm sees the seven breaker settings through the section view exactly as it always
    /// saw them through the whole catalog. Narrowing is a read; the one door that flips them is
    /// still <c>suite_breakers</c>.
    /// </summary>
    [Fact]
    public void The_section_view_reads_a_breaker_setting_it_still_may_not_write()
    {
        Assert.Equal(
            "AutoHarvest/Mode: Disabled\n" +
            "AutoHarvest/CollectFruitTrees: yes\n" +
            "AutoHarvest/CollectTreasureTrees: yes",
            GameMcpAcceptanceFixture.CallText(
                "suite_configuration",
                new JObject { ["section"] = "AutoHarvest" },
                BoundConfigurationContext()));
        Assert.True(GameMcpAutomationFeatures.IsBreakerSetting("AutoHarvest", "Mode"));
    }

    /// <summary>
    /// One value, one spelling, whichever call printed it. A boolean is written the way every other
    /// boolean on this wire reads — <c>yes</c> and <c>no</c> — on the whole catalog, on one
    /// section's rows, in <c>mode=describe</c>, and in the <c>{before, after}</c> pair a committed
    /// write hands back. The config file's own <c>true</c>/<c>false</c> is a fact about the file.
    /// </summary>
    [Fact]
    public void A_boolean_reads_the_same_way_on_every_surface_that_prints_it()
    {
        var context = BoundConfigurationContext();

        Assert.Contains(
            "AutoBuy/IncludeStructures: yes",
            GameMcpAcceptanceFixture.CallText("suite_configuration", context: context));
        Assert.Contains(
            "AutoBuy/IncludeStructures: yes",
            GameMcpAcceptanceFixture.CallText(
                "suite_configuration", new JObject { ["section"] = "AutoBuy" }, context));

        var described = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            context, describe: true, section: "AutoBuy"));
        Assert.Equal(
            "yes",
            (string?)described["settings"]!.Values<JObject>()
                .Single(setting => (string?)setting!["setting"] == "AutoBuy/IncludeStructures")!
                ["value"]);

        // The pair a committed write returns is this same projection, so the echo cannot drift
        // from the read a caller compared it against.
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });
        var before = GameMcpConfigurationSchema.SerializePublishedValue(
            configuration.Current, "AutoBuy", "IncludeStructures");
        var write = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", "false", store.CurrentGeneration,
            out var reason, out _);
        Assert.True(write == AutomataConfigurationWrite.Committed, reason);
        var after = GameMcpConfigurationSchema.SerializePublishedValue(
            configuration.Current, "AutoBuy", "IncludeStructures");

        Assert.Equal("yes", before);
        Assert.Equal("no", after);
    }

    /// <summary>
    /// The wire's spelling is a display fact and stops at the wire. The config file is TOML and
    /// keeps TOML's <c>true</c>/<c>false</c> whichever spelling the caller wrote, so a save written
    /// through this surface is byte-identical to one written through the in-game controls and a
    /// file already on disk still loads.
    /// </summary>
    /// <remarks>
    /// Each case is seeded to the opposite value first, because BepInEx does not treat writing what
    /// an entry already holds as a change: <c>ConfigEntry&lt;T&gt;.set_Value</c> compares and returns
    /// before it stores or raises. Without the seed, three of these six spellings would be asking
    /// the store to publish a value that never moved — which exercises the settlement arm rather
    /// than the spelling this fact is about. The seed's own outcome is deliberately not asserted:
    /// whether it changed anything depends on the bound default, and this fact does not pin that.
    /// </remarks>
    [Theory]
    [InlineData("yes", "true")]
    [InlineData("YES", "true")]
    [InlineData("True", "true")]
    [InlineData("no", "false")]
    [InlineData("No", "false")]
    [InlineData("FALSE", "false")]
    public void A_boolean_written_as_the_wire_spells_it_is_stored_as_the_file_spells_it(
        string written,
        string persisted)
    {
        var file = new ConfigFile();
        var configuration = BepInExAutomataConfiguration.Bind(file);
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        store.SetGameMcp(
            "AutoBuy", "IncludeStructures", persisted == "true" ? "no" : "yes",
            store.CurrentGeneration, out _, out _);

        var write = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", written, store.CurrentGeneration,
            out var reason, out _);
        Assert.True(write == AutomataConfigurationWrite.Committed, reason);
        file.Save();

        Assert.True(file.TryGetPersisted("AutoBuy", "IncludeStructures", out var stored));
        Assert.Equal(persisted, stored);
    }

    /// <summary>
    /// A write the suite accepted and then could not publish is the suite's failure, not the
    /// caller's. It shipped as <c>configuration_write_rejected</c> — ERR_INPUT, "the caller's own
    /// argument is what is wrong" — for a call whose argument the suite had just taken, and the two
    /// shared one bool so no producer could tell them apart.
    /// </summary>
    /// <remarks>
    /// All three arms are driven against a real store. The third is reached the way the game
    /// reaches it: BepInEx's <c>ConfigEntry&lt;T&gt;.set_Value</c> compares the incoming value with
    /// the one it holds and returns before storing or raising, so writing <c>no</c> onto an entry
    /// already holding it is accepted and publishes nothing.
    /// </remarks>
    [Fact]
    public void An_accepted_write_the_suite_cannot_publish_is_the_suites_failure()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        var refused = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", "maybe", store.CurrentGeneration, out _, out _);
        var committed = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", "no", store.CurrentGeneration, out _, out _);
        var unconfirmed = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", "no", store.CurrentGeneration, out _, out _);

        Assert.Equal(AutomataConfigurationWrite.Refused, refused);
        Assert.Equal(AutomataConfigurationWrite.Committed, committed);
        Assert.Equal(AutomataConfigurationWrite.Unconfirmed, unconfirmed);
        Assert.Equal(
            GameMcpDecisionReason.ClassUnavailable,
            GameMcpDecisionReason.Class("configuration_write_unconfirmed"));
        Assert.NotEqual(
            GameMcpDecisionReason.Class("configuration_write_rejected"),
            GameMcpDecisionReason.Class("configuration_write_unconfirmed"));
        Assert.True(GameMcpDecisionReason.IsSuiteDefect("configuration_write_unconfirmed"));
        Assert.False(GameMcpDecisionReason.IsSuiteDefect("configuration_write_rejected"));
        Assert.Equal(
            "The setting was accepted but the suite could not confirm it took; read it back and, " +
            "if it is unchanged, report this.",
            GameMcpDecisionReason.For("configuration_write_unconfirmed"));
        Assert.Equal(
            "failed",
            GameMcpCommandResult.Failed(
                "configuration_write_unconfirmed",
                GameMcpDecisionReason.For("configuration_write_unconfirmed")).Status);
    }

    /// <summary>
    /// The whole page, scanned. One row spelling a boolean the .NET way would put two vocabularies
    /// on a surface a caller reads top to bottom, so no rendered value on either mode may be a
    /// <c>true</c>/<c>false</c> token — the setting names and prose that legitimately contain those
    /// letters are not values, so the scan is of what follows each <c>: </c>.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_configuration_row_prints_a_dot_net_boolean(bool describe)
    {
        var arguments = new JObject();
        if (describe) arguments["mode"] = "describe";
        var page = GameMcpAcceptanceFixture.CallText(
            "suite_configuration", arguments, BoundConfigurationContext());

        var offending = new List<string>();
        foreach (var line in page.Split('\n'))
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0) continue;
            var value = line[(separator + 2)..].Trim();
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                offending.Add(line.Trim());
        }

        Assert.Empty(offending);
        Assert.Contains("yes", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A caller handing back exactly what a read showed is never refused for casing, and neither is
    /// one that types the .NET spelling. Both commit, and both read back in the surface's own
    /// spelling rather than in the caller's.
    /// </summary>
    [Theory]
    [InlineData("no", "yes")]
    [InlineData("No", "Yes")]
    [InlineData("NO", "YES")]
    [InlineData("false", "true")]
    [InlineData("False", "True")]
    [InlineData("FALSE", "TRUE")]
    public void A_boolean_write_is_accepted_however_the_caller_cased_it(string off, string on)
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        var offWrite = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", off, store.CurrentGeneration,
            out var offReason, out _);
        Assert.True(offWrite == AutomataConfigurationWrite.Committed, offReason);
        Assert.Equal(
            "no",
            GameMcpConfigurationSchema.SerializePublishedValue(
                configuration.Current, "AutoBuy", "IncludeStructures"));

        var onWrite = store.SetGameMcp(
            "AutoBuy", "IncludeStructures", on, store.CurrentGeneration,
            out var onReason, out _);
        Assert.True(onWrite == AutomataConfigurationWrite.Committed, onReason);
        Assert.Equal(
            "yes",
            GameMcpConfigurationSchema.SerializePublishedValue(
                configuration.Current, "AutoBuy", "IncludeStructures"));
    }

    /// <summary>
    /// What a setting takes is said in the words a caller writing a value would use, and in one
    /// vocabulary for the whole catalog: a setting that takes <c>true</c> is a <c>bool</c>, never
    /// the runtime's <c>System.Boolean</c>. An enum keeps its own suite name, because that name is
    /// a concept the caller already meets elsewhere and its values are listed beside it — but not
    /// the namespace it happens to be declared in, which is a fact about the code and not the game.
    /// </summary>
    [Fact]
    public void Every_setting_names_its_type_in_one_player_facing_vocabulary()
    {
        var described = GameMcpTestHarness.Json(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            BoundConfigurationContext(), describe: true));

        Assert.Equal(
            new[]
            {
                "General/Enabled: bool",
                "General/Pace: AutomationPace",
                "AutoBuy/Mode: AutoBuyOperationMode",
                "AutoBuy/AffordabilityMode: AutoBuyAffordabilityMode",
                "AutoBuy/UpgradeAffordabilityMode: AutoBuyAffordabilityMode",
                "AutoBuy/IncludeStructures: bool",
                "AutoBuy/IncludeUpgrades: bool",
                "AutoBuy/AutoLevelSpells: bool",
                "AutoBuy/FollowStrategistGoal: bool",
                "AutoBuy/LeaveQueueSlots: int",
                "AutoCast/Mode: AutoCastOperationMode",
                "AutoCast/StartResourcePercent: float",
                "AutoCast/ManualPauseSeconds: float",
                "AutoCast/FullCharge: bool",
                "AutoCast/RitualAware: bool",
                "AutoConcept/Mode: AutoConceptOperationMode",
                "AutoConcept/SlotManagementMode: AutoConceptSlotManagementMode",
                "AutoConcept/TrainingPeriodSeconds: int",
                "AutoConcept/RateReservePercent: float",
                "AutoConcept/MinimumDrainRatio: float",
                "AutoHarvest/Mode: AutoHarvestOperationMode",
                "AutoHarvest/CollectFruitTrees: bool",
                "AutoHarvest/CollectTreasureTrees: bool",
                "AutoItems/Mode: AutoItemsOperationMode",
                "AutoItems/UseScrolls: bool",
                "AutoItems/UseRelics: bool",
                "AutoItems/TemporaryItemAllowlist: string",
                "AutoScribe/Mode: AutoScribeOperationMode",
                "AutoScribe/Roles: string",
                "Reserves/AbsoluteReserve: string",
                "Reserves/RelativeReserveMultiplier: float",
                "Mentor/Mode: MentorOperationMode",
                "AutoLevels/Mode: AutoLevelsOperationMode",
                "AutoLevels/SpellCostSeconds: double",
                "AutoLevels/DrainBufferSeconds: double",
                "QualityOfLife/Mode: QualityOfLifeOperationMode",
                "QualityOfLife/MutePassiveNotifications: bool",
                "GameFixes/Mode: GameFixesOperationMode",
                "GameFixes/SoundPoolGuard: bool",
                "GameFixes/MergeRepeatedSounds: bool",
            },
            described["settings"]!.Values<JObject>()
                .Select(setting =>
                    (string?)setting!["setting"] + ": " + (string?)setting!["type"]));
    }

    /// <summary>
    /// A refusal and the described row say the same word for the same setting, so a caller told
    /// what a value must parse as reads the vocabulary it already saw rather than a second one. An
    /// enum is refused by naming the values it accepts, as it always was: those are the choices
    /// themselves, not a word for their type.
    /// </summary>
    [Theory]
    // `yes` was this row's unparseable value until the wire started spelling booleans that way.
    // It commits now, and the value that does not is one that is neither spelling.
    [InlineData("AutoBuy", "IncludeStructures", "maybe", "bool")]
    [InlineData("AutoBuy", "LeaveQueueSlots", "one", "int")]
    [InlineData("AutoCast", "StartResourcePercent", "half", "float")]
    [InlineData(
        "AutoBuy", "AffordabilityMode", "Cheap", "BuyAll, Excess10, Excess100, Excess1000")]
    public void A_refused_write_names_what_it_takes_in_the_words_the_read_used(
        string section,
        string key,
        string requested,
        string expected)
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        Assert.Equal(
            AutomataConfigurationWrite.Refused,
            store.SetGameMcp(
                section, key, requested, store.CurrentGeneration, out var reason, out _));
        Assert.Equal(section + "/" + key + " must parse exactly as " + expected, reason);
    }

    /// <summary>
    /// A writable setting of a type the wire has no word for is a defect surfaced where the schema
    /// is built, not a row that quietly wears .NET's name for it. Every type the suite binds today
    /// has a word, so this never fires on a shipped build.
    /// </summary>
    [Fact]
    public void A_type_the_wire_has_no_word_for_is_refused_rather_than_leaked()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => GameMcpConfigurationValuePolicy.SettingTypeWord(typeof(decimal)));

        Assert.Equal(
            "no wire word is declared for writable setting type 'System.Decimal'; declare one in " +
            "GameMcpConfigurationValuePolicy.SettingTypeWord before making a setting of that " +
            "type writable",
            failure.Message);
    }

    private static GameMcpFrameContext BoundConfigurationContext()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        configuration.AttachMentor(MentorConfig.Bind(new ConfigFile()));
        return GameMcpTestHarness.Context(
            writable: configuration.CreateGameMcpWritableSchema(),
            configuration: configuration.Current);
    }

    [Fact]
    public void ValidWritePublishesOnceAndStaleWriteDoesNot()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(configuration, (_, _) => publications++);
        var before = store.CurrentGeneration;
        Assert.Equal(AutomataConfigurationWrite.Committed, store.SetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Active",
            before,
            out _,
            out _));
        Assert.Equal(AutomataConfigurationWrite.Refused, store.SetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Disabled",
            before,
            out _,
            out _));
        Assert.Equal(1, publications);
    }

    /// <remarks>
    /// BepInEx writes its own domain for a config-file comment, and splicing that text into a
    /// refusal made the surface say "must be From 0 to 60" — the game's file format leaking into a
    /// player-facing sentence, with no machine field a caller could retry against.
    /// </remarks>
    [Fact]
    public void A_write_outside_the_declared_domain_answers_with_the_domain_as_numbers()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var store = new AutomataConfigurationStore(configuration, (_, _) => { });

        Assert.Equal(AutomataConfigurationWrite.Refused, store.SetGameMcp(
            "AutoCast",
            "ManualPauseSeconds",
            "600",
            store.CurrentGeneration,
            out var reason,
            out var bound));

        Assert.Equal("AutoCast/ManualPauseSeconds must be from 0 to 60", reason);
        Assert.Equal(0d, bound.Minimum);
        Assert.Equal(60d, bound.Maximum);
    }
}
