using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutomataWorldCollectionPolicies
{
    /// <summary>
    /// The fastest the world is re-read, and how often a slower pace checks whether it is due.
    /// </summary>
    /// <remarks>
    /// This is the suite's one planning cadence: every ordinary service evaluates after each
    /// publication, so the pace the player picks is the pace of all automation. A live capture costs
    /// tens of milliseconds of the game's own frame, which is the whole reason a slower pace exists;
    /// service-specific cadence controls would create competing clocks over the same immutable
    /// reading.
    /// </remarks>
    internal static readonly MonotonicDuration FastestInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));

    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));

    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(5));

    /// <summary>How long to wait before retrying when nothing could be read at all.</summary>
    internal static readonly MonotonicDuration UnavailableRetry =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(5));

    internal static ServiceId ServiceId => new("orbautomata.world-collection");

    internal static WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(FastestInterval);

    internal static MonotonicDuration Interval(AutomationPace pace) => pace switch
    {
        AutomationPace.Fastest => FastestInterval,
        AutomationPace.Fast => MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(500)),
        AutomationPace.Balanced => MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)),
        AutomationPace.Relaxed => MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(5)),
        AutomationPace.CpuSaver => MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30)),
        _ => throw new ArgumentOutOfRangeException(nameof(pace), pace, "Unknown automation pace."),
    };

    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}

/// <summary>
/// The shared world pass: the capture reads the game, the worker derives and publishes.
/// </summary>
/// <remarks>
/// <para>
/// Collection is infrastructure rather than a feature, and it would have been simpler to run it from
/// the frame pump directly. Registering it as a service is what keeps it inside budget accounting,
/// tracing, health projection, and emergency stop — the machinery that already exists to answer "what
/// is this costing and is it healthy", which is exactly what will be asked of the one pass every
/// other service depends on.
/// </para>
/// <para>
/// It has no configuration gate. Every other service decides whether it is switched on; this one runs
/// whenever the runtime does, because a consumer that finds the world empty cannot tell "collection
/// is off" from "the save has nothing in it".
/// </para>
/// </remarks>
internal static class AutomataWorldCollectionService
{
    internal static IServiceCycleSourceDefinition<
        AutomataWorldCollectionState,
        AutomataWorldCollectionAction> Define(
        IAutomataWorldCapturePort capture,
        IServiceWorldPublicationSink<GameWorldState> publish,
        AutomataWorldDemand demand)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        if (demand is null) throw new ArgumentNullException(nameof(demand));

        var metadata = new AutomataServiceMetadata(
            AutomataWorldCollectionPolicies.ServiceId,
            AutomataWorldCollectionPolicies.DefaultWakePolicy,
            AutomataWorldCollectionPolicies.FaultRecoveryPolicy);

        return AutomataService.DefineSource<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction>(
                in metadata,
                createWorker: static () => new AutomataWorldCollectionWorker(),
                shouldStart: ShouldStart,
                capture: Capture,
                describeAction: static (in AutomataWorldCollectionAction action) =>
                    ServiceActionJournalAttribution.Publication,
                execute: Execute);

        ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            IsDue(config.General.Pace, in context, demand.IsRequested)
                ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
                : ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.AfterDecision(AutomataWorldCollectionPolicies.FastestInterval));

        ServiceCaptureResult Capture(
            GameWorldCycleFrame frame,
            in SuiteRuntimeConfiguration config,
            in ServiceCaptureContext context)
        {
            if (!capture.IsAvailable)
            {
                return ServiceCaptureResult.Unavailable(
                    CommonServiceDecisionCodes.CaptureUnavailable,
                    WakePolicy.AfterDecision(AutomataWorldCollectionPolicies.UnavailableRetry));
            }

            frame.CollectedAt = context.CapturedAt;
            frame.CollectedAtUtcTicks = DateTime.UtcNow.Ticks;
            capture.Collect(frame);
            return ServiceCaptureResult.Captured(CommonServiceDecisionCodes.Captured);
        }

        // The one point at which the shared world becomes live, and it is on the main thread. Actions
        // dispatch before any service's ShouldStart runs, so a snapshot acquired this frame is
        // visible to every consumer this same frame, and no consumer can see it change mid-decision.
        ServiceActionResult Execute(
            in AutomataWorldCollectionAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.CommittedPublication(
                CommonActionResultCodes.Committed,
                ServicePublicationEvidence.World(publish.Publish(action.World, action.Generation)));
    }

    /// <summary>
    /// Whether the next reading is due under the player's pace.
    /// </summary>
    /// <remarks>
    /// The pace slows idle reading only. A new lifecycle has published nothing yet, and a caller
    /// verifying a press it just made needs the world that shows it, so both read at the fastest
    /// cadence whatever the pace. A slower pace answers "not yet" at that same cadence, which is how
    /// a request made mid-wait is seen within a quarter second instead of at the end of the wait.
    /// </remarks>
    internal static bool IsDue(
        AutomationPace pace,
        in ServiceCycleStartContext context,
        bool demanded)
    {
        if (demanded || !context.PreviousReceipt.IsPresent) return true;
        var elapsed = context.Now.Ticks - context.PreviousReceipt.CompletedAt.Ticks;
        return elapsed >= AutomataWorldCollectionPolicies.Interval(pace).Ticks;
    }
}
