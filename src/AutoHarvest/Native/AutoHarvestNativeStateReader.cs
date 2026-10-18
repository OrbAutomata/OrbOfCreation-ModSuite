using System;
using System.Collections;
using System.Reflection;
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

        var contract = resolved.Contract;
        var plotResolution = _registryResolver.Resolve(plotUuid, contract.Types.Plot);
        var actionResolution = _registryResolver.Resolve(actionUuid, contract.Types.Action);
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
                contract,
                resolved.Shared,
                refreshed,
                refreshed,
                resolved.Treasure)
            : new ResolvedAutoHarvestPair(
                contract,
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
        try
        {
            if (!InvokeBool(resolved.Contract.PlotIsVisible, resolved.Target.Plot))
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
            if (!InvokeBool(resolved.Contract.InstanceIsVisible, prototype))
                return AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused;
        }

        try
        {
            if (!InvokeBool(resolved.Contract.InstanceHasEnoughForOneInstance, prototype))
                return AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused;
        }

        try
        {
            if (InvokeInt(resolved.Contract.InstanceGetMaximumRemInstances, prototype) <= 0)
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
        var contract = resolved.Contract;
        var binding = resolved.Target;
        if (Invoke(contract.PlotGetActionInstances, binding.Plot, Array.Empty<object>()) is not IList instances)
            return null;
        object? match = null;
        foreach (var instance in instances)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (instance is null || instance.GetType() != contract.Types.Instance) return null;
            var plot = Invoke(contract.InstanceGetElement, instance, Array.Empty<object>());
            var action = Invoke(contract.InstanceGetAction, instance, Array.Empty<object>());
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
        if (plot.GetType() != resolved.Contract.Types.Plot ||
            action.GetType() != resolved.Contract.Types.Action)
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
            TryRead(resolved.Contract.PlotStableId, plot, out var plotUuid) ? plotUuid : default,
            TryRead(resolved.Contract.ActionStableId, action, out var actionUuid) ? actionUuid : default,
            exactFruit,
            exactTreasure,
            supportedActionReference);
    }

#if SERVICE_CYCLE_PROFILE
    private object? GetValue(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.GetValue(field, owner, _profileOperations);

    private object? Invoke(MethodInfo method, object owner, object[] arguments) =>
        AutoHarvestReflectionAccess.Invoke(method, owner, arguments, _profileOperations);

    private bool InvokeBool(MethodInfo method, object owner) =>
        AutoHarvestReflectionAccess.InvokeBool(method, owner, _profileOperations);

    private int InvokeInt(MethodInfo method, object owner, params object[] arguments) =>
        AutoHarvestReflectionAccess.InvokeInt(method, owner, _profileOperations, arguments);

    private bool TryRead(
        AutoHarvestStableIdAccessor accessor,
        object value,
        out Guid identity) => accessor.TryRead(value, out identity, _profileOperations);
#else
    private static object? GetValue(FieldInfo field, object owner) => field.GetValue(owner);

    private static object? Invoke(MethodInfo method, object owner, object[] arguments) =>
        method.Invoke(owner, arguments);

    private static bool InvokeBool(MethodInfo method, object owner) =>
        (bool)(method.Invoke(owner, Array.Empty<object>()) ??
            throw new InvalidOperationException($"{method.DeclaringType?.FullName}.{method.Name} returned null"));

    private static int InvokeInt(MethodInfo method, object owner, params object[] arguments) =>
        (int)(method.Invoke(owner, arguments) ??
            throw new InvalidOperationException($"{method.DeclaringType?.FullName}.{method.Name} returned null"));

    private static bool TryRead(
        AutoHarvestStableIdAccessor accessor,
        object value,
        out Guid identity) => accessor.TryRead(value, out identity);
#endif
}
