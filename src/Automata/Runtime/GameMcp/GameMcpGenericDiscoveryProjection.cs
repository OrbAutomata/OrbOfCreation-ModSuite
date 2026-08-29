#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpGenericDiscoveryProjection
{
    internal static GameMcpValue Project(in GenericDiscoverySubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();

        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested entity discovered";
        return result.Freeze();
    }
}
#endif
