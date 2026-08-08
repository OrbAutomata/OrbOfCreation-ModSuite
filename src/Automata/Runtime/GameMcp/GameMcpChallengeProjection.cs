#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpChallengeProjection
{
    internal static GameMcpValue Project(in ChallengeSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();

        // A press that was attempted spends before it asks for offers, so its failure carries the
        // settled budget on both sides — the pair a committed press publishes, on the one path a
        // caller would otherwise have to infer the spend from. A refusal that never pressed names
        // the one axis that failed and hands over the number its sentence read, so a planner learns
        // the live budget instead of a hand-written rule that can drift.
        if (submission.RerollsLeft >= 0 && submission.RerollsLeftAfter >= 0)
        {
            var budget = new GameMcpObjectBuilder();
            budget["before"] = submission.RerollsLeft;
            budget["after"] = submission.RerollsLeftAfter;
            result["rerollsLeft"] = budget;
        }
        else if (submission.RerollsLeft >= 0) result["rerollsLeft"] = submission.RerollsLeft;
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested challenge transition";
        return result.Freeze();
    }
}
#endif
