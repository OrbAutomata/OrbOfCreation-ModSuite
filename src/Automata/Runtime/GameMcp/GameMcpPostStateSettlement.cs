#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>One bounded freshness rule shared by every MCP mutation projection.</summary>
internal static class GameMcpPostStateSettlement
{
    internal const float MaximumWaitSeconds = 1f;

    /// <summary>
    /// What a lifecycle transition is allowed to take. A reset tears the world down behind a native
    /// scene fade and republishes it from scratch, which is not the same order of work as the next
    /// world after a button press. The frame-scale budget answered the heaviest verb in the game
    /// with a timeout on a reset that had plainly worked.
    /// </summary>
    internal const float LifecycleWaitSeconds = 15f;

    internal static float WaitSeconds(GameMcpCommand command) =>
        command.Kind == GameMcpCommandKind.Prestige ? LifecycleWaitSeconds : MaximumWaitSeconds;

    internal static bool IsStrictlyNewer(ulong candidate, ulong mutationWorld) =>
        candidate > mutationWorld;

    internal static bool HasSettledWorld(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        long actionCompletedAtUtcTicks) =>
        state?.World is not null &&
        IsStrictlyNewer(state.World.Generation.Value, mutationWorld) &&
        state.World.Snapshot.CollectedAtUtcTicks > actionCompletedAtUtcTicks;

    /// <summary>
    /// The single settlement predicate used by mutation response dispatch. Most actions settle
    /// as soon as the next immutable world exists; discovery offers additionally require the
    /// event-driven offer transition to be visible in that world.
    /// </summary>
    internal static bool IsReady(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        long actionCompletedAtUtcTicks,
        GameMcpCommand command) =>
        HasSettledWorld(state, mutationWorld, actionCompletedAtUtcTicks) &&
        HasRequestedOutcome(state!, command) &&
        (command.Kind != GameMcpCommandKind.DiscoveryTreeOffer ||
         GameMcpWorldQuery.HasDiscoveryPostState(
             state!,
             command.TargetId,
             command.Mode,
             command.SecondaryId));

    private static bool HasRequestedOutcome(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (command.Kind == GameMcpCommandKind.Cast &&
            string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return WorldSpellSlotLookup.TryFind(
                    state.World!.Snapshot.SpellSlots,
                    command.Amount - 1,
                    out var slot) &&
                slot.Occupied &&
                slot.SpellRecipeId == command.TargetId &&
                slot.Toggled &&
                !slot.Casting;
        }
        if (command.Kind == GameMcpCommandKind.StructureLifecycle)
        {
            return WorldLookup.TryFind(
                    state.World!.Snapshot.Structures,
                    command.TargetId,
                    out var structure) &&
                structure.Reading.Disabled ==
                    string.Equals(command.Mode, "disable", System.StringComparison.Ordinal);
        }
        if (command.Kind == GameMcpCommandKind.Concept)
            return ConceptAmountReached(state, command);
        if (command.Kind == GameMcpCommandKind.Research && command.Mode == "develop")
            return ResearchAmountReached(state, command);
        if (command.Kind != GameMcpCommandKind.SpellComposition) return true;
        var workbench = state.World!.Snapshot.SpellWorkbench;
        return command.Mode switch
        {
            "set_output_level" => workbench.OutputLevel == command.Amount,
            "set_reserve_level" => workbench.ReserveLevel == command.Amount,
            _ => false,
        };
    }

    /// <summary>
    /// The game writes an Alchemy queue decrease straight through but holds an increase for a
    /// quarter second before submitting it, so the queued quantity moves a settlement ahead of the
    /// active one. Waiting on the active quantity is what keeps the response in the same
    /// coordinate the concept projection publishes.
    /// </summary>
    private static bool ConceptAmountReached(GameMcpFrameContext state, GameMcpCommand command)
    {
        var beforeWorld = command.FrameContext?.World?.Snapshot;
        if (beforeWorld is null) return false;
        var before = WorldAlchemyInstanceLookup.TryFind(
            beforeWorld.AlchemyInstances, command.TargetId, out var previous)
            ? previous.Quantity
            : 0;
        var after = WorldAlchemyInstanceLookup.TryFind(
            state.World!.Snapshot.AlchemyInstances, command.TargetId, out var current)
            ? current.Quantity
            : 0;
        return command.Mode switch
        {
            "add" => after == checked(before + command.Amount),
            "remove_owned" => after == checked(before - command.Amount),
            "rotate_out" => after == 0,
            _ => false,
        };
    }

