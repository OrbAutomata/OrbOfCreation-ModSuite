using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the structure enable/disable button.</summary>
internal sealed class StructureLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private StructureLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal StructureLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal StructureLifecycleSubmission Submit(in StructureLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(StructureLifecyclePreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(StructureLifecyclePreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("the screen this structure is on"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(StructureLifecyclePreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("the screen this structure is on", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(StructureLifecyclePreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var resolution = _registry.Resolve(action.StructureId, native.StructureType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(StructureLifecyclePreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? EntityIdentityFormatter.PlayerName(action.StructureId) + " became stale."
                        : resolution.Reason);
            var structure = resolution.Value!;
            if (!native.Available(structure))
                return Reject(StructureLifecyclePreflight.NotAvailable,
                    EntityIdentityFormatter.PlayerName(action.StructureId) + " is not available yet.");
            var beforeDisabled = native.Disabled(structure);
            var expectedDisabled = action.Kind == StructureLifecycleActionKind.Disable;
            if (beforeDisabled == expectedDisabled)
                return Reject(StructureLifecyclePreflight.AlreadyInState,
                    EntityIdentityFormatter.PlayerName(action.StructureId) + " is already " +
                    (expectedDisabled ? "disabled." : "enabled."));
            if (!_tryCaptureMutationPermit())
                return Reject(StructureLifecyclePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, structure, expectedDisabled);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(StructureLifecyclePreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("the screen this structure is on", exception));
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

    private static StructureLifecycleSubmission Execute(
        in StructureLifecycleAction action,
        StructureLifecycleNativeBindings native,
        object structure,
        bool expectedDisabled)
    {
        var stage = StructureLifecycleNativeStage.NativeCallback;
        try
        {
            native.Toggle(structure);
            stage = StructureLifecycleNativeStage.Verification;
            return native.Disabled(structure) == expectedDisabled
                ? Verified()
                : Fault(in action, StructureLifecyclePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("the screen this structure is on"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (native.Disabled(structure) == expectedDisabled) return Verified();
            return Fault(in action, StructureLifecyclePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("the screen this structure is on", exception));
        }
    }

    private static StructureLifecycleSubmission Reject(
        StructureLifecyclePreflight preflight,
        string reason) => StructureLifecycleSubmission.Reject(preflight, reason);

    private static StructureLifecycleSubmission Verified() =>
        new(StructureLifecyclePreflight.Proceeded, StructureLifecycleNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested structure state is visible.");

    private static StructureLifecycleSubmission Fault(
        in StructureLifecycleAction action,
        StructureLifecyclePreflight preflight,
        StructureLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Structure " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.StructureId) + ": " + reason);

    private void BindLifecycle()
    {
        if (StructureLifecycleNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}
