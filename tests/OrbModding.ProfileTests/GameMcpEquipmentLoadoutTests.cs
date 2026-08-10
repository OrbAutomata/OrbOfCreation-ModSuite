using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpEquipmentLoadoutTests
{
    private static readonly Guid EquipmentId = Guid.Parse("f4000000-0000-0000-0000-000000000001");
    private static readonly Guid TypeId = Guid.Parse("f4000000-0000-0000-0000-000000000002");
    private static readonly Guid ResourceId = Guid.Parse("f4000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_requires_mode_identity_and_explicit_amount()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_equipment");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode", "uuid", "amount" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "equip", "unequip" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.NotNull(schema["properties"]!["amount"]);
    }

    [Fact]
    public void Validation_names_missing_mode_and_rejects_removed_generation_metadata()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var id = Guid.NewGuid().ToString("D");
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject { ["name"] = "game_equipment", ["arguments"] = new JObject { ["uuid"] = id } }));
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_equipment",
                ["arguments"] = new JObject { ["mode"] = "equip", ["uuid"] = id, ["amount"] = 1, ["worldGeneration"] = 9 },
            }));

        var missingErrors = Assert.IsType<JArray>(missing.Body!["error"]!["data"]!["validationErrors"]);
        Assert.Contains(missingErrors.Values<JObject>(),
            error => (string?)error!["code"] == "missing_required" && (string?)error["field"] == "mode");
        Assert.Equal(-32602, (int?)rejected.Body?["error"]?["code"]);
        Assert.Contains(
            rejected.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "unexpected_field" &&
                     (string?)error["field"] == "worldGeneration");
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_newer_world_poststate()
    {
        var failure = new EquipmentLoadoutSubmission(EquipmentLoadoutPreflight.VerificationFailed,
            EquipmentLoadoutNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), "stack unchanged");
        var success = new EquipmentLoadoutSubmission(EquipmentLoadoutPreflight.Proceeded,
            EquipmentLoadoutNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "stack changed");

        var failed = Json(GameMcpEquipmentLoadoutProjection.Project(in failure));
        var committed = Json(GameMcpEquipmentLoadoutProjection.Project(in success));

        Assert.Equal("requested equipped stack count", (string?)failed["missingOutcome"]);
        Assert.Single(failed.Properties());
        Assert.Empty(committed.Properties());
    }

    [Fact]
    public void Equipment_row_is_a_named_complete_next_decision_with_live_usage_holdings()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 2401);
        var response = Json(GameMcpWorldQuery.GetRow(
            context, "equipment", EquipmentId.ToString("D")));

        var row = response["row"]!;
        Assert.Equal("Prismatic Lens", (string?)row["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(TypeId), (string?)row["equipmentType"]!["uuid"]);
        Assert.Equal("Focus", (string?)row["equipmentType"]!["name"]);
        Assert.Equal(1, (int)row["equippedCount"]!);
        Assert.Equal(4, (int)row["maximumStacks"]!);
        Assert.Equal(2, (int)row["equip"]!["maximumAmount"]!);
        Assert.Equal(1, (int)row["unequip"]!["maximumAmount"]!);
        var cost = Assert.Single(row["equip"]!["usageCosts"]!).Value<JObject>()!;
        Assert.Equal("Focus", (string?)cost["resource"]!["name"]);
        Assert.Equal("20", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);
        Assert.Null(row["receipt"]);
        Assert.Null(row["payment"]);
    }

    [Fact]
    public void EquipmentAmountRefusalCarriesTheCeilingItsOwnSentenceNames()
    {
        var refused = Json(Assert.IsType<GameMcpObject>(
            GameMcpEquipmentLoadoutProjection.Project(
                EquipmentLoadoutSubmission.Reject(
                    EquipmentLoadoutPreflight.AmountUnavailable,
                    "This artifact can equip at most 2 more stacks with the current " +
                    "capacity and resources.",
                    2))));

        Assert.Equal(2, (int)refused["maximumAmount"]!);

        var noCeiling = Json(Assert.IsType<GameMcpObject>(
            GameMcpEquipmentLoadoutProjection.Project(
                EquipmentLoadoutSubmission.Reject(
                    EquipmentLoadoutPreflight.NotCreated,
                    "The artifact has not been created."))));

        Assert.Null(noCeiling["maximumAmount"]);
    }

    /// <summary>
    /// Zero equipped meant two different things — owning none, and owning some with none equipped —
    /// and the only way to tell them apart was to attempt an equip and read the refusal.
    /// </summary>
    [Fact]
    public void An_equipment_list_row_says_whether_the_artifact_exists_at_all()
    {
        var uncreated = Guid.Parse("f4b19e00-0000-0000-0000-000000000009");
        var world = World();
        var rows = PublicationTable<WorldEquipment>.Create(new[]
        {
            world.Equipment.AsSpan()[0],
            new WorldEquipment(uncreated, false, 0, BigDouble.Zero, 0, false,
                BigDouble.One, BigDouble.One, BigDouble.One, 0, 0, -1, BigDouble.Zero),
        });
        var context = GameMcpTestHarness.Context(
            new GameWorldState
            {
                CollectedAtEpoch = world.CollectedAtEpoch,
                CollectedAtUtcTicks = world.CollectedAtUtcTicks,
                EntityIdentities = world.EntityIdentities,
                Resources = world.Resources,
                Equipment = rows,
                CollectionCategories = world.CollectionCategories,
            },
            generation: 2402);

        var listed = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.ListRows(context, "equipment", 0, 50).Freeze(),
            world.EntityIdentities));

        var listedRows = Assert.IsType<JArray>(listed["rows"]);
        var created = listedRows.Values<JObject>()
            .Single(row => (string?)row!["uuid"] == GameMcpTestHarness.Handle(EquipmentId))!;
        var missing = listedRows.Values<JObject>()
            .Single(row => (string?)row!["uuid"] == GameMcpTestHarness.Handle(uncreated))!;

        Assert.True((bool)created["created"]!);
        Assert.Equal(1, (int)created["equippedCount"]!);
        Assert.False((bool)missing["created"]!);
        Assert.Equal(0, (int)missing["equippedCount"]!);
    }

    private static GameWorldState World()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(80), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8, false,
            new BigDouble(80), BigDouble.Zero);
        var decision = new WorldEquipmentDecision(true, string.Empty, TypeId, 1, 4, 1, 3,
            1, 2, 2, 1, true,
            PublicationTable<WorldEquipmentUsageCost>.Create(new[]
            {
                new WorldEquipmentUsageCost(ResourceId, new BigDouble(20)),
            }));
        var equipment = new WorldEquipment(EquipmentId, true, 0, BigDouble.Zero, 3, false,
            BigDouble.One, BigDouble.One, BigDouble.One, 1, 0, -1, BigDouble.Zero,
            loadout: decision);
        var identityRows = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(EquipmentId, "EquipmentSO", "Prismatic Lens", "prismaticLens"),
            new EntityIdentityName(TypeId, "EquipmentTypeSO", "Focus", "focus"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Focus", "focusResource"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 19,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(19, identityRows),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[] { equipment }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("equipment", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            value,
            World().EntityIdentities));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}
