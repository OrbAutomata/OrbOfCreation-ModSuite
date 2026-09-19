#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingProjection
{
    internal static GameMcpValue Project(in CraftingPlayerSubmission submission)
    {
        if (submission.Verified)
        {
            var result = new JObject();
            if (submission.Postcondition is CraftingPlayerPostcondition.DirectEffectAdvanced or
                CraftingPlayerPostcondition.InstantCompleted)
                result["completed"] = true;
            else if (submission.Postcondition is CraftingPlayerPostcondition.QueueAdmitted or
                     CraftingPlayerPostcondition.InstanceQuantityIncreased)
                result["started"] = true;
            return result.Freeze();
        }
        if (submission.CallOutcome.MutationAttempts == 0) return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested craft completion",
        }.Freeze();
    }

    internal static bool ProvedCompletion(GameMcpValue? details) => Observed(details, "completed");

    /// <summary>Whether the action itself saw the craft enter the game's crafting queue.</summary>
    internal static bool ProvedQueueEntry(GameMcpValue? details) => Observed(details, "started");

    private static bool Observed(GameMcpValue? details, string name)
    {
        if (details is not GameMcpObject result) return false;
        for (var index = 0; index < result.Properties.Count; index++)
        {
            var property = result.Properties[index];
            if (property.Name == name && property.Value is GameMcpScalar scalar &&
                scalar.Value is bool observed)
                return observed;
        }
        return false;
    }

    internal static GameMcpValue Project(
        in CraftingInstanceLifecycleSubmission submission)
    {
        if (submission.Verified || submission.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        var failure = new JObject
        {
            ["missingOutcome"] = "requested crafting-instance transition",
        };
        if (submission.SideEffect.Observed)
        {
            // The action observes GetAutomationQuantity(), which counts repetitions rather than
            // the badge's amount, so it is published under that name.
            failure["observed"] = new JObject
            {
                ["repetitions"] = new JObject
                {
                    ["before"] = submission.SideEffect.AutomationBefore,
                    ["after"] = submission.SideEffect.AutomationAfter,
                },
            };
        }
        return failure.Freeze();
    }
}
#endif
