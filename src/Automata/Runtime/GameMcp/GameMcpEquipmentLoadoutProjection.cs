#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpEquipmentLoadoutProjection
{
    internal static GameMcpValue Project(in EquipmentLoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested equipped stack count";

        // A refusal whose sentence names a ceiling carries that same ceiling as a number, read from
        // the admission capture the sentence was written from.
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }
}
#endif
