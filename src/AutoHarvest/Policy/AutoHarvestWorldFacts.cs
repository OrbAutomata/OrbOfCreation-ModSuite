using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Turns a harvest pair's world rows into the facts the policy decides on.
/// </summary>
/// <remarks>
/// <para>
/// Every fact here is a property of the game's world and comes from the shared snapshot. Action
/// safety and the two queue facts are not, and are not decided here — see
/// <see cref="AutoHarvestPolicy.EvaluateSubmission"/>.
/// </para>
/// <para>
/// Nothing here reflects. That is the point — this is the whole of what Auto Harvest needs to know
/// about the game to decide, expressed as a function of an immutable snapshot.
/// </para>
/// </remarks>
internal static class AutoHarvestWorldFacts
{
    /// <summary>
    /// What one run of the action must cost the plot for the pair to be worth acting on.
    /// </summary>
    /// <remarks>
    /// A harvest action that consumed more than one of the plot per run would be spending the plot
    /// down rather than collecting from it, and Auto Harvest declines rather than guessing at a rate.
    /// The two supported pairs cost one; anything else is a build this feature was not audited
    /// against.
    /// </remarks>
    private const int ExpectedElementCost = 1;

    internal static AutoHarvestPairFacts For(GameWorldState world, Guid plotId, Guid actionId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        if (!WorldPlotActionLookup.TryFind(world.PlotActions, plotId, actionId, out var pair) ||
            !WorldLookup.TryFind(world.PlotNodes, plotId, out var plot))
        {
            return Unknown;
        }

        // The action boundary submits into the plot's one instance of this action. Without exactly
        // one there is nothing to submit into, so the pair is not decidable rather than not ready.
        var submittable = pair.Reading.InstanceCount == 1;

        // A false latch is not a native refusal. It asks the action boundary to validate the exact
        // current action once; only that fresh return can say the prerequisites are unmet.
        return new AutoHarvestPairFacts(
            AutoHarvestEvidenceState.Verified,
            Evidence(plot.Reading.Visible),
            Evidence(pair.Reading.OfferedCount == 1),
            submittable
                ? pair.Reading.PrerequisiteEvidence
                : PlotActionPrerequisiteEvidence.Unknown,
            submittable ? Evidence(IsReady(in plot, in pair)) : AutoHarvestEvidenceState.Unknown);
    }

    /// <summary>
    /// Whether the plot could start one more run of the action right now.
    /// </summary>
    internal static bool IsReady(in WorldPlotNode plot, in WorldPlotAction pair) =>
        plot.RemainingQuantity > 0 &&
        pair.ElementCostKnown &&
        pair.ElementCost == ExpectedElementCost &&
        pair.HasEnoughForOneInstance &&
        pair.MaximumRemainingInstances > 0;

    /// <summary>Whether the pair is already queued or running.</summary>
    internal static bool IsQueuedOrRunning(in AutoHarvestSubmissionState state) =>
        state.SupportedCollectCount != 0;

    /// <summary>Whether the action queue has room for one more entry.</summary>
    internal static bool HasActionSlot(in AutoHarvestSubmissionState state) =>
        state.NativeHasEmptyEntry && state.EmptyEntryCount >= 1;

    /// <summary>
    /// The facts for a pair the snapshot does not describe.
    /// </summary>
    /// <remarks>
    /// Identity is what fails, and it fails first in the policy, so the rejection names the real
    /// problem — the world has no such plot-and-action pair — rather than reporting the four facts
    /// that follow as separately unknown. Also the shape a pair takes when the service has blocked its
    /// contract: nothing about it is known, because nothing about it was asked.
    /// </remarks>
    internal static AutoHarvestPairFacts Unknown =>
        new(
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestEvidenceState.Unknown,
            PlotActionPrerequisiteEvidence.Unknown,
            AutoHarvestEvidenceState.Unknown);

    private static AutoHarvestEvidenceState Evidence(bool value) =>
        value ? AutoHarvestEvidenceState.Verified : AutoHarvestEvidenceState.Rejected;
}
