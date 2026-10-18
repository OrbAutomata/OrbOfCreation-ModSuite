using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the structure enable/disable button.</summary>
internal sealed class StructureLifecycleGameAction : NativeGameAction<StructureLifecycleNativeBindings>
{
    private const string Screen = "the screen this structure is on";

    private static readonly GameActionCodes Codes = new(
        StructureLifecycleActionResultCodes.WrongThread,
        StructureLifecycleActionResultCodes.ContractUnavailable,
        StructureLifecycleActionResultCodes.MutationPermitUnavailable,
        StructureLifecycleActionResultCodes.PostCommitFault,
        StructureLifecycleActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal StructureLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Structure", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out StructureLifecycleNativeBindings? bindings, out string reason) =>
                StructureLifecycleNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal NativeActionOutcome Submit(in StructureLifecycleAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, native => Press(in request, native));
    }

    private NativeActionOutcome Press(in StructureLifecycleAction action, StructureLifecycleNativeBindings native)
    {
        if (!_registry.TryResolveCurrent(action.StructureId, native.StructureType,
                out var structure, out var reason))
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.IdentityUnavailable, reason);
        var name = EntityIdentityFormatter.PlayerName(action.StructureId);
        if (!native.Available(structure))
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.NotAvailable,
                name + " is not available yet.");
        var expectedDisabled = action.Kind == StructureLifecycleActionKind.Disable;
        if (native.Disabled(structure) == expectedDisabled)
            return NativeActionOutcome.Reject(StructureLifecycleActionResultCodes.AlreadyInState,
                name + " is already " + (expectedDisabled ? "disabled." : "enabled."));
        if (!TryPermit(out var refusal)) return refusal;
        return Execute(name, "The requested structure state is visible.", !expectedDisabled,
            () => native.Disabled(structure), () => native.Toggle(structure),
            (_, after) => after == expectedDisabled);
    }
}
