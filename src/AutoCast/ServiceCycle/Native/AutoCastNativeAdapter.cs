using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Preflight disposition of one native cast submission, before any audited mutation was invoked.
/// <see cref="Proceeded"/> means the mutation was attempted and the submission carries verifier
/// evidence; the others are the reasons a submission never got that far.
/// </summary>
internal enum AutoCastPreflight
{
    Proceeded = 0,

    /// <summary>The native contract could not be bound, or a previous mutation blocked it.</summary>
    ContractUnavailable,

    /// <summary>A target request is already open, so nothing can be submitted into it.</summary>
    TargetingInProgress,

    /// <summary>The game says the caster is not free.</summary>
    CasterBusy,

    /// <summary>The position no longer holds the spell the plan named.</summary>
    SlotIdentityChanged,

    /// <summary>The game refused the cast on its own readiness terms.</summary>
    NotReady,

    /// <summary>The spell has nothing to aim at.</summary>
    NoValidTarget,

    /// <summary>A full-charge hold was wanted and could not be taken, so no cast was submitted.</summary>
    ChargeHoldRefused,

    /// <summary>
    /// The spell resolved in the named position, and the game says that spell has no charged cast.
    /// </summary>
    /// <remarks>
    /// Asked only after the position resolved to the spell the caller named, so a moved or emptied
    /// slot answers as the slot fact it is instead of as a claim about the spell's capabilities.
    /// </remarks>
    NotChargeable,

    /// <summary>The requested active spell is not a toggle spell.</summary>
    NotToggleable,

    /// <summary>The requested toggle spell is not currently active.</summary>
    AlreadyInactive,

    /// <summary>The player's Cancellable Spells setting disables the native toggle-off path.</summary>
    CancellationDisabled,

    /// <summary>The spell is already running, so a fire press would start no cast.</summary>
    /// <remarks>
    /// <c>Spell.Fire</c> branches on <c>IsCasting()</c> before it looks at anything else: with
    /// Cancellable Spells off it shows a warning and returns, and with it on it ends the running
    /// cast instead of starting one. Neither is the cast the caller asked for, and neither moves the
    /// game's own per-cast counter — which is what made a discarded press byte-identical to a real
    /// one for a caller that could only read the counter.
    /// </remarks>
    AlreadyCasting,
}

/// <summary>
/// The neutral outcome of one native cast submission: either a preflight rejection with no mutation,
/// or an attempted audited mutation carrying its outcome and call evidence. It never exposes a native
/// object — the action port maps it to a service result.
/// </summary>
internal readonly struct AutoCastSubmission
{
    private AutoCastSubmission(
        AutoCastPreflight preflight,
        bool hasEvidence,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        Preflight = preflight;
        HasEvidence = hasEvidence;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason;
    }

    public AutoCastPreflight Preflight { get; }
    public bool HasEvidence { get; }
    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool Verified => HasEvidence && Outcome == NativeMutationOutcome.Verified;

    /// <summary>What the boundary would tell an operator, whichever way it went.</summary>
    public string Reason { get; }

    public static AutoCastSubmission Rejected(AutoCastPreflight preflight, string reason)
    {
        if (preflight == AutoCastPreflight.Proceeded)
            throw new ArgumentOutOfRangeException(nameof(preflight));
        return new AutoCastSubmission(preflight, hasEvidence: false, default, default, reason);
    }

    /// <summary>A charge release, which is a native call with nothing to verify a delta against.</summary>
    public static AutoCastSubmission Released(bool succeeded, string reason) =>
        new(
            AutoCastPreflight.Proceeded,
            hasEvidence: true,
            succeeded ? NativeMutationOutcome.Verified : NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(1, 1, succeeded ? 1 : 0),
            reason);

    public static AutoCastSubmission Attempted<TState>(
        NativeMutationEvidence<TState> evidence,
        int nativeCallsAttempted) =>
        new(
            AutoCastPreflight.Proceeded,
            hasEvidence: true,
            evidence.Outcome,
            new NativeMutationCallOutcome(
                Math.Max(1, nativeCallsAttempted), 1, evidence.IsVerified ? 1 : 0),
            evidence.IsVerified ? string.Empty : evidence.Format());
}

