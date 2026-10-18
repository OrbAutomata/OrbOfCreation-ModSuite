using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The single mutation boundary for removing an equipped spell or moving it to another loadout
/// position. It re-resolves the runtime UUID and every mutable gate on Unity's main thread.
/// </summary>
internal sealed class SpellLoadoutGameAction : NativeGameAction<SpellLoadoutNativeBindings>
{
    private const string Screen = "Magic > Spellbook > Loadout";

    private static readonly GameActionCodes Codes = new(
        SpellLoadoutActionResultCodes.WrongThread,
        SpellLoadoutActionResultCodes.ContractUnavailable,
        SpellLoadoutActionResultCodes.MutationPermitUnavailable,
        SpellLoadoutActionResultCodes.PostCommitFault,
        SpellLoadoutActionResultCodes.VerificationFailed);

    private readonly Func<bool> _readAutoCastOn;
    private readonly TypedRegistryResolver _registry;

    internal SpellLoadoutGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null,
        Func<bool>? readAutoCastOn = null)
        : base("Spell loadout", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out SpellLoadoutNativeBindings? bindings, out string reason) =>
                SpellLoadoutNativeBindings.TryCreate(resolveType ?? ReflectionUtil.FindLoadedType,
                    includeContract ?? (_ => true), out bindings, out reason))
    {
        _readAutoCastOn = readAutoCastOn ?? (static () => false);
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal NativeActionOutcome Submit(in SpellLoadoutAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, native => request.Kind switch
        {
            SpellLoadoutActionKind.Remove => Remove(in request, native),
            SpellLoadoutActionKind.Move => Move(in request, native),
            _ => NativeActionOutcome.Reject(
                SpellLoadoutActionResultCodes.ContractUnavailable,
                "Unknown spell loadout action kind " + (int)request.Kind + "."),
        });
    }

    private NativeActionOutcome Remove(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryAdmitScreen(native, out var screenRefusal, out var screenReason))
            return NativeActionOutcome.Reject(screenRefusal, screenReason);
        if (!TryResolve(native, action.SpellInstanceId, out var manager, out _, out var spell,
                out _, out _, out var reason))
            return NativeActionOutcome.Reject(SpellLoadoutActionResultCodes.IdentityUnavailable, reason);
        if (!TryAdmitRemoval(native, spell, out var refusal, out var refusalReason))
            return NativeActionOutcome.Reject(refusal, refusalReason);
        if (!TryPermit(out var permitRefusal)) return permitRefusal;

        var target = action.SpellInstanceId;
        return Execute(EntityIdentityFormatter.PlayerName(target),
            "The exact runtime spell is absent from the loadout.", false,
            () => FindTargetSlot(native, target) == -1,
            () => native.Remove(manager, spell),
            static (_, landed) => landed);
    }

    /// <summary>
    /// The screen has to exist before any of its controls can be worked, and locked is a different
    /// answer from "this spell is busy". <c>ViewSO.IsAvailable()</c> is the game's own question —
    /// <c>prerequisites.Container.Check()</c> — so this is read, never inferred.
    /// </summary>
    private bool TryAdmitScreen(
        SpellLoadoutNativeBindings native,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        var resolution = _registry.Resolve(
            KnownEntities.MagicSpellbookLoadout.Uuid, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
        {
            refusal = SpellLoadoutActionResultCodes.ContractUnavailable;
            reason = "The game's Magic > Spellbook > Loadout screen could not be read, so " +
                "whether the loadout bar can be worked at all is unknown.";
            return false;
        }
        if (!native.IsViewAvailable(view))
        {
            refusal = SpellLoadoutActionResultCodes.ScreenLocked;
            reason = "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no " +
                "loadout bar to change. Buy the Spellbook Loadout upgrade first.";
            return false;
        }
        refusal = CommonActionResultCodes.Committed;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The gate <c>SpellManager.RemoveSpell</c> applies to itself: it destroys nothing unless the
    /// spell is at full charges and is neither casting nor readying a cast. Calling anyway is not
    /// a harmless no-op — the refusal branch switches the spell to a time-based cooldown — so the
    /// suite asks the same three questions first and never makes a call the game would refuse.
    /// </summary>
    private bool TryAdmitRemoval(
        SpellLoadoutNativeBindings native,
        object spell,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        var name = RemovalTargetName(native, spell);
        if (native.IsCasting(spell) || native.IsReadyingCast(spell))
        {
            refusal = SpellLoadoutActionResultCodes.CastInProgress;
            reason = SpellRemovalRefusal.MidCast(name) +
                AutoCastRoute(native.ReadMaximumCharges(spell));
            return false;
        }
        if (!native.IsAtMaxCharges(spell))
        {
            var maximum = native.ReadMaximumCharges(spell);
            refusal = SpellLoadoutActionResultCodes.SpellRecharging;
            reason = SpellRemovalRefusal.Recharging(
                    name,
                    native.ReadCurrentCharges(spell),
                    maximum,
                    native.ReadCooldownRemaining(spell)) +
                AutoCastRoute(maximum);
            return false;
        }
        refusal = CommonActionResultCodes.Committed;
        reason = string.Empty;
        return true;
    }

    private string AutoCastRoute(int maximum) =>
        _readAutoCastOn()
            ? " Auto Cast is on and casts it again as soon as it can, except while Magic > " +
              "Spellbook > Loadout is on screen: game_navigate(screen=\"Magic\", " +
              "subtab=\"Spellbook/Loadout\"), wait there until it reads " + maximum + " of " +
              maximum + " charges, then remove it."
            : string.Empty;

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

    private NativeActionOutcome Move(
        in SpellLoadoutAction action,
        SpellLoadoutNativeBindings native)
    {
        if (!TryAdmitScreen(native, out var screenRefusal, out var screenReason))
            return NativeActionOutcome.Reject(screenRefusal, screenReason);
        if (!TryResolve(native, action.SpellInstanceId, out _, out var active, out _,
                out var sourceSlot, out var slotCount, out var reason))
            return NativeActionOutcome.Reject(SpellLoadoutActionResultCodes.IdentityUnavailable, reason);
        if (action.DestinationSlot >= slotCount)
            return NativeActionOutcome.Reject(
                SpellLoadoutActionResultCodes.DestinationOutOfRange,
                "There is no slot " + (action.DestinationSlot + 1) +
                "; the loadout bar has slots 1 to " + slotCount + ".");
        if (sourceSlot == action.DestinationSlot)
            return NativeActionOutcome.Reject(
                SpellLoadoutActionResultCodes.AlreadyInRequestedState,
                EntityIdentityFormatter.PlayerName(action.SpellInstanceId) +
                " is already in slot " + (sourceSlot + 1) + ".");
        if (!TryPermit(out var refusal)) return refusal;

        var target = action.SpellInstanceId;
        var destination = action.DestinationSlot;
        return Execute(EntityIdentityFormatter.PlayerName(target),
            "The exact runtime spell is in the requested slot.", false,
            () => TargetAtSlot(native, target, destination),
            () =>
            {
                native.Swap(active, sourceSlot, destination);
                native.UpdateObservable(active);
            },
            static (_, landed) => landed);
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
}
