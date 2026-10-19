using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

internal static class GameMcpTestHarness
{
    /// <summary>
    /// Every upgrade on the game's ungrouped Upgrades list and no screen panel, published through
    /// the production deriver. A live world always publishes where its upgrades are drawn.
    /// </summary>
    internal static PublicationTable<WorldUpgradeListMembership> OnTheUpgradesList(params Guid[] upgrades)
    {
        var buffer = new WorldRelationBuffer<WorldUpgradeListMembership>();
        foreach (var upgrade in upgrades)
            buffer.Append(new WorldUpgradeListMembership(upgrade, KnownEntities.UpgradesAll.Uuid));
        return WorldUpgradeListMembershipDeriver.Build(buffer);
    }

    private static readonly Lazy<EntityIdentityCatalogSnapshot> Identities =
        new(LoadIdentityFixture);

    internal static EntityIdentityCatalogSnapshot EntityCatalog => Identities.Value;

    /// <summary>
    /// The wire spelling of an id. Identity stays the whole UUID inside the suite; a response says
    /// it in the one constant-width handle every emission site uses.
    /// </summary>
    internal static string Handle(Guid uuid) => GameMcpEntityHandle.Format(uuid);

    /// <summary>
    /// The one page a tool call answers with. A refusal about what a known tool was asked arrives
    /// here, in the same shape as every other refusal, rather than as a protocol error beside it.
    /// </summary>
    internal static string Page(GameMcpProtocolResponse response)
    {
        Assert.Null(response.Body!["error"]);
        return (string)Assert.Single(
            response.Body["result"]!["content"]!.Values<JObject>(),
            content => (string?)content!["type"] == "text")!["text"]!;
    }

    /// <summary>
    /// The caller's half of the handle contract: what a response said, read back as an id argument
    /// through the same resolver the tool boundary uses.
    /// </summary>
    internal static Guid ResolveHandle(string handle)
    {
        Assert.Equal(
            GameMcpEntityHandle.ResolutionOutcome.Resolved,
            GameMcpEntityHandle.Resolve(handle, EntityCatalog, out var uuid, out _));
        return uuid;
    }

    internal static WorldResource BandwidthResource(
        Guid resourceId,
        BigDouble quantity,
        BigDouble capacity)
    {
        var rateInputs = default(RawResourceRateInputs);
        var modifiers = default(RawResourceModifiers);
        var traits = new RawResourceTraits(
            0d, 0d, 0d,
            false, false, false,
            true, false, false, false,
            BigDouble.Zero, 0, 0, 0d, false, 0d,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var reading = new RawResourceSample(
            resourceId, quantity, capacity, true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        var headroom = BigDouble.Max(capacity - quantity, BigDouble.Zero);
        return new WorldResource(
            in reading, true, headroom, 0d, quantity >= capacity, quantity, BigDouble.Zero);
    }

    internal static GameMcpFrameContext Context(
        WorldPublication<GameWorldState>? world = null,
        ulong configurationGeneration = 3,
        long lifecycleGeneration = 9,
        FeatureStatusSnapshot[]? features = null,
        DecisionJournalStatus? trace = null,
        GameMcpWritableSettingDescriptor[]? writable = null,
        SuiteRuntimeConfiguration? configuration = null,
        GameLifecycleState lifecycleState = GameLifecycleState.Playing,
        string sceneName = "Main")
    {
        return new GameMcpFrameContext(
            world,
            runtime: null,
            new ConfigurationPublication(
                new ConfigGeneration(configurationGeneration),
                configuration ?? new SuiteRuntimeConfiguration()),
            lifecycleGeneration,
            sceneName,
            nativeContractsAvailable: true,
            features ?? Array.Empty<FeatureStatusSnapshot>(),
            trace ?? DecisionJournalStatus.Unavailable,
            traceWriterRevision: 0,
            writable ?? Array.Empty<GameMcpWritableSettingDescriptor>(),
            modalDismissAvailable: false,
            modalDismissUnavailableReason: "the modal action boundary was not composed",
            lifecycleState: lifecycleState);
    }

    /// <summary>
    /// The frame a read of this world sees. A world that carries its own bound catalog keeps it, so
    /// the frame and the encoder name rows from one catalog, as they do live; a world with none is
    /// given the pinned build's.
    /// </summary>
    internal static GameMcpFrameContext Context(
        GameWorldState world,
        ulong generation = 1001,
        ulong configurationGeneration = 3,
        long lifecycleGeneration = 9)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world.EntityIdentities.IsBound
                ? world
                : world with { EntityIdentities = EntityCatalog },
            new WorldGeneration(generation));
        return Context(
            publisher.ReadLatest(),
            configurationGeneration: configurationGeneration,
            lifecycleGeneration: lifecycleGeneration);
    }

    /// <summary>
    /// The pinned build's catalog with a fixture's own names added. A name given for an id the
    /// build already knows replaces the build's, so every id resolves to exactly one name.
    /// </summary>
    internal static EntityIdentityCatalogSnapshot CatalogWith(params EntityIdentityName[] rows)
    {
        var named = new HashSet<Guid>(rows.Select(row => row.EntityId));
        return EntityIdentityCatalogSnapshot.Bound(
            EntityCatalog.Generation,
            EntityCatalog.Rows.AsSpan().ToArray()
                .Where(row => !named.Contains(row.EntityId))
                .Concat(rows)
                .OrderBy(row => row.EntityId)
                .ToArray());
    }