/// <summary>
/// The native execution surface for Auto Cast, and the only place the service mutates the game.
/// </summary>
/// <remarks>
/// <para>
/// The port does only what has to happen on the main thread against the live game: bind the
/// contracts, re-resolve the loadout position, re-check the terms that can have moved since planning
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
/// The target preflight stays here and stays expensive. It walks the recipe's effect graph looking
/// for target requests and asks each whether anything is in range, which is main-thread work over
/// live objects with no snapshot form. It runs last, after every cheap refusal, for the same reason
/// the legacy engine ran it last.
/// </para>
/// </remarks>
internal interface IAutoCastNativePort
{
    AutoCastSubmission Fire(int slotIndex, Guid spellRecipeId, bool holdFullCharge);

    AutoCastSubmission ReleaseCharge(int slotIndex, Guid spellRecipeId);

    AutoCastSubmission ToggleOff(int slotIndex, Guid spellRecipeId);

    /// <summary>Whether the game currently has a target request open.</summary>
    bool IsTargeting();

    /// <summary>Whether the game says a spell can be cast at all right now.</summary>
    bool IsCasterBusy();
}

internal sealed class AutoCastNativeAdapter : IAutoCastNativePort, IDisposable
{
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The charge-input source id, unchanged so an in-flight hold is still ours to release.</summary>
    private const string ChargeInputSource = "OrbAutomata.AutoCast.FullCharge";

    /// <summary>The legacy engine's bound on target rounds per cast, kept exactly.</summary>
    private const int MaximumTargetRounds = 16;

    private readonly Dictionary<Guid, string> _blockedSpells = new();

    private Type? _managerType;
    private Type? _spellType;
    private Type? _targetingType;
    private Type? _settingsType;
    private object? _manager;
    private object? _activeSpells;
    private FieldInfo? _activeSpellsValue;
    private MethodInfo? _fireSpellIndex;
    private MethodInfo? _canCastASpell;
    private MethodInfo? _isTargeting;
    private MethodInfo? _getTargetingLink;
    private MethodInfo? _submitTarget;
    private MethodInfo? _canCast;
    private MethodInfo? _canCharge;
    private MethodInfo? _isEmpty;
    private MethodInfo? _canFire;
    private MethodInfo? _isCasting;
    private MethodInfo? _isToggled;
    private MethodInfo? _canCancelSpells;
    private MethodInfo? _getReference;
    private MethodInfo? _setChargeInput;
    private MethodInfo? _getScalingInfo;
    private string? _blockedReason;

    private bool IsBound =>
        _manager is not null && _activeSpells is not null && _blockedReason is null;

    public AutoCastSubmission Fire(int slotIndex, Guid spellRecipeId, bool holdFullCharge)
    {
        if (!TryInitialize(out var reason))
            return AutoCastSubmission.Rejected(AutoCastPreflight.ContractUnavailable, reason);

        try
        {
            // A target request already open is the one thing that makes a cast submission land
            // somewhere nobody asked for, so it is checked before anything else.
            if (IsTargeting())
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.TargetingInProgress, "a target request was already active");
            }

