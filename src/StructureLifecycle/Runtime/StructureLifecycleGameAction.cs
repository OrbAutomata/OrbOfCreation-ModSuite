using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

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

    internal NativeActionOutcome Submit(in StructureLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("the screen this structure is on", _bindingFailure));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return NativeActionOutcome.Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("the screen this structure is on", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return NativeActionOutcome.Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var resolution = _registry.Resolve(action.StructureId, native.StructureType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.IdentityUnavailable,
                    resolution.IsResolved
                        ? EntityIdentityFormatter.PlayerName(action.StructureId) + " became stale."
                        : resolution.Reason);
            var structure = resolution.Value!;
            if (!native.Available(structure))
                return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.NotAvailable,
                    EntityIdentityFormatter.PlayerName(action.StructureId) + " is not available yet.");
            var beforeDisabled = native.Disabled(structure);
            var expectedDisabled = action.Kind == StructureLifecycleActionKind.Disable;
            if (beforeDisabled == expectedDisabled)
                return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.AlreadyInState,
                    EntityIdentityFormatter.PlayerName(action.StructureId) + " is already " +
                    (expectedDisabled ? "disabled." : "enabled."));
            if (!_tryCaptureMutationPermit())
                return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, structure, expectedDisabled);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.ContractUnavailable,
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

    private static NativeActionOutcome Execute(
        in StructureLifecycleAction action,
        StructureLifecycleNativeBindings native,
        object structure,
        bool expectedDisabled)
    {
        var stage = "NativeCallback";
        try
        {
            native.Toggle(structure);
            stage = "Verification";
            return native.Disabled(structure) == expectedDisabled
                ? Verified()
                : Fault(in action, StructureLifecycleActionResultCodes.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("the screen this structure is on"));
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            if (native.Disabled(structure) == expectedDisabled) return Verified();
            return Fault(in action, StructureLifecycleActionResultCodes.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("the screen this structure is on", exception));
        }
    }

    private static NativeActionOutcome Verified() =>
        NativeActionOutcome.Commit(new NativeMutationCallOutcome(1, 1, 1),
            "The requested structure state is visible.");

    private static NativeActionOutcome Fault(
        in StructureLifecycleAction action,
        ServiceActionResultCode code,
        string stage,
        NativeMutationOutcome outcome,
        string reason) =>
        NativeActionOutcome.Fault(code, outcome, new NativeMutationCallOutcome(1, 1, 0),
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

}
