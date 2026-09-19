#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpGenericLevelProjection
{
    /// <summary>
    /// What the press bought and what the game asked for it.
    /// </summary>
    /// <remarks>
    /// <c>free</c> used to be read off the world standing before the press, which prices only the
    /// next rung. A ×5 rune buy off a ladder whose first level is free answered <c>free: yes</c>
    /// while Time Advancements fell 94 → 84. The press is the only thing that saw all five prices,
    /// so the press is what answers: <c>free</c> when every level it bought asked for nothing, and
    /// otherwise what it was charged, for how many levels.
    /// </remarks>
    internal static GameMcpValue Project(in GenericLevelSubmission submission)
    {
        var result = new GameMcpObjectBuilder();
        if (!submission.Verified)
        {
            if (submission.CallOutcome.MutationAttempts > 0)
                result["missingOutcome"] = "requested level increase";
            return result.Freeze();
        }
        if (submission.LevelsCharged == 0) return result.Freeze();
        if (submission.ChargedNothing)
        {
            result["free"] = true;
            return result.Freeze();
        }
        var charged = new GameMcpArrayBuilder();
        var charges = submission.Charges;
        for (var index = 0; index < charges.Length; index++)
        {
            var charge = charges[index];
            charged.Add(new GameMcpObjectBuilder
            {
                ["resourceId"] = charge.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(charge.Amount),
            });
        }
        result["charged"] = charged;
        result["chargedLevels"] = submission.LevelsCharged;
        return result.Freeze();
    }
}
#endif
