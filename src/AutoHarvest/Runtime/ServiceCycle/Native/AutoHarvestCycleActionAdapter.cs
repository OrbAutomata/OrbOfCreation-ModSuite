using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Configuration;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestCycleActionAdapter : IAutoHarvestCycleActionPort
{
    private readonly IAutoHarvestBindingPort _bindings;
    private readonly IAutoHarvestMutationPort _mutation;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly AutomationCycleGuard<AutoHarvestCycleAction> _guard;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
    private readonly IAutoHarvestProfileBindingObservation _profileBindings;
#endif

    public AutoHarvestCycleActionAdapter(
        IAutoHarvestBindingPort bindings,
        IAutoHarvestMutationPort mutation,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        Action<string> warn
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations,
        IAutoHarvestProfileBindingObservation profileBindings
#endif
        )
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _guard = new AutomationCycleGuard<AutoHarvestCycleAction>(
            "Auto Harvest",
            static action => "Auto Harvest failed to collect " +
                EntityIdentityFormatter.Format(AutoHarvestPairSpecification.For(action.Pair).Plot.Uuid),
            warn ?? throw new ArgumentNullException(nameof(warn)));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
        _profileBindings = profileBindings ?? throw new ArgumentNullException(nameof(profileBindings));
#endif
    }

    public ServiceActionResult TryExecute(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => AutoHarvestServicePolicies.BackingOffThePair(
            TryExecuteCore(in action, in config, in context, requireAutomationPolicy: true));

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: false);
#endif

    private ServiceActionResult TryExecuteCore(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context,
        bool requireAutomationPolicy)
    {
        if (requireAutomationPolicy &&
            (!AutoHarvestConfigurationPolicy.IsOperational(config) ||
             !AutoHarvestConfigurationPolicy.IsSelected(config, action.Pair)))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!_guard.Owns(_ownsActionFamily) || !_tryCaptureMutationPermit())
            return ServiceActionResult.Rejected(AutoHarvestActionResultCodes.ActionFamilyUnavailable);

        ResolvedAutoHarvestPair resolved;
#if SERVICE_CYCLE_PROFILE
        var temperature = _profileBindings.PrepareTemperature();
        var bindingStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
            in context,
            temperature);
#endif
        try
        {
            var pairs = _bindings.ResolvePairSet();
            var resolution = pairs.For(action.Pair);
            if (!resolution.Succeeded)
                return ServiceActionResult.Faulted(FailureCode(resolution.Failure), resolution.Failure.Reason);
            resolved = resolution.Pair;
            var lifecycle = resolved.LifecycleGeneration;
            if (!_guard.TryAdmitEpoch(
                    () => lifecycle,
                    unchecked((long)context.Cycle.Lifecycle.Value),
                    in action,
                    out var refusal))
                return refusal;
#if SERVICE_CYCLE_PROFILE
            if (_profileBindings.TryComplete(temperature)) bindingStage.Complete();
#endif
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return _guard.Fault(ex, in action).ToResult();
        }
#if SERVICE_CYCLE_PROFILE
        finally
        {
            bindingStage.Abandon();
        }
#endif

        var result = _mutation.Submit(
            resolved,
            action.Facts,
            action.Safety
#if SERVICE_CYCLE_PROFILE
            , in context
#endif
            );
        return _guard.Finish(in result, in action);
    }

    /// <summary>
    /// A proven mismatch is named by how far it reaches, which is what the worker holds for the rest
    /// of the lifecycle; any other failure to resolve is an adapter fault and is retried.
    /// </summary>
    private static ServiceActionResultCode FailureCode(in AutoHarvestNativeFailure failure)
    {
        if (failure.Kind != AutoHarvestRuntimeFailureKind.Contract) return CommonActionResultCodes.AdapterFault;
        return failure.Scope == AutoHarvestRuntimeFailureScope.Feature
            ? AutoHarvestActionResultCodes.FeatureContractUnavailable
            : AutoHarvestActionResultCodes.PairContractUnavailable;
    }
}
