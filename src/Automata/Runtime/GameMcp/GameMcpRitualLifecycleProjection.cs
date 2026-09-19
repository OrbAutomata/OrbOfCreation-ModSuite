#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpRitualLifecycleProjection
{
    internal static GameMcpValue Project(in RitualLifecycleSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested Ritual transition";

        // A refusal whose sentence names a floor and a ceiling carries both as numbers, read from
        // the admission capture the sentence was written from.
        if (submission.MinimumAmount >= 0) result["minimumAmount"] = submission.MinimumAmount;
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }
}
#endif
