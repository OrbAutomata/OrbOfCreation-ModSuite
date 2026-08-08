#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpChallengeProjection
{
    internal static GameMcpValue Project(in ChallengeSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();

        // The refusal names the one axis that failed and hands over the number its sentence read,
        // so a planner learns the live budget instead of a hand-written rule that can drift.
        if (submission.RerollsLeft >= 0) result["rerollsLeft"] = submission.RerollsLeft;
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested challenge transition";
        return result.Freeze();
    }
}
#endif
