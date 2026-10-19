using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoConceptService
{
    internal static IAutomataServiceDefinition<AutoConceptCycleState, AutoConceptCycleAction> Define(
        IAutoConceptCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        var metadata = new AutomataServiceMetadata(
            AutoConceptServicePolicies.ServiceId,
            AutoConceptServicePolicies.DefaultWakePolicy,
            AutoConceptServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoConceptCycleState, AutoConceptCycleAction>(
            in metadata,
            static () => new AutoConceptWorkerDefinition(),
            ShouldStart,
            static (in AutoConceptCycleAction action) =>
            {
                var target = AutoConceptServicePolicies.RetryTarget(action.Kind, action.RecipeId);
                return ServiceActionJournalAttribution.Native(target.Id, target.NativeType, target.ActionKind);
            },
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoConceptConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);
}

internal static class AutoConceptServicePolicies
{
    public static ServiceId ServiceId => new("orbautomata.auto-concept");
    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));

    private const byte AddKind = 0;
    private const byte RemoveKind = 1;

    /// <summary>
    /// A Concept's add and its remove back off apart, so a failing one never holds the other back.
    /// A rotation and an owned removal are the same native remove.
    /// </summary>
    internal static ServiceRetryTarget RetryTarget(AutoConceptActionKind kind, Guid recipeId) =>
        new(recipeId, ServiceActionNativeTypeId.AlchemyRecipeSO, kind == AutoConceptActionKind.Add ? AddKind : RemoveKind);

    internal static bool IsRemove(ServiceRetryTarget target) => target.ActionKind == RemoveKind;

    /// <summary>A retry target in a sentence: which Concept, and what the service was doing with it.</summary>
    internal static string Describe(ServiceRetryTarget target) =>
        (IsRemove(target) ? "removing " : "adding ") + EntityIdentityFormatter.Format(target.Id);
}
