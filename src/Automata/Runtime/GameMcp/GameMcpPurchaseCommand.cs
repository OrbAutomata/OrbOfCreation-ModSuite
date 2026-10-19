#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPurchaseCommand
{
    /// <remarks>
    /// A purchase answers from the queued-level delta its own native verifier observed, so there is
    /// nothing left for a settled world to add: it queues levels the game drains over the following
    /// seconds, and every world after the press describes a queue already draining rather than the
    /// press. Waiting for one could only turn a purchase that verifiably committed into a
    /// <c>post_state_timeout</c>, which is the same lie in a slower costume.
    /// </remarks>
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Purchase,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.FromPress,
        prepare: Prepare);

    internal static string NativeTypeFor(string mode) =>
        mode == "structure" ? "StructureSO" : "UpgradeSO";

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        if (context.World is not null)
        {
            var structure = WorldLookup.TryFind(
                context.World.Snapshot.Structures, request.Uuid, out _);
            var upgrade = WorldLookup.TryFind(
                context.World.Snapshot.Upgrades, request.Uuid, out _);
            if (structure != upgrade)
            {
                draft.Mode = structure ? "structure" : "upgrade";
                draft.NativeType = NativeTypeFor(draft.Mode);
            }
        }
    }
}
#endif
