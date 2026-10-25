using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class QualityOfLifeService
{
    internal static ServiceId ServiceId => new("orbautomata.quality-of-life");
    internal static ServiceRetryTarget RetryTarget(Guid id) => new(id, ServiceActionNativeTypeId.PassiveAbilitySO);

    internal static IAutomataServiceDefinition<QualityOfLifeCycleState, QualityOfLifeCycleAction> Define(
        PassiveNotificationMuteGameAction gameAction, QualityOfLifeFeatureDependencies dependencies)
    {
        var guard = new AutomationCycleGuard<QualityOfLifeCycleAction>("Quality of Life",
            static action => "Could not mute " + EntityIdentityFormatter.Format(action.PassiveId), dependencies.Warn);
        var metadata = new AutomataServiceMetadata(ServiceId, WakePolicy.OnPublication,
            new ServiceFaultRecoveryPolicy(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        return AutomataService.Define<QualityOfLifeCycleState, QualityOfLifeCycleAction>(in metadata,
            static () => new QualityOfLifeWorkerDefinition(),
            (in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
                QualityOfLifePolicy.IsOperational(config) && AutomationCycleGuard.Owns(dependencies.OwnsActionFamily) &&
                gameAction.BindingsAvailable
                    ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
                    : ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, WakePolicy.OnPublication),
            static (in QualityOfLifeCycleAction action) =>
                ServiceActionJournalAttribution.Native(action.PassiveId, ServiceActionNativeTypeId.PassiveAbilitySO),
            (in QualityOfLifeCycleAction action, in SuiteRuntimeConfiguration config, in ServiceActionContext context) =>
            {
                if (!QualityOfLifePolicy.IsOperational(config))
                    return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
                if (!guard.Owns(dependencies.OwnsActionFamily))
                    return ServiceActionResult.Rejected(QualityOfLifeActionResultCodes.MutationPermitUnavailable,
                        dependencies.OwnershipFailure());
                var outcome = gameAction.Submit(action.PassiveId, action.LifecycleEpoch);
                var result = guard.Finish(in outcome, in action);
                return result.Disposition == ServiceActionDisposition.Faulted ||
                    result.Code == QualityOfLifeActionResultCodes.IdentityUnavailable ||
                    result.Code == QualityOfLifeActionResultCodes.ContractUnavailable
                    ? result.BackingOffTarget(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30)),
                        initialBackoff: MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)))
                    : result;
            });
    }
}
