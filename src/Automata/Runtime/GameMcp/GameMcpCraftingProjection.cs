#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingProjection
{
    /// <summary>
    /// Queues <c>amount</c> crafts of the recipe, or with <c>max</c> what the screen's Mx press
    /// would, through the one craft GameAction and answers with how many crafts its own sentinel
    /// verified. A refusal after crafts landed keeps them and names why the rest stopped; a
    /// submission that faulted answers as the fault.
    /// </summary>
    internal static GameMcpCommandResult Press(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration,
        bool max,
        Func<int, CraftingPlayerSubmission> press)
    {
        var asked = new BigDouble(command.Amount, 0);
        var landed = BigDouble.Zero;
        var completed = false;
        ServiceActionResult verified = default;
        string? remainder = null;
        while (max ? landed <= BigDouble.Zero : landed < asked)
        {
            var submission = press(command.Amount - (int)landed.ToDouble());
            if (!submission.Verified)
            {
                if (landed <= BigDouble.Zero || submission.CallOutcome.MutationAttempts > 0)
                {
                    var stopped = CraftingPlayerActionResultMapper.Map(in submission);
                    return GameMcpCommandResult.FromAction(
                        in stopped,
                        command.Kind,
                        lifecycle,
                        configurationGeneration,
                        landed <= BigDouble.Zero
                            ? submission.Reason
                            : GameMcpNumberFormatter.Format(landed) + " of " +
                              GameMcpNumberFormatter.Format(asked) + " asked went through " +
                              "before this craft. " + submission.Reason,
                        Project(in submission));
                }
                remainder = submission.Reason;
                break;
            }
            verified = CraftingPlayerActionResultMapper.Map(in submission);
            completed = submission.Postcondition is
                CraftingPlayerPostcondition.DirectEffectAdvanced or
                CraftingPlayerPostcondition.InstantCompleted;
            landed += submission.Crafted;
        }
        return GameMcpCommandResult.FromAction(
            in verified,
            command.Kind,
            lifecycle,
            configurationGeneration,
            details: new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                [completed ? "completed" : "queued"] = remainder is null
                    ? (object)new GameMcpDomainValue(landed)
                    : GameMcpNumberFormatter.Format(landed) + " of " +
                      GameMcpNumberFormatter.Format(asked) + " asked; the rest stopped: " +
                      remainder,
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
