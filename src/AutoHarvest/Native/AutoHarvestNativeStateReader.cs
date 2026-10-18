using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal sealed partial class AutoHarvestNativeStateReader : IAutoHarvestSubmissionStatePort
{
    private readonly TypedRegistryResolver? _registryResolver;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;

    internal AutoHarvestNativeStateReader(AutomataProfileOperations profileOperations) =>
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));

    internal AutoHarvestNativeStateReader(
        TypedRegistryResolver registryResolver,
        AutomataProfileOperations profileOperations)
        : this(profileOperations) =>
        _registryResolver = registryResolver ?? throw new ArgumentNullException(nameof(registryResolver));
#else
    internal AutoHarvestNativeStateReader()
    {
    }

    internal AutoHarvestNativeStateReader(TypedRegistryResolver registryResolver) =>
        _registryResolver = registryResolver ?? throw new ArgumentNullException(nameof(registryResolver));
#endif

    public bool TryResolveCurrentPair(
        in ResolvedAutoHarvestPair resolved,
        out ResolvedAutoHarvestPair current)
    {
        current = default;
        if (_registryResolver is null ||
            !Guid.TryParse(resolved.Target.PlotUuid, out var plotUuid) ||
            !Guid.TryParse(resolved.Target.ActionUuid, out var actionUuid))
            return false;

        var bindings = resolved.Bindings;
        var plotResolution = _registryResolver.Resolve(plotUuid, bindings.PlotType);
        var actionResolution = _registryResolver.Resolve(actionUuid, bindings.ActionType);
#if SERVICE_CYCLE_PROFILE
        if (plotResolution.IsResolved) _profileOperations.AddStableIdRead();
        if (actionResolution.IsResolved) _profileOperations.AddStableIdRead();
#endif
        if (!plotResolution.IsResolved ||
            !actionResolution.IsResolved ||
            plotResolution.LifecycleGeneration != resolved.LifecycleGeneration ||
            actionResolution.LifecycleGeneration != resolved.LifecycleGeneration)
            return false;

        var target = resolved.Target;
        var refreshed = new AutoHarvestPairBinding(
            target.Pair,
            plotResolution.Value!,
            actionResolution.Value!,
            target.PlotUuid,
            target.ActionUuid,
            target.RewardPool,
            plotResolution,
            actionResolution,
            target.RewardResolution);
        current = target.Pair == AutoHarvestPair.FruitTree
            ? new ResolvedAutoHarvestPair(
                bindings,
                resolved.Shared,
                refreshed,
                refreshed,
                resolved.Treasure)
            : new ResolvedAutoHarvestPair(
                bindings,
                resolved.Shared,
                refreshed,
                resolved.Fruit,
                refreshed);
        return true;
    }

    public ServiceActionResultCode ValidateClickAdmission(
        in ResolvedAutoHarvestPair resolved,
        out object? prototype)
    {
        prototype = null;
        var bindings = resolved.Bindings;
        try
        {
            if (!bindings.PlotIsVisible(resolved.Target.Plot))
                return AutoHarvestActionResultCodes.NativePlotVisibilityRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativePlotVisibilityRefused;
        }

        try
        {
            prototype = ReadPrototype(resolved);
            if (prototype is null)
                return AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused;
        }

        try
        {
            if (!bindings.InstanceIsVisible(prototype))
                return AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused;
        }

        try
        {
            if (!bindings.InstanceHasEnoughForOne(prototype))
                return AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused;
        }

        try
        {
            if (bindings.InstanceMaximumRemaining(prototype) <= 0)
                return AutoHarvestActionResultCodes.NativeMaximumRemainingInstancesRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeMaximumRemainingInstancesRefused;
        }

        return CommonActionResultCodes.Committed;
    }

    /// <summary>
    /// Resolves the live offered instance the action boundary submits into.
    /// </summary>
    /// <remarks>
    /// The exact membership/count result is one of the click-time gates. Returning null is a named
    /// ordinary refusal at the caller, never permission to fall back to a planned instance.
    /// </remarks>
    public object? ReadPrototype(in ResolvedAutoHarvestPair resolved)
    {
        var bindings = resolved.Bindings;
        var binding = resolved.Target;
        var instances = bindings.PlotActionInstances(binding.Plot);
        if (instances is null) return null;
        object? match = null;
        foreach (var instance in instances)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (instance is null || instance.GetType() != bindings.InstanceType) return null;
            var plot = bindings.InstancePlot(instance);
            var action = bindings.InstanceAction(instance);
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return null;
            var expected = binding.Pair == AutoHarvestPair.FruitTree
                ? AutoHarvestObservedPair.FruitTree
                : AutoHarvestObservedPair.TreasureTree;
            if (observed != expected) continue;
            if (match is not null) return null;
            match = instance;
        }
        return match;
    }

    private AutoHarvestObservedPair ClassifyPair(
        in ResolvedAutoHarvestPair resolved,
        object? plot,
        object? action)
    {
        if (plot is null || action is null) return AutoHarvestObservedPair.Contradictory;
        var bindings = resolved.Bindings;
        if (plot.GetType() != bindings.PlotType ||
            action.GetType() != bindings.ActionType)
            return AutoHarvestObservedPair.Contradictory;
        var exactFruit = resolved.Fruit is not null &&
            ReferenceEquals(plot, resolved.Fruit.Plot) &&
            ReferenceEquals(action, resolved.Fruit.Action);
        var exactTreasure = resolved.Treasure is not null &&
            ReferenceEquals(plot, resolved.Treasure.Plot) &&
            ReferenceEquals(action, resolved.Treasure.Action);
        var supportedActionReference =
            resolved.Fruit is not null && ReferenceEquals(action, resolved.Fruit.Action) ||
            resolved.Treasure is not null && ReferenceEquals(action, resolved.Treasure.Action);
        return AutoHarvestIdentityPolicy.Classify(
            ReadUuid(bindings.PlotUuid, plot),
            ReadUuid(bindings.ActionUuid, action),
            exactFruit,
            exactTreasure,
            supportedActionReference);
    }

    private Guid ReadUuid(Func<object, Guid> read, object value)
    {
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddStableIdRead();
#endif
        return read(value);
    }
}
