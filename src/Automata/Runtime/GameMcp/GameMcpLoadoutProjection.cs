#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpLoadoutProjection
{
    internal static GameMcpValue Project(in LoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested loadout transition";

        // A refusal whose sentence names a slot range carries both bounds as numbers, read from
        // the live snapshot list the sentence was written from.
        if (submission.MinimumSlot >= 0) result["minimumSlot"] = submission.MinimumSlot;
        if (submission.MaximumSlot >= 0) result["maximumSlot"] = submission.MaximumSlot;
        return result.Freeze();
    }
}
#endif