            if (IsCasterBusy())
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.CasterBusy, "the native spell system is busy");
            }

            if (!TryResolveSlot(slotIndex, spellRecipeId, out var spell, out var identityReason))
                return AutoCastSubmission.Rejected(AutoCastPreflight.SlotIdentityChanged, identityReason);

            if (_blockedSpells.TryGetValue(spellRecipeId, out var blocked))
                return AutoCastSubmission.Rejected(AutoCastPreflight.ContractUnavailable, blocked);

            // Asked of the spell this position actually resolved to, not of a published loadout a
            // rearrangement can have outrun. A hold on a spell the game does not charge would set an
            // input the game ignores and fire an ordinary cast under a charged name; a hold asked
            // for on a slot that moved or emptied is a slot fact, and TryResolveSlot above has
            // already said which one it was.
            if (holdFullCharge && _canCharge!.Invoke(spell, Array.Empty<object>()) is not true)
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.NotChargeable,
                    "the game offers this spell no charged cast to hold");
            }

            // Spell.Fire's own first branch, asked before pressing it. A running spell answers the
            // press with a warning popup or with an end-of-cast, never with a new cast, and the
            // press left no trace a caller could read afterwards.
            if (_isCasting!.Invoke(spell, Array.Empty<object>()) is true)
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.AlreadyCasting,
                    "the spell is already running, so the press would start no cast");
            }

            // The game's own answer, asked again. The plan was made against a reading of it that is
            // up to a generation old, and a cooldown that came back in between is the ordinary case.
            if (_canCast!.Invoke(spell, Array.Empty<object>()) is not true)
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.NotReady, "the game refused the cast on its own readiness terms");
            }

            if (!HasValidTargets(spell))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.NoValidTarget, "the native target selector has no valid target");
            }

            if (holdFullCharge && !TrySetChargeHold(spell, true, out var holdReason))
            {
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.ChargeHoldRefused,
                    $"a charged-spell hold could not be established: {holdReason}");
            }

            var submission = Submit(slotIndex, spellRecipeId);

            // A hold taken for a cast that never landed is a charge input stuck down with nothing
            // holding it, so it is let go on every failing path exactly as the legacy engine did.
            if (holdFullCharge && !submission.Verified) TrySetChargeHold(spell, false, out _);
            return submission;
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return AutoCastSubmission.Rejected(
                AutoCastPreflight.ContractUnavailable,
                $"cast submission failed: {ex.GetBaseException().Message}");
        }
    }

    public AutoCastSubmission ReleaseCharge(int slotIndex, Guid spellRecipeId)
    {
        if (!TryInitialize(out var reason))
            return AutoCastSubmission.Rejected(AutoCastPreflight.ContractUnavailable, reason);

        try
        {
            // Deliberately not gated on the spell still charging. Letting go of a charge input is
            // idempotent and always safe; refusing to let go because a stale reading disagreed is
            // how an input gets stuck down with nobody tracking it.
            if (!TryResolveSlot(slotIndex, spellRecipeId, out var spell, out var identityReason))
                return AutoCastSubmission.Rejected(AutoCastPreflight.SlotIdentityChanged, identityReason);

            var released = TrySetChargeHold(spell, false, out var releaseReason);
            return AutoCastSubmission.Released(released, released ? string.Empty : releaseReason);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return AutoCastSubmission.Rejected(
                AutoCastPreflight.ContractUnavailable,
                $"charge release failed: {ex.GetBaseException().Message}");
        }
    }

    public AutoCastSubmission ToggleOff(int slotIndex, Guid spellRecipeId)
    {
        if (!TryInitialize(out var reason))
            return AutoCastSubmission.Rejected(AutoCastPreflight.ContractUnavailable, reason);

        try
        {
            if (!TryResolveSlot(slotIndex, spellRecipeId, out var spell, out var identityReason))
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.SlotIdentityChanged, identityReason);
            if (_blockedSpells.TryGetValue(spellRecipeId, out var blocked))
                return AutoCastSubmission.Rejected(AutoCastPreflight.ContractUnavailable, blocked);
            if (_isToggled!.Invoke(spell, Array.Empty<object>()) is not true)
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.NotToggleable,
                    "the equipped spell is not a toggle spell");
            if (_isCasting!.Invoke(spell, Array.Empty<object>()) is not true)
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.AlreadyInactive,
                    "the toggle spell is already off");
            if (_canCancelSpells!.Invoke(null, Array.Empty<object>()) is not true)
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.CancellationDisabled,
                    "enable Cancellable Spells in the game's settings before turning this spell off");
            if (_canFire!.Invoke(spell, Array.Empty<object>()) is not true)
                return AutoCastSubmission.Rejected(
                    AutoCastPreflight.NotReady,
                    "the spell's visible cast button is not available right now");

            var nativeCalls = 0;
            var evidence = NativeMutationVerifier.Execute(
                "toggle spell off",
                EntityIdentityFormatter.Format(spellRecipeId),
                "Spell.IsCasting true to false",
                () => _isCasting.Invoke(spell, Array.Empty<object>()) is true,
                () =>
                {
                    using (AutoCastManualSignal.EnterAutomatedFire())
                    {
                        nativeCalls++;
                        _fireSpellIndex!.Invoke(_manager, new object[] { slotIndex });
                    }
                },
                (before, after) => before && !after);
            if (!evidence.IsVerified && evidence.MutationWasAttempted)
            {
                _blockedSpells[spellRecipeId] =
                    "native toggle-off blocked until the next lifecycle: " + evidence.Format();
            }
            return AutoCastSubmission.Attempted(evidence, nativeCalls);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return AutoCastSubmission.Rejected(
                AutoCastPreflight.ContractUnavailable,
                "toggle-off submission failed: " + ex.GetBaseException().Message);
        }
    }

    public bool IsTargeting()
    {
        // Fails closed. A targeting contract that cannot be read is reported as "a request is open",
        // because submitting a cast into an unknown targeting state is the outcome worth avoiding.
        try
        {
            return _isTargeting?.Invoke(null, Array.Empty<object>()) as bool? ?? true;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    public bool IsCasterBusy()
    {
        try
        {
            return _canCastASpell?.Invoke(null, Array.Empty<object>()) is not true;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    public void Dispose() => InvalidateLifecycle();

    /// <summary>
    /// Drops every bound contract and every quarantine.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The contracts are instance-keyed — the manager singleton and its loadout
    /// list are replaced when the game reloads — and a spell blocked because its mutation could not
    /// be verified deserves another chance in a run of the game where the contract may hold again.
    /// </remarks>
    public void InvalidateLifecycle()
    {
        _blockedSpells.Clear();
        _managerType = null;
        _spellType = null;
        _targetingType = null;
        _settingsType = null;
        _manager = null;
        _activeSpells = null;
        _activeSpellsValue = null;
        _fireSpellIndex = null;
        _canCastASpell = null;
        _isTargeting = null;
        _getTargetingLink = null;
        _submitTarget = null;
        _canCast = null;
        _canFire = null;
        _isCasting = null;
        _isToggled = null;
        _canCancelSpells = null;
        _getReference = null;
        _setChargeInput = null;
        _getScalingInfo = null;
        _blockedReason = null;
    }

    /// <summary>
    /// Submits the cast and resolves whatever target requests it opens, verified as an exact one-fire
    /// delta on the hook every cast in the game passes through.
    /// </summary>
    /// <remarks>
    /// The postcondition is the <c>Spell.Fire</c> patch's own epoch advancing by exactly one. Two is
    /// as wrong as none: it would mean the submission cast twice, and a cast that spent twice what
    /// was planned is precisely what verification exists to catch.
    /// </remarks>
    private AutoCastSubmission Submit(int slotIndex, Guid spellRecipeId)
    {
        var nativeCalls = 0;
        var failure = string.Empty;
        var evidence = NativeMutationVerifier.Execute(
            "Auto Cast fire",
            EntityIdentityFormatter.Format(spellRecipeId),
            "Spell.Fire hook epoch exact delta +1",
            () => AutoCastManualSignal.FireEpoch,
            () =>
            {
                if (!FireAndResolveTargets(slotIndex, ref nativeCalls, out failure))
                    throw new InvalidOperationException(failure);
            },
            (before, after) => after == before + 1);

        var submission = AutoCastSubmission.Attempted(evidence, nativeCalls);
        if (!evidence.IsVerified && evidence.MutationWasAttempted)
        {
            _blockedSpells[spellRecipeId] =
                $"native cast blocked until the next lifecycle: {evidence.Format()}";
        }

        return submission;
    }

    private bool FireAndResolveTargets(int slotIndex, ref int nativeCalls, out string reason)
    {
        // The service's own fire must not read as the player's. The scope is around this call only,
        // exactly as before: a target submission that re-entered Spell.Fire would be a different
        // cast, and counting it as ours would hide it.
        using (AutoCastManualSignal.EnterAutomatedFire())
        {
            nativeCalls++;
            _fireSpellIndex!.Invoke(_manager, new object[] { slotIndex });
        }

        for (var round = 0; round < MaximumTargetRounds && IsTargeting(); round++)
        {
            object? link = null;
            if (_getTargetingLink is not null)
            {
                nativeCalls++;
                link = _getTargetingLink.Invoke(null, Array.Empty<object>());
            }

            object? target = null;
            if (link is not null)
            {
                var getRandom = link.GetType().GetMethod(
                    "GetRandom", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
                if (getRandom is not null)
                {
                    nativeCalls++;
                    target = getRandom.Invoke(link, Array.Empty<object>());
                }
            }

            if (target is null || _submitTarget is null)
            {
                reason = "native target selector returned no valid target";
                return false;
            }

            nativeCalls++;
            _submitTarget.Invoke(null, new[] { target });
        }

        if (IsTargeting())
        {
            reason = "target request limit exceeded";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TrySetChargeHold(object spell, bool holding, out string reason)
    {
        if (_setChargeInput is null)
        {
            reason = "Spell.SetChargeInput(string, bool) unavailable";
            return false;
        }

        try
        {
            _setChargeInput.Invoke(spell, new object[] { ChargeInputSource, holding });
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    /// <summary>
    /// Whether anything is in range of whatever this spell targets.
    /// </summary>
    /// <remarks>
    /// A spell that requests no targets needs none, so an empty walk is a pass rather than a refusal.
    /// The walk is bounded by depth and follows only members whose names say they hold effects, which
    /// is what keeps a reflective traversal of live game objects from becoming unbounded.
    /// </remarks>
    private bool HasValidTargets(object spell)
    {
        if (_getReference is null || _getScalingInfo is null) return true;

        var recipe = _getReference.Invoke(spell, Array.Empty<object>());
        var scaling = _getScalingInfo.Invoke(spell, Array.Empty<object>());
        if (recipe is null || scaling is null) return true;

        // Every request, not any. A spell whose effects open three target requests will open all three
        // when it fires, and one of them having nothing to aim at is a cast that stalls halfway with a
        // target prompt on screen. That is the whole reason the preflight exists.
        foreach (var request in FindTargetRequests(recipe))
        {
            var options = ReflectionUtil.ReadMember(request, "targetOptions");
            var hasValid = options?.GetType().GetMethods(ReflectionUtil.InstanceFlags)
                .FirstOrDefault(method =>
                    method.Name == "HasValidTargetsLeft" && method.GetParameters().Length == 1);

            // An unreadable preflight contract refuses. The alternative is submitting a cast whose
            // targeting nobody checked, which is the outcome this whole boundary exists to prevent.
            if (options is null || hasValid is null) return false;

            try
            {
                if (hasValid.Invoke(options, new[] { scaling }) is not true) return false;
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<object> FindTargetRequests(object root) =>
        Traverse(root, 0, new HashSet<object>(NodeIdentity.Instance))
            .Where(value => value.GetType().Name == "RequestTargetEffectScript");

    /// <summary>
    /// Walks the recipe's effect graph, bounded by depth and by member name.
    /// </summary>
    /// <remarks>
    /// Following only members whose names say they hold effects is what keeps a reflective traversal
    /// of live game objects from wandering into the whole scene graph. Identity rather than equality
    /// tracks what has been seen, because these are game objects whose <c>Equals</c> is not ours.
    /// </remarks>
    private static IEnumerable<object> Traverse(object value, int depth, ISet<object> visited)
    {
        const int MaximumDepth = 7;
        if (depth > MaximumDepth || value is string || !visited.Add(value)) yield break;

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                foreach (var nested in Traverse(item, depth + 1, visited)) yield return nested;
            }

            yield break;
        }

        yield return value;
        foreach (var member in ReadEffectMembers(value))
        {
            foreach (var nested in Traverse(member, depth + 1, visited)) yield return nested;
        }
    }

    private static IEnumerable<object> ReadEffectMembers(object value)
    {
        var type = value.GetType();
        foreach (var field in type.GetFields(ReflectionUtil.InstanceFlags))
        {
            if (IsEffectMember(field.Name) && field.GetValue(value) is { } fieldValue)
                yield return fieldValue;
        }

        foreach (var property in type.GetProperties(ReflectionUtil.InstanceFlags))
        {
            if (!IsEffectMember(property.Name) || property.GetIndexParameters().Length > 0) continue;

            object? propertyValue = null;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception ex) when (IsReflectionFailure(ex))
            {
            }

            if (propertyValue is not null) yield return propertyValue;
        }
    }

    private static bool IsEffectMember(string name) =>
        name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Reference identity, so the walk cannot be fooled by a game type's own equality.</summary>
    private sealed class NodeIdentity : IEqualityComparer<object>
    {
        internal static readonly NodeIdentity Instance = new();

        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);

        public int GetHashCode(object value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
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
    private bool TryResolveSlot(int slotIndex, Guid spellRecipeId, out object spell, out string reason)
    {
        spell = null!;
        if (spellRecipeId == Guid.Empty)
        {
            reason = "the planned slot carried no spell identity";
            return false;
        }

        if (_activeSpellsValue!.GetValue(_activeSpells) is not IList slots)
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
        if (candidate is null || candidate.GetType() != _spellType)
        {
            reason = $"Spell slot {slotIndex + 1} is empty.";
            return false;
        }

        // The game's own emptiness, asked before the identity read. An empty socket is a live Spell
        // carrying no recipe, so reading its identity answered "what occupies it now could not be
        // named" — true, and no use to a caller whose slot is simply empty.
        if (_isEmpty!.Invoke(candidate, Array.Empty<object>()) is true)
        {
            reason = $"Spell slot {slotIndex + 1} is empty.";
            return false;
        }

        // Naming the occupant is the point of this refusal. "The identity changed" leaves a caller
        // with no next move; "the slot now holds Firebolt" says which plan to redo and against what.
        var recipe = _getReference?.Invoke(candidate, Array.Empty<object>());
        var identity = recipe is null ? null : ReflectionUtil.ReadStableId(recipe);
        if (!Guid.TryParse(identity, out var liveId) || liveId != spellRecipeId)
        {
            reason = liveId == Guid.Empty
                ? $"Spell slot {slotIndex + 1} no longer holds the spell that was planned, and what " +
                  "occupies it now could not be named."
                : $"Spell slot {slotIndex + 1} now holds " +
                  $"{EntityIdentityFormatter.Format(liveId)}, not the spell that was planned.";
            return false;
        }

        spell = candidate;
        reason = string.Empty;
        return true;
    }

    private bool TryInitialize(out string reason)
    {
        if (IsBound)
        {
            reason = string.Empty;
            return true;
        }

        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }

        try
        {
            _managerType = ReflectionUtil.FindLoadedType("SpellManager");
            _spellType = ReflectionUtil.FindLoadedType("Spell");
            _targetingType = ReflectionUtil.FindLoadedType("TargetingManager");
            _settingsType = ReflectionUtil.FindLoadedType("SettingsManager");
            if (_managerType is null || _spellType is null || _targetingType is null ||
                _settingsType is null)
                return Retry("native cast types are not registered yet", out reason);

            _manager = _managerType.GetField("instance", StaticFlags)?.GetValue(null);
            if (_manager is null || _manager.GetType() != _managerType)
                return Retry("SpellManager is not ready", out reason);

            _activeSpells = ReflectionUtil.ReadMember(_manager, "activeSpells");
            if (_activeSpells is null) return Retry("the equipped loadout is not ready", out reason);
            _activeSpellsValue = FindField(_activeSpells.GetType(), "value");

            _fireSpellIndex = _managerType.GetMethod(
                "FireSpellIndex", ReflectionUtil.InstanceFlags, null, new[] { typeof(int) }, null);
            _canCastASpell = _managerType.GetMethod("CanCastASpell", StaticFlags, null, Type.EmptyTypes, null);
            _isTargeting = _targetingType.GetMethod("IsTargeting", StaticFlags, null, Type.EmptyTypes, null);
            _getTargetingLink = _targetingType.GetMethod("GetTargetingLink", StaticFlags);
            _submitTarget = _targetingType.GetMethods(StaticFlags)
                .FirstOrDefault(method => method.Name == "SubmitTarget" && method.GetParameters().Length == 1);
            _canCast = FindMethod(_spellType, "CanCast");
            _canCharge = FindMethod(_spellType, "CanCharge");
            _isEmpty = FindMethod(_spellType, "IsEmpty");
            _canFire = FindMethod(_spellType, "CanFire");
            _isCasting = FindMethod(_spellType, "IsCasting");
            _isToggled = FindMethod(_spellType, "IsToggledSpell");
            _canCancelSpells = _settingsType.GetMethod(
                "CanCancelSpells", StaticFlags, null, Type.EmptyTypes, null);
            _getReference = FindMethod(_spellType, "get_reference");
            _getScalingInfo = FindMethod(_spellType, "GetScalingInfo");
            _setChargeInput = _spellType.GetMethod(
                "SetChargeInput",
                ReflectionUtil.InstanceFlags,
                null,
                new[] { typeof(string), typeof(bool) },
                null);

            if (_activeSpellsValue is null || _fireSpellIndex is null || _canCastASpell is null ||
                _isTargeting is null || _getTargetingLink is null || _submitTarget is null ||
                _canCast is null || _canCharge is null || _isEmpty is null ||
                _canFire is null || _isCasting is null ||
                _isToggled is null || _canCancelSpells is null || _setChargeInput is null)
            {
                return Block("native cast accessors are unavailable", out reason);
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return Block($"cast contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }

        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name) =>
        type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);

    private bool Block(string message, out string reason)
    {
        _blockedReason = message;
        reason = message;
        return false;
    }

    private static bool Retry(string message, out string reason)
    {
        reason = message;
        return false;
    }

    private static bool IsReflectionFailure(Exception ex) =>
        ex is TargetInvocationException or ArgumentException or InvalidOperationException
            or InvalidCastException or OverflowException or TargetException or MemberAccessException;
}
