using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single mutation boundary for removing an equipped spell or moving it to another loadout
/// position. It re-resolves the runtime UUID and every mutable gate on Unity's main thread.
/// </summary>
internal sealed class SpellLoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellLoadoutGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal SpellLoadoutSubmission Submit(in SpellLoadoutAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.WrongThread,
                "Spell loadout actions are bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped spell loadout binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try
        {
            return action.Kind switch
            {
                SpellLoadoutActionKind.Remove => Remove(in action, native),
                SpellLoadoutActionKind.Move => Move(in action, native),
                _ => SpellLoadoutSubmission.Reject(
                    SpellLoadoutPreflight.ContractUnavailable,
                    "Unknown spell loadout action kind " + (int)action.Kind + "."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.ContractUnavailable,
                "Spell loadout preflight failed before mutation: " + ex.GetBaseException().Message);
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

    private SpellLoadoutSubmission Remove(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryResolve(native, action.SpellInstanceId, out var manager, out _, out var spell,
                out var sourceSlot, out _, out var reason))
            return SpellLoadoutSubmission.Reject(SpellLoadoutPreflight.IdentityUnavailable, reason);
        if (!native.CanRemove(spell))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.NativeRemoveRefused,
                "The game will not let " +
                EntityIdentityFormatter.PlayerName(action.SpellInstanceId) +
                " be taken off the loadout bar.");
        if (!TryCapturePermit(out reason))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.Remove(manager, spell);
            return TargetAbsent(native, action.SpellInstanceId)
                ? Verified(new NativeMutationCallOutcome(1, 1, 1),
                    "The exact runtime spell is absent from the loadout.")
                : Fault(
                    in action,
                    SpellLoadoutPreflight.VerificationFailed,
                    SpellLoadoutNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(1, 1, 0),
                    "The requested runtime spell remains equipped.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (TargetAbsentBestEffort(native, action.SpellInstanceId))
                return Verified(
                    new NativeMutationCallOutcome(1, 1, 1),
                    "SpellManager.RemoveSpell threw after the requested removal became observable.");
            return Fault(
                in action,
                SpellLoadoutPreflight.PostCommitFault,
                SpellLoadoutNativeStage.Remove,
                NativeMutationOutcome.ExecutionThrew,
                new NativeMutationCallOutcome(1, 1, 0),
                "SpellManager.RemoveSpell threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private SpellLoadoutSubmission Move(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryResolve(native, action.SpellInstanceId, out _, out var active, out _,
                out var sourceSlot, out var slotCount, out var reason))
            return SpellLoadoutSubmission.Reject(SpellLoadoutPreflight.IdentityUnavailable, reason);
        if (action.DestinationSlot >= slotCount)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.DestinationOutOfRange,
                "There is no slot " + (action.DestinationSlot + 1) +
                "; the loadout bar has slots 1 to " + slotCount + ".");
        if (sourceSlot == action.DestinationSlot)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.AlreadyInRequestedState,
                EntityIdentityFormatter.PlayerName(action.SpellInstanceId) +
                " is already in slot " + (sourceSlot + 1) + ".");
        if (!TryCapturePermit(out reason))
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.MutationPermitUnavailable,
                reason);

        var nativeCalls = 0;
        var stage = SpellLoadoutNativeStage.Swap;
        try
        {
            nativeCalls = 1;
            native.Swap(active, sourceSlot, action.DestinationSlot);
            stage = SpellLoadoutNativeStage.Notify;
            nativeCalls = 2;
            native.UpdateObservable(active);
            return TargetAtSlot(native, action.SpellInstanceId, action.DestinationSlot)
                ? Verified(
                    new NativeMutationCallOutcome(2, 1, 1),
                    "The exact runtime spell is in the requested slot.")
                : Fault(
                    in action,
                    SpellLoadoutPreflight.VerificationFailed,
                    SpellLoadoutNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(2, 1, 0),
                    "The requested runtime spell is not in the destination slot.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (TargetAtSlotBestEffort(native, action.SpellInstanceId, action.DestinationSlot))
                return Verified(
                    new NativeMutationCallOutcome(nativeCalls, 1, 1),
                    "The native reorder pipeline threw after the requested slot order became observable.");
            return Fault(
                in action,
                SpellLoadoutPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                new NativeMutationCallOutcome(nativeCalls, 1, 0),
                "The native reorder pipeline threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static bool TryResolve(
        SpellLoadoutNativeBindings native,
        Guid targetId,
        out object manager,
        out object active,
        out object spell,
        out int slot,
        out int slotCount,
        out string reason)
    {
        manager = native.ReadManager()!;
        active = null!;
        spell = null!;
        slot = -1;
        slotCount = 0;
        if (manager is null)
        {
            reason = "SpellManager.instance is unavailable in this lifecycle.";
            return false;
        }
        active = native.ReadActive(manager);
        var values = native.ReadActiveValues(active);
        slotCount = values.Count;
        var matches = 0;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null) continue;
            if (candidate.GetType() != native.SpellType)
                throw new InvalidOperationException("Equipped slot " + index + " did not hold an exact Spell.");
            if (native.IsEmpty(candidate)) continue;
            var id = ReadIdentity(native, candidate, index);
            if (id != targetId) continue;
            spell = candidate;
            slot = index;
            matches++;
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(targetId) +
              " is not one of the spells you have equipped."
            : EntityIdentityFormatter.PlayerName(targetId) + " is equipped in " + matches +
              " slots, so this cannot tell which one you mean.";
        return false;
    }

    private static bool TargetAbsent(SpellLoadoutNativeBindings native, Guid targetId) =>
        FindTargetSlot(native, targetId) == -1;

    private static bool TargetAtSlot(
        SpellLoadoutNativeBindings native, Guid targetId, int destination)
    {
        var manager = native.ReadManager() ??
            throw new InvalidOperationException("SpellManager.instance was null during verification.");
        var active = native.ReadActive(manager);
        var values = native.ReadActiveValues(active);
        if (destination < 0 || destination >= values.Count) return false;
        var candidate = values[destination];
        return candidate is not null && candidate.GetType() == native.SpellType &&
            !native.IsEmpty(candidate) && ReadIdentity(native, candidate, destination) == targetId;
    }

    private static int FindTargetSlot(SpellLoadoutNativeBindings native, Guid targetId)
    {
        var manager = native.ReadManager() ??
            throw new InvalidOperationException("SpellManager.instance was null during verification.");
        var values = native.ReadActiveValues(native.ReadActive(manager));
        var slot = -1;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null || candidate.GetType() != native.SpellType || native.IsEmpty(candidate))
                continue;
            if (ReadIdentity(native, candidate, index) != targetId) continue;
            if (slot >= 0) return -2;
            slot = index;
        }
        return slot;
    }

    private static Guid ReadIdentity(
        SpellLoadoutNativeBindings native,
        object spell,
        int slot)
    {
        var container = native.ReadSpellGuid(spell) ??
            throw new InvalidOperationException("Equipped slot " + slot + " had no GuidContainer.");
        var id = native.ReadGuidValue(container);
        if (id == Guid.Empty)
            throw new InvalidOperationException("Equipped slot " + slot + " had an empty runtime UUID.");
        return id;
    }

    private static bool TargetAbsentBestEffort(
        SpellLoadoutNativeBindings native, Guid targetId)
    {
        try { return TargetAbsent(native, targetId); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static bool TargetAtSlotBestEffort(
        SpellLoadoutNativeBindings native, Guid targetId, int destination)
    {
        try { return TargetAtSlot(native, targetId, destination); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static SpellLoadoutSubmission Verified(
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        return new SpellLoadoutSubmission(
            SpellLoadoutPreflight.Proceeded,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified,
            callOutcome,
            reason);
    }

    private static SpellLoadoutSubmission Fault(
        in SpellLoadoutAction action,
        SpellLoadoutPreflight preflight,
        SpellLoadoutNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        var exactReason = "Spell loadout " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.SpellInstanceId) + ": " + reason;
        return new SpellLoadoutSubmission(
            preflight,
            stage,
            outcome,
            callOutcome,
            exactReason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit())
        {
            reason = string.Empty;
            return true;
        }
        reason = _readOwnershipFailure();
        if (reason.Length == 0)
            reason = "The suite does not own the spell loadout action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!SpellLoadoutNativeBindings.TryCreate(
                resolve,
                include,
                out _bindings,
                out _bindingFailure))
            _bindings = null;
    }

    private static bool IsExpected(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or OverflowException or
            TargetInvocationException or MemberAccessException;
}
