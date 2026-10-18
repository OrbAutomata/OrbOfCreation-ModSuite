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
    private readonly IAutoHarvestGatePort _gates;
    private readonly IAutoHarvestContractCircuit _contractCircuit;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly AutomationCycleGuard<AutoHarvestCycleAction> _guard = new(
        "Auto Harvest",
        static action => "Auto Harvest failed to collect " +
            EntityIdentityFormatter.Format(AutoHarvestPairSpecification.For(action.Pair).Plot.Uuid),
        static message => Plugin.Log?.LogAutomataWarning(message));
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
    private readonly IAutoHarvestProfileBindingObservation _profileBindings;
#endif

    public AutoHarvestCycleActionAdapter(
        IAutoHarvestBindingPort bindings,
        IAutoHarvestMutationPort mutation,
        IAutoHarvestGatePort gates,
        IAutoHarvestContractCircuit contractCircuit,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations,
        IAutoHarvestProfileBindingObservation profileBindings
#endif
        )
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _contractCircuit = contractCircuit ?? throw new ArgumentNullException(nameof(contractCircuit));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
        _profileBindings = profileBindings ?? throw new ArgumentNullException(nameof(profileBindings));
#endif
    }

    public ServiceActionResult TryExecute(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: true);

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

        if (_gates.IsQuarantined(action.Pair))
            return ServiceActionResult.Faulted(AutoHarvestActionResultCodes.PairFaulted);
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
                return ServiceActionResult.Faulted(ContractFailureCode(action.Pair));
            resolved = resolution.Pair;
            if (!AutoHarvestNativeLifecycle.Matches(
                    resolved.LifecycleGeneration,
                    context.Cycle.Lifecycle))
                return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
            _gates.ObserveResolvedPairs(pairs);
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
        if (result.Code == AutoHarvestActionResultCodes.PairFaulted) _gates.Quarantine(action.Pair);
        return _guard.Finish(in result, in action);
    }

    /// <summary>
    /// How far the contract failure that just refused this pair reaches.
    /// </summary>
    /// <remarks>
    /// The resolver trips the circuit as it fails, so the scope is readable immediately afterwards. A
    /// resolution that failed without tripping it failed for some other reason and stays an
    /// unattributed adapter fault rather than being reported as a contract the build does not have.
    /// </remarks>
    private ServiceActionResultCode ContractFailureCode(AutoHarvestPair pair)
    {
        var failure = _contractCircuit.FailureFor(pair);
        if (!failure.IsValid) return CommonActionResultCodes.AdapterFault;
        return failure.Scope == AutoHarvestRuntimeFailureScope.Feature
            ? AutoHarvestActionResultCodes.FeatureContractUnavailable
            : AutoHarvestActionResultCodes.PairContractUnavailable;
    }
}
