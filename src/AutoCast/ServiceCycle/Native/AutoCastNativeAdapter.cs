using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The neutral outcome of one native cast submission: either a preflight rejection with no mutation,
/// or an attempted audited mutation carrying its outcome and call evidence. It never exposes a native
/// object — the action port maps it to a service result.
/// </summary>
internal readonly struct AutoCastSubmission
{
    private AutoCastSubmission(NativeActionOutcome outcome, Guid occupant = default)
    {
        Outcome = outcome;
        Occupant = occupant;
    }

    public NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The spell found holding the slot this call named, when that is what refused it.
    /// </summary>
    /// <remarks>
    /// The identity lived only inside the sentence, so a caller that wanted to act on whatever had
    /// taken the slot had nowhere to read it but a regular expression over prose — while the
    /// structured id beside it named the spell that was planned, which is the one thing already
    /// known to be absent.
    /// </remarks>
    public Guid Occupant { get; }

    public static AutoCastSubmission Rejected(
        ServiceActionResultCode code,
        GameActionReason reason,
        Guid occupant = default) =>
        new(NativeActionOutcome.Reject(code, reason), occupant);

    public static AutoCastSubmission Skipped(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Skip(code, reason));

    /// <summary>
    /// Ordinary snapshot staleness for the service, exactly like an unready spell: the cast started
    /// between planning and pressing. No penalty, and the MCP caller reads it as the refusal it is.
    /// </summary>
    public static AutoCastSubmission AlreadyCasting(string reason) =>
        Skipped(AutoCastActionResultCodes.SpellAlreadyCasting, reason);

    public static AutoCastSubmission ContractUnavailable(GameActionReason reason) =>
        new(NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason));

    /// <summary>A charge release, which is a native call with nothing to verify a delta against.</summary>
    public static AutoCastSubmission Released(Guid spellRecipeId, Exception? failure) =>
        new(failure is null
            ? NativeActionOutcome.Commit(new NativeMutationCallOutcome(1, 1, 1), string.Empty)
            : NativeActionOutcome.Fault(
                CommonActionResultCodes.AdapterFault,
                NativeMutationOutcome.ExecutionThrew,
                new NativeMutationCallOutcome(1, 1, 0),
                GameActionFaultLog.Reason(
                    $"{AutoCastNativeAdapter.Feature} could not let go of the charge hold on " +
                    $"{EntityIdentityFormatter.PlayerName(spellRecipeId)}.",
                    failure,
                    AutoCastNativeAdapter.Feature)));

    /// <summary>
    /// A pressed spell, committed when the sentinel shows the change and otherwise a fault whose
    /// sentence says what was pressed and what was missing, with every exception in the suite log.
    /// </summary>
    /// <param name="escaped">
    /// What the verifier rethrew once the after-state it read did not show the change; the evidence
    /// is then unset.
    /// </param>
    /// <param name="purpose">What the press was for, after the spell's name; empty for a cast.</param>
    /// <param name="ownFailure">Why the suite itself stopped partway, which no exception explains.</param>
    /// <param name="stuckHold">What letting go of the charge hold taken for the press threw.</param>
    public static AutoCastSubmission Attempted<TState>(
        NativeMutationEvidence<TState> evidence,
        Exception? escaped,
        int nativeCallsAttempted,
        Guid spellRecipeId,
        string purpose,
        Func<TState, TState, string> missed,
        string ownFailure,
        Exception? stuckHold)
    {
        var attempted = escaped is not null || evidence.MutationWasAttempted;
        var committed = escaped is null && evidence.IsVerified;
        var calls = attempted
            ? new NativeMutationCallOutcome(Math.Max(1, nativeCallsAttempted), 1, committed ? 1 : 0)
            : default;
        if (committed) return new(NativeActionOutcome.Commit(calls, string.Empty));

        var spell = EntityIdentityFormatter.PlayerName(spellRecipeId);
        var pressed = $"{AutoCastNativeAdapter.Feature} pressed {spell}{purpose}";
        var cause = escaped ?? evidence.Exception;
        string sentence;
        if (escaped is not null)
            sentence = $"{pressed}, and nothing was confirmed after the game threw.";
        else if (!evidence.MutationWasAttempted)
            sentence = $"{AutoCastNativeAdapter.Feature} could not read {spell} before the press.";
        else if (ownFailure.Length > 0)
        {
            sentence = $"{pressed}, and {ownFailure}.";
            cause = null;
        }
        else if (!evidence.HasAfter)
            sentence = $"{pressed}, and the spell could not be read after the press.";
        else if (evidence.Exception is null)
            sentence = $"{pressed}, {missed(evidence.Before, evidence.After)}.";
        else
            sentence = $"{pressed}, {missed(evidence.Before, evidence.After)} after the game threw.";
        if (stuckHold is not null) sentence += " The charge hold taken for it could not be let go.";

        return new(NativeActionOutcome.Fault(
            CommonActionResultCodes.AdapterFault,
            escaped is null ? evidence.Outcome : NativeMutationOutcome.ExecutionThrew,
            calls,
            new GameActionReason(
                sentence,
                GameActionFaultLog.RecordReference(
                    cause, AutoCastNativeAdapter.Feature, "Letting go of the charge hold", stuckHold))));
    }
}

