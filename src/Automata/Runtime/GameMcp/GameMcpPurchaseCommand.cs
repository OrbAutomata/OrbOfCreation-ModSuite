#if SERVICE_CYCLE_PROFILE
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
        GameMcpPostStateRule.FromPress);
}
#endif
