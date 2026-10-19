using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpDiscoveryTreeTestSupport;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpDiscoveryTreeOfferRuntimeTests
{
    [Fact]
    public void AnOfferJourneyRunsOnReadsAndPressesAlone()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
            rerollsLeft = 1,
        };
        tree.SetGuid(Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8"));
        var firstId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var secondId = Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
        var firstNative = new DiscoveryTestItemSO();
        firstNative.SetGuid(firstId);
        var secondNative = new DiscoveryTestItemSO();
        secondNative.SetGuid(secondId);
        tree.allDiscoverableItems.Add(firstNative);
        tree.allDiscoverableItems.Add(secondNative);
        DiscoveryTreeSO.All.Add(tree);
        const long lifecycle = 7;

        try
        {
            using var action = new DiscoveryTreeOfferGameAction(
                () => lifecycle,
                () => true,
                () => string.Empty);

            var idle = DiscoveryWorld(new WorldDiscoveryTree(
                tree.GetGuid(), true, 0, BigDouble.Zero, 1, false, Guid.Empty,
                Array.Empty<Guid>(), false, true, Array.Empty<WorldResourceCost>(),
                Guid.Empty, Guid.Empty, 0, 0, false, 0, 2, 3, 3, true, true, false));
            var idleRead = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
                GameMcpTestHarness.Context(idle, generation: 90),
                "discovery-trees", tree.GetGuid().ToString("D")));
            Assert.True((bool)idleRead["row"]!["initiate"]!["available"]!);
            Assert.NotNull(idleRead["name"]);
            var readTreeId = GameMcpTestHarness.ResolveHandle((string)idleRead["uuid"]!);

            var initiated = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Initiate,
                readTreeId,
                Guid.Empty,
                lifecycle));
            Assert.True(initiated.Outcome.Verified, initiated.Outcome.Reason);

            // The native update loop materializes offers after Crafting. The fixture advances that
            // external transition, then every player decision below comes back from MCP reads.
            tree.actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice;
            tree.currentChoiceIds.Add(new GuidContainer(firstId));
            tree.currentChoiceIds.Add(new GuidContainer(secondId));
            var timeRuneRows = new[]
            {
                new WorldTimeRune(
                    firstId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            };
            var glyphRows = new[]
            {
                new WorldGlyph(
                    secondId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            };
            var choice = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, new[] { firstId, secondId }, false, false,
                    Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 0, 2, 3, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var choiceContext = GameMcpTestHarness.Context(choice, generation: 91);
            var initiatedResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    choiceContext, "discovery-trees", readTreeId));
            var readOffers = initiatedResponse["offers"]!
                .Values<JObject>()
                .Select(offer => GameMcpTestHarness.ResolveHandle((string)offer!["uuid"]!))
                .ToArray();
            Assert.Equal(new[] { firstId, secondId }, readOffers);
            Assert.All(initiatedResponse["offers"]!.Values<JObject>(), offer =>
                Assert.NotNull(offer!["name"]));

            var rerolled = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Reroll,
                readTreeId,
                Guid.Empty,
                lifecycle));
            Assert.True(rerolled.Outcome.Verified, rerolled.Outcome.Reason);
            Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);

            tree.actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice;
            tree.currentChoiceIds.Add(new GuidContainer(firstId));
            tree.currentChoiceIds.Add(new GuidContainer(secondId));
            var rerollWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, new[] { firstId, secondId }, false, false,
                    Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 1, 2, 4, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var rerollContext = GameMcpTestHarness.Context(rerollWorld, generation: 92);
            var rerollResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    rerollContext, "discovery-trees", readTreeId));
            Assert.Equal("choice", (string?)rerollResponse["mode"]);
            Assert.False((bool)rerollResponse["reroll"]!["available"]!);

            foreach (var readOffer in readOffers)
            {
                var explanation = GameMcpTestHarness.Detail(rerollContext, readOffer);
                Assert.Null(explanation["status"]);
                Assert.NotNull(explanation["name"]);
                Assert.Equal(
                    readOffer == secondId,
                    !(bool)explanation["predicates"]!["visible"]!["available"]!);
                if (readOffer == secondId)
                    Assert.Null(explanation["predicates"]!["visible"]!["reasonCode"]);
            }

            var selected = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Select,
                readTreeId,
                readOffers[0],
                lifecycle));
            Assert.True(selected.Outcome.Verified, selected.Outcome.Reason);
            var selectedWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    tree.selectedChoiceId.guid, readOffers, false, false,
                    Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 0, 2, 3, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var selectedResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    GameMcpTestHarness.Context(selectedWorld, generation: 93),
                    "discovery-trees",
                    readTreeId));
            var selectedOffer = GameMcpTestHarness.ResolveHandle(
                (string)selectedResponse["selectedOffer"]!["uuid"]!);
            Assert.Equal(firstId.ToString("D"), selectedOffer.ToString("D"));

            // One entity spells its identity one way: the reference under selectedOffer says the
            // same handle and the same name the offer's own row says.
            var selectedRow = selectedResponse["offers"]!.Values<JObject>()
                .Single(offer =>
                    (string?)offer!["uuid"] == GameMcpTestHarness.Handle(firstId))!;
            Assert.NotNull(selectedRow["name"]);
            Assert.Equal(
                (string?)selectedRow["name"],
                (string?)selectedResponse["selectedOffer"]!["name"]);

            var confirmed = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Confirm,
                readTreeId,
                selectedOffer,
                lifecycle));
            Assert.True(confirmed.Outcome.Verified, confirmed.Outcome.Reason);

            var nextCostResource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
            var confirmedWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 0, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, Array.Empty<Guid>(), false, true,
                    new[]
                    {
                        new WorldResourceCost(
                            nextCostResource,
                            new BigDouble(1.4d, 4),
                            new BigDouble(2d, 25)),
                    },
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 2, 4, 3, true, true, false));
            var confirmResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    GameMcpTestHarness.Context(confirmedWorld, generation: 94),
                    "discovery-trees",
                    readTreeId));
            Assert.Equal("idle", (string?)confirmResponse["mode"]);
            Assert.True((bool)confirmResponse["initiate"]!["available"]!);
            var nextCost = Assert.Single(confirmResponse["initiate"]!["costs"]!.Values<JObject>())!;
            Assert.Equal("Knowledge", (string?)nextCost["resource"]!["name"]);
            Assert.Equal("1.40e4", (string?)nextCost["cost"]);
            Assert.Equal("2.00e25", (string?)nextCost["spendableAmount"]);
            Assert.True((bool)nextCost["affordable"]!);

            static int CommittedBytes(JObject postState)
            {
                var response = (JObject)postState.DeepClone();
                response.AddFirst(new JProperty("status", "committed"));
                return System.Text.Encoding.UTF8.GetByteCount(
                    response.ToString(Newtonsoft.Json.Formatting.None));
            }
            Assert.Equal(
                new[] { 433, 492, 348 },
                new[]
                {
                    CommittedBytes(rerollResponse),
                    CommittedBytes(selectedResponse),
                    CommittedBytes(confirmResponse),
                });
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    [Fact]
    public void Localhost_mcp_owns_offer_family_only_during_one_operation()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.OwnsDiscoveryTreeOffers);

        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.DiscoveryTreeOffer,
            "reroll",
            out var scope,
            out var reason), reason);
        using (scope)
        {
            Assert.True(ownership.OwnsDiscoveryTreeOffers);
            Assert.True(ownership.Family(GameMcpCommandKind.DiscoveryTreeOffer).TryCapture());
        }
        Assert.False(ownership.OwnsDiscoveryTreeOffers);
        Assert.False(ownership.Family(GameMcpCommandKind.DiscoveryTreeOffer).TryCapture());
    }

    [Fact]
    public void CommittedConfirmReturnsIdleTreeWithNextDecisionFacts()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offer = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var resource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var submission = new DiscoveryTreeOfferSubmission(
            NativeActionOutcome.Commit(
                new NativeMutationCallOutcome(1, 1, 1),
                "verified confirm"));
        var mapped = submission.Outcome.ToResult();
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm",
            tree, offer, "DiscoveryTreeSO", 1,
            string.Empty, string.Empty, false);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            9,
            3,
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Confirm,
                in submission));
        var postState = DiscoveryWorld(new WorldDiscoveryTree(
            tree, true, 0, BigDouble.Zero, 0, false, Guid.Empty,
            Array.Empty<Guid>(), false, true,
            new[]
            {
                new WorldResourceCost(
                    resource, new BigDouble(7.5d, 6), new BigDouble(2.43d, 25)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 5, 4, 8, 5, false, true, false));
        terminal = terminal.WithSettledPostState(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(postState, generation: 95),
            "discovery-trees",
            tree));

        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            projected.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(responseBytes < 700);
        Assert.True(responseBytes < 1719);
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "category",
                "mode", "rerollsLeft", "treeDiscovered", "discoverableCount",
                "hasRemainingDiscoveries", "initiate",
            },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["code"]);
        Assert.Equal("idle", (string?)projected["mode"]);
        Assert.Equal(5, (int)projected["treeDiscovered"]!);
        Assert.True((bool)projected["initiate"]!["available"]!);
        var cost = Assert.Single(projected["initiate"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("7.50e6", (string?)cost["cost"]);
        Assert.Equal("2.43e25", (string?)cost["spendableAmount"]);
        Assert.Null(projected["discovered"]);
        Assert.Null(projected["totalDiscovered"]);
        Assert.Null(projected["mutationScope"]);
        Assert.Null(projected["receiptId"]);
    }

    [Fact]
    public void CommittedInitiateReturnsNamedOrderedOffersAndErasesPaymentCeremony()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var submission = new DiscoveryTreeOfferSubmission(
            NativeActionOutcome.Commit(
                new NativeMutationCallOutcome(2, 2, 2),
                "verified initiate"));
        var mapped = submission.Outcome.ToResult();
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "initiate",
            tree, Guid.Empty, "DiscoveryTreeSO", 1,
            string.Empty, string.Empty, false);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            9,
            3,
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Initiate,
                in submission));
        var firstOffer = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var secondOffer = Guid.Parse("b0387ddd-2bd8-4799-8cd0-f8c624458930");
        var postState = DiscoveryWorld(
            new WorldDiscoveryTree(
                tree, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
                new[] { firstOffer, secondOffer }, false, false,
                Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                0, 0, false, 2, 2, 5, 3, false, true, false),
            glyphs: new[]
            {
                new WorldGlyph(
                    firstOffer, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
                new WorldGlyph(
                    secondOffer, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });
        terminal = terminal.WithSettledPostState(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(postState, generation: 96),
            "discovery-trees",
            tree));

        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            projected.ToString(Newtonsoft.Json.Formatting.None));

        Assert.True(responseBytes < 1721);
        Assert.True(responseBytes < 4096);
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "category",
                "mode", "rerollsLeft", "treeDiscovered", "discoverableCount",
                "hasRemainingDiscoveries", "offers", "reroll",
            },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["code"]);
        Assert.Equal("choice", (string?)projected["mode"]);
        Assert.Equal(
            new[] { "Weak", "Magnified" },
            projected["offers"]!.Values<JObject>().Select(offer => (string?)offer!["name"]));
        Assert.True((bool)projected["reroll"]!["available"]!);
        Assert.Null(projected["payment"]);
        Assert.Null(projected["reason"]);
        Assert.Null(projected["nativeCallsAttempted"]);
        Assert.Null(projected["mutationAttempts"]);
        Assert.Null(projected["mutationsCommitted"]);
        Assert.Null(projected["decisionWorldGeneration"]);
        Assert.Null(projected["targetUuid"]);
        Assert.Null(projected["crafting"]);
        Assert.Null(projected["offersPending"]);
    }

    [Fact]
    public void Main_thread_runtime_delegates_to_the_shared_GameAction()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
        };
        DiscoveryTreeSO.All.Add(tree);
        var lifecycle = 7L;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => Guid.Empty);
        var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycle));
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: status));

        try
        {
            using var runtime = AutomataServiceCycleComposition.Create(
                configuration,
                new ConfigGeneration(1),
                new AutomataServiceCycleHostDependencies(
                    () => 1,
                    () => lifecycle,
                    new ServiceActionTimelineRegistry()),
                new IAutomataServiceCycleFeature[] { feature },
                new ManualLogSource(),
                composeGameActions: actions => actions.Add("game_discover", new DiscoveryTreeOfferGameAction(
                    () => lifecycle,
                    () => true,
                    () => string.Empty)));
            var command = new GameMcpCommand(
                1,
                GameMcpCommandKind.DiscoveryTreeOffer,
                lifecycle,
                1,
                "initiate",
                tree.GetGuid(),
                Guid.Empty,
                "DiscoveryTreeSO", 1,
                string.Empty,
                string.Empty,
                false);

            var result = runtime.GameMcp.ExecuteGameMcp(command);
            var projected = GameMcpTestHarness.Json(result.Project(command));

            Assert.Equal("committed", result.Status);
            Assert.Equal(1, tree.initiateCalls);
            Assert.Equal(new[] { "status" },
                projected.Properties().Select(property => property.Name));
            Assert.Null(projected["code"]);
            Assert.Null(projected["nativeCallsAttempted"]);
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    [Fact]
    public void TwoClaimedOfferActionsShareOnePinnedContextAndSecondRevalidatesFirstMutation()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
        };
        DiscoveryTreeSO.All.Add(tree);
        var lifecycle = 7L;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => Guid.Empty);
        var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycle));
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: status));

        try
        {
            using var runtime = AutomataServiceCycleComposition.Create(
                configuration,
                new ConfigGeneration(1),
                new AutomataServiceCycleHostDependencies(
                    () => 1,
                    () => lifecycle,
                    new ServiceActionTimelineRegistry()),
                new IAutomataServiceCycleFeature[] { feature },
                new ManualLogSource(),
                composeGameActions: actions => actions.Add("game_discover", new DiscoveryTreeOfferGameAction(
                    () => lifecycle,
                    () => true,
                    () => string.Empty)));
            var worldTree = new WorldDiscoveryTree(
                tree.GetGuid(), true, 0, BigDouble.Zero, 0, false, Guid.Empty,
                Array.Empty<Guid>(), false, true, Array.Empty<WorldResourceCost>(), Guid.Empty,
                Guid.Empty, 0, 0, false, 0, 1, 3, 2, true, true, false);
            var world = new GameWorldState
            {
                DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { worldTree }),
                CollectedAtEpoch = lifecycle,
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            };
            var context = GameMcpTestHarness.Context(world, generation: 44);
            var inbox = new GameMcpFrameInbox();
            var operations = new[]
            {
                inbox.Submit(OfferRequest(tree.GetGuid())),
                inbox.Submit(OfferRequest(tree.GetGuid())),
            };
            var observedContexts = new List<GameMcpFrameContext>();
            var terminals = new List<GameMcpCommandResult>();
            var faults = new List<Exception>();

            var drained = GameMcpFrameBatchExecutor.Drain(
                inbox,
                _ => context,
                (operation, pinned) =>
                {
                    observedContexts.Add(pinned);
                    var command = new GameMcpCommand(
                        operation.Sequence,
                        GameMcpCommandKind.DiscoveryTreeOffer,
                        expectedLifecycleGeneration: lifecycle,
                        expectedConfigurationGeneration: 1,
                        mode: "initiate",
                        tree.GetGuid(),
                        Guid.Empty,
                        "DiscoveryTreeSO", 1,
                        string.Empty,
                        string.Empty,
                        false,
                        operation,
                        pinned);
                    var result = runtime.GameMcp.ExecuteGameMcp(command);
                    terminals.Add(result);
                    return new GameMcpToolExecution(
                        result.Project(command),
                        result.InlinePng,
                        result.IsProtocolError);
                },
                (_, _, exception) =>
                {
                    faults.Add(exception);
                    return GameMcpToolExecution.Error(new GameMcpObjectBuilder
                    {
                        ["status"] = "faulted",
                        ["reason"] = exception.Message,
                    }.Freeze());
                });

            Assert.Equal(2, drained);
            Assert.Empty(faults);
            Assert.Equal("committed", terminals[0].Status);
            Assert.Equal("refused", terminals[1].Status);
            Assert.Equal("wrong_mode", terminals[1].Code);
            Assert.Equal(1, tree.initiateCalls);
            Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
            Assert.All(observedContexts, observed => Assert.Same(context, observed));
            Assert.All(operations, operation => Assert.True(operation.Completion.TryWait(
                TimeSpan.FromMilliseconds(50), out _)));
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    private static GameMcpOperationRequest OfferRequest(Guid treeId) =>
        new GameMcpOperationRequestBuilder
        {
            ToolName = "game_discover",
            Route = GameMcpTestHarness.Route("game_discover"),
            Uuid = treeId,
            Mode = "initiate",
        }.Freeze();
}
