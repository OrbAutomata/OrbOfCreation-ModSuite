using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class GameMcpStreamableHttpProtocolTests
{
    [Fact]
    public async Task FaultedGameActionReceiptSurvivesTheHttpProtocolExactlyOnce()
    {
        var inbox = new GameMcpFrameInbox();
        var port = GameMcpTestHarness.FreeLoopbackPort();
        using var server = GameMcpHttpServer.TryStart(
            inbox,
            GameMcpToolCatalog.Shipping,
            GameMcpCommandCatalog.Shipping,
            _ => { },
            _ => { },
            port);
        Assert.NotNull(server);

        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        using var client = new HttpClient();
        using var request = Post(
            server!.Endpoint,
            Request(
                11,
                "tools/call",
                new JObject
                {
                    ["name"] = "game_discover",
                    ["arguments"] = new JObject
                    {
                        ["mode"] = "offer_initiate",
                        ["uuid"] = tree.ToString("D"),
                    },
                }));
        request.Headers.Add(
            "MCP-Protocol-Version",
            GameMcpProtocolRouter.LatestProtocolVersion);

        var pending = client.SendAsync(request);
        GameMcpFrameOperation[] claimed = Array.Empty<GameMcpFrameOperation>();
        Assert.True(SpinWait.SpinUntil(
            () => (claimed = inbox.ClaimPending()).Length > 0 || pending.IsCompleted,
            TimeSpan.FromSeconds(1)));
        Assert.False(pending.IsCompleted, "the mutating request bypassed the Unity-frame claim");
        var operation = Assert.Single(claimed);

        const string reason =
            "Initiate postconditions did not match the audited native transition: " +
            "expected Crafting mode 1, observed mode 0";
        var submission = new DiscoveryTreeOfferSubmission(
            NativeActionOutcome.Fault(
                DiscoveryTreeOfferActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(2, 1, 0),
                reason));
        var mapped = submission.Outcome.ToResult();
        var context = GameMcpTestHarness.Context(
            new GameWorldState
            {
                CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                    Array.Empty<WorldCollectionCategoryStatus>()),
                CollectedAtEpoch = 1,
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            },
            generation: 77);
        var command = new GameMcpCommand(
            operation.Sequence,
            GameMcpCommandKind.DiscoveryTreeOffer,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "initiate",
            targetId: tree,
            secondaryId: Guid.Empty,
            derivedNativeType: "DiscoveryTreeSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            sourceOperation: operation,
            frameContext: context);
        var terminal = GameMcpCommandResult.FromAction(
            mapped.WithReason(reason),
            observedLifecycleGeneration: 9,
            observedConfigurationGeneration: 3,
            details: GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Initiate,
                in submission));
        Assert.Equal("faulted", terminal.Status);
        Assert.True(terminal.HasActionResult);
        Assert.False(terminal.IsProtocolError);

        inbox.Complete(
            operation,
            new GameMcpToolExecution(
                terminal.Project(command),
                terminal.InlinePng,
                terminal.IsProtocolError));

        using var response = await pending;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.Equal(Encoding.UTF8.GetByteCount(bodyText), response.Content.Headers.ContentLength);
        var body = JObject.Parse(bodyText);
        Assert.Single(body.Properties(), property => property.Name == "result");
        Assert.NotNull(body["result"]);
        Assert.Null(body["error"]);
        Assert.Null(body["result"]!["isError"]);
        Assert.Null(body["result"]!["structuredContent"]);
        var page = (string)Assert.Single(
            body["result"]!["content"]!.Values<JObject>())!["text"]!;
        var lines = page.Split('\n');
        Assert.Equal("faulted (ERR_UNAVAILABLE): " + reason, lines[0]);
        Assert.Contains("uuid: " + GameMcpTestHarness.Handle(tree), lines);
        Assert.Contains("missingOutcome: crafting mode", lines);
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("worldGeneration", page, StringComparison.Ordinal);
        Assert.DoesNotContain("mutation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("receipt", page, StringComparison.Ordinal);
    }

    [Fact]
    public void InfrastructureFaultRemainsAnMcpProtocolError()
    {
        var terminal = GameMcpCommandResult.Faulted(
            "operation_dispatch_fault",
            "frame operation could not be executed");

        Assert.False(terminal.HasActionResult);
        Assert.True(terminal.IsProtocolError);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1)]
    public void AdministrativeAndGameplayTerminalsOmitShadowAndSchedulerNoise(int kindValue)
    {
        var kind = (GameMcpCommandKind)kindValue;
        var tool = kind == GameMcpCommandKind.Purchase ? "game_purchase" : "suite_config_set";
        var source = new GameMcpFrameOperation(
            1,
            new GameMcpOperationRequestBuilder
            {
                ToolName = tool,
                Route = GameMcpTestHarness.Route(tool),
            }.Freeze());
        var command = new GameMcpCommand(
            1,
            kind,
            expectedLifecycleGeneration: kind == GameMcpCommandKind.Purchase ? 7 : 0,
            expectedConfigurationGeneration: 9,
            mode: kind == GameMcpCommandKind.Purchase ? "purchase" : "AutoCast",
            targetId: kind == GameMcpCommandKind.Purchase ? System.Guid.NewGuid() : System.Guid.Empty,
            secondaryId: System.Guid.Empty,
            derivedNativeType: kind == GameMcpCommandKind.Purchase ? "StructureSO" : string.Empty,
            amount: 1,
            payloadKey: kind == GameMcpCommandKind.ConfigurationSet ? "Mode" : string.Empty,
            payloadValue: kind == GameMcpCommandKind.ConfigurationSet ? "Disabled" : string.Empty,
            saveCapture: false,
            sourceOperation: source);
        var result = GameMcpTestHarness.Json(GameMcpCommandResult.Rejected(
            "native_rejected",
            "exact refusal",
            observedLifecycleGeneration: 7,
            observedConfigurationGeneration: 9).Project(command));

        Assert.Equal("refused", (string?)result["status"]);
        Assert.Equal("ERR_REFUSED", (string?)result["reasonCode"]);
        Assert.Equal("exact refusal", (string?)result["reason"]);
        Assert.Null(result["mutationScope"]);
        Assert.Null(result["worldGenerationMismatch"]);
        Assert.Null(result["observedWorldGeneration"]);
        Assert.Null(result["sequence"]);
        Assert.Null(result["disposition"]);
        Assert.Null(result["resultCode"]);
        Assert.Null(result["resultCodeName"]);
        Assert.Null(result["submittedAtUtc"]);
        Assert.Null(result["processedAtUtc"]);
        Assert.Null(result["respondedAtUtc"]);
        Assert.Null(result["collectedAtUtc"]);
        Assert.Null(result["pendingCount"]);
        Assert.Null(result["capacity"]);
    }

    [Fact]
    public void WorldGetSchemaTakesIdentityAloneAndAcceptsSingularOrBatchIdentity()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "world_get")!;
        var schema = (JObject)tool["inputSchema"]!;
        var properties = (JObject)schema["properties"]!;

        // Identity leads, because identity is the whole ask; the table an id lives in is a
        // disambiguator the id itself answers for.
        Assert.Null(schema["required"]);
        Assert.Equal(
            new[] { "uuids", "uuid", "category" },
            properties.Properties().Select(property => property.Name));
        Assert.Equal("string", (string?)properties["uuid"]?["type"]);
        Assert.Equal("array", (string?)properties["uuids"]?["type"]);
        Assert.Equal(1, (int)properties["uuids"]!["minItems"]!);
        Assert.Equal(
            GameMcpWorldList.MaximumBatchSize,
            (int)properties["uuids"]!["maxItems"]!);
        Assert.Null(schema["oneOf"]);
    }

    /// <summary>
    /// A batch refused as a whole used to carry an empty results collection beside the refusal,
    /// four rendered lines saying nothing the refusal sentence had not already said.
    /// </summary>
    [Fact]
    public void A_world_get_refused_as_a_whole_answers_in_the_one_refusal_line()
    {
        var result = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            GameMcpTestHarness.Context(),
            "resources",
            new[] { Guid.NewGuid().ToString("D") }));

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        Assert.Null(result["results"]);
    }

    [Fact]
    public void RecordEqualityMetadataProjectsAsBoundedTypeName()
    {
        var projected = GameMcpObjectProjector.Project(new SuiteRuntimeConfiguration());

        Assert.True(projected.ToString().Length < 20_000);
        Assert.Equal(
            typeof(SuiteRuntimeConfiguration).FullName,
            (string?)projected["equalityContract"]);
    }

    private sealed class WedgedProjection
    {
        public string Readable => "read";

        public string Wedged =>
            throw new InvalidOperationException("SpellManager.instance was null while projecting");
    }

    /// <summary>
    /// A projected value that will not read says so in the suite's own words, names the key it is
    /// about, and hands back the reference the whole exception is filed under.
    /// </summary>
    /// <remarks>
    /// The generic projector has no screen and no press, so neither shared GameAction sentence fits
    /// it — but the key it failed on is the one fact a caller can act on, and the exception behind
    /// it is the one thing a caller cannot. The two are joined by the reference rather than by
    /// putting the second on the wire, and the neighbouring keys still project.
    /// </remarks>
    [Fact]
    public void A_projected_value_that_will_not_read_names_its_key_and_carries_the_log_reference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        try
        {
            var projected = GameMcpObjectProjector.Project(new WedgedProjection());

            var wedged = projected["wedged"]!;
            Assert.Equal("not_available", (string?)wedged["status"]);
            var reason = (string?)wedged["reason"] ?? string.Empty;
            Assert.StartsWith("wedged could not be read.", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("SpellManager.instance was null", reason);

            var reference = Assert.Single(
                Regex.Matches(reason, "MCP-[0-9A-F]{8}").Select(match => match.Value));
            var line = Assert.Single(
                logged, entry => entry.Contains(reference, StringComparison.Ordinal));
            Assert.Contains("SpellManager.instance was null", line);
            Assert.Contains("wedged", line);

            Assert.Equal("read", (string?)projected["readable"]);
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
        }
    }

    [Fact]
    public void RouterImplementsInitializeDiscoveryResourceAndToolSemantics()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox, GameMcpToolCatalog.Shipping);

        var initialize = router.Handle(Request(
            1,
            "initialize",
            new JObject
            {
                ["protocolVersion"] = GameMcpProtocolRouter.LatestProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "profile-test",
                    ["version"] = "1",
                },
            }));
        Assert.Equal(200, initialize.StatusCode);
        Assert.Equal(
            GameMcpProtocolRouter.LatestProtocolVersion,
            (string?)initialize.Body?["result"]?["protocolVersion"]);
        Assert.Equal(
            GameMcpProtocolRouter.ServerName,
            (string?)initialize.Body?["result"]?["serverInfo"]?["name"]);

        var tools = router.Handle(Request(2, "tools/list", new JObject()));
        var toolNames = tools.Body!["result"]!["tools"]!
            .Values<JObject>()
            .Select(static tool => (string?)tool!["name"])
            .ToArray();
        Assert.Contains("world_overview", toolNames);
        Assert.Contains("world_get", toolNames);
        Assert.Contains("world_search", toolNames);
        Assert.Contains("suite_health", toolNames);
        Assert.Contains("trace_health", toolNames);
        Assert.Contains("game_purchase", toolNames);
        Assert.Contains("game_cast", toolNames);
        Assert.Contains("game_concept", toolNames);
        Assert.Contains("game_agromancy", toolNames);
        Assert.Contains("game_spell_mastery", toolNames);
        Assert.Contains("game_screenshot", toolNames);
        Assert.Contains("game_screen_catalog", toolNames);
        Assert.Contains("game_navigate", toolNames);
        Assert.Contains("game_screen_elements", toolNames);
        Assert.Contains("game_tooltip", toolNames);
        Assert.Contains("game_probe", toolNames);

        var resources = router.Handle(Request(3, "resources/list", new JObject()));
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://world/overview");
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://trace/health");

        var call = GameMcpTestHarness.Handle(
            router,
            inbox,
            Request(
                4,
                "tools/call",
                new JObject
                {
                    ["name"] = "world_overview",
                    ["arguments"] = new JObject(),
                }),
            operation => GameMcpTestHarness.ExecuteRead(
                operation,
                GameMcpTestHarness.Context()));
        Assert.StartsWith(
            "unavailable (ERR_UNAVAILABLE): ",
            (string?)call.Body?["result"]?["content"]?[0]?["text"]);

        var initialized = router.Handle(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized",
        });
        Assert.Equal(202, initialized.StatusCode);
        Assert.Null(initialized.Body);
    }

    /// <summary>
    /// The retired verb, end to end: the server does not have it. A caller holding the old name is
    /// told the name is not one this server has and pointed at the list, rather than met with a
    /// page that answers <c>rows 0/0</c> for ever.
    /// </summary>
    /// <remarks>
    /// The query is the one that used to prove the page was worth having. A combat action was the
    /// clearest thing it was for — the game prints the word, no world category published a row for
    /// it, and no other verb would say it — and that is exactly the gap the
    /// <c>character-actions</c> category closed. The word is on <c>world_search</c> now, with the
    /// facts the page could never hold beside it.
    /// </remarks>
    [Fact]
    public void TheRetiredCatalogVerbIsNoLongerAToolThisServerHas()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(
            100,
            "tools/call",
            new JObject
            {
                ["name"] = "entity_catalog",
                ["arguments"] = new JObject { ["query"] = "Summon Reinforcements" },
            }));

        Assert.Null(response.Body!["result"]);
        Assert.Equal(-32602, (int)response.Body!["error"]!["code"]!);
        Assert.Equal(
            "unknown tool 'entity_catalog'; call tools/list",
            (string?)response.Body!["error"]!["message"]);
        Assert.True(
            GameMcpEntityCapabilityMap.TryCategoryForNativeType("CharacterActionSO", out var moved));
        Assert.Equal("character-actions", moved);
    }

    [Fact]
    public void WrongUuidIsRejectedByToolBoundary()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(
            8,
            "tools/call",
            new JObject
            {
                ["name"] = "game_purchase",
                ["arguments"] = new JObject
                {
                    ["uuid"] = "not-a-uuid",
                    ["amount"] = 1,
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): uuid must be a whole canonical UUID or an id handle " +
            "that names one published entity",
            GameMcpTestHarness.Page(response));
    }

    /// <summary>
    /// The all-zero UUID is perfectly well formed and names nothing. It used to collide with the
    /// sentinel the id reader uses for "no id was sent", so a live round probing it was told its id
    /// was malformed and went off to debug a string that was fine. What is wrong with it is that
    /// nothing carries it.
    /// </summary>
    [Fact]
    public void A_well_formed_uuid_that_names_nothing_refuses_as_unknown_rather_than_malformed()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(
            8,
            "tools/call",
            new JObject
            {
                ["name"] = "world_get",
                ["arguments"] = new JObject
                {
                    ["uuid"] = "00000000-0000-0000-0000-000000000000",
                },
            }));

        Assert.Equal(
            "refused (ERR_NOT_FOUND): no entity in this build carries the id given for " +
            "uuid; page world_categories for the category you meant, or check the id you copied",
            GameMcpTestHarness.Page(response));
    }

    /// <summary>
    /// A handle valid ninety seconds earlier used to read as a typo the moment the run ended, so a
    /// caller had reason to throw away good ids after any teardown. It now says the same lifecycle
    /// fact the whole UUID for the same entity already answered with.
    /// </summary>
    [Fact]
    public void A_handle_after_teardown_says_the_lifecycle_fact_rather_than_calling_it_malformed()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(
            8,
            "tools/call",
            new JObject
            {
                ["name"] = "world_get",
                ["arguments"] = new JObject
                {
                    ["category"] = "resources",
                    ["uuid"] = "b11072",
                },
            }));

        Assert.Equal(
            "refused (ERR_UNAVAILABLE): No entity catalog is published, so no id handle " +
            "resolves; the whole UUID still reads, and handles resolve again once a save " +
            "is loaded.",
            GameMcpTestHarness.Page(response));
    }

    [Fact]
    public void ArgumentValidationRetainsMissingRequiredAndUnexpectedFieldErrors()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var response = router.Handle(Request(
            9,
            "tools/call",
            new JObject
            {
                ["name"] = "game_purchase",
                ["arguments"] = new JObject
                {
                    ["id"] = "d8afa4f2-4326-49ce-a08c-743170abea75",
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'uuid' is missing; required field 'amount' is missing; field 'id' " +
            "is not accepted by game_purchase", GameMcpTestHarness.Page(response));
    }

    [Fact]
    public async Task HttpTransportIsLoopbackOnlyAndEnforcesStreamableHttpHeaders()
    {
        var port = GameMcpTestHarness.FreeLoopbackPort();
        var messages = new System.Collections.Generic.List<string>();
        using var server = GameMcpHttpServer.TryStart(
            new GameMcpFrameInbox(),
            GameMcpToolCatalog.Shipping,
            GameMcpCommandCatalog.Shipping,
            messages.Add,
            messages.Add,
            port);
        Assert.NotNull(server);
        Assert.True(server!.IsListening);
        Assert.Equal("http://127.0.0.1:" + port + "/mcp", server.Endpoint);

        using var client = new HttpClient();
        using var initialize = Post(
            server.Endpoint,
            Request(
                1,
                "initialize",
                new JObject
                {
                    ["protocolVersion"] = GameMcpProtocolRouter.LatestProtocolVersion,
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "http-profile-test",
                        ["version"] = "1",
                    },
                }));
        using var initializeResponse = await client.SendAsync(initialize);
        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.Equal(
            GameMcpProtocolRouter.LatestProtocolVersion,
            initializeResponse.Headers.GetValues("MCP-Protocol-Version").Single());
        var initializeJson = JObject.Parse(await initializeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            GameMcpProtocolRouter.ServerName,
            (string?)initializeJson["result"]?["serverInfo"]?["name"]);

        using var notification = Post(
            server.Endpoint,
            new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized",
            });
        notification.Headers.Add(
            "MCP-Protocol-Version",
            GameMcpProtocolRouter.LatestProtocolVersion);
        using var notificationResponse = await client.SendAsync(notification);
        Assert.Equal(HttpStatusCode.Accepted, notificationResponse.StatusCode);
        Assert.Equal(0, notificationResponse.Content.Headers.ContentLength);

        using var hostile = Post(server.Endpoint, Request(2, "ping", new JObject()));
        hostile.Headers.Add("Origin", "https://example.com");
        hostile.Headers.Add(
            "MCP-Protocol-Version",
            GameMcpProtocolRouter.LatestProtocolVersion);
        using var hostileResponse = await client.SendAsync(hostile);
        Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

        using var get = new HttpRequestMessage(HttpMethod.Get, server.Endpoint);
        using var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }

    private static HttpRequestMessage Post(string uri, JObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static JObject Request(int id, string method, JObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };
}