    private static bool ResearchAmountReached(GameMcpFrameContext state, GameMcpCommand command)
    {
        var beforeWorld = command.FrameContext?.World?.Snapshot;
        return beforeWorld is not null &&
            WorldLookup.TryFind(beforeWorld.Research, command.TargetId, out var before) &&
            WorldLookup.TryFind(state.World!.Snapshot.Research, command.TargetId, out var after) &&
            (before.Decision.QueueMode
                ? PendingResearchLevels(after) == checked(PendingResearchLevels(before) + command.Amount)
                : !before.IsDeveloping && after.IsDeveloping);
    }

    private static int PendingResearchLevels(in WorldResearch research) =>
        checked(research.Level + research.QueuedLevels + (research.IsDeveloping ? 1 : 0));

    internal static GameMcpValue TimedOut(
        GameMcpCommand command,
        GameMcpFrameContext? latest)
    {
        if (command.Kind == GameMcpCommandKind.SpellComposition && latest?.World is not null)
        {
            var workbench = latest.World.Snapshot.SpellWorkbench;
            var observed = command.Mode == "set_output_level"
                ? workbench.OutputLevel
                : workbench.ReserveLevel;
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled " + command.PayloadKey + " dial is " + observed +
                ", not the requested " + command.Amount);
        }
        if (command.Kind == GameMcpCommandKind.Cast &&
            string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled spell slot did not show the requested toggle as off");
        }
        if (command.Kind == GameMcpCommandKind.StructureLifecycle)
        {
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled attribute did not show the requested enabled state");
        }
        if (command.Kind == GameMcpCommandKind.Research &&
            string.Equals(command.Mode, "develop", System.StringComparison.Ordinal) &&
            latest?.World is not null)
        {
            var beforeWorld = command.FrameContext?.World?.Snapshot;
            var before = default(WorldResearch);
            var hasBefore = beforeWorld is not null && WorldLookup.TryFind(
                beforeWorld.Research, command.TargetId, out before);
            var hasAfter = WorldLookup.TryFind(
                latest.World.Snapshot.Research, command.TargetId, out var after);
            var queueMode = hasBefore
                ? before.Decision.QueueMode
                : hasAfter && after.Decision.QueueMode;
            if (queueMode)
            {
                var observed = hasAfter ? PendingResearchLevels(after).ToString() : "not published";
                if (!hasBefore)
                {
                    return GameMcpWorldQuery.PostStateUnavailable(
                        "requested_state_not_reached",
                        "the research queue before state was not published; the settled queue has " +
                        observed + " pending levels, so the requested increase cannot be verified");
                }
                var expected = checked(PendingResearchLevels(before) + command.Amount);
                return GameMcpWorldQuery.PostStateUnavailable(
                    "requested_state_not_reached",
                    "the settled research queue has " + observed +
                    " pending levels, not the requested " + expected);
            }
            var beforeState = hasBefore
                ? before.IsDeveloping ? "developing" : "idle"
                : "not published";
            var afterState = hasAfter
                ? after.IsDeveloping ? "developing" : "idle"
                : "not published";
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "The develop press did not start this research: it was " + beforeState +
                " before and it is " + afterState + " now. Check its price and its caps on the " +
                "Research screen.");
        }
        if (command.Kind == GameMcpCommandKind.Concept && latest?.World is not null)
        {
            var observed = WorldAlchemyInstanceLookup.TryFind(
                latest.World.Snapshot.AlchemyInstances, command.TargetId, out var concept)
                ? concept.Quantity.ToString()
                : "not published";
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the assigned concepts did not reach the requested amount; " + observed +
                " are assigned now.");
        }
        if (command.Kind == GameMcpCommandKind.Prestige)
        {
            return GameMcpWorldQuery.PrestigeSettlementPending(
                command.ExpectedLifecycleGeneration,
                latest?.LifecycleGeneration,
                latest?.SceneName);
        }
        return GameMcpWorldQuery.PostStateUnavailable(
            "post_state_timeout",
            "The press went through, but the game had not redrawn within a second, so this " +
            "answer cannot say what changed. Read the entity again.");
    }
}
#endif