/// <summary>
/// The native execution surface for Auto Cast, and the only place the service mutates the game.
/// </summary>
/// <remarks>
/// <para>
/// The port does only what has to happen on the main thread against the live game: re-resolve the
/// loadout position, re-check the terms that can have moved since planning
/// or that were never publishable at all, and submit through the audited
/// <see cref="NativeMutationVerifier"/> so the returned submission carries evidence of what actually
/// committed. Choosing which slot to cast is not here — that is the worker's job, decided against a
/// snapshot rather than by walking the live loadout on this thread.
/// </para>
/// <para>
/// Resolution is by position and identity, not by reference. A plan that crosses to a worker cannot
/// carry a native object, so the position is indexed and the spell sitting in it is checked against
/// the identity the plan named. That pair is what the legacy engine's <c>ReferenceEquals</c> was.
/// </para>
/// <para>
/// The target preflight stays here and stays expensive. It reads the target requests the recipe's
/// cast effects open and asks each whether anything is in range, which is main-thread work over
/// live objects with no snapshot form. It runs last, after every cheap refusal, for the same reason
/// the legacy engine ran it last.
/// </para>
/// </remarks>
internal interface IAutoCastNativePort
{
    AutoCastSubmission Fire(int slotIndex, Guid spellRecipeId, bool holdFullCharge);

    AutoCastSubmission ReleaseCharge(int slotIndex, Guid spellRecipeId);

    AutoCastSubmission ToggleOff(int slotIndex, Guid spellRecipeId);

    /// <summary>Whether the game says a ritual battle is running, or null when that cannot be read.</summary>
    bool? IsRitualRunning();
}

internal sealed class AutoCastNativeAdapter : IAutoCastNativePort
{
    internal const string Feature = "Auto Cast";

    /// <summary>The charge-input source id, unchanged so an in-flight hold is still ours to release.</summary>
    private const string ChargeInputSource = "OrbAutomata.AutoCast.FullCharge";

    /// <summary>The legacy engine's bound on target rounds per cast, kept exactly.</summary>
    private const int MaximumTargetRounds = 16;

    private readonly AutoCastNativeBindings? _bindings;
    private readonly string _bindingFailure;

    public AutoCastNativeAdapter() =>
        AutoCastNativeBindings.TryCreate(out _bindings, out _bindingFailure);

    internal AutoCastNativeAdapter(Func<string, Type?>? resolveType, Func<string, bool>? includeContract) =>
        AutoCastNativeBindings.TryCreate(out _bindings, out _bindingFailure, resolveType, includeContract);

    public AutoCastSubmission Fire(int slotIndex, Guid spellRecipeId, bool holdFullCharge)
    {
        if (!TryReadLoadout(out var native, out var manager, out var loadout, out var reason))
            return AutoCastSubmission.ContractUnavailable(reason);

        try
        {
            // A target request already open is the one thing that makes a cast submission land
            // somewhere nobody asked for, so it is checked before anything else.
            if (native.IsTargeting())
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.TargetingInProgress,
                    "a target request was already active");
            }

