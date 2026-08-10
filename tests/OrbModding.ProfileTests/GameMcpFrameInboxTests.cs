using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpFrameInboxTests
{
    [Fact]
    public void ClaimTakesEveryPendingOperationInSubmissionSequence()
    {
        var inbox = new GameMcpFrameInbox();
        var submitted = Enumerable.Range(0, 10)
            .Select(_ => Submit(inbox, "world_overview"))
            .ToArray();

        var claimed = inbox.ClaimPending();

        Assert.Equal(submitted, claimed);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(static value => (long)value),
            claimed.Select(static operation => operation.Sequence));
    }

    [Fact]
    public void SubmissionAfterClaimBelongsToNextFrame()
    {
        var inbox = new GameMcpFrameInbox();
        var first = Submit(inbox, "world_overview");

        Assert.Equal(new[] { first }, inbox.ClaimPending());
        var next = Submit(inbox, "world_overview");
        Assert.Equal(new[] { next }, inbox.ClaimPending());
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void CancelBeforeClaimPreventsExecutionAndIsOmittedFromClaim()
    {
        var inbox = new GameMcpFrameInbox();
        var canceled = Submit(inbox, "world_overview");
        var live = Submit(inbox, "world_categories");
        var cancellation = Failure("request_canceled_before_claim");

        Assert.True(canceled.Completion.TryCancelBeforeClaim(cancellation));
        Assert.Equal(new[] { live }, inbox.ClaimPending());
        Assert.True(canceled.Completion.TryWait(
            TimeSpan.FromMilliseconds(50), out var terminal));
        Assert.Equal("ERR_UNAVAILABLE", (string?)GameMcpTestHarness.Json(terminal.Payload!)["reasonCode"]);
    }

    [Fact]
    public void CancellationAfterClaimCannotHideLaterTerminalResult()
    {
        var inbox = new GameMcpFrameInbox();
        var operation = Submit(inbox, "game_agromancy");
        Assert.Equal(new[] { operation }, inbox.ClaimPending());

        Assert.False(operation.Completion.TryCancelBeforeClaim(Failure("timeout")));
        inbox.Complete(operation, Failure("native_terminal"));

        Assert.Equal("ERR_REFUSED", (string?)GameMcpTestHarness.Json(
                operation.Completion.WaitForClaimedTerminal().Payload!)["reasonCode"]);
    }

    [Fact]
    public void CompletingTwiceFailsLoudly()
    {
        var inbox = new GameMcpFrameInbox();
        var operation = Submit(inbox, "world_overview");
        Assert.Equal(new[] { operation }, inbox.ClaimPending());
        inbox.Complete(operation, Failure("first"));
        Assert.Throws<InvalidOperationException>(() =>
            inbox.Complete(operation, Failure("second")));
    }

    [Fact]
    public void CloseCompletesEveryUnclaimedOperationInline()
    {
        var inbox = new GameMcpFrameInbox();
        var first = Submit(inbox, "world_overview");
        var second = Submit(inbox, "world_categories");
        inbox.Close("shutdown", "server stopping");

        Assert.True(first.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var firstResult));
        Assert.True(second.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var secondResult));
        Assert.Equal("ERR_REFUSED", (string?)GameMcpTestHarness.Json(firstResult.Payload!)["reasonCode"]);
        Assert.Equal("ERR_REFUSED", (string?)GameMcpTestHarness.Json(secondResult.Payload!)["reasonCode"]);
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void GameplayAndEmergencyOperationsRetainSubmissionOrderWithoutPriorityQueues()
    {
        var inbox = new GameMcpFrameInbox();
        var gameplay = Submit(inbox, "game_agromancy", GameMcpOperationClass.Gameplay);
        var stop = Submit(
            inbox,
            "suite_emergency_stop",
            GameMcpOperationClass.SuiteAdministration);

        Assert.Equal(new[] { gameplay, stop }, inbox.ClaimPending());
    }

    [Fact]
    public void EmergencyStopTakesEffectAtItsSubmittedPositionInsideOneClaimedBatch()
    {
        var inbox = new GameMcpFrameInbox();
        Submit(inbox, "game_agromancy", GameMcpOperationClass.Gameplay);
        Submit(inbox, "suite_emergency_stop", GameMcpOperationClass.SuiteAdministration);
        Submit(inbox, "game_agromancy", GameMcpOperationClass.Gameplay);
        var stopped = false;
        var outcomes = new List<string>();

        Assert.Equal(3, GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(),
            (operation, _) =>
            {
                if (operation.Request.ToolName == "suite_emergency_stop")
                {
                    stopped = true;
                    outcomes.Add("stop_committed");
                    return Success();
                }
                outcomes.Add(stopped ? "gameplay_rejected" : "gameplay_committed");
                return stopped ? Failure("emergency_stop") : Success();
            },
            (_, _, _) => Failure("unexpected_fault")));

        Assert.Equal(
            new[] { "gameplay_committed", "stop_committed", "gameplay_rejected" },
            outcomes);
    }

    [Fact]
    public void JsonProjectionAndProtocolRoutingFailInsideFrameExecutionAndEncodeAfterward()
    {
        var inbox = new GameMcpFrameInbox();
        var encode = Submit(inbox, "world_overview");
        var reflect = Submit(inbox, "suite_configuration");
        var route = Submit(inbox, "world_categories");
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());

        Assert.Equal(3, GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(),
            (operation, _) =>
            {
                if (operation == encode)
                    GameMcpDocumentJsonEncoder.Encode(
                        new GameMcpObjectBuilder().Freeze(),
                        GameMcpTestHarness.EntityCatalog);
                else if (operation == reflect)
                    GameMcpObjectProjector.Project(new object());
                else
                    router.Handle(GameMcpAcceptanceFixture.Request(1, "ping", new JObject()));
                return Success();
            },
            (_, _, exception) => Failure(exception.Message)));

        var terminals = new[] { encode, reflect, route }
            .Select(operation => operation.Completion.WaitForClaimedTerminal())
            .ToArray();
        Assert.All(terminals, terminal => Assert.Contains(
            "cannot run inside a Unity frame operation",
            (string?)GameMcpTestHarness.Json(terminal.Payload!)["reason"]));

        var protocol = terminals[0].ToProtocolResult();
        Assert.Null(protocol["structuredContent"]);
        Assert.Contains(
            "cannot run inside a Unity frame operation",
            (string?)protocol["content"]![0]!["text"]);
        Assert.True((bool)protocol["isError"]!);
    }

    [Fact]
    public void EmptyFrameTouchesNoContextOwnerOrOperationHandler()
    {
        var inbox = new GameMcpFrameInbox();
        var contextCalls = 0;
        var executionCalls = 0;
        var faultCalls = 0;

        var drained = GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ =>
            {
                contextCalls++;
                return GameMcpTestHarness.Context();
            },
            (_, _) =>
            {
                executionCalls++;
                return Success();
            },
            (_, _, _) =>
            {
                faultCalls++;
                return Failure("unexpected_fault");
            });

        Assert.Equal(0, drained);
        Assert.Equal(0, contextCalls);
        Assert.Equal(0, executionCalls);
        Assert.Equal(0, faultCalls);
    }

    [Fact]
    public void RepeatedEmptyFramesAllocateNothingForMcp()
    {
        var inbox = new GameMcpFrameInbox();
        GameMcpFrameContext Capture(GameMcpFrameData _) =>
            throw new Xunit.Sdk.XunitException("idle frame captured context");
        GameMcpToolExecution? Execute(GameMcpFrameOperation _, GameMcpFrameContext __) =>
            throw new Xunit.Sdk.XunitException("idle frame executed operation");
        GameMcpToolExecution Fault(
            GameMcpFrameOperation _,
            GameMcpFrameContext? __,
            Exception ___) =>
            throw new Xunit.Sdk.XunitException("idle frame projected fault");
        Func<GameMcpFrameData, GameMcpFrameContext> capture = Capture;
        Func<GameMcpFrameOperation, GameMcpFrameContext, GameMcpToolExecution?> execute = Execute;
        Func<GameMcpFrameOperation, GameMcpFrameContext?, Exception, GameMcpToolExecution> fault =
            Fault;

        Assert.Equal(0, GameMcpFrameBatchExecutor.Drain(inbox, capture, execute, fault));

        // The claim is that an idle frame allocates nothing, which is a fact about the steady
        // state. A fixed warm-up cannot express that: tiered-JIT promotion charges thread-local
        // bookkeeping to whichever window it lands in, and which window that is depends on what the
        // rest of the suite warmed first. So measure windows until one is silent — that is the
        // steady state, reached rather than assumed — and then hold the next window to the
        // invariant. A drain that really allocates never yields a silent window and fails here with
        // every window it measured; a runtime that merely compiles falls silent and stays silent.
        long MeasureWindow()
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 10_000; index++)
                GameMcpFrameBatchExecutor.Drain(inbox, capture, execute, fault);
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }

        var windows = new List<long>();
        do
        {
            windows.Add(MeasureWindow());
        }
        while (windows[^1] != 0 && windows.Count < 32);

        Assert.True(
            windows[^1] == 0,
            $"idle drains never fell silent over {windows.Count} windows of 10,000: " +
            string.Join(", ", windows));
        Assert.Equal(0, MeasureWindow());
    }

    [Fact]
    public void ContextCaptureFaultCompletesEveryClaimedOperationExactlyOnce()
    {
        var inbox = new GameMcpFrameInbox();
        var operations = Enumerable.Range(0, 3)
            .Select(_ => Submit(inbox, "suite_health"))
            .ToArray();
        var executionCalls = 0;

        Assert.Equal(3, GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => throw new InvalidOperationException("health owner failed"),
            (_, _) =>
            {
                executionCalls++;
                return Success();
            },
            (_, context, exception) =>
            {
                Assert.Null(context);
                return Failure(exception.Message);
            }));

        Assert.Equal(0, executionCalls);
        Assert.All(operations, operation => Assert.Contains(
            "health owner failed",
            (string?)GameMcpTestHarness.Json(
                operation.Completion.WaitForClaimedTerminal().Payload!)["reason"]));
    }

    [Fact]
    public void OneFrameExecutesAllTenClaimedOperationsInSequenceAgainstOneContext()
    {
        var inbox = new GameMcpFrameInbox();
        var submitted = Enumerable.Range(0, 10)
            .Select(_ => Submit(inbox, "world_overview"))
            .ToArray();
        var contextCalls = 0;
        var executed = new List<long>();
        var contexts = new List<GameMcpFrameContext>();

        var drained = GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ =>
            {
                contextCalls++;
                return GameMcpTestHarness.Context(configurationGeneration: 71);
            },
            (operation, context) =>
            {
                executed.Add(operation.Sequence);
                contexts.Add(context);
                return Success();
            },
            (_, _, _) => Failure("unexpected_fault"));

        Assert.Equal(10, drained);
        Assert.Equal(1, contextCalls);
        Assert.Equal(submitted.Select(static operation => operation.Sequence), executed);
        Assert.All(contexts, context => Assert.Same(contexts[0], context));
        Assert.All(submitted, operation => Assert.True(operation.Completion.TryWait(
            TimeSpan.FromMilliseconds(50), out _)));
    }

    [Fact]
    public void ArrivalDuringExecutionIsOwnedByTheNextFrameWithoutLossOrReordering()
    {
        var inbox = new GameMcpFrameInbox();
        var first = Submit(inbox, "world_overview");
        GameMcpFrameOperation? late = null;
        var executed = new List<long>();

        Assert.Equal(1, GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(),
            (operation, _) =>
            {
                executed.Add(operation.Sequence);
                late = Submit(inbox, "world_categories");
                return Success();
            },
            (_, _, _) => Failure("unexpected_fault")));
        Assert.Equal(new[] { first.Sequence }, executed);
        Assert.NotNull(late);

        Assert.Equal(1, GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(),
            (operation, _) =>
            {
                executed.Add(operation.Sequence);
                return Success();
            },
            (_, _, _) => Failure("unexpected_fault")));
        Assert.Equal(new[] { first.Sequence, late!.Sequence }, executed);
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void ClaimedBatchPinsWorldAndConfigurationReferencesWithoutCopying()
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        var firstWorld = new GameWorldState
        {
            CollectedAtEpoch = 4,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        publisher.Publish(firstWorld, new WorldGeneration(40));
        var pinned = publisher.ReadLatest();
        var currentConfiguration = 70UL;
        var inbox = new GameMcpFrameInbox();
        Submit(inbox, "world_overview");
        Submit(inbox, "suite_configuration");
        var observedWorlds = new List<WorldPublication<GameWorldState>?>();
        var observedConfigurations = new List<ulong>();

        GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(
                pinned,
                configurationGeneration: currentConfiguration),
            (_, context) =>
            {
                observedWorlds.Add(context.World);
                observedConfigurations.Add(context.ConfigurationGeneration.Value);
                if (observedWorlds.Count == 1)
                {
                    publisher.Publish(new GameWorldState
                    {
                        CollectedAtEpoch = 5,
                        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
                    }, new WorldGeneration(41));
                    currentConfiguration = 71;
                }
                return Success();
            },
            (_, _, _) => Failure("unexpected_fault"));

        Assert.All(observedWorlds, world => Assert.Same(pinned, world));
        Assert.Equal(new ulong[] { 70, 70 }, observedConfigurations);
        Assert.Equal((ulong)41, publisher.ReadLatest().Generation.Value);
        Assert.Equal((ulong)71, currentConfiguration);
    }

    [Fact]
    public void TwoGameplayOperationsRevalidateLiveStateSequentiallyInOneFrame()
    {
        var inbox = new GameMcpFrameInbox();
        Submit(inbox, "game_discover", GameMcpOperationClass.Gameplay);
        Submit(inbox, "game_discover", GameMcpOperationClass.Gameplay);
        var liveOfferRevision = 0;
        var observedBeforeExecution = new List<int>();

        var drained = GameMcpFrameBatchExecutor.Drain(
            inbox,
            _ => GameMcpTestHarness.Context(configurationGeneration: 80),
            (_, _) =>
            {
                observedBeforeExecution.Add(liveOfferRevision);
                liveOfferRevision++;
                return Success();
            },
            (_, _, _) => Failure("unexpected_fault"));

        Assert.Equal(2, drained);
        Assert.Equal(new[] { 0, 1 }, observedBeforeExecution);
        Assert.Equal(2, liveOfferRevision);
    }

    private static GameMcpFrameOperation Submit(
        GameMcpFrameInbox inbox,
        string tool,
        GameMcpOperationClass classification = GameMcpOperationClass.ReadOnly) =>
        inbox.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = tool,
            Classification = classification,
            RequiredData = GameMcpFrameData.None,
        }.Freeze());

    private static GameMcpToolExecution Failure(string code) =>
        GameMcpToolExecution.Error(new GameMcpObjectBuilder
        {
            ["status"] = "rejected",
            ["code"] = code,
            ["reason"] = code,
        }.Freeze());

    private static GameMcpToolExecution Success() =>
        GameMcpToolExecution.Read(new GameMcpObjectBuilder
        {
            ["status"] = "available",
        }.Freeze());
}
