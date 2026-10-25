using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class StrategistServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.strategist");

    /// <summary>
    /// One bulletin a minute: the derivation is too heavy to repeat for every world, and lifetime
    /// gain read a minute apart is still a sustained rate. A new lifecycle starts a new runner, so a
    /// new game is judged on its first world.
    /// </summary>
    internal static MonotonicDuration Cadence => MonotonicDuration.FromTimeSpan(
        TimeSpan.FromSeconds(StrategistIncomeWindow.MinimumSeconds));

    /// <summary>How soon to look again while the world is the empty one a lifecycle leaves behind.</summary>
    internal static MonotonicDuration EmptyWorldRetry => MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    internal static WakePolicy DefaultWakePolicy => WakePolicy.AfterDecision(Cadence);

    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
}

/// <summary>One cycle's bulletin, carried to the main thread to be published.</summary>
internal readonly struct StrategistCycleAction
{
    internal StrategistCycleAction(StrategistBulletin bulletin) =>
        Bulletin = bulletin ?? throw new ArgumentNullException(nameof(bulletin));

    internal StrategistBulletin Bulletin { get; }
}

/// <summary>The window, and nothing else: a lifecycle replacement recreates it empty.</summary>
internal struct StrategistCycleState
{
    internal LifecycleGeneration Lifecycle;
    internal StrategistIncomeWindow Window;
}

/// <summary>
/// The strategist: an ordinary service that wakes once a minute.
/// The worker reads the pinned world into facts and the window, derives the bulletin, and the one
/// main-thread step publishes it.
/// </summary>
/// <remarks>
/// It has no configuration and no switch. The full judgment goes to its latest-value slot for
/// surfaces to read. Its active goal is advisory policy for consumers that opt in through their own
/// configuration; resource stances stay empty. An unchanged goal moves no strategy generation.
/// </remarks>
internal static class StrategistService
{
    internal static IAutomataServiceDefinition<StrategistCycleState, StrategistCycleAction> Define(
        IStrategistBulletinSink publish)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        var metadata = new AutomataServiceMetadata(
            StrategistServicePolicies.ServiceId,
            StrategistServicePolicies.DefaultWakePolicy,
            StrategistServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<StrategistCycleState, StrategistCycleAction>(
            in metadata,
            static () => new StrategistWorkerDefinition(),
            static (in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
                ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready),
            static (in StrategistCycleAction action) => ServiceActionJournalAttribution.Publication,
            (in StrategistCycleAction action, in SuiteRuntimeConfiguration config, in ServiceActionContext context) =>
                Publish(publish, in action));
    }

    internal static ServiceActionResult Publish(
        IStrategistBulletinSink publish,
        in StrategistCycleAction action)
    {
        var goalId = action.Bulletin.Advice.GoalId;
        var strategy = goalId == Guid.Empty
            ? SuiteStrategyDefaults.Neutral
            : new SuiteStrategy { Provenance = SuiteStrategyProvenance.Goal, ActiveGoalId = goalId };
        var sequence = publish.PublishStrategistBulletin(action.Bulletin, strategy);
        return ServiceActionResult.CommittedPublication(
            CommonActionResultCodes.Committed,
            ServicePublicationEvidence.StrategistBulletin(sequence));
    }

    /// <summary>
    /// One cycle: the window takes the world's reading, then the bulletin is derived from the facts.
    /// The empty world a lifecycle boundary leaves behind has nothing to judge.
    /// </summary>
    internal static bool TryDerive(
        GameWorldState world,
        ulong worldGeneration,
        StrategistIncomeWindow window,
        out StrategistBulletin bulletin)
    {
        bulletin = null!;
        if (world.Resources.Count == 0) return false;
        var now = StrategistFactsProjection.ClockSeconds(world);
        var required = StrategistFactsProjection.RequiredWindowSeconds(world);
        window.Observe(now, required, StrategistFactsProjection.WindowSamples(world));
        bulletin = StrategistDerivation.Derive(
            StrategistFactsProjection.Project(world, worldGeneration),
            window.Read(now, required));
        return true;
    }
}

internal sealed class StrategistWorkerDefinition :
    IServiceCycleWorkerDefinition<StrategistCycleState, StrategistCycleAction>
{
    public StrategistCycleState CreateState(LifecycleGeneration lifecycle) =>
        new() { Lifecycle = lifecycle, Window = new StrategistIncomeWindow() };

    public void ReleaseState(ref StrategistCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref StrategistCycleState state,
        ServiceActionWriter<StrategistCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Strategist state belongs to a different lifecycle.");
        if (!StrategistService.TryDerive(world, context.Identity.World.Value, state.Window, out var bulletin))
            return WakePolicy.AfterDecision(StrategistServicePolicies.EmptyWorldRetry);
        actions.Add(new StrategistCycleAction(bulletin));
        return StrategistServicePolicies.DefaultWakePolicy;
    }

    public void ProjectState(
        in StrategistCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
    }
}
