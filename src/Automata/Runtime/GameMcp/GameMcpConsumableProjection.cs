#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConsumableProjection
{
    internal static GameMcpValue Project(in ConsumablePlayerSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested consumable transition";
        return result.Freeze();
    }
}
#endif