    /// <summary>
    /// A clean report for every category the read surface registers and every collection report a
    /// listable category is built from.
    /// </summary>
    internal static WorldCollectionCategoryStatus[] AllCategoriesCollected() =>
        GameMcpWorldCategories.RegisteredCategoryNames()
            .Concat(GameMcpWorldCategories.ListedCollectionReportNames())
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();

    internal static WorldCollectionCategoryStatus Clean(string category) =>
        new(
            category,
            WorldCategoryOutcome.Collected,
            sampled: 0,
            skipped: 0,
            firstFailure: string.Empty);

    internal static JObject Json(GameMcpObjectBuilder value) =>
        Json(value.Freeze(), EntityCatalog);

    internal static JObject Json(GameMcpValue value) =>
        Json(value, EntityCatalog);

    /// <summary>
    /// A page encoded with the world's own identity catalog, the way a live frame is. The pinned
    /// build's catalog would leave a sentence composed at read time naming a UUID the normalizer
    /// then resolves for every other field.
    /// </summary>
    internal static JObject Json(GameMcpValue value, GameWorldState world) =>
        Json(value, world.EntityIdentities);

    internal static JObject Json(
        GameMcpObjectBuilder value,
        EntityIdentityCatalogSnapshot identities) =>
        Json(value.Freeze(), identities);

    internal static JObject Json(GameMcpValue value, EntityIdentityCatalogSnapshot identities) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, identities));

    internal static T[] SortedByEntityId<T>(params T[] rows)
        where T : IWorldEntity
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    internal static EntityIdentityName[] SortedByEntityId(params EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    internal static WorldLevelEffect[] SortedByOwnerAndOrdinal(params WorldLevelEffect[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            return owner != 0 ? owner : left.Ordinal.CompareTo(right.Ordinal);
        });
        return rows;
    }

    /// <summary>The publication order the deriver guarantees: owner, then source, then ordinal.</summary>
    internal static WorldEntityKeyword[] SortedByOwnerSourceAndOrdinal(
        params WorldEntityKeyword[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var source = ((int)left.Source).CompareTo((int)right.Source);
            return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
        });
        return rows;
    }

    /// <summary>
    /// One id's block of the detail read, resolved the way a caller with only an id resolves it.
    /// </summary>
    internal static JObject Detail(GameMcpFrameContext state, Guid uuid) =>
        Detail(state, string.Empty, uuid);

    /// <summary>One id's block of the detail read, addressed to one named table.</summary>
    internal static JObject Detail(GameMcpFrameContext state, string category, Guid uuid) =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(state, category, new[] { uuid.ToString("D") }))
                ["results"]!.Values<JObject>())!;

    internal static GameMcpProtocolResponse Handle(
        GameMcpProtocolRouter router,
        GameMcpFrameInbox inbox,
        JObject request,
        Func<GameMcpFrameOperation, GameMcpToolExecution> execute)
    {
        var pending = Task.Run(() => router.Handle(request));
        GameMcpFrameOperation[] claimed = Array.Empty<GameMcpFrameOperation>();
        Assert.True(SpinWait.SpinUntil(
            () => (claimed = inbox.ClaimPending()).Length > 0 || pending.IsCompleted,
            TimeSpan.FromSeconds(10)));
        Assert.False(pending.IsCompleted, "stateful request completed without a Unity-frame claim");
        for (var index = 0; index < claimed.Length; index++)
            inbox.Complete(claimed[index], execute(claimed[index]));
        Assert.True(pending.Wait(TimeSpan.FromSeconds(10)));
        return pending.Result;
    }

    internal static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static GameMcpToolRoute Route(string tool)
    {
        Assert.True(GameMcpToolCatalog.Shipping.TryFind(tool, out var descriptor), tool);
        return descriptor.Routes[descriptor.Routes.Count - 1];
    }

    internal static GameMcpToolExecution ExecuteRead(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context)
    {
        var request = operation.Request;
        if (!GameMcpFrameReads.TryExecuteFrameRead(request, context, out var execution))
        {
            execution = GameMcpToolExecution.Error(new GameMcpObjectBuilder
            {
                ["status"] = "not_available",
                ["code"] = "test_executor_missing",
                ["reason"] = request.ToolName,
            }.Freeze());
        }
        return execution.WithEntityIdentities(
            context.World?.Snapshot.EntityIdentities ?? EntityCatalog);
    }

    private static EntityIdentityCatalogSnapshot LoadIdentityFixture()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var displayByUuid = File.ReadLines(Path.Combine(data, "entity-display-names.tsv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .ToDictionary(
                cells => Guid.Parse(cells[0]),
                cells => cells.Length > 3 ? cells[3] : string.Empty);
        var rows = File.ReadLines(Path.Combine(data, "entity-mappings.tsv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(cells =>
            {
                var uuid = Guid.Parse(cells[0]);
                displayByUuid.TryGetValue(uuid, out var displayName);
                return new EntityIdentityName(
                    uuid,
                    cells[2],
                    displayName ?? string.Empty,
                    cells[1]);
            })
            .OrderBy(row => row.EntityId)
            .ToArray();
        return EntityIdentityCatalogSnapshot.Bound(9, rows);
    }
}
