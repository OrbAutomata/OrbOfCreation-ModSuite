#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingProjection
{
    /// <summary>
    /// Presses the recipe's Craft button <c>amount</c> times through the one craft GameAction and
    /// answers with how many presses its own sentinel verified. A refusal after the first press
    /// keeps what landed and names why the rest stopped; a press that faulted answers as the fault.
    /// </summary>
    internal static GameMcpCommandResult Press(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration,
        Func<CraftingPlayerSubmission> press)
    {
        var landed = 0;
        var completed = false;
        ServiceActionResult verified = default;
        string? remainder = null;
        while (landed < command.Amount)
        {
            var submission = press();
            if (!submission.Verified)
            {
                if (landed == 0 || submission.CallOutcome.MutationAttempts > 0)
                {
                    var stopped = CraftingPlayerActionResultMapper.Map(in submission);
                    return GameMcpCommandResult.FromAction(
                        in stopped,
                        command.Kind,
                        lifecycle,
                        configurationGeneration,
                        landed == 0
                            ? submission.Reason
                            : landed + " of " + command.Amount + " asked went through before " +
                              "this press. " + submission.Reason,
                        Project(in submission));
                }
                remainder = "the rest stopped: " + submission.Reason;
                break;
            }
            verified = CraftingPlayerActionResultMapper.Map(in submission);
            completed = submission.Postcondition is
                CraftingPlayerPostcondition.DirectEffectAdvanced or
                CraftingPlayerPostcondition.InstantCompleted;
            landed++;
        }
        return GameMcpCommandResult.FromAction(
            in verified,
            command.Kind,
            lifecycle,
            configurationGeneration,
            details: new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                [completed ? "completed" : "queued"] =
                    GameMcpWorldQuery.QueuedValue(command.Amount, landed, remainder),
            }.Freeze());
    }

    internal static GameMcpValue Project(in CraftingPlayerSubmission submission)
    {
        if (submission.Verified || submission.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested craft completion",
        }.Freeze();
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
