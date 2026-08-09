using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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

namespace OrbModding.ProfileTests;

public sealed class GameMcpStreamableHttpProtocolTests
{
    [Fact]
    public void LiveCatalogSnapshotServesHiddenNamesWithoutReloading()
    {
        var catalog = GameMcpTestHarness.EntityCatalog;
        Assert.Null(GameMcpTestHarness.Json(
            GameMcpEntityCatalog.Search(catalog, "Hidden Component", 0, 20).Freeze())["status"]);
        Assert.Same(catalog, GameMcpTestHarness.EntityCatalog);
    }

    [Fact]
    public void OfflineCatalogAssetsAreNotEmbeddedInTheShippedPlugin()
    {
        var resources = typeof(GameMcpEntityCatalog).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(
            "OrbModSuite.GameMcp.entity-mappings.tsv", resources);
        Assert.DoesNotContain(
            "OrbModSuite.GameMcp.entity-display-names.tsv", resources);
    }

    [Fact]
    public void ToolEncodingUsesTheWorldPinnedCatalogRatherThanTheLatestLifecycle()
    {
        var uuid = Guid.NewGuid();
        var pinned = EntityIdentityCatalogSnapshot.Bound(
            41,
            new[]
            {
                new EntityIdentityName(uuid, "ResearchSO", "Pinned Name", "PinnedAsset"),
            });
        var later = EntityIdentityCatalogSnapshot.Bound(
            42,
            new[]
            {
                new EntityIdentityName(uuid, "ResearchSO", "Later Name", "LaterAsset"),
            });
        var previous = EntityIdentityCatalogPublication.Current;
        try
        {
            EntityIdentityCatalogPublication.Publish(later);

            var result = GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["uuid"] = uuid,
            }.Freeze()).WithEntityIdentities(pinned).ToProtocolResult();

