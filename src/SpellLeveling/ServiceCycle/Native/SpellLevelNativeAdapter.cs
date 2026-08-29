using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Preflight disposition of one native level submission, before any audited mutation was invoked.
/// <see cref="Proceeded"/> means the mutation was attempted and the submission carries verifier
/// evidence; the others are the reasons a submission never got that far.
/// </summary>
internal enum SpellLevelPreflight
{
    Proceeded = 0,

    /// <summary>The native contract could not be bound or the spell is no longer in the registry.</summary>
    ContractUnavailable,

    /// <summary>No discovered spell's leveling prerequisite passes, so leveling is not unlocked.</summary>
    ProgressionLocked,

    /// <summary>The spell is no longer ready, or its level cost is no longer covered.</summary>
    NotAffordable,

    /// <summary>An <c>All</c> was planned but the level-all upgrade is no longer committed.</summary>
    BatchUnavailable,
}

/// <summary>
/// The neutral outcome of one native level submission: either a preflight rejection with no mutation,
/// or an attempted audited mutation carrying its outcome and call evidence. It never exposes a native
/// object — the action port maps it to a service result.
/// </summary>
internal readonly struct SpellLevelSubmission
{
    private SpellLevelSubmission(
        SpellLevelPreflight preflight,
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

    public SpellLevelPreflight Preflight { get; }
    public bool HasEvidence { get; }
    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool Verified => HasEvidence && Outcome == NativeMutationOutcome.Verified;

    /// <summary>What the boundary would tell an operator, whichever way it went.</summary>
    public string Reason { get; }

    public static SpellLevelSubmission Rejected(SpellLevelPreflight preflight, string reason)
    {
        if (preflight == SpellLevelPreflight.Proceeded)
            throw new ArgumentOutOfRangeException(nameof(preflight));
        return new SpellLevelSubmission(preflight, hasEvidence: false, default, default, reason);
    }

    public static SpellLevelSubmission Attempted<TState>(NativeMutationEvidence<TState> evidence) =>
        new(
            SpellLevelPreflight.Proceeded,
            hasEvidence: true,
            evidence.Outcome,
            NativeMutationCallOutcome.FromEvidence(evidence),
            evidence.IsVerified ? string.Empty : evidence.Format());
}

/// <summary>
/// The native execution surface for Spell Leveling, and the only place the service mutates the game.
/// </summary>
/// <remarks>
/// <para>
/// The port does only what has to happen on the main thread against the live game: bind the
/// contracts, re-resolve the spell, re-check the terms that can have moved since planning, and submit
/// through the audited <see cref="NativeMutationVerifier"/> so the returned submission carries
/// evidence of what actually committed. Choosing which spell to level is not here — that is the
/// worker's job, decided against a snapshot rather than by walking the live registry on this thread.
/// </para>
/// <para>
/// Resolution is by identity, not by reference. A plan that crosses to a worker cannot carry a native
/// object, so the spell is looked up by UUID and the identity match is what a reference check would
/// otherwise have been.
/// </para>
/// </remarks>
internal interface ISpellLevelNativePort
{
    SpellLevelSubmission Submit(SpellLevelActionKind kind, Guid uuid);
}

internal sealed class SpellLevelNativeAdapter : ISpellLevelNativePort, ISpellLevelCapabilityPort, IDisposable
{
    private readonly TypedRegistryResolver _registryResolver;
    private Type? _recipeType;
    private object? _manager;
    private object? _availableRecipes;
    private object? _levelAllUpgrade;
    private TypedRegistryResolution? _levelAllResolution;
    private FieldInfo? _recipeValuesField;
    private FieldInfo? _levelingPrerequisitesField;
    private FieldInfo? _masteryLevelField;
    private MethodInfo? _prerequisitesCheck;
    private MethodInfo? _isDiscovered;
    private MethodInfo? _isReady;
    private MethodInfo? _getLevelCost;
    private MethodInfo? _costHasEnough;
    private MethodInfo? _costPerform;
    private MethodInfo? _purchaseLevel;
    private MethodInfo? _getUpgradePurchaseLevel;
    private MethodInfo? _tryLevelAll;
    private string? _blockedReason;

    public SpellLevelNativeAdapter(TypedRegistryResolver? registryResolver = null)
    {
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    private bool IsBound =>
        _manager is not null &&
        _availableRecipes is not null &&
        _levelAllResolution is not null &&
        _registryResolver.IsCurrent(_levelAllResolution) &&
        _blockedReason is null;

    public SpellLevelSubmission Submit(SpellLevelActionKind kind, Guid uuid)
    {
        if (!TryInitialize(out var reason))
            return SpellLevelSubmission.Rejected(SpellLevelPreflight.ContractUnavailable, reason);

        try
        {
            // A native batch is admitted from the live set, not from the one ranked identity the
            // worker carries for filing evidence. In particular, one unaffordable head spell must
            // not veto a later spell that the game's own batch can level.
            if (kind == SpellLevelActionKind.All)
                return SubmitAll(uuid);

            if (!TryResolveRecipe(uuid, out var recipe))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelPreflight.ContractUnavailable,
                    "the planned spell is no longer in the available recipes");
            }

            // Everything the snapshot could not say, asked of the game itself. Discovery is published
            // and re-read anyway: it is one call, and a boundary that trusts half a plan is harder to
            // reason about than one that trusts none of it.
            if (_isDiscovered!.Invoke(recipe, Array.Empty<object>()) is not true)
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelPreflight.NotAffordable, "the spell is no longer discovered");
            }

            if (!PrerequisitesPass(recipe))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelPreflight.ProgressionLocked, "spell leveling is not unlocked");
            }

            if (_isReady!.Invoke(recipe, Array.Empty<object>()) is not true)
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelPreflight.NotAffordable,
                    "the spell no longer has a ready mastery level");
            }

            var cost = _getLevelCost!.Invoke(recipe, Array.Empty<object>());
            if (cost is null || _costHasEnough!.Invoke(cost, Array.Empty<object>()) is not true)
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelPreflight.NotAffordable, "the spell-level cost is no longer affordable");
            }

            return SubmitSingle(uuid, recipe, cost);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelPreflight.ContractUnavailable,
                $"spell-level mutation failed: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// What the game says the feature can currently do. The <c>All</c> half is snapshot-derivable and
    /// the worker derives it; this exists for the <c>Locked</c> half, which is not.
    /// </summary>
    public bool TryReadCapability(out AutoSpellLevelCapability capability)
    {
        capability = AutoSpellLevelCapability.Locked;
        if (!TryInitialize(out _)) return false;

        try
        {
            if (ReadPurchaseLevel(_levelAllUpgrade!) > 0)
            {
                capability = AutoSpellLevelCapability.All;
                return true;
            }

            foreach (var recipe in ReadRecipes())
            {
                if (recipe is null || recipe.GetType() != _recipeType) continue;
                if (_isDiscovered!.Invoke(recipe, Array.Empty<object>()) is not true) continue;
                if (!PrerequisitesPass(recipe)) continue;
                capability = AutoSpellLevelCapability.Single;
                return true;
            }

            return true;
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            Block($"spell-level capability check failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void Dispose() => InvalidateLifecycle();

    public void InvalidateLifecycle()
    {
        _recipeType = null;
        _manager = null;
        _availableRecipes = null;
        _levelAllUpgrade = null;
        _levelAllResolution = null;
        _recipeValuesField = null;
        _levelingPrerequisitesField = null;
        _masteryLevelField = null;
        _prerequisitesCheck = null;
        _isDiscovered = null;
        _isReady = null;
        _getLevelCost = null;
        _costHasEnough = null;
        _costPerform = null;
        _purchaseLevel = null;
        _getUpgradePurchaseLevel = null;
        _tryLevelAll = null;
        _blockedReason = null;
    }

    private SpellLevelSubmission SubmitSingle(Guid uuid, object recipe, object cost)
    {
        var evidence = NativeMutationVerifier.Execute(
            "Spell level single",
            uuid.ToString("D"),
            "mastery level exact delta +1",
            () => ReadMasteryLevel(recipe),
            () =>
            {
                _costPerform!.Invoke(cost, Array.Empty<object>());
                _purchaseLevel!.Invoke(recipe, Array.Empty<object>());
            },
            (before, after) => after == before + 1);
        return Complete(evidence);
    }

    private SpellLevelSubmission SubmitAll(Guid uuid)
    {
        // The capability is re-read rather than trusted: the worker decided All from a snapshot, and
        // an upgrade refunded or reset since then would make this a call the game refuses.
        if (ReadPurchaseLevel(_levelAllUpgrade!) <= 0)
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelPreflight.BatchUnavailable,
                "the level-all upgrade is no longer committed");
        }

        var discovered = false;
        var prerequisitePassed = false;
        foreach (var recipe in ReadRecipes())
        {
            if (recipe is null || recipe.GetType() != _recipeType) continue;
            if (_isDiscovered!.Invoke(recipe, Array.Empty<object>()) is not true) continue;
            discovered = true;
            if (!PrerequisitesPass(recipe)) continue;
            prerequisitePassed = true;
            if (_isReady!.Invoke(recipe, Array.Empty<object>()) is not true) continue;

            var cost = _getLevelCost!.Invoke(recipe, Array.Empty<object>());
            if (cost is null || _costHasEnough!.Invoke(cost, Array.Empty<object>()) is not true) continue;

            return SubmitAllVerified(uuid);
        }

        if (discovered && !prerequisitePassed)
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelPreflight.ProgressionLocked,
                "spell leveling is not unlocked");
        }

        return SpellLevelSubmission.Rejected(
            SpellLevelPreflight.NotAffordable,
            "no ready spell has an affordable level cost");
    }

    private SpellLevelSubmission SubmitAllVerified(Guid uuid)
    {
        var evidence = NativeMutationVerifier.Execute(
            "Spell level all",
            uuid == Guid.Empty ? "all ready spells" : uuid.ToString("D"),
            "total mastery level positive delta",
            ReadTotalMasteryLevels,
            () => _tryLevelAll!.Invoke(_manager, Array.Empty<object>()),
            (before, after) => after > before);
        return Complete(evidence);
    }

    /// <summary>
    /// Files the evidence and, when a mutation was attempted but could not be verified, blocks the
    /// contract until the next lifecycle.
    /// </summary>
    /// <remarks>
    /// An attempted native call whose postcondition did not hold means the suite no longer understands
    /// this contract, and the only safe thing to do with a mutation you cannot verify is stop making
    /// it. A rejected preflight is not that — nothing was called — and does not block.
    /// </remarks>
    private SpellLevelSubmission Complete<TState>(NativeMutationEvidence<TState> evidence)
    {
        var submission = SpellLevelSubmission.Attempted(evidence);
        if (!evidence.IsVerified && evidence.MutationWasAttempted)
            Block($"native spell-level mutation blocked until the next lifecycle: {evidence.Format()}");
        return submission;
    }

    private bool PrerequisitesPass(object recipe)
    {
        var prerequisites = _levelingPrerequisitesField!.GetValue(recipe);
        return prerequisites is not null &&
            _prerequisitesCheck!.Invoke(prerequisites, Array.Empty<object>()) is true;
    }

    private bool TryResolveRecipe(Guid uuid, out object recipe)
    {
        var wanted = uuid.ToString("D");
        foreach (var candidate in ReadRecipes())
        {
            if (candidate is null || candidate.GetType() != _recipeType) continue;
            if (!string.Equals(ReflectionUtil.ReadStableId(candidate), wanted, StringComparison.Ordinal))
                continue;
            recipe = candidate;
            return true;
        }

        recipe = null!;
        return false;
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
            var managerType = ReflectionUtil.FindLoadedType("SpellManager");
            _recipeType = ReflectionUtil.FindLoadedType("SpellRecipeSO");
            var upgradeType = ReflectionUtil.FindLoadedType(KnownEntities.UnlockLevelAllSpells.ManagedTypeName);
            if (managerType is null || _recipeType is null || upgradeType is null)
                return Retry("native spell-level types are not registered yet", out reason);

            var upgradeResolution = _registryResolver.Resolve(
                KnownEntities.UnlockLevelAllSpells.Uuid, upgradeType);
            if (!upgradeResolution.IsResolved)
            {
                return HandleRegistryFailure(
                    EntityIdentityFormatter.Format(KnownEntities.UnlockLevelAllSpells.Uuid),
                    upgradeResolution,
                    out reason);
            }

            _levelAllUpgrade = upgradeResolution.Value;
            _levelAllResolution = upgradeResolution;

            _manager = FindField(managerType, "instance", true)?.GetValue(null);
            if (_manager is null || _manager.GetType() != managerType)
                return Retry("SpellManager is not ready", out reason);
            var availableField = FindField(managerType, "availableSpellRecipes", false);
            _availableRecipes = availableField?.GetValue(_manager);
            if (_availableRecipes is null) return Retry("available spell recipes are not ready", out reason);

            _recipeValuesField = FindField(_availableRecipes.GetType(), "value", false);
            _levelingPrerequisitesField = FindField(_recipeType, "levelingPrerequisites", false);
            _masteryLevelField = FindField(_recipeType, "masteryLevel", false);
            _isDiscovered = FindMethod(_recipeType, "IsDiscovered");
            _isReady = FindMethod(_recipeType, "IsReadyToLevelMastery");
            _getLevelCost = FindMethod(_recipeType, "GetLevelCost");
            _purchaseLevel = FindMethod(_recipeType, "PurchaseLevel");
            _getUpgradePurchaseLevel = FindMethod(upgradeType, "GetPurchaseLevel");
            _tryLevelAll = FindMethod(managerType, "TryLevelAllSpells");
            if (_recipeValuesField is null || _levelingPrerequisitesField is null || _masteryLevelField is null ||
                _isDiscovered is null || _isReady is null || _getLevelCost is null || _purchaseLevel is null ||
                _getUpgradePurchaseLevel is null || _tryLevelAll is null)
                return Block("native spell-level accessors are unavailable", out reason);

            _prerequisitesCheck = FindMethod(_levelingPrerequisitesField.FieldType, "Check");
            _costHasEnough = FindMethod(_getLevelCost.ReturnType, "HasEnough");
            _costPerform = FindMethod(_getLevelCost.ReturnType, "PerformCost");
            if (_prerequisitesCheck is null || _costHasEnough is null || _costPerform is null)
                return Block("native spell prerequisite or cost accessors are unavailable", out reason);

            _ = ReadRecipes();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsReflectionFailure(ex) || ex is FormatException)
        {
            return Block($"spell-level contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private IEnumerable ReadRecipes()
    {
        if (_recipeValuesField!.GetValue(_availableRecipes) is not IEnumerable recipes)
            throw new InvalidOperationException("available spell recipe contents are unavailable");
        return recipes;
    }

    private int ReadPurchaseLevel(object upgrade) =>
        Convert.ToInt32(_getUpgradePurchaseLevel!.Invoke(upgrade, Array.Empty<object>()) ?? 0);

    private int ReadMasteryLevel(object recipe) =>
        Convert.ToInt32(_masteryLevelField!.GetValue(recipe) ?? 0);

    private long ReadTotalMasteryLevels()
    {
        long total = 0;
        foreach (var recipe in ReadRecipes())
            if (recipe is not null && recipe.GetType() == _recipeType) total += Math.Max(0, ReadMasteryLevel(recipe));
        return total;
    }

    private static FieldInfo? FindField(Type type, string name, bool isStatic)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var field = current.GetField(name, flags);
            if (field is not null) return field;
        }

        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

    private bool HandleRegistryFailure(
        string label,
        TypedRegistryResolution resolution,
        out string reason)
    {
        var message = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable ? Retry(message, out reason) : Block(message, out reason);
    }

    private void Block(string message) => _blockedReason = message;

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
            or InvalidCastException or OverflowException;
}
