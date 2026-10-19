using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeService
{
    internal static IAutomataServiceDefinition<AutoScribeCycleState, AutoScribeCycleAction> Define(
        AutoScribeIdentityProfile profile,
        IAutoScribeCycleActionPort actions,
        Func<bool> ownsActionFamily)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (ownsActionFamily is null) throw new ArgumentNullException(nameof(ownsActionFamily));
        var metadata = new AutomataServiceMetadata(
            AutoScribeServicePolicies.ServiceId,
            WakePolicy.OnPublication,
            new ServiceFaultRecoveryPolicy(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        return AutomataService.Define<AutoScribeCycleState, AutoScribeCycleAction>(
            in metadata,
            () => new AutoScribeWorkerDefinition(profile),
            (in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
                ShouldStart(in config, in context, ownsActionFamily),
            static (in AutoScribeCycleAction action) =>
                ServiceActionJournalAttribution.Native(
                    action.RecipeId,
                    ServiceActionNativeTypeId.CraftingRecipeSO),
            actions.TryExecute);
    }

    internal static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context,
        Func<bool> ownsActionFamily)
    {
        if (!AutoScribeConfigurationPolicy.IsOperational(config))
            return ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);
        if (!AutomationCycleGuard.Owns(ownsActionFamily))
            return ServiceStartDecision.Wait(
                AutoScribeServiceDecisionCodes.ActionFamilyUnavailable,
                WakePolicy.OnPublication);
        return ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    }
}

internal static class AutoScribeServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-scribe");

    internal static ServiceRetryTarget RetryTarget(Guid recipeId) =>
        new(recipeId, ServiceActionNativeTypeId.CraftingRecipeSO);

    internal static bool PaidWithoutWork(ServiceActionResultCode code) =>
        code == AutoScribeActionResultCodes.PostPaymentFault ||
        code == AutoScribeActionResultCodes.VerificationFailed;
}

internal static class AutoScribeServiceDecisionCodes
{
    internal static ServiceDecisionCode ActionFamilyUnavailable => new(4220);
}
