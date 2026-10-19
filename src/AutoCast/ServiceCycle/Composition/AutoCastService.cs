using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Assembles the typed Auto Cast service from its action port and the manual pause its start gate
/// consults. Per-cycle debugging value flows through the always-on decision journal via the worker's
/// state projection.
/// </summary>
internal static class AutoCastService
{
    internal static IAutomataServiceDefinition<
        AutoCastCycleState,
        AutoCastCycleAction> Define(
            IAutoCastCycleActionPort actions,
            AutoCastManualPauseState manualPause)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (manualPause is null) throw new ArgumentNullException(nameof(manualPause));

        var metadata = new AutomataServiceMetadata(
            AutoCastServicePolicies.ServiceId,
            AutoCastServicePolicies.DefaultWakePolicy,
            AutoCastServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<
            AutoCastCycleState,
            AutoCastCycleAction>(
                in metadata,
                createWorker: static () => new AutoCastWorkerDefinition(),
                shouldStart: ShouldStart,
                describeAction: static (in AutoCastCycleAction action) =>
                    ServiceActionJournalAttribution.Native(
                        action.SpellRecipeId,
                        ServiceActionNativeTypeId.SpellRecipeSO,
                        (byte)action.Kind),
                execute: actions.TryExecute);

        // Auto Cast's own configuration, plus the one piece of runtime state that is the suite's
        // rather than the game's: a manual cast silences the rotation, and the cheapest place to
        // honour that is by not opening a cycle at all. Whether the shared world is fresh enough to
        // act on is not a feature question — the runtime gates a service bound to a world generation
        // source before this is ever called.
        ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context)
        {
            var operational = AutoCastConfigurationPolicy.IsOperational(config);
            actions.ObserveOperational(operational);
            if (!operational)
            {
                return ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.OnPublication);
            }

            manualPause.Refresh(context.Now, config);
            var remaining = manualPause.Remaining(context.Now);
            return remaining > MonotonicDuration.Zero
                ? ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, WakePolicy.AfterDecision(remaining))
                : ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        }
    }
}
