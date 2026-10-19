using System;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpPostStateSettlementTests
{
    [Fact]
    public void ActionRegistrationDependsOnlyOnRuntimeAdmission()
    {
        Assert.True(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: true));
        Assert.False(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: false));
    }

    [Fact]
    public void PostStateSettlementRejectsAWorldCapturedBeforeTheActionEvenIfPublishedLater()
    {
        Assert.Equal(1f, GameMcpPostStateSettlement.MaximumWaitSeconds);
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(41, 41));
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(40, 41));
        Assert.True(GameMcpPostStateSettlement.IsStrictlyNewer(42, 41));

        var actionCompleted = DateTime.UtcNow.Ticks;
        var staleCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted - 1 },
            generation: 42);
        var settledCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted + 1 },
            generation: 42);

        Assert.False(GameMcpPostStateSettlement.HasSettledWorld(
            staleCapture, 41, actionCompleted));
        Assert.True(GameMcpPostStateSettlement.HasSettledWorld(
            settledCapture, 41, actionCompleted));
    }

    [Fact]
    public void TimedOutPostStateCarriesExceptionalEvidenceInsteadOfAnEmptyCommit()
    {
        var value = GameMcpPostStateSettlement.TimedOut(
            GameMcpAcceptanceFixture.NativeCommand(),
            latest: null);

        Assert.Equal(
            "{\"postStateUnavailable\":{\"reasonCode\":\"ERR_UNAVAILABLE\",\"reason\":\"The " +
            "press went through, but the game had not redrawn within a second, so this answer " +
            "cannot say what changed. Read the entity again.\"}}",
            GameMcpTestHarness.Json(value).ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void ConceptSettlementWaitsForTheActiveCountItsResponsePublishes()
    {
        var recipe = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var completedAt = DateTime.UtcNow.Ticks;
        var before = new GameWorldState
        {
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 440, true, BigDouble.One),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Concept, 9, 3, "add", recipe, Guid.Empty,
            "AlchemyRecipeSO", 5, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 41));
        // The game takes the queue increase a settlement before it submits it.
        var queuedOnly = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 445, true, BigDouble.One),
            }),
        };
        var submitted = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 445, 445, true, BigDouble.One),
            }),
        };

        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(queuedOnly, generation: 42), 41, completedAt, command));
        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(submitted, generation: 42), 41, completedAt, command));

        var delta = GameMcpTestHarness.Json(GameMcpPostStateSettlement.Project(
            GameMcpTestHarness.Context(submitted, generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal(440, (int)delta["activeCount"]!["before"]!);
        Assert.Equal(445, (int)delta["activeCount"]!["after"]!);

        var timeout = GameMcpTestHarness.Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(queuedOnly, generation: 42)));
        Assert.Equal("ERR_STATE", (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains("440 are assigned now",
            (string?)timeout["postStateUnavailable"]!["reason"]);
    }

    [Fact]
    public void PlotPostStateReportsTheObservedRequestedPairQuantityChange()
    {
        var plotId = KnownEntities.FruitTreePlot.Uuid;
        var actionId = KnownEntities.FruitTreeCollect.Uuid;
        var before = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 4),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 2, true),
            }),
        };
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Harvest,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "add_plot_action",
            targetId: plotId,
            secondaryId: actionId,
            derivedNativeType: "PlotNodeSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var committed = GameMcpCommandResult.Committed("committed", 9, 3);
        var activeWorld = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 3),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 3, true),
            }),
        };

        var active = GameMcpTestHarness.Json(GameMcpPostStateSettlement.Project(
            GameMcpTestHarness.Context(activeWorld), command, committed));
        Assert.Equal(2, (int)active["active"]!["before"]!);
        Assert.Equal(3, (int)active["active"]!["after"]!);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)active["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["plot"]!["name"]));
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)active["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["action"]!["name"]));

        // A commit answers with what the press changed. Whether another one is possible is a read,
        // and world_get agromancy-plot-actions carries the same decision.
        Assert.Null(active["next"]);

        var fastActionDetails = new GameMcpObjectBuilder
        {
            ["active"] = new GameMcpObjectBuilder
            {
                ["before"] = 0,
                ["after"] = 1,
            }.Freeze(),
        }.Freeze();
        var fastAction = GameMcpTestHarness.Json(GameMcpPostStateSettlement.Project(
            GameMcpTestHarness.Context(new GameWorldState
            {
                PlotActions = activeWorld.PlotActions,
            }),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3, fastActionDetails)));
        Assert.Equal(0, (int)fastAction["active"]!["before"]!);
        Assert.Equal(1, (int)fastAction["active"]!["after"]!);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)fastAction["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["plot"]!["name"]));
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)fastAction["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["action"]!["name"]));
        Assert.Null(fastAction["postStateUnavailable"]);

        // One request shape, two answers, one identity. The commit's delta names both entities in
        // their own blocks, and the plot's was being promoted on one path and the action's on the
        // other, so a caller correlating by uuid mis-filed half of its calls.
        var commitProjection = GameMcpTestHarness.Json(
            GameMcpCommandResult.Committed("committed", 9, 3)
                .WithSettledPostState(GameMcpPostStateSettlement.Project(
                    GameMcpTestHarness.Context(activeWorld), command, committed))
                .Project(command));
        var refusalProjection = GameMcpTestHarness.Json(
            GameMcpCommandResult.Rejected("amount_unavailable", "The plot allows fewer than that.")
                .Project(command));

        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)commitProjection["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(plotId), (string?)refusalProjection["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)commitProjection["action"]!["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(actionId), (string?)refusalProjection["action"]!["uuid"]);
    }

    private static WorldPlotAction PlotAction(
        Guid plotId,
        Guid actionId,
        int instanceCount,
        int maximumRemainingInstances) =>
        new(
            new RawPlotAction(
                plotId,
                actionId,
                offeredCount: 1,
                instanceCount,
                PlotActionPrerequisiteEvidence.NativeLatchedTrue),
            elementCost: 2,
            elementCostKnown: true,
            hasEnoughForOneInstance: true,
            maximumRemainingInstances);
}
