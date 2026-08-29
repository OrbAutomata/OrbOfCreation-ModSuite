using System;
using System.Globalization;
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
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private SpellLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellLoadoutGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
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

    internal SpellLoadoutSubmission Submit(in SpellLoadoutAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Magic > Spellbook > Loadout"));

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("Magic > Spellbook > Loadout", ex));
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellLoadoutSubmission.Reject(
                SpellLoadoutPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

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
                GameActionAnswer.CouldNotRead("Magic > Spellbook > Loadout", ex));
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
        if (!TryAdmitScreen(native, out var screenRefusal, out var screenReason))
            return SpellLoadoutSubmission.Reject(screenRefusal, screenReason);
        if (!TryResolve(native, action.SpellInstanceId, out var manager, out _, out var spell,
                out var sourceSlot, out _, out var reason))
            return SpellLoadoutSubmission.Reject(SpellLoadoutPreflight.IdentityUnavailable, reason);
        if (!TryAdmitRemoval(native, spell, out var refusal, out var refusalReason))
            return SpellLoadoutSubmission.Reject(refusal, refusalReason);
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
                    "The game errored while removing this spell, but the spell was gone " +
                    "from the slot afterwards.");
            return Fault(
                in action,
                SpellLoadoutPreflight.PostCommitFault,
                SpellLoadoutNativeStage.Remove,
                NativeMutationOutcome.ExecutionThrew,
                new NativeMutationCallOutcome(1, 1, 0),
                GameActionAnswer.GameErrored("Magic > Spellbook > Loadout", ex));
        }
    }

    /// <summary>
    /// The screen has to exist before any of its controls can be worked, and locked is a different
    /// answer from "this spell is busy". <c>ViewSO.IsAvailable()</c> is the game's own question —
    /// <c>prerequisites.Container.Check()</c> — so this is read, never inferred.
    /// </summary>
    private bool TryAdmitScreen(
        SpellLoadoutNativeBindings native,
        out SpellLoadoutPreflight refusal,
        out string reason)
    {
        var resolution = _registry.Resolve(
            KnownEntities.MagicSpellbookLoadout.Uuid, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
        {
            refusal = SpellLoadoutPreflight.ContractUnavailable;
            reason = "The game's Magic > Spellbook > Loadout screen could not be read, so " +
                "whether the loadout bar can be worked at all is unknown.";
            return false;
        }
        if (!native.IsViewAvailable(view))
        {
            refusal = SpellLoadoutPreflight.ScreenLocked;
            reason = "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no " +
                "loadout bar to change. Buy the Spellbook Loadout upgrade first.";
            return false;
        }
        refusal = SpellLoadoutPreflight.Proceeded;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The gate <c>SpellManager.RemoveSpell</c> applies to itself: it destroys nothing unless the
    /// spell is at full charges and is neither casting nor readying a cast. Calling anyway is not
    /// a harmless no-op — the refusal branch switches the spell to a time-based cooldown — so the
    /// suite asks the same three questions first and never makes a call the game would refuse.
    /// </summary>
    private static bool TryAdmitRemoval(
        SpellLoadoutNativeBindings native,
        object spell,
        out SpellLoadoutPreflight refusal,
        out string reason)
    {
        var name = RemovalTargetName(native, spell);
        if (native.IsCasting(spell) || native.IsReadyingCast(spell))
        {
            refusal = SpellLoadoutPreflight.CastInProgress;
            reason = name + " is mid-cast. The game answers a removal now with " +
                "\"Cannot remove a spell that is still recharging.\" " +
                "Wait for the cast to finish, then remove it.";
            return false;
        }
        if (!native.IsAtMaxCharges(spell))
        {
            var maximum = native.ReadMaximumCharges(spell);
            refusal = SpellLoadoutPreflight.SpellRecharging;
            reason = name + " is still recharging, and the game only removes a spell at full " +
                "charges: \"Cannot remove a spell that is still recharging.\" It holds " +
                native.ReadCurrentCharges(spell) + " of " + maximum + " charges" +
                NextChargeClause(native, spell) + ". Remove it once it reads " + maximum +
                " of " + maximum + ".";
            return false;
        }
        refusal = SpellLoadoutPreflight.Proceeded;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The recipe name, which is the identity a removal answers with and the only one a caller can
    /// look up — a runtime spell instance is in no catalog.
    /// </summary>
    private static string RemovalTargetName(SpellLoadoutNativeBindings native, object spell)
    {
        var recipe = native.ReadSpellRecipe(spell);
        return recipe is null
            ? "This spell"
            : EntityIdentityFormatter.PlayerName(native.ReadRecipeIdentity(recipe));
    }

    private static string NextChargeClause(SpellLoadoutNativeBindings native, object spell)
    {
        var remaining = native.ReadCooldownRemaining(spell);
        return remaining > BigDouble.Zero
            ? ", and the next one is " +
                Math.Round(remaining.ToDouble(), 1).ToString(CultureInfo.InvariantCulture) +
                "s away"
            : string.Empty;
    }

    private SpellLoadoutSubmission Move(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryAdmitScreen(native, out var screenRefusal, out var screenReason))
            return SpellLoadoutSubmission.Reject(screenRefusal, screenReason);
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
                GameActionAnswer.GameErrored("Magic > Spellbook > Loadout", ex));
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
            reason = "The spellbook is not loaded right now, so no spell can be moved or removed.";
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