            if (!TryResolveSlot(
                    native, loadout, slotIndex, spellRecipeId, out var spell, out var identityReason,
                    out var occupant))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SlotIdentityChanged, identityReason, occupant);
            }

            // SpellManager.CanCastASpell() is false while any loaded spell is readying a cast, this
            // one included, so the press's own spell is asked first and "another spell" is only
            // said when it is true.
            if (native.IsReadyingCast(spell))
            {
                return AutoCastSubmission.AlreadyCasting(
                    "the spell is still starting its cast, so the press would start no cast");
            }

            if (!native.CanCastASpell())
            {
                return AutoCastSubmission.Skipped(
                    AutoCastActionResultCodes.NativeCasterBusy,
                    "another spell is still casting, and the game casts one spell at a time");
            }

            // Asked of the spell this position actually resolved to, not of a published loadout a
            // rearrangement can have outrun. A hold on a spell the game does not charge would set an
            // input the game ignores and fire an ordinary cast under a charged name; a hold asked
            // for on a slot that moved or emptied is a slot fact, and TryResolveSlot above has
            // already said which one it was.
            if (holdFullCharge && !native.CanCharge(spell))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SpellNotChargeable,
                    "the game offers this spell no charged cast to hold");
            }

            // Spell.Fire's own first branch, asked before pressing it. A running spell answers the
            // press with a warning popup or with an end-of-cast, never with a new cast, and the
            // press left no trace a caller could read afterwards.
            if (native.IsCasting(spell))
            {
                return AutoCastSubmission.AlreadyCasting(
                    "the spell is already running, so the press would start no cast");
            }

            // The game's own answer, asked again. The plan was made against a reading of it that is
            // up to a generation old, and a cooldown that came back in between is the ordinary case.
            if (!native.CanCast(spell))
            {
                return AutoCastSubmission.Skipped(
                    AutoCastActionResultCodes.SpellNotReady,
                    "the game refused the cast on its own readiness terms");
            }

            if (!HasValidTargets(native, spell))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.NoValidTarget,
                    "the native target selector has no valid target");
            }

            if (holdFullCharge && TrySetChargeHold(native, spell, true) is { } holdFailure)
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.ChargeHoldRefused,
                    GameActionFaultLog.Reason("a charged-spell hold could not be established.", holdFailure, Feature));
            }

            return Submit(native, manager, slotIndex, spellRecipeId, holdFullCharge ? spell : null);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoCastSubmission.ContractUnavailable(
                GameActionFaultLog.Reason("the cast could not be checked before the press.", ex, Feature));
        }
    }

    public AutoCastSubmission ReleaseCharge(int slotIndex, Guid spellRecipeId)
    {
        if (!TryReadLoadout(out var native, out _, out var loadout, out var reason))
            return AutoCastSubmission.ContractUnavailable(reason);

        try
        {
            // Deliberately not gated on the spell still charging. Letting go of a charge input is
            // idempotent and always safe; refusing to let go because a stale reading disagreed is
            // how an input gets stuck down with nobody tracking it.
            if (!TryResolveSlot(
                    native, loadout, slotIndex, spellRecipeId, out var spell, out var identityReason,
                    out var occupant))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SlotIdentityChanged, identityReason, occupant);
            }

            return AutoCastSubmission.Released(spellRecipeId, TrySetChargeHold(native, spell, false));
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoCastSubmission.ContractUnavailable(
                GameActionFaultLog.Reason("the charge hold could not be checked before letting it go.", ex, Feature));
        }
    }

    public AutoCastSubmission ToggleOff(int slotIndex, Guid spellRecipeId)
    {
        if (!TryReadLoadout(out var native, out var manager, out var loadout, out var reason))
            return AutoCastSubmission.ContractUnavailable(reason);

        object spell;
        try
        {
            if (!TryResolveSlot(
                    native, loadout, slotIndex, spellRecipeId, out spell, out var identityReason,
                    out var occupant))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SlotIdentityChanged, identityReason, occupant);
            }
            if (!native.IsToggled(spell))
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SpellNotToggleable,
                    "the equipped spell is not a toggle spell");
            if (!native.IsCasting(spell))
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.SpellAlreadyInactive,
                    "the toggle spell is already off");
            if (!native.CanCancelSpells())
                return AutoCastSubmission.Rejected(
                    AutoCastActionResultCodes.CancellationDisabled,
                    "enable Cancellable Spells in the game's settings before turning this spell off");
            if (!native.CanFire(spell))
                return AutoCastSubmission.Skipped(
                    AutoCastActionResultCodes.SpellNotReady,
                    "the spell's visible cast button is not available right now");
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoCastSubmission.ContractUnavailable(
                GameActionFaultLog.Reason("the toggle could not be checked before the press.", ex, Feature));
        }

        var nativeCalls = 0;
        NativeMutationEvidence<bool> evidence = default;
        Exception? escaped = null;
        try
        {
            evidence = NativeMutationVerifier.Execute(
                "toggle spell off",
                EntityIdentityFormatter.Format(spellRecipeId),
                "Spell.IsCasting true to false",
                () => native.IsCasting(spell),
                () =>
                {
                    using (AutoCastManualSignal.EnterAutomatedFire())
                    {
                        nativeCalls++;
                        native.FireSpellIndex(manager, slotIndex);
                    }
                },
                static (before, after) => before && !after);
        }
        catch (Exception ex) when (nativeCalls > 0 && !ExceptionDetail.IsProcessFatal(ex))
        {
            escaped = ex;
        }
        return AutoCastSubmission.Attempted(
            evidence, escaped, nativeCalls, spellRecipeId, " to turn it off",
            static (_, _) => "and it kept running", string.Empty, stuckHold: null);
    }

    public bool? IsRitualRunning()
    {
        if (_bindings is not { } native) return null;
        try
        {
            return native.BattleManager() is { } battle ? native.IsInCombat(battle) : null;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Submits the cast and resolves whatever target requests it opens, verified on the hook every
    /// cast in the game passes through: its epoch rose and no target prompt was left open.
    /// </summary>
    /// <remarks>
    /// A charge hold taken for the press is let go on every path that did not commit, as the legacy
    /// engine did: a hold left down for a cast that never landed has nothing holding it.
    /// </remarks>
    private static AutoCastSubmission Submit(
        AutoCastNativeBindings native,
        object manager,
        int slotIndex,
        Guid spellRecipeId,
        object? heldSpell)
    {
        var nativeCalls = 0;
        var failure = string.Empty;
        NativeMutationEvidence<(long Epoch, bool Targeting)> evidence = default;
        Exception? escaped = null;
        try
        {
            evidence = NativeMutationVerifier.Execute<(long Epoch, bool Targeting)>(
                "Auto Cast fire",
                EntityIdentityFormatter.Format(spellRecipeId),
                "Spell.Fire hook epoch rose and no target prompt left open",
                () => (AutoCastManualSignal.FireEpoch, native.IsTargeting()),
                () =>
                {
                    if (!FireAndResolveTargets(native, manager, slotIndex, ref nativeCalls, out failure))
                        throw new InvalidOperationException(failure);
                },
                static (before, after) => after.Epoch > before.Epoch && !after.Targeting);
        }
        catch (Exception ex) when (nativeCalls > 0 && !ExceptionDetail.IsProcessFatal(ex))
        {
            escaped = ex;
        }

        var stuckHold = heldSpell is not null && (escaped is not null || !evidence.IsVerified)
            ? TrySetChargeHold(native, heldSpell, false)
            : null;
        return AutoCastSubmission.Attempted(
            evidence, escaped, nativeCalls, spellRecipeId, string.Empty,
            static (before, after) => after.Epoch > before.Epoch
                ? "and a target prompt was left open"
                : "and no cast started",
            failure, stuckHold);
    }

    private static bool FireAndResolveTargets(
        AutoCastNativeBindings native,
        object manager,
        int slotIndex,
        ref int nativeCalls,
        out string reason)
    {
        // The service's own fire must not read as the player's. The scope is around this call only,
        // exactly as before: a target submission that re-entered Spell.Fire would be a different
        // cast, and counting it as ours would hide it.
        using (AutoCastManualSignal.EnterAutomatedFire())
        {
            nativeCalls++;
            native.FireSpellIndex(manager, slotIndex);
        }

        for (var round = 0; round < MaximumTargetRounds && native.IsTargeting(); round++)
        {
            nativeCalls++;
            var link = native.GetTargetingLink();

            object? target = null;
            if (link is not null)
            {
                nativeCalls++;
                target = native.GetRandom(link);
            }

            if (target is null)
            {
                reason = "the game's target selector offered no target";
                return false;
            }

            nativeCalls++;
            native.SubmitTarget(target);
        }

        if (native.IsTargeting())
        {
            reason = "the cast still asked for a target after " +
                MaximumTargetRounds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " were given";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Sets the charge input, answering what the game threw when it could not.</summary>
    private static Exception? TrySetChargeHold(AutoCastNativeBindings native, object spell, bool holding)
    {
        try
        {
            native.SetChargeInput(spell, ChargeInputSource, holding);
            return null;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return ex;
        }
    }

    /// <summary>
    /// Whether anything is in range of whatever this spell targets.
    /// </summary>
    /// <remarks>
    /// A spell that requests no targets needs none, so a recipe without target requests is a pass
    /// rather than a refusal.
    /// </remarks>
    private static bool HasValidTargets(AutoCastNativeBindings native, object spell)
    {
        var recipe = native.Recipe(spell);
        var scaling = native.ScalingInfo(spell);
        if (recipe is null || scaling is null) return true;

        // Every request, not any. A spell whose effects open three target requests will open all three
        // when it fires, and one of them having nothing to aim at is a cast that stalls halfway with a
        // target prompt on screen. That is the whole reason the preflight exists.
        foreach (var request in TargetRequests(native, recipe))
        {
            // An unreadable preflight contract refuses. The alternative is submitting a cast whose
            // targeting nobody checked, which is the outcome this whole boundary exists to prevent.
            if (native.TargetOptions(request) is not { } options) return false;

            try
            {
                if (!native.HasValidTargetsLeft(options, scaling)) return false;
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<object> TargetRequests(AutoCastNativeBindings native, object recipe)
    {
        if (native.OnCastEffects(recipe) is not { } blocks) yield break;
        foreach (var block in blocks)
        {
            if (block is null || native.EffectScripts(block) is not { } scripts) continue;
            foreach (var script in scripts)
            {
                if (script is not null && script.GetType() == native.RequestType) yield return script;
            }
        }
    }

    /// <summary>
    /// Finds the live spell at a position and checks it is still the one the plan named.
    /// </summary>
    /// <remarks>
    /// A plan that could not name the spell — an occupied slot whose recipe the snapshot could not
    /// read — carries <see cref="Guid.Empty"/>, and that is refused rather than treated as a wildcard.
    /// Casting whatever happens to be in a position is exactly the mistake identity checking exists
    /// to prevent.
    /// </remarks>
    private static bool TryResolveSlot(
        AutoCastNativeBindings native,
        object loadout,
        int slotIndex,
        Guid spellRecipeId,
        out object spell,
        out string reason,
        out Guid occupant)
    {
        spell = null!;
        occupant = Guid.Empty;
        if (spellRecipeId == Guid.Empty)
        {
            reason = "the planned slot carried no spell identity";
            return false;
        }

        if (native.Slots(loadout) is not { } slots)
        {
            reason = "the equipped loadout is unreadable";
            return false;
        }

        if (slotIndex >= slots.Count)
        {
            reason = $"Spell slot {slotIndex + 1} is not on the bar right now.";
            return false;
        }

        var candidate = slots[slotIndex];
        if (candidate is null || candidate.GetType() != native.SpellType)
        {
            reason = $"Spell slot {slotIndex + 1} is empty.";
            return false;
        }

        // The game's own emptiness, asked before the identity read. An empty socket is a live Spell
        // carrying no recipe, so reading its identity answered "what occupies it now could not be
        // named" — true, and no use to a caller whose slot is simply empty.
        if (native.IsEmpty(candidate))
        {
            reason = $"Spell slot {slotIndex + 1} is empty.";
            return false;
        }

        // Naming the occupant is the point of this refusal. "The identity changed" leaves a caller
        // with no next move; "the slot now holds Firebolt" says which plan to redo and against what.
        // It rides out as an id as well as a name, because a caller that wants to act on whatever
        // took the slot had nowhere else to read it: the structured id beside the sentence names
        // the spell that was planned, which is the one thing already known not to be there.
        var recipe = native.Recipe(candidate);
        var liveId = recipe is null ? Guid.Empty : native.RecipeId(recipe);
        if (liveId != spellRecipeId)
        {
            occupant = liveId;
            reason = liveId == Guid.Empty
                ? $"Spell slot {slotIndex + 1} no longer holds the spell that was planned, and what " +
                  "occupies it now could not be named."
                : $"Spell slot {slotIndex + 1} now holds " +
                  $"{EntityIdentityFormatter.PlayerHandle(liveId)}, not the spell that was planned.";
            return false;
        }

        spell = candidate;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The bound set and the live manager and loadout, read fresh for every call: the singleton and
    /// its list are replaced whenever the game reloads.
    /// </summary>
    private bool TryReadLoadout(
        out AutoCastNativeBindings native,
        out object manager,
        out object loadout,
        out string reason)
    {
        native = null!;
        manager = null!;
        loadout = null!;
        if (_bindings is null)
        {
            reason = _bindingFailure;
            return false;
        }

        native = _bindings;
        if (native.Manager() is not { } liveManager)
        {
            reason = "SpellManager is not ready";
            return false;
        }

        if (native.ActiveSpells(liveManager) is not { } liveLoadout)
        {
            reason = "the equipped loadout is not ready";
            return false;
        }

        manager = liveManager;
        loadout = liveLoadout;
        reason = string.Empty;
        return true;
    }
}
