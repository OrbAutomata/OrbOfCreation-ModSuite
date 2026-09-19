#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLoadoutProjection
{
    internal static GameMcpValue Project(in SpellLoadoutSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested spell slot state";
        return result.Freeze();
    }
}
#endif