            Assert.Contains("Pinned Name", result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Later", result.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            EntityIdentityCatalogPublication.Publish(previous);
        }
    }

    /// <summary>
    /// One answer, said once. The result used to carry a machine copy beside the page a caller
    /// reads, so every client decoded and truncated the same bytes twice.
    /// </summary>
    [Fact]
    public void ToolResultEmitsThePageOnceWithoutAStructuredDuplicate()
    {
        var large = new string('x', 80_000);
        var result = GameMcpToolExecution.Read(new GameMcpObjectBuilder
        {
            ["status"] = "available",
            ["large"] = large,
        }.Freeze()).ToProtocolResult();

        Assert.Null(result["structuredContent"]);
        Assert.Null(result["isError"]);
        var content = Assert.Single(result["content"]!.Values<JObject>());
        Assert.Equal("text", (string?)content["type"]);
        Assert.Equal("large: " + large, (string?)content["text"]);
    }

    [Fact]
    public void CanonicalEncoderPreservesWrittenEmptyArraysAndOmitsNullsAndEmptyObjects()
    {
        var nested = new GameMcpObjectBuilder
        {
            ["emptyArray"] = new GameMcpArrayBuilder(),
            ["nullValue"] = null,
        };
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["status"] = "available",
            ["emptyArray"] = new GameMcpArrayBuilder(),
            ["emptyObject"] = new GameMcpObjectBuilder(),
            ["emptyAfterFiltering"] = nested,
            ["nullValue"] = null,
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var result = Assert.IsType<JObject>(encoded);
        Assert.Equal(
            new[] { "status", "emptyArray", "emptyAfterFiltering" },
            result.Properties().Select(property => property.Name));
        Assert.Empty(result["emptyArray"]!);
        Assert.Empty(result["emptyAfterFiltering"]!["emptyArray"]!);
    }

    [Fact]
    public async Task FaultedGameActionReceiptSurvivesTheHttpProtocolExactlyOnce()
    {
        var inbox = new GameMcpFrameInbox();
        var port = FreeLoopbackPort();
        using var server = GameMcpHttpServer.TryStart(
            inbox,
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
            DiscoveryTreeOfferPreflight.VerificationFailed,
            DiscoveryTreeOfferNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            reason);
        var mapped = DiscoveryTreeOfferActionResultMapper.Map(in submission);
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
            capture: false,
            saveCapture: false,
            sourceOperation: operation,
            frameContext: context);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            observedLifecycleGeneration: 9,
            observedConfigurationGeneration: 3,
            exactReason: reason,
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
    [InlineData(2, 6)]
    [InlineData(3, 1)]
    public void AdministrativeAndGameplayTerminalsOmitShadowAndSchedulerNoise(
        int classificationValue,
        int kindValue)
    {
        var classification = (GameMcpOperationClass)classificationValue;
        var kind = (GameMcpCommandKind)kindValue;
        var source = new GameMcpFrameOperation(
            1,
            new GameMcpOperationRequestBuilder
            {
                ToolName = kind == GameMcpCommandKind.Purchase
                    ? "game_purchase"
                    : "suite_config_set",
                Classification = classification,
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
            capture: false,
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
    public void WorldGetSchemaRequiresCategoryAndAcceptsSingularOrBatchIdentity()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "world_get")!;
        var schema = (JObject)tool["inputSchema"]!;
        var properties = (JObject)schema["properties"]!;

        Assert.Equal(new[] { "category" }, schema["required"]!.Values<string>());
        Assert.Equal(
            new[] { "category", "uuids", "uuid" },
            properties.Properties().Select(property => property.Name));
        Assert.Equal("string", (string?)properties["uuid"]?["type"]);
        Assert.Equal("array", (string?)properties["uuids"]?["type"]);
        Assert.Equal(1, (int)properties["uuids"]!["minItems"]!);
        Assert.Equal(
            GameMcpWorldQuery.MaximumBatchSize,
            (int)properties["uuids"]!["maxItems"]!);
        Assert.Null(schema["oneOf"]);
    }

    [Fact]
    public void WorldGetAlwaysReturnsTheOrderedResultsCollection()
    {
        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            GameMcpTestHarness.Context(),
            "resources",
            new[] { Guid.NewGuid().ToString("D") }));

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        Assert.Empty(result["results"]!);
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

    [Fact]
    public void RouterImplementsInitializeDiscoveryResourceAndToolSemantics()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);

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
        Assert.Contains("entity_catalog", toolNames);
        Assert.Contains("suite_health", toolNames);
        Assert.Contains("trace_health", toolNames);
        Assert.DoesNotContain("decision_journal", toolNames);
        Assert.Contains("game_purchase", toolNames);
        Assert.Contains("game_cast", toolNames);
        Assert.Contains("game_concept", toolNames);
        Assert.Contains("game_agromancy", toolNames);
        Assert.Contains("game_spell_level", toolNames);
        Assert.DoesNotContain("action_receipt", toolNames);
        Assert.Contains("game_screenshot", toolNames);
        Assert.Contains("game_screen_catalog", toolNames);
        Assert.Contains("game_navigate", toolNames);
        Assert.Contains("game_tooltips", toolNames);
        Assert.Contains("game_tooltip", toolNames);
        Assert.Contains("game_probe", toolNames);

        var resources = router.Handle(Request(3, "resources/list", new JObject()));
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://world/overview");
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://trace/health");
        Assert.DoesNotContain(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://journal/status");

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

    [Fact]
    public void LiveCatalogCoversRuntimeIdentitiesAndFindsHiddenContentByDisplayName()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var response = GameMcpTestHarness.Handle(
            router,
            inbox,
            Request(
                100,
                "tools/call",
                new JObject
                {
                    ["name"] = "entity_catalog",
                    ["arguments"] = new JObject
                    {
                        ["query"] = "Hidden Component",
                        ["limit"] = 20,
                    },
                }),
            operation => GameMcpTestHarness.ExecuteRead(
                operation,
                GameMcpTestHarness.Context()));

        var page = (string)Assert.Single(
            response.Body!["result"]!["content"]!.Values<JObject>())!["text"]!;
        var lines = page.Split('\n');

        // The catalog is where the asset name and the runtime type still live: somebody browsing
        // asks for them, and nothing else on the surface carries them any more.
        Assert.Equal(2, lines.Length);
        Assert.Contains("0d0474", lines[1]);
        Assert.Contains("AttributeSO", lines[1]);
        Assert.Contains("HiddenComponent", lines[1]);
        Assert.Contains("Hidden Component", lines[1]);
        Assert.Contains("not-world-projected", lines[1]);
        Assert.DoesNotContain("catalogSource", page, StringComparison.Ordinal);
        Assert.DoesNotContain("totalCatalogRows", page, StringComparison.Ordinal);
        Assert.DoesNotContain("nameSource", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyLiveCatalogSearchKeepsExplicitCardinalityAndCollection()
    {
        var result = GameMcpTestHarness.Json(
            GameMcpEntityCatalog.Search(
                GameMcpTestHarness.EntityCatalog,
                "definitely-no-such-live-entity",
                0,
                20).Freeze());

        Assert.Null(result["status"]);
        Assert.Equal(0, (int)result["total"]!);
        Assert.Null(result["returned"]);
        Assert.Empty(result["rows"]!);
        Assert.Null(result["query"]);
        Assert.Null(result["limit"]);
        Assert.Null(result["truncated"]);
        Assert.Null(result["nextOffset"]);
    }

    [Fact]
    public void CatalogSearchPagesWithNextOffsetAndNeverRepeatsARow()
    {
        var first = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog,
            "a",
            0,
            1).Freeze());

        var total = (int)first["total"]!;
        Assert.True(total > 1);
        Assert.Null(first["returned"]);
        Assert.Null(first["truncated"]);
        Assert.Null(first["hasMore"]);
        Assert.Single(first["rows"]!);
        Assert.Equal(1, (int)first["nextOffset"]!);

        var second = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog,
            "a",
            (int)first["nextOffset"]!,
            1).Freeze());

        Assert.Equal(total, (int)second["total"]!);
        Assert.NotEqual(
            (string?)first["rows"]![0]!["uuid"],
            (string?)second["rows"]![0]!["uuid"]);

        var last = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog,
            "a",
            total - 1,
            1).Freeze());

        Assert.Single(last["rows"]!);
        Assert.Null(last["nextOffset"]);
    }

    [Fact]
    public void CatalogSearchRefusesANegativeOffset()
    {
        var result = GameMcpTestHarness.Json(GameMcpEntityCatalog.Search(
            GameMcpTestHarness.EntityCatalog,
            "a",
            -1,
            10).Freeze());

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_INPUT", (string?)result["reasonCode"]);
    }

    [Fact]
    public void LiveCatalogUsesAssetNameWhenPlayerFacingNameIsAbsent()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var response = GameMcpTestHarness.Handle(
            router,
            inbox,
            Request(
                101,
                "tools/call",
                new JObject
                {
                    ["name"] = "entity_catalog",
                    ["arguments"] = new JObject
                    {
                        ["query"] = "01ae245e-21b8-4034-8e95-e0a191145e43",
                    },
                }),
            operation => GameMcpTestHarness.ExecuteRead(
                operation,
                GameMcpTestHarness.Context()));

        var page = (string)Assert.Single(
            response.Body!["result"]!["content"]!.Values<JObject>())!["text"]!;
        var lines = page.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("OrbAnim2", lines[1]);
        Assert.Contains("asset", lines[1]);
        Assert.Contains("not-world-projected", lines[1]);
        Assert.DoesNotContain("internalName", page, StringComparison.Ordinal);
        Assert.DoesNotContain("hasDisplayName", page, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongUuidIsRejectedByToolBoundary()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
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

        Assert.Equal(-32602, (int?)response.Body?["error"]?["code"]);
        Assert.Contains(
            "must be a whole canonical UUID or an id handle",
            (string?)response.Body?["error"]?["message"]);
    }

    [Fact]
    public void ArgumentValidationRetainsMissingRequiredAndUnexpectedFieldErrors()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
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

        Assert.Equal(-32602, (int?)response.Body?["error"]?["code"]);
        Assert.Equal(
            "argument_validation_failed",
            (string?)response.Body?["error"]?["data"]?["kind"]);
        var errors = response.Body!["error"]!["data"]!["validationErrors"]!
            .OfType<JObject>()
            .ToArray();
        Assert.Contains(errors, error =>
            (string?)error["code"] == "missing_required" &&
            (string?)error["field"] == "uuid");
        Assert.Contains(errors, error =>
            (string?)error["code"] == "unexpected_field" &&
            (string?)error["field"] == "id");
    }

    [Fact]
    public async Task HttpTransportIsLoopbackOnlyAndEnforcesStreamableHttpHeaders()
    {
        var port = FreeLoopbackPort();
        var messages = new System.Collections.Generic.List<string>();
        using var server = GameMcpHttpServer.TryStart(
            new GameMcpFrameInbox(),
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

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
    public void WorldOverviewCountsOccupiedQueueRowsInsteadOfPublishedRows()
    {
        var queue = Guid.NewGuid();
        var slots = new[]
        {
            new WorldActionQueueSlot(queue, 0, empty: false, Guid.NewGuid(), Guid.NewGuid(), 1, engaged: true),
            new WorldActionQueueSlot(queue, 1, empty: true, Guid.Empty, Guid.Empty, 0, engaged: false),
        };
        var world = new GameWorldState
        {
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(slots, slots.Length),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(2));

        var overview = GameMcpTestHarness.Json(
            GameMcpWorldQuery.Overview(Snapshot(publisher.ReadLatest())));

        Assert.Equal(1, (int?)overview["running"]?["occupiedActionQueueSlots"]);
        Assert.Equal(0, (int?)overview["running"]?["activeConceptAssignments"]);
    }

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
            Clean("structures"),
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(912));

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            Snapshot(publisher.ReadLatest()), "purchase-costs", 0, 10));

        Assert.Null(result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = Assert.Single(result["rows"]!.Values<JObject>())!;

        // The row is a price, not the priced entity. It says so instead of publishing the target's
        // UUID as its own identity, which world_get would then refuse.
        Assert.Null(row["uuid"]);
        Assert.False((bool)row["addressable"]!);
        Assert.Equal("purchase-costs", (string?)row["category"]);
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
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(original, new WorldGeneration(909));
        var pinned = Snapshot(publisher.ReadLatest());

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
        var router = new GameMcpProtocolRouter(inbox);
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
        // no row has to echo an index back.
        var lines = page.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("results 3", lines[0]);
        Assert.Contains("value=no", lines[1]);
        Assert.Contains("ERR_NOT_FOUND", lines[2]);
        Assert.Contains(GameMcpTestHarness.Handle(missingId), lines[2]);
        Assert.Contains("value=yes", lines[3]);
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(901));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(state));
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
        Assert.True((bool)resource["available"]!);
        Assert.Equal(0, (int)resource["count"]!);
        Assert.Null(resource["worldProperty"]);
        Assert.Null(resource["rowType"]);
        Assert.Null(resource["name"]);

        var rituals = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "rituals")!;
        Assert.False((bool)rituals["available"]!);
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(902));
        var state = Snapshot(publisher.ReadLatest());

        var unknown = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "does-not-exist", 0, 10));
        Assert.Equal("unavailable", (string?)unknown["status"]);
        Assert.Equal("ERR_INPUT", (string?)unknown["reasonCode"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)unknown["reason"]));

        var unavailable = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "resources", 0, 10));
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(903));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(state));
        var resources = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "resources")!;
        Assert.False((bool)resources["available"]!);
        Assert.Contains("collection is partial", (string?)resources["reason"]);
        Assert.Contains("quantity was unreadable", (string?)resources["reason"]);

        var rows = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "resources", 0, 10));
        Assert.Equal("unavailable", (string?)rows["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)rows["reasonCode"]);

        var search = GameMcpTestHarness.Json(
            GameMcpWorldQuery.Search(state, "resource", 0, 10));
        Assert.Null(search["status"]);
        Assert.Null(search["reasonCode"]);
        Assert.NotEmpty(search["unavailableCategories"]!.Values<JObject>());
        Assert.Empty(search["rows"]!);

        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(state));
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
        var reports = GameMcpWorldQuery.RegisteredCategoryNames()
            .Concat(new[]
            {
                "structure-costs",
                "upgrade-costs",
                "crafting-recipe-state",
                "crafting-decisions",
                "concept-instances",
                "consumable-inventory",
                "loadouts",
                "harvest-elements",
                "plot-actions",
                "action-queue-slots",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(category => string.Equals(
                    category,
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
                : Clean(category))
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(904));
        var state = Snapshot(publisher.ReadLatest());

        var unaffectedSearch = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            state,
            unaffectedId.ToString("D"),
            0,
            10));
        Assert.Null(unaffectedSearch["status"]);
        Assert.Null(unaffectedSearch["code"]);
        Assert.Single(unaffectedSearch["rows"]!.Values<JObject>());

        var affectedSearch = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            state,
            affectedId.ToString("D"),
            0,
            10));
        Assert.Null(affectedSearch["status"]);
        Assert.Null(affectedSearch["reasonCode"]);
        var affectedMatch = Assert.Single(affectedSearch["rows"]!.Values<JObject>());
        Assert.Equal("unavailable", (string?)affectedMatch["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)affectedMatch["reasonCode"]);
        var searchFailure = Assert.Single(
            affectedMatch["implicatedSkippedRows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(affectedId), (string?)searchFailure["owner"]!["uuid"]);
        Assert.Equal("Upgrade", (string?)searchFailure["ownerKind"]);
        Assert.Equal(4, (int)searchFailure["ordinal"]!);
        Assert.Equal("ListRequirement", (string?)searchFailure["conditionTypeName"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)searchFailure["reasonCode"]);

        var unaffectedGet = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "upgrades",
            unaffectedId.ToString("D")));
        Assert.Equal("available", (string?)unaffectedGet["status"]);
        Assert.NotNull(unaffectedGet["row"]);

        var affectedGet = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "upgrades",
            affectedId.ToString("D")));
        Assert.Equal("unavailable", (string?)affectedGet["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)affectedGet["reasonCode"]);
        Assert.NotNull(affectedGet["partialRow"]);
        Assert.Single(affectedGet["implicatedSkippedRows"]!.Values<JObject>());

        var batch = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            state,
            "upgrades",
            new[] { unaffectedId.ToString("D"), affectedId.ToString("D") }));
        Assert.Null(batch["status"]);
        Assert.Null(batch["found"]);
        Assert.Null(batch["incomplete"]);
        var batchRows = batch["results"]!.OfType<JObject>().ToArray();
        Assert.Null(batchRows[0]["status"]);
        Assert.Equal("unavailable", (string?)batchRows[1]["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)batchRows[1]["reasonCode"]);
        Assert.Single(batchRows[1]["implicatedSkippedRows"]!.Values<JObject>());

        var page = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "upgrades", 0, 10));
        Assert.Null(page["status"]);
        Assert.Null(page["reasonCode"]);
        Assert.Equal(2, page["rows"]!.Count());
        Assert.Null(page["implicatedSkippedRows"]);
        var pageRows = page["rows"]!.Values<JObject>().ToArray();
        Assert.Null(pageRows[0]["status"]);
        Assert.Equal(10, (int)pageRows[0]["remainingLevels"]!);
        Assert.Equal("unavailable", (string?)pageRows[1]["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)pageRows[1]["reasonCode"]);
        Assert.NotNull(pageRows[1]["partialRow"]);
        Assert.Single(pageRows[1]["implicatedSkippedRows"]!.Values<JObject>());

        var requirements = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(state, "entity-requirements", 0, 10));
        Assert.Null(requirements["status"]);
        Assert.Null(requirements["reasonCode"]);
        Assert.NotNull(requirements["rows"]);
        var requirementRow = Assert.Single(requirements["rows"]!.Values<JObject>());
        Assert.Equal("unavailable", (string?)requirementRow["status"]);
        var requirementFailure = Assert.Single(
            requirementRow["implicatedSkippedRows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(affectedId), (string?)requirementFailure["owner"]!["uuid"]);
        Assert.Equal("ListRequirement", (string?)requirementFailure["conditionTypeName"]);

        // Which conditions this build authors that the suite cannot model does not change between
        // calls, and the overview is read far more often than the rows are. It says how many, of
        // what, on whom, and which read holds the leaves themselves.
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(state));
        var gap = (string?)overview["collection"]!["gap"];
        Assert.NotNull(gap);
        Assert.Contains("1 requirement leaves of type ListRequirement", gap);
        Assert.Contains(GameMcpTestHarness.Handle(affectedId), gap);
        Assert.Contains("world_get", gap);
        Assert.Null(overview["collection"]!["skippedEntities"]);
    }

    [Fact]
    public void WorldSearchCountsOnlyRowsItCanReturn()
    {
        var reports = GameMcpWorldQuery.RegisteredCategoryNames()
            .Concat(new[]
            {
                "structure-costs",
                "upgrade-costs",
                "crafting-recipe-state",
                "crafting-decisions",
                "concept-instances",
                "consumable-inventory",
                "harvest-elements",
                "plot-actions",
                "action-queue-slots",
                "loadouts",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(Clean)
            .ToArray();
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { Upgrade(Guid.Empty) }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 45,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(949));

        var search = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            Snapshot(publisher.ReadLatest()),
            "00000000",
            0,
            10));

        Assert.Null(search["status"]);
        Assert.Equal(0, (int)search["total"]!);
        Assert.Empty(search["rows"]!.Values<JObject>());
        Assert.Null(search["nextOffset"]);
    }

    [Fact]
    public void SearchExcludesCompositeOnlyOwnersAndWorldListRetainsTheirLocalizedEvidence()
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
        var reports = GameMcpWorldQuery.RegisteredCategoryNames()
            .Concat(new[]
            {
                "structure-costs",
                "upgrade-costs",
                "crafting-recipe-state",
                "crafting-decisions",
                "concept-instances",
                "consumable-inventory",
                "harvest-elements",
                "plot-actions",
                "action-queue-slots",
                "loadouts",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(category => string.Equals(
                    category,
                    "entity-requirements",
                    StringComparison.Ordinal)
                ? new WorldCollectionCategoryStatus(
                    "entity requirements",
                    WorldCategoryOutcome.Collected,
                    sampled: 180,
                    skipped: 1,
                    firstFailure: "ListRequirement is not modeled")
                : Clean(category))
            .ToArray();
        var world = new GameWorldState
        {
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(
                new[] { unknownLeaf }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(reports),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(948));
        var state = Snapshot(publisher.ReadLatest());

        var search = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
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
            GameMcpWorldQuery.ListRows(state, "entity-requirements", 0, 10));
        Assert.Null(page["status"]);
        Assert.Null(page["reasonCode"]);
        var incompleteRow = Assert.Single(page["rows"]!.Values<JObject>());
        Assert.Equal("unavailable", (string?)incompleteRow["status"]);
        var failure = Assert.Single(
            incompleteRow["implicatedSkippedRows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(ownerId), (string?)failure["owner"]!["uuid"]);
        Assert.Equal(1, (int)failure["ordinal"]!);
        Assert.Equal("ListRequirement", (string?)failure["conditionTypeName"]);
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(904));
        var state = Snapshot(publisher.ReadLatest());

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
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
                    Clean("structures"),
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(905));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(state));
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "purchase-costs")!;
        var mastery = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "mastery-experience")!;

        Assert.False((bool)purchaseCosts["available"]!);
        Assert.Contains("UpgradeSO cost capture failed", (string?)purchaseCosts["reason"]);
        Assert.True((bool)mastery["available"]!);
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
                    Clean("structures"),
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(906));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(state));
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "purchase-costs")!;
        var plotActions = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["category"] == "agromancy-plot-actions")!;

        Assert.False((bool)purchaseCosts["available"]!);
        Assert.Equal("modifier variables failed", (string?)purchaseCosts["reason"]);
        Assert.False((bool)plotActions["available"]!);
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
                    Clean("structures"),
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
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(907));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(state));
        foreach (var name in new[] { "resources", "purchase-costs" })
        {
            var category = categories["categories"]!
                .Values<JObject>()
                .Single(item => (string?)item!["category"] == name)!;
            Assert.False((bool)category["available"]!);
            Assert.Equal(
                "frame-global modifier reconstruction failed",
                (string?)category["reason"]);
        }
    }

    [Fact]
    public void ResearchProjectionReportsPersistentChallengeRequirementAdjustmentAndSource()
    {
        var researchId = Guid.Parse("d8afa4f2-4326-49ce-a08c-743170abea75");
        var challengeId = Guid.Parse("8331509a-56e8-4e42-9553-6170cf32349d");
        var modifierId = Guid.Parse("85f0cbee-0710-40ad-8357-acdecb8f13cc");
        var adjustment = new WorldResearchRequirementAdjustment(
            modifierId,
            challengeId,
            "ChallengeSO",
            modifierType: 0,
            amount: new BigDouble(-5d),
            order: 0,
            passive: true);
        var research = new WorldResearch(
            researchId,
            level: 10,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel: 20,
            researchTime: 60d,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available: true,
            visible: true,
            complete: false,
            canDevelop: true,
            withinDevelopRange: true,
            meetsLevelRequirements: true,
            stillHasLeeway: true,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: true,
            purchasedLevels: 10,
            baseLevel: 10,
            bonusLevel: 0,
            totalLevel: 10,
            artificialMaxLevel: 0,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirementLevel: 10,
            effectiveRequirementLevel: 5,
            requirementAdjustments: PublicationTable<WorldResearchRequirementAdjustment>.Create(
                new[] { adjustment }),
            modifiers: new RawResearchModifiers(
                BigDouble.Zero,
                BigDouble.Zero,
                new BigDouble(100d),
                BigDouble.Zero,
                new BigDouble(5d)));
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(24, new[]
            {
                new EntityIdentityName(modifierId, "IntVariableSO", "Requirement Offset", "requirementOffset"),
                new EntityIdentityName(challengeId, "ChallengeSO", "Improved Scribing", "improvedScribing"),
            }.OrderBy(row => row.EntityId).ToArray()),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("research") }),
            CollectedAtEpoch = 24,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(908));

        var result = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.GetRow(
                Snapshot(publisher.ReadLatest()),
                "research",
                researchId.ToString("D")).Freeze(),
            world.EntityIdentities));

        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            result.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(responseBytes < 1_461, "research projection was " + responseBytes + " bytes");

        Assert.Equal("available", (string?)result["status"]);
        var row = (JObject)result["row"]!;
        Assert.Equal(10, (int)row["baseRequirementLevel"]!);
        Assert.Equal(5, (int)row["effectiveRequirementLevel"]!);
        Assert.Equal(-5, (int)row["requirementLevelAdjustment"]!);
        var projected = Assert.Single(row["requirementAdjustments"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(modifierId), (string?)projected["modifier"]!["uuid"]);
        Assert.Equal("Requirement Offset", (string?)projected["modifier"]!["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(challengeId), (string?)projected["source"]!["uuid"]);
        Assert.Equal("Improved Scribing", (string?)projected["source"]!["name"]);
        Assert.Equal("ChallengeSO", (string?)projected["sourceNativeType"]);
        Assert.Equal("-5", (string?)projected["amount"]);
        Assert.True((bool)projected["passive"]!);
    }

    [Fact]
    public void CraftingRecipeProjectionKeepsAuthoredEdgesAndNativeBlockerVerdicts()
    {
        var recipeId = Guid.Parse("b1b7d331-587a-4b4c-87cf-4a8f57c8256b");
        var typeId = Guid.Parse("5d343cb8-d676-4561-9c46-4bc74de5fcfd");
        var resourceId = Guid.Parse("4d4a9dd0-6b71-4ac2-89a1-7a4dde91ed54");
        var consumableId = Guid.Parse("a969e17f-1e72-4e69-9149-603b5bac33e0");
        var reading = new RawCraftingRecipeSample(
            recipeId,
            visible: false,
            canBuyAtStartingQuantity: false,
            startingQuantity: new BigDouble(2d),
            useQuantityAsLevel: true,
            timeToComplete: 9.5d,
            outputWithinCapacity: false,
            typeCount: 1,
            authoredInputCount: 1,
            generatedOutputCount: 0,
            consumableOutputCount: 1,
            engagementEffectCount: 1,
            completionEffectCount: 1);
        var recipe = new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Create(new[]
            {
                new WorldCraftingRecipeTypeLink(recipeId, typeId),
            }),
            PublicationTable<WorldCraftingRecipeResource>.Create(new[]
            {
                new WorldCraftingRecipeResource(
                    recipeId,
                    WorldCraftingRecipeResourceKind.AuthoredInput,
                    resourceId,
                    new BigDouble(3d),
                    resourceStateAvailable: true,
                    visible: true,
                    bandwidthResource: true,
                    trueQuantity: new BigDouble(80d),
                    isCapped: true,
                    capacity: new BigDouble(100d),
                    usage: new BigDouble(4d),
                    drain: new BigDouble(1.5d)),
            }),
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Create(new[]
            {
                new WorldCraftingRecipeConsumableOutput(recipeId, 0, 1, consumableId),
            }),
            PublicationTable<WorldCraftingRecipeDrainBlock>.Create(new[]
            {
                new WorldCraftingRecipeDrainBlock(recipeId, 0, new BigDouble(0.75d)),
            }));
        var world = new GameWorldState
        {
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { recipe }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                GameMcpTestHarness.BandwidthResource(
                    resourceId, new BigDouble(80), new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("crafting-recipes"),
                    Clean("crafting-recipe-state"),
                    Clean("crafting-decisions"),
                    Clean("resources"),
                }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(911));
        var state = Snapshot(publisher.ReadLatest());

        var result = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            state,
            "crafting-recipes",
            recipeId.ToString("D")));

        Assert.Equal("available", (string?)result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = (JObject)result["row"]!;
        Assert.Equal(GameMcpTestHarness.Handle(recipeId), (string?)row["uuid"]);
        Assert.False((bool)row["visible"]!);
        Assert.False((bool)row["canStart"]!);
        Assert.Equal(
            new[]
            {
                "hidden_or_undiscovered",
                "native_purchase_refused",
                "output_capacity_blocked",
            },
            row["blockers"]!.Values<string>());
        Assert.Equal(GameMcpTestHarness.Handle(typeId),
            (string?)Assert.Single(row["types"]!)!["uuid"]);
        var input = Assert.Single(row["inputs"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(resourceId), (string?)input["resource"]!["uuid"]);
        Assert.True((bool)input["bandwidth"]!);
        Assert.Equal("3", (string?)input["cost"]);
        Assert.Equal("20", (string?)input["spendableAmount"]);
        Assert.Equal("100", (string?)input["capacity"]);
        Assert.True((bool)input["affordable"]!);
        var output = Assert.Single(row["consumableOutputs"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(consumableId), (string?)output["uuid"]);
        var drain = Assert.Single(row["drainBlockers"]!.Values<JObject>())!;
        Assert.Equal("ERR_LIMIT", (string?)drain["reasonCode"]);
        Assert.Equal("0.75", (string?)drain["availableRatio"]);

        var incompleteWorld = new GameWorldState
        {
            CraftingRecipes = world.CraftingRecipes,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("crafting-recipes") }),
            CollectedAtEpoch = 26,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        publisher.Publish(incompleteWorld, new WorldGeneration(912));
        var unavailable = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Snapshot(publisher.ReadLatest()),
            "crafting-recipes",
            recipeId.ToString("D")));
        Assert.Equal("unavailable", (string?)unavailable["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)unavailable["reasonCode"]);
        Assert.Contains(
            "crafting-recipe-state",
            (string?)unavailable["reason"],
            StringComparison.Ordinal);
    }

    private static GameMcpFrameContext Snapshot(
        WorldPublication<GameWorldState> publication)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            publication.Snapshot with
            {
                EntityIdentities = GameMcpTestHarness.EntityCatalog,
            },
            publication.Generation);
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(),
            configurationGeneration: 12,
            lifecycleGeneration: 17);
    }

    private static WorldCollectionCategoryStatus Clean(string category) =>
        new(
            category,
            WorldCategoryOutcome.Collected,
            sampled: 0,
            skipped: 0,
            firstFailure: string.Empty);

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
