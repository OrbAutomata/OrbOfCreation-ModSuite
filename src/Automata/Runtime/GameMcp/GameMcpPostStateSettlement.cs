#if SERVICE_CYCLE_PROFILE
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

    internal static bool Settles(GameMcpCommand command) => Rule(command).Settles;

    internal static float WaitSeconds(GameMcpCommand command) => Rule(command).WaitSeconds;

    internal static GameMcpFrameData Data(GameMcpCommand command) => Rule(command).Data;

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
    /// as soon as the next immutable world exists; a kind whose post-state rule names the outcome
    /// it waits for additionally requires that outcome to be visible in that world.
    /// </summary>
    internal static bool IsReady(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        long actionCompletedAtUtcTicks,
        GameMcpCommand command) =>
        HasSettledWorld(state, mutationWorld, actionCompletedAtUtcTicks) &&
        Rule(command).Reached(state!, command);

    /// <summary>
    /// Projects the smallest changed fact after any gameplay mutation, through the post-state rule
    /// its command descriptor registers. Transport code only waits and delegates here.
    /// </summary>
    /// <remarks>
    /// A committed answer used to carry what it charged and what the next level asks. Both were
    /// wire-only bookkeeping: the paid rows priced the game's multi-buy variable rather than the
    /// count the call committed, so a two-level buy reported one level's price as though it were the
    /// whole charge, and the caller that most needed the number got the one running in the
    /// dangerous direction. The world publication still holds every cost curve — Auto Buy plans off
    /// it — and a refusal that cannot afford something still names what it needs and what is held.
    /// A live caller gets the outcome.
    /// </remarks>
    internal static GameMcpValue Project(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) => Rule(command).Project(state, command, committed);

    internal static GameMcpValue TimedOut(
        GameMcpCommand command,
        GameMcpFrameContext? latest) => Rule(command).TimedOut(command, latest);

    internal static GameMcpValue PostStateTimeout() =>
        GameMcpWorldQuery.PostStateUnavailable(
            "post_state_timeout",
            "The press went through, but the game had not redrawn within a second, so this " +
            "answer cannot say what changed. Read the entity again.");

    private static GameMcpPostStateRule Rule(GameMcpCommand command) =>
        GameMcpCommandCatalog.Shipping.PostState(command.Kind);
}
#endif
