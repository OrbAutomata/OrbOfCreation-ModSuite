using System;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed partial class AutoHarvestBindingResolver :
    IAutoHarvestBindingPort
#if SERVICE_CYCLE_PROFILE
    , IAutoHarvestProfileBindingObservation
#endif
{
    private readonly TypedRegistryResolver _registryResolver;
    private readonly IAutoHarvestContractCircuit _contractCircuit;
    private readonly AutoHarvestNativeBindings? _bindings;
    private readonly string _bindReason;
    private AutoHarvestSharedBinding? _shared;
    private AutoHarvestPairBinding? _fruit;
    private AutoHarvestPairBinding? _treasure;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;
    private AutoHarvestProfileTemperatureTracker _profileTemperature;
#endif

    public AutoHarvestBindingResolver(
        TypedRegistryResolver registryResolver,
        IAutoHarvestContractCircuit contractCircuit
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
#endif
        , Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _registryResolver = registryResolver ?? throw new ArgumentNullException(nameof(registryResolver));
        _contractCircuit = contractCircuit ?? throw new ArgumentNullException(nameof(contractCircuit));
#if SERVICE_CYCLE_PROFILE
        _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
#endif
        if (!AutoHarvestNativeBindings.TryCreate(out _bindings, out _bindReason, resolveType, includeContract))
            _contractCircuit.Block(AutoHarvestPair.FruitTree, AutoHarvestRuntimeFailureScope.Feature);
    }

#if SERVICE_CYCLE_PROFILE
    ServiceCycleProfileTemperature IAutoHarvestProfileBindingObservation.CurrentTemperature =>
        _profileTemperature.Current;

    ServiceCycleProfileTemperature IAutoHarvestProfileBindingObservation.PrepareTemperature()
    {
        if (_profileTemperature.Current == ServiceCycleProfileTemperature.Warm &&
            !HasCurrentProfileBindingSet())
        {
            _profileTemperature.ObserveUnexpectedDrift();
        }
        return _profileTemperature.Current;
    }

    bool IAutoHarvestProfileBindingObservation.TryComplete(
        ServiceCycleProfileTemperature observed) =>
        _profileTemperature.TryComplete(observed);
#endif

    public AutoHarvestResolvedPairSet ResolvePairSet()
    {
        if (_bindings is null)
        {
            return FailedPairSet(AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Contract,
                AutoHarvestRuntimeFailureScope.Feature,
                _bindReason));
        }

        var fruitBlocked = _contractCircuit.FailureFor(AutoHarvestPair.FruitTree);
        var treasureBlocked = _contractCircuit.FailureFor(AutoHarvestPair.TreasureTree);
        if (IsFeatureFailure(fruitBlocked)) return FailedPairSet(fruitBlocked);
        if (IsFeatureFailure(treasureBlocked)) return FailedPairSet(treasureBlocked);
        if (fruitBlocked.IsValid && treasureBlocked.IsValid)
            return FailedPairSet(fruitBlocked, treasureBlocked);
        if (!TryResolveShared(out var sharedFailure))
            return FailedPairSet(sharedFailure);

        var fruitSucceeded = TryResolvePair(
            AutoHarvestPair.FruitTree,
            fruitBlocked,
            out var fruitFailure);
        var treasureSucceeded = TryResolvePair(
            AutoHarvestPair.TreasureTree,
            treasureBlocked,
            out var treasureFailure);

        if (fruitSucceeded && _fruit is null ||
            treasureSucceeded && _treasure is null ||
            !AutoHarvestBindingCoherence.IsCurrent(
                _registryResolver,
                _shared!,
                fruitSucceeded ? _fruit : null,
                treasureSucceeded ? _treasure : null))
        {
#if SERVICE_CYCLE_PROFILE
            _profileTemperature.ObserveUnexpectedDrift();
#endif
            _fruit = null;
            _treasure = null;
            return FailedPairSet(AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Retryable,
                AutoHarvestRuntimeFailureScope.Pair));
        }

        return AutoHarvestResolvedPairSet.Create(
            _bindings,
            _shared!,
            fruitSucceeded ? _fruit : null,
            fruitFailure,
            treasureSucceeded ? _treasure : null,
            treasureFailure);
    }

    public void InvalidateLifecycle()
    {
#if SERVICE_CYCLE_PROFILE
        _profileTemperature.InvalidateLifecycle();
#endif
        _fruit = null;
        _treasure = null;
        _shared = null;
    }

    private static AutoHarvestResolvedPairSet FailedPairSet(
        in AutoHarvestNativeFailure failure)
    {
        var failed = AutoHarvestPairResolution.Failed(failure);
        return new AutoHarvestResolvedPairSet(failed, failed);
    }

    private static AutoHarvestResolvedPairSet FailedPairSet(
        in AutoHarvestNativeFailure fruitFailure,
        in AutoHarvestNativeFailure treasureFailure) =>
        new(
            AutoHarvestPairResolution.Failed(fruitFailure),
            AutoHarvestPairResolution.Failed(treasureFailure));

    private static bool IsFeatureFailure(in AutoHarvestNativeFailure failure) =>
        failure.IsValid &&
        failure.Scope == AutoHarvestRuntimeFailureScope.Feature;

#if SERVICE_CYCLE_PROFILE
    private bool HasCurrentProfileBindingSet() =>
        _shared is not null &&
        _fruit is not null &&
        _treasure is not null &&
        AutoHarvestBindingCoherence.IsCurrent(
            _registryResolver,
            _shared,
            _fruit,
            _treasure);
#endif
}
