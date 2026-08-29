#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellCompositionProjection
{
    internal static GameMcpValue Project(in SpellCompositionSubmission submission, string dial)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();

        // The commit names which dial moved; so does the refusal. Two dials share one tool, and a
        // refusal that named neither left the caller to remember what it had asked for.
        if (dial.Length > 0) result["dial"] = dial;
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested dial value";

        // A refusal whose sentence names a range carries that same range as numbers, read from the
        // native capture the sentence was written from.
        if (submission.Minimum >= 0 && submission.Maximum >= 0)
        {
            result["minimum"] = submission.Minimum;
            result["maximum"] = submission.Maximum;
        }
        return result.Freeze();
    }
}
#endif