public sealed class GameMcpWorldEnvelopeTests
{
    [Fact]
    public void PurchaseCostRowsExposeAuthoredEffectiveSourcesAndAffordability()
    {
        var entityId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var resourceId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var sourceId = Guid.Parse("99999999-8888-4777-8666-555555555555");
        var sources = PublicationTable<WorldPurchaseCostModifierSource>.Create(new[]
        {
            new WorldPurchaseCostModifierSource(
                "structure.cost_per_quantity",
                sourceId,
                "ValueModifierVariable",
                "modifier scaled by cost scaling and committed quantity",
                new BigDouble(1.25d),
                hasModifierType: true,
                modifierType: 3,
                order: 2),
        });
        var costs = PublicationTable<WorldPurchaseCost>.Create(new[]
        {
            new WorldPurchaseCost(
                entityId,
                resourceId,
                new BigDouble(100d),
                new BigDouble(250d),
                exactGroupedLevels: 3,
                new BigDouble(900d),
                sources,
                affordabilityEvaluated: true,
                new BigDouble(300d),
                new BigDouble(250d),
                resourceAffordable: true,
                resourceAffordabilityReasonCode: "affordable",
                affordable: true,
                affordabilityReasonCode: "affordable"),
        });
        var reports = new[]
        {
            Clean("attributes"),
            Clean("upgrades"),
            Clean("resources"),
            Clean("modifier-variables"),
            Clean("int-variables"),
            Clean("structure-costs"),
            Clean("upgrade-costs"),
        };
        var world = new GameWorldState
        {
            PurchaseCosts = costs,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var result = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world), "purchase-costs", 0, 10));

        Assert.Null(result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = Assert.Single(result["rows"]!.Values<JObject>())!;

        // The row is a price, not the priced entity. Having no `uuid` is how it says so — there
        // is no handle to hand back — and the category it belongs to is the one the caller named
        // to get this page, so neither is written out again.
        Assert.Null(row["uuid"]);
        Assert.Null(row["addressable"]);
        Assert.Null(row["category"]);
        Assert.Equal(GameMcpTestHarness.Handle(entityId), (string?)row["target"]!["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(resourceId), (string?)row["resource"]!["uuid"]);
        Assert.Equal("250", (string?)row["cost"]);
        Assert.Equal("300", (string?)row["spendableAmount"]);
        Assert.True((bool)row["affordable"]!);
        Assert.Null(row["baseExactAmount"]);
        Assert.Null(row["effectiveExactAmount"]);
        Assert.Null(row["costModifiers"]);
    }

    [Fact]
    public void BatchGetKeepsInputCorrelationAndOnePinnedGeneration()
    {
        var firstId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var secondId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var missingId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var original = new GameWorldState
        {
            BoolVariables = PublicationTable<WorldBoolVariable>.Create(new[]
            {
                new WorldBoolVariable(firstId, true, false, true, 1),
                new WorldBoolVariable(secondId, false, false, true, 2),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("bool-variables") }),
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(original, new WorldGeneration(909));
        var pinned = GameMcpTestHarness.Context(publisher.ReadLatest());

        var replacement = new GameWorldState
        {
            BoolVariables = PublicationTable<WorldBoolVariable>.Create(new[]
            {
                new WorldBoolVariable(firstId, false, false, true, 10),
                new WorldBoolVariable(secondId, true, false, true, 20),
            }),
            CollectionCategories = original.CollectionCategories,
            CollectedAtEpoch = 32,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        publisher.Publish(replacement, new WorldGeneration(910));

        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox, GameMcpToolCatalog.Shipping);
        var response = GameMcpTestHarness.Handle(
            router,
            inbox,
            GameMcpAcceptanceFixture.Request(
                10,
                "tools/call",
                new JObject
                {
                    ["name"] = "world_get",
                    ["arguments"] = new JObject
                    {
                        ["category"] = "bool-variables",
                        ["uuids"] = new JArray(
                            secondId.ToString("D"),
                            missingId.ToString("D"),
                            firstId.ToString("D")),
                    },
                }),
            operation => GameMcpTestHarness.ExecuteRead(operation, pinned));
        var page = (string)Assert.Single(
            response.Body!["result"]!["content"]!.Values<JObject>())!["text"]!;

        // Three asks, three answers, in the order they were asked: the correlation is the order, so
        // no row has to echo an index back. Each answer is the one detail skeleton — identity at the
        // top, the published row under `row:` — whether the call named one id or three. A batch used
        // to be a table of comma-joined field lists, which is a shape the same id read on its own
        // never had.
        var lines = page.Split('\n');
        Assert.Equal(14, lines.Length);
        Assert.Equal("results 3:", lines[0]);
        Assert.Equal("  uuid: " + GameMcpTestHarness.Handle(secondId), lines[1]);
        Assert.Equal("  category: bool-variables", lines[3]);
        Assert.Equal("  row: value=no, initialValue=no, isSaved=yes", lines[4]);
        Assert.Equal(string.Empty, lines[5]);
        Assert.Contains("ERR_NOT_FOUND", lines[6]);
        Assert.Contains(GameMcpTestHarness.Handle(missingId), lines[7]);
        Assert.Equal("  uuid: " + GameMcpTestHarness.Handle(firstId), lines[10]);
        Assert.Equal("  category: bool-variables", lines[12]);
        Assert.Equal("  row: value=yes, initialValue=no, isSaved=yes", lines[13]);
        Assert.DoesNotContain("inputIndex", page, StringComparison.Ordinal);
        Assert.DoesNotContain("worldGeneration", page, StringComparison.Ordinal);
    }

    [Fact]
    public void OneResponseUsesOnePublishedWorldAndCarriesOnlyPinnedGenerations()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Collected,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: string.Empty),
                    new WorldCollectionCategoryStatus(
                        "rituals",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "the RitualSO registry was unreadable"),
                },
                2),
            CollectedAtEpoch = 17,
            CollectedAtUtcTicks = new DateTime(2026, 7, 30, 0, 30, 0, DateTimeKind.Utc).Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var categories = GameMcpTestHarness.Json(GameMcpWorldOverview.ListCategories(state));
        Assert.Null(categories["status"]);
        Assert.Null(categories["worldGeneration"]);
        Assert.Null(categories["lifecycleGeneration"]);
        Assert.Null(categories["structuralEpoch"]);
        Assert.Null(categories["collectedEpoch"]);
        Assert.Null(categories["collectedAtUtc"]);
        Assert.Null(categories["respondedAtUtc"]);
        Assert.Null(categories["identityModes"]);

        var resource = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "resources")!;
        Assert.Null(resource["available"]);
        Assert.Null(resource["reason"]);
        Assert.Equal(0, (int)resource["count"]!);
        Assert.Null(resource["worldProperty"]);
        Assert.Null(resource["rowType"]);
        Assert.Null(resource["name"]);

        var rituals = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "rituals")!;
        Assert.Null(rituals["available"]);
        Assert.Contains("registry was unreadable", (string?)rituals["reason"]);
    }

    [Fact]
    public void UnknownAndUncollectedQueriesReturnTypedNotAvailableAnswers()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "ResourceSO.quantity was not bound"),
                },
                1),
            CollectedAtEpoch = 18,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var unknown = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "does-not-exist", 0, 10));
        Assert.Equal("unavailable", (string?)unknown["status"]);
        Assert.Equal("ERR_INPUT", (string?)unknown["reasonCode"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)unknown["reason"]));

        var unavailable = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "resources", 0, 10));
        Assert.Equal("unavailable", (string?)unavailable["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)unavailable["reasonCode"]);
        Assert.Contains("quantity was not bound", (string?)unavailable["reason"]);

    }

    [Fact]
    public void PartiallyCollectedCategoryIsNotPresentedAsAuthoritative()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Collected,
                        sampled: 3,
                        skipped: 1,
                        firstFailure: "one ResourceSO quantity was unreadable"),
                },
                1),
            CollectedAtEpoch = 19,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var categories = GameMcpTestHarness.Json(GameMcpWorldOverview.ListCategories(state));
        var resources = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "resources")!;
        Assert.Null(resources["available"]);
        Assert.Contains("collection is partial", (string?)resources["reason"]);
        Assert.Contains("quantity was unreadable", (string?)resources["reason"]);

        var rows = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "resources", 0, 10));
        Assert.Equal("unavailable", (string?)rows["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)rows["reasonCode"]);

        var search = GameMcpTestHarness.Json(
            GameMcpWorldSearch.Search(state, "resource", 0, 10));
        Assert.Null(search["status"]);
        Assert.Null(search["reasonCode"]);
        Assert.NotEmpty(search["unavailableCategories"]!.Values<JObject>());
        Assert.Empty(search["rows"]!);

        var overview = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(state));
        Assert.False((bool)overview["collection"]!["complete"]!);
        Assert.Equal(3, (int)overview["collection"]!["read"]!);
        Assert.Equal(1, (int)overview["collection"]!["skipped"]!);
        var degraded = Assert.Single(
            overview["collection"]!["unavailableCategories"]!.Values<JObject>())!;
        Assert.Equal(3, (int)degraded["read"]!);
        Assert.Equal(1, (int)degraded["skipped"]!);
    }

    [Fact]
    public void UnmodeledRequirementLeafPoisonsOnlyItsExactOwner()
    {
        var unaffectedId = Guid.Parse("71000000-0000-4000-8000-000000000001");
        var affectedId = Guid.Parse("71000000-0000-4000-8000-000000000002");
        var noScaling = default(WorldRequirementScaling);
        var unknownLeaf = new WorldEntityRequirement(
            affectedId,
            WorldRequirementOwnerKind.Upgrade,
            ordinal: 4,
            WorldRequirementConditionKind.Unknown,
            "ListRequirement",
            Guid.Empty,
            reqType: -1,
            baseValue: 0d,
            in noScaling,
            in noScaling);
        var reports = GameMcpTestHarness.AllCategoriesCollected()
            .Select(report => string.Equals(
                    report.Category,
                    "entity-requirements",
                    StringComparison.Ordinal)
                ? new WorldCollectionCategoryStatus(
                    "entity requirements",
                    WorldCategoryOutcome.Collected,
                    sampled: 179,
                    skipped: 1,
                    firstFailure:
                        "this build authors a condition this suite does not model: " +
                        "ListRequirement. Entities gated by one are never planned.")
                : report)
            .ToArray();
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                Upgrade(unaffectedId),
                Upgrade(affectedId),
            }),
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(
                new[] { unknownLeaf }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 20,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var unaffectedSearch = GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            state,
            unaffectedId.ToString("D"),
            0,
            10));
        Assert.Null(unaffectedSearch["status"]);
        Assert.Null(unaffectedSearch["code"]);
        Assert.Single(unaffectedSearch["rows"]!.Values<JObject>());

        var affectedSearch = GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            state,
            affectedId.ToString("D"),
            0,
            10));
        Assert.Null(affectedSearch["status"]);
        Assert.Null(affectedSearch["reasonCode"]);
        // A hit is a row of a page, so it keeps its own columns and says the one thing that is
        // missing. The refusal block it used to be swapped for lives on world_get.
        var affectedMatch = Assert.Single(affectedSearch["rows"]!.Values<JObject>());
        Assert.Equal(GameMcpTestHarness.Handle(affectedId), (string?)affectedMatch["uuid"]);
        Assert.Equal("upgrades", (string?)affectedMatch["category"]);
        Assert.Null(affectedMatch["status"]);
        Assert.Null(affectedMatch["reasonCode"]);
        Assert.Null(affectedMatch["partialRow"]);
        Assert.Null(affectedMatch["implicatedSkippedRows"]);
        Assert.Equal(
            "One of this entity's requirements uses a condition this build does not " +
            "model, so its graph is incomplete. world_get on this uuid names the requirement.",
            (string?)affectedMatch["incomplete"]);

        var unaffectedGet = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            state,
            "upgrades",
            unaffectedId.ToString("D")));
        // The singular read is the live read, so it says exactly what the batch block below says:
        // the row is there and nothing is implicated.
        Assert.Equal("available", (string?)unaffectedGet["status"]);
        Assert.NotNull(unaffectedGet["row"]);
        Assert.Null(unaffectedGet["implicatedSkippedRows"]);

        var affectedGet = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            state,
            "upgrades",
            affectedId.ToString("D")));
        Assert.Equal("unavailable", (string?)affectedGet["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)affectedGet["reasonCode"]);
        Assert.NotNull(affectedGet["partialRow"]);
        // world_get is where the leaf itself is named.
        var getFailure = Assert.Single(
            affectedGet["implicatedSkippedRows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(affectedId), (string?)getFailure["uuid"]);
        Assert.Equal(4, (int)getFailure["ordinal"]!);
        Assert.Null(getFailure["collectorReason"]);
        Assert.Equal(
            "One of this entity's requirements uses a condition this build does not model, so " +
            "its graph is incomplete.",
            (string?)getFailure["reason"]);

        var batch = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            state,
            "upgrades",
            new[] { unaffectedId.ToString("D"), affectedId.ToString("D") }));
        Assert.Null(batch["status"]);
        Assert.Null(batch["found"]);
        Assert.Null(batch["incomplete"]);
        var batchRows = batch["results"]!.OfType<JObject>().ToArray();

        // The unaffected owner is not poisoned by its neighbour's unmodelled leaf: it keeps its own
        // row and names nothing implicated.
        Assert.NotNull(batchRows[0]["row"]);
        Assert.Null(batchRows[0]["implicatedSkippedRows"]);
        Assert.Null(batchRows[0]["status"]);
        Assert.Equal("unavailable", (string?)batchRows[1]["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)batchRows[1]["reasonCode"]);
        Assert.Single(batchRows[1]["implicatedSkippedRows"]!.Values<JObject>());

        var page = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "upgrades", 0, 10));
        Assert.Null(page["status"]);
        Assert.Null(page["reasonCode"]);
        Assert.Equal(2, page["rows"]!.Count());
        Assert.Null(page["implicatedSkippedRows"]);
        var pageRows = page["rows"]!.Values<JObject>().ToArray();
        Assert.Null(pageRows[0]["status"]);
        Assert.Equal(10, (int)pageRows[0]["maximum"]!);
        Assert.Null(pageRows[0]["incomplete"]);
        Assert.Null(pageRows[1]["status"]);
        Assert.Null(pageRows[1]["reasonCode"]);
        Assert.Null(pageRows[1]["partialRow"]);
        Assert.Null(pageRows[1]["implicatedSkippedRows"]);
        Assert.Equal(10, (int)pageRows[1]["maximum"]!);
        Assert.Equal(
            "One of this entity's requirements uses a condition this build does not " +
            "model, so its graph is incomplete. world_get on this uuid names the requirement.",
            (string?)pageRows[1]["incomplete"]);

        var requirements = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "entity-requirements", 0, 10));
        Assert.Null(requirements["status"]);
        Assert.Null(requirements["reasonCode"]);
        Assert.NotNull(requirements["rows"]);
        var requirementRow = Assert.Single(requirements["rows"]!.Values<JObject>());
        Assert.Null(requirementRow["status"]);
        Assert.Null(requirementRow["implicatedSkippedRows"]);
        Assert.Equal(
            "One of this entity's requirements uses a condition this build does not " +
            "model, so its graph is incomplete. world_get on this uuid names the requirement.",
            (string?)requirementRow["incomplete"]);

        // The pin the whole item exists for, widened past the envelope case it was written for:
        // no ERR_ token rides a list row, and none rides a world_get fact block either. The eight
        // classes are a press vocabulary. The one exception on a read page is a block carrying a
        // `status`, which is not a fact block: it is the read saying it could not serve this id,
        // and it names the verb that can.
        foreach (var rendered in new[] { page, requirements, affectedSearch })
            foreach (var row in rendered["rows"]!.Values<JObject>())
                Assert.DoesNotContain("ERR_", row!.ToString(), StringComparison.Ordinal);
        foreach (var block in batch["results"]!.Values<JObject>())
            AssertNoClassOutsideAStatus(block!);

        // Which conditions this build authors that the suite cannot model does not change between
        // calls, and the overview is read far more often than the rows are. It says how many, of
        // what, on whom, and which read holds the leaves themselves.
        var overview = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(state));
        var gap = (string?)overview["collection"]!["gap"];
        Assert.NotNull(gap);
        Assert.Equal(
            "1 requirement leaf on 1 entity uses a condition this build authors and this suite " +
            "does not model (ListRequirement), so those entities are never planned. world_list " +
            "category=entity-requirements lists the leaves, and world_get on one of the " +
            "entities names its own.",
            gap);
        Assert.DoesNotContain(GameMcpTestHarness.Handle(affectedId), gap);
        Assert.Null(overview["collection"]!["skippedEntities"]);
    }

    /// <summary>
    /// Every <c>ERR_</c> class inside a <c>world_get</c> block sits in a block that also carries a
    /// <c>status</c>, and nothing else does — neither a key of its own nor a sentence spelling one.
    /// </summary>
    private static void AssertNoClassOutsideAStatus(JObject block)
    {
        if (block["status"] is not null) return;
        Assert.Null(block["reasonCode"]);
        foreach (var property in block.Properties())
        {
            if (property.Value is JObject nested)
            {
                AssertNoClassOutsideAStatus(nested);
            }
            else if (property.Value is JArray array)
            {
                foreach (var item in array.OfType<JObject>()) AssertNoClassOutsideAStatus(item);
            }
            else if (property.Value is JValue { Type: JTokenType.String } text)
            {
                Assert.DoesNotContain(
                    "ERR_", (string?)text ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WorldSearchCountsOnlyRowsItCanReturn()
    {
        var reports = GameMcpTestHarness.AllCategoriesCollected();
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { Upgrade(Guid.Empty) }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 45,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var search = GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            GameMcpTestHarness.Context(world),
            "00000000",
            0,
            10));

        Assert.Null(search["status"]);
        Assert.Equal(0, (int)search["total"]!);
        Assert.Empty(search["rows"]!.Values<JObject>());
        Assert.Null(search["nextOffset"]);
    }

    [Fact]
    public void SearchExcludesCompositeOnlyOwnersAndWorldListSaysWhatItCouldNotModel()
    {
        var ownerId = Guid.Parse("b4505524-0000-4000-8000-000000000001");
        var scaling = default(WorldRequirementScaling);
        var unknownLeaf = new WorldEntityRequirement(
            ownerId,
            WorldRequirementOwnerKind.Upgrade,
            ordinal: 1,
            WorldRequirementConditionKind.Unknown,
            "ListRequirement",
            Guid.Empty,
            reqType: -1,
            baseValue: 0d,
            in scaling,
            in scaling);
        var reports = GameMcpTestHarness.AllCategoriesCollected()
            .Select(report => string.Equals(
                    report.Category,
                    "entity-requirements",
                    StringComparison.Ordinal)
                ? new WorldCollectionCategoryStatus(
                    "entity requirements",
                    WorldCategoryOutcome.Collected,
                    sampled: 180,
                    skipped: 1,
                    firstFailure: "ListRequirement is not modeled")
                : report)
            .ToArray();
        var world = new GameWorldState
        {
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(
                new[] { unknownLeaf }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var search = GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            state,
            ownerId.ToString("D"),
            0,
            10));
        Assert.Null(search["status"]);
        Assert.Equal(0, (int)search["total"]!);
        Assert.Null(search["returned"]);
        Assert.Empty(search["rows"]!);
        Assert.Null(search["partialMatches"]);
        Assert.Null(search["implicatedSkippedRows"]);

        var page = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "entity-requirements", 0, 10));
        Assert.Null(page["status"]);
        Assert.Null(page["reasonCode"]);
        var incompleteRow = Assert.Single(page["rows"]!.Values<JObject>());
        Assert.Null(incompleteRow["status"]);
        Assert.Null(incompleteRow["reasonCode"]);
        Assert.Null(incompleteRow["partialRow"]);
        Assert.Null(incompleteRow["implicatedSkippedRows"]);
        Assert.Equal(1, (int)incompleteRow["ordinal"]!);
        Assert.Equal(
            "One of this entity's requirements uses a condition this build does not " +
            "model, so its graph is incomplete. world_get on this uuid names the requirement.",
            (string?)incompleteRow["incomplete"]);
        Assert.DoesNotContain("ERR_", incompleteRow.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeWorldRowsCannotMatchAnUnrelatedGuidField()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "structure-costs",
                        WorldCategoryOutcome.Collected,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: string.Empty),
                },
                1),
            CollectedAtEpoch = 20,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var result = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            state,
            "purchase-costs",
            Guid.NewGuid().ToString("D")));

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_INPUT", (string?)result["reasonCode"]);
        Assert.Contains("world_list", (string?)result["reason"]);
    }

    [Fact]
    public void MergedWorldTablesRequireEveryProducerReport()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("attributes"),
                    Clean("upgrades"),
                    Clean("resources"),
                    Clean("modifier-variables"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    new WorldCollectionCategoryStatus(
                        "upgrade-costs",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "UpgradeSO cost capture failed"),
                    Clean("spell-recipes"),
                    Clean("alchemy-recipes"),
                    Clean("equipment"),
                },
                10),
            CollectedAtEpoch = 21,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var categories = GameMcpTestHarness.Json(GameMcpWorldOverview.ListCategories(state));
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "purchase-costs")!;
        var mastery = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "mastery-experience")!;

        Assert.Null(purchaseCosts["available"]);
        Assert.Contains("UpgradeSO cost capture failed", (string?)purchaseCosts["reason"]);
        Assert.Null(mastery["available"]);
        Assert.Null(mastery["reason"]);
    }

    [Fact]
    public void DerivedWorldTablesRequireEveryUpstreamReport()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("attributes"),
                    Clean("upgrades"),
                    Clean("resources"),
                    new WorldCollectionCategoryStatus(
                        "modifier-variables",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "modifier variables failed"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    Clean("upgrade-costs"),
                    Clean("plot-nodes"),
                    new WorldCollectionCategoryStatus(
                        "plot-node-actions",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "plot node actions failed"),
                    Clean("agromancy-plot-actions"),
                },
                10),
            CollectedAtEpoch = 22,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var categories = GameMcpTestHarness.Json(GameMcpWorldOverview.ListCategories(state));
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "purchase-costs")!;
        var plotActions = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "agromancy-plot-actions")!;

        Assert.Null(purchaseCosts["available"]);
        Assert.Equal("modifier variables failed", (string?)purchaseCosts["reason"]);
        Assert.Null(plotActions["available"]);
        Assert.Equal("plot node actions failed", (string?)plotActions["reason"]);
    }

    [Fact]
    public void ModifierFoldingDegradationInvalidatesEveryDependentDerivedTable()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("resources"),
                    Clean("attributes"),
                    Clean("upgrades"),
                    Clean("modifier-variables"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    Clean("upgrade-costs"),
                    new WorldCollectionCategoryStatus(
                        "modifier-folding",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "frame-global modifier reconstruction failed"),
                },
                8),
            CollectedAtEpoch = 23,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = GameMcpTestHarness.Context(world);

        var categories = GameMcpTestHarness.Json(GameMcpWorldOverview.ListCategories(state));
        foreach (var name in new[] { "resources", "purchase-costs" })
        {
            var category = categories["categories"]!
                .Values<JObject>()
                .Single(item => (string?)item!["category"] == name)!;
            Assert.Null(category["available"]);
            Assert.Equal(
                "frame-global modifier reconstruction failed",
                (string?)category["reason"]);
        }
    }

    private static WorldUpgrade Upgrade(Guid id)
    {
        var raw = new RawUpgradeSample(
            id,
            level: 0,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1d,
            cachedCostLevel: 0);
        return new WorldUpgrade(
            in raw,
            isBounded: true,
            isExhausted: false,
            remainingLevels: 10,
            committedLevel: 0,
            isDeveloping: false,
            developmentProgress: 0d);
    }
}
