using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped generic discovery transaction. Admission and exact cost reads complete before
/// the mutation permit; the native UI's payment-then-discover order is preserved. Only exact target
/// identity and the requested discovered outcome gate success.
/// </summary>
internal sealed class GenericDiscoveryGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private GenericDiscoveryNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal GenericDiscoveryGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch,
            identity.Read,
            identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal GenericDiscoverySubmission Submit(in GenericDiscoveryAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.WrongThread,
                "Generic discovery is bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped generic discovery binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try
        {
            currentEpoch = _readLifecycleEpoch();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != currentEpoch)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try
        {
            if (!native.SupportedTypes.TryGetValue(action.ExpectedNativeType, out var expectedType))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.UnsupportedType,
                    "Native type " + action.ExpectedNativeType +
                    " is not in the audited generic discovery family.");
            var resolution = _registry.Resolve(action.TargetId, expectedType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? "The typed registry resolution became stale before discovery admission."
                        : resolution.Reason);
            var target = resolution.Value!;
            if (!native.DiscoverableType.IsInstanceOfType(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    "The exact registered " + action.ExpectedNativeType +
                    " does not implement IDiscoverable at the action boundary.");
            if (!GenericDiscoverySurfaces.Owns(action.Surface, action.ExpectedNativeType))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.UnsupportedType,
                    "Discovery surface " + action.Surface + " does not own native type " +
                    action.ExpectedNativeType + ".");
            if (!RecipeStillMatches(
                    in action,
                    native,
                    target,
                    out var compositionReason))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.CompositionChanged,
                    compositionReason);

            if (native.IsDiscovered(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.AlreadyDiscovered,
                    EntityIdentityFormatter.PlayerName(action.TargetId) + " is already discovered.");
            if (!native.IsVisible(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.NotVisible,
                    EntityIdentityFormatter.PlayerName(action.TargetId) +
                    " is not visible on its discovery screen.");
            if (!native.CanDiscover(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.DiscoveryUnavailable,
                    EntityIdentityFormatter.PlayerName(action.TargetId) +
                    " cannot be discovered right now.");

            var cost = native.GetCost(target);
            if (cost is null || cost.GetType() != native.CostType)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.ContractUnavailable,
                    "IDiscoverable.GetDiscoverCost() returned a non-ResourceCostList value.");
            if (!native.HasEnough(cost))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.Unaffordable,
                    EntityIdentityFormatter.PlayerName(action.TargetId) +
                    " has a discovery cost you cannot afford.");
            if (!TryCapturePermit(out var permitReason))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.MutationPermitUnavailable,
                    permitReason);

            return Execute(in action, native, target, cost);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                "Generic discovery preflight failed before mutation: " +
                exception.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private GenericDiscoverySubmission Execute(
        in GenericDiscoveryAction action,
        GenericDiscoveryNativeBindings native,
        object target,
        object cost)
    {
        var stage = GenericDiscoveryNativeStage.Payment;
        var nativeCalls = 0;
        try
        {
            nativeCalls = 1;
            native.PerformCost(cost);
            stage = GenericDiscoveryNativeStage.Discover;
            nativeCalls = 2;
            native.Discover(target);
            stage = GenericDiscoveryNativeStage.Verification;
            return native.IsDiscovered(target)
                ? Verified(nativeCalls)
                : Fault(
                    in action,
                    GenericDiscoveryPreflight.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    "The requested target remained undiscovered after the native callback.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (IsDiscoveredBestEffort(native, target))
                return Verified(nativeCalls);
            return Fault(
                in action,
                GenericDiscoveryPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                "Native generic discovery threw before the requested discovered outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static GenericDiscoverySubmission Verified(
        int nativeCalls) =>
        new(
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(nativeCalls, 1, 1),
            "The exact requested UUID is discovered.");

    private static GenericDiscoverySubmission Fault(
        in GenericDiscoveryAction action,
        GenericDiscoveryPreflight preflight,
        GenericDiscoveryNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        string reason)
    {
        var exactReason = "Generic discovery " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.TargetId) + ": " + reason;
        return new GenericDiscoverySubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
            exactReason);
    }

    private static bool IsDiscoveredBestEffort(
        GenericDiscoveryNativeBindings native,
        object target)
    {
        try { return native.IsDiscovered(target); }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    private bool TryCapturePermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readOwnershipFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "The suite no longer owns GenericDiscovery.";
            return false;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            reason = "The generic discovery mutation permit could not be captured: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private bool RecipeStillMatches(
        in GenericDiscoveryAction action,
        GenericDiscoveryNativeBindings native,
        object target,
        out string reason)
    {
        var nativeGlyphs = native.GetGlyphRecipe(target);
        var nativeResources = native.GetResourceRecipe(target);
        var glyphs = new List<ResolvedComponent>();
        var resources = new List<ResolvedComponent>();
        var glyphCount = 0;
        var resourceCount = 0;
        try
        {
            for (var index = 0; index < action.Components.Count; index++)
            {
                var component = action.Components[index];
                var glyph = _registry.Resolve(component.ComponentId, native.GlyphType);
                var resource = _registry.Resolve(component.ComponentId, native.ResourceType);
                var isGlyph = glyph.IsResolved && _registry.IsCurrent(glyph);
                var isResource = resource.IsResolved && _registry.IsCurrent(resource);
                if (isGlyph == isResource)
                {
                    reason = isGlyph
                        ? "Component " + EntityIdentityFormatter.PlayerName(component.ComponentId) +
                          " resolved as both GlyphSO and ResourceSO."
                        : "Component " + EntityIdentityFormatter.PlayerName(component.ComponentId) +
                          " is no longer a live GlyphSO or ResourceSO.";
                    return false;
                }
                var destination = isGlyph ? glyphs : resources;
                var value = isGlyph ? glyph.Value! : resource.Value!;
                destination.Add(new ResolvedComponent(component.ComponentId, value));
                if (isGlyph) glyphCount = checked(glyphCount + component.Count);
                else resourceCount = checked(resourceCount + component.Count);
            }
        }
        catch (OverflowException)
        {
            reason = "The submitted discovery component counts exceed the action boundary.";
            return false;
        }

        if (nativeGlyphs.Count != glyphCount || nativeResources.Count != resourceCount)
        {
            reason = "The live native recipe now requires " + nativeGlyphs.Count +
                " glyph components and " + nativeResources.Count +
                " resource components, not " + glyphCount + " and " + resourceCount + ".";
            return false;
        }
        for (var index = 0; index < glyphs.Count; index++)
            if (!nativeGlyphs.Contains(glyphs[index].Value))
            {
                reason = "The live native glyph recipe no longer contains " +
                    EntityIdentityFormatter.PlayerName(glyphs[index].Identity) + ".";
                return false;
            }
        for (var index = 0; index < resources.Count; index++)
            if (!nativeResources.Contains(resources[index].Value))
            {
                reason = "The live native resource recipe no longer contains " +
                    EntityIdentityFormatter.PlayerName(resources[index].Identity) + ".";
                return false;
            }
        reason = string.Empty;
        return true;
    }

    private void BindLifecycle()
    {
        if (GenericDiscoveryNativeBindings.TryCreate(
                out var bindings,
                out var reason,
                _resolveType,
                _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is not
        StackOverflowException and not
        OutOfMemoryException and not
        AccessViolationException;

    private readonly struct ResolvedComponent
    {
        internal ResolvedComponent(Guid identity, object value)
        {
            Identity = identity;
            Value = value;
        }

        internal Guid Identity { get; }
        internal object Value { get; }
    }
}
