using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
namespace OrbAutomata;
internal static class AutoLevelsService
{
    internal static ServiceId ServiceId => new("orbautomata.auto-levels");
    internal static bool IsOperational(SuiteRuntimeConfiguration config) => config.General.Enabled &&
        !config.Safety.EmergencyDisable && config.AutoLevels.Mode == AutoLevelsOperationMode.Active;
    internal static Guid Target(LevelDial dial) => dial switch
    {
        LevelDial.Output => KnownEntities.SpellOutputLevel.Uuid,
        LevelDial.Reserve => KnownEntities.ReserveLevel.Uuid,
        LevelDial.Druidry => KnownEntities.DruidryLevel.Uuid,
        LevelDial.Alchemy => KnownEntities.AlchemyOutputLevel.Uuid,
        _ => throw new ArgumentOutOfRangeException(nameof(dial)),
    };
    internal static ServiceRetryTarget RetryTarget(LevelDial dial) => new(Target(dial), ServiceActionNativeTypeId.IntVariable);
    internal static IAutomataServiceDefinition<AutoLevelsCycleState, AutoLevelsCycleAction> Define(
        SpellCompositionGameAction gameAction, AutoLevelsFeatureDependencies dependencies)
    {
        var guard = new AutomationCycleGuard<AutoLevelsCycleAction>("Auto Levels",
            static action => "Could not adjust " + action.Dial + " level", dependencies.Warn);
        var metadata = new AutomataServiceMetadata(ServiceId, WakePolicy.OnPublication,
            new ServiceFaultRecoveryPolicy(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        return AutomataService.Define<AutoLevelsCycleState, AutoLevelsCycleAction>(in metadata,
            static () => new AutoLevelsWorkerDefinition(),
            (in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
                IsOperational(config) && AutomationCycleGuard.Owns(dependencies.OwnsActionFamily) && gameAction.BindingsAvailable
                    ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
                    : ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, WakePolicy.OnPublication),
            static (in AutoLevelsCycleAction action) => ServiceActionJournalAttribution.Native(Target(action.Dial), ServiceActionNativeTypeId.IntVariable),
            (in AutoLevelsCycleAction action, in SuiteRuntimeConfiguration config, in ServiceActionContext context) =>
            {
                if (!IsOperational(config)) return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
                if (!guard.Owns(dependencies.OwnsActionFamily))
                    return ServiceActionResult.Rejected(SpellCompositionActionResultCodes.MutationPermitUnavailable, dependencies.OwnershipFailure());
                if (Math.Abs((long)action.TargetLevel - action.CurrentLevel) != 1)
                    return ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected, "Auto Levels changes one level per world reading.");
                var request = new SpellCompositionAction(action.Dial, action.TargetLevel, action.LifecycleEpoch, action.CurrentLevel, Target(action.Dial));
                var outcome = gameAction.Submit(in request).Outcome;
                var result = guard.Finish(in outcome, in action);
                return result.Disposition == ServiceActionDisposition.Faulted
                    ? result.BackingOffTarget(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30)),
                        initialBackoff: MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1))) : result;
            });
    }
}
