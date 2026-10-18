using System;
using System.Collections;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The neutral outcome of one native level submission: either a preflight rejection with no mutation,
/// or an attempted audited mutation carrying its outcome and call evidence. It never exposes a native
/// object — the action port maps it to a service result.
/// </summary>
internal readonly struct SpellLevelSubmission
{
    private SpellLevelSubmission(NativeActionOutcome outcome, ResourceCharge[]? charges = null)
    {
        Outcome = outcome;
        _charges = charges;
    }

    private readonly ResourceCharge[]? _charges;

    public NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The price the game named for a single level on the frame that paid it. A level-all press
    /// is the game's own batch, which names no price to the suite, so it carries none.
    /// </summary>
    public ReadOnlySpan<ResourceCharge> Charges =>
        _charges is null ? default : _charges.AsSpan();

    public static SpellLevelSubmission Rejected(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    public static SpellLevelSubmission ContractUnavailable(string reason) =>
        new(NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason));

    public static SpellLevelSubmission Attempted<TState>(
        NativeMutationEvidence<TState> evidence,
        ResourceCharge[]? charges = null)
    {
        var calls = NativeMutationCallOutcome.FromEvidence(evidence);
        return new(
            evidence.IsVerified
                ? NativeActionOutcome.Commit(calls, string.Empty)
                : NativeActionOutcome.Fault(
                    CommonActionResultCodes.AdapterFault, evidence.Outcome, calls, evidence.Format()),
            charges);
    }
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
    private readonly SpellLevelNativeBindings? _bindings;
    private readonly string _bindingFailure;
    private object? _manager;
    private object? _availableRecipes;
    private object? _levelAllUpgrade;
    private TypedRegistryResolution? _levelAllResolution;
    private string? _blockedReason;

    public SpellLevelNativeAdapter(TypedRegistryResolver? registryResolver = null)
        : this(registryResolver, resolveType: null, includeContract: null)
    {
    }

    internal SpellLevelNativeAdapter(
        TypedRegistryResolver? registryResolver,
        Func<string, Type?>? resolveType,
        Func<string, bool>? includeContract)
    {
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
        SpellLevelNativeBindings.TryCreate(out _bindings, out _bindingFailure, resolveType, includeContract);
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
            return SpellLevelSubmission.ContractUnavailable(reason);
        var native = _bindings!;

        try
        {
            // A native batch is admitted from the live set, not from the one ranked identity the
            // worker carries for filing evidence. In particular, one unaffordable head spell must
            // not veto a later spell that the game's own batch can level.
            if (kind == SpellLevelActionKind.All)
                return SubmitAll(uuid);

            if (!TryResolveRecipe(uuid, out var recipe))
            {
                return SpellLevelSubmission.ContractUnavailable(
                    "the planned spell is no longer in the available recipes");
            }

            // Everything the snapshot could not say, asked of the game itself. Discovery is published
            // and re-read anyway: it is one call, and a boundary that trusts half a plan is harder to
            // reason about than one that trusts none of it.
            if (!native.IsDiscovered(recipe))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelActionResultCodes.LevelNotAffordable, "the spell is no longer discovered");
            }

            if (!PrerequisitesPass(recipe))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelActionResultCodes.ProgressionLocked, "spell leveling is not unlocked");
            }

            if (!native.IsReady(recipe))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelActionResultCodes.LevelNotAffordable,
                    "the spell no longer has a ready mastery level");
            }

            var cost = native.LevelCost(recipe);
            if (cost is null || !native.HasEnough(cost))
            {
                return SpellLevelSubmission.Rejected(
                    SpellLevelActionResultCodes.LevelNotAffordable, "the spell-level cost is no longer affordable");
            }

            return SubmitSingle(uuid, recipe, cost);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return SpellLevelSubmission.ContractUnavailable(
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
        var native = _bindings!;

        try
        {
            if (native.PurchasedLevel(_levelAllUpgrade!) > 0)
            {
                capability = AutoSpellLevelCapability.All;
                return true;
            }

            foreach (var recipe in ReadRecipes())
            {
                if (recipe is null || recipe.GetType() != native.RecipeType) continue;
                if (!native.IsDiscovered(recipe)) continue;
                if (!PrerequisitesPass(recipe)) continue;
                capability = AutoSpellLevelCapability.Single;
                return true;
            }

            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            Block($"spell-level capability check failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void Dispose() => InvalidateLifecycle();

    public void InvalidateLifecycle()
    {
        _manager = null;
        _availableRecipes = null;
        _levelAllUpgrade = null;
        _levelAllResolution = null;
        _blockedReason = null;
    }

    private SpellLevelSubmission SubmitSingle(Guid uuid, object recipe, object cost)
    {
        var native = _bindings!;
        var charges = ReadCharges(cost);
        var evidence = NativeMutationVerifier.Execute(
            "Spell level single",
            uuid.ToString("D"),
            "mastery level exact delta +1",
            () => native.MasteryLevel(recipe),
            () =>
            {
                native.PerformCost(cost);
                native.PurchaseLevel(recipe);
            },
            (before, after) => after == before + 1);
        return Complete(evidence, charges);
    }

    private ResourceCharge[]? ReadCharges(object cost)
    {
        var native = _bindings!;
        if (native.CostEntries(cost) is not { Count: > 0 } entries)
            return null;
        var charges = new ResourceCharge[entries.Count];
        var count = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var resource = entry is null ? null : native.EntryResource(entry);
            if (resource is null) continue;
            charges[count++] = new ResourceCharge(native.ResourceId(resource), native.EntryValue(entry!));
        }
        if (count == 0) return null;
        Array.Resize(ref charges, count);
        return charges;
    }

    private SpellLevelSubmission SubmitAll(Guid uuid)
    {
        var native = _bindings!;
        // The capability is re-read rather than trusted: the worker decided All from a snapshot, and
        // an upgrade refunded or reset since then would make this a call the game refuses.
        if (native.PurchasedLevel(_levelAllUpgrade!) <= 0)
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelActionResultCodes.LevelAllLocked,
                "the level-all upgrade is no longer committed");
        }

        var discovered = false;
        var prerequisitePassed = false;
        foreach (var recipe in ReadRecipes())
        {
            if (recipe is null || recipe.GetType() != native.RecipeType) continue;
            if (!native.IsDiscovered(recipe)) continue;
            discovered = true;
            if (!PrerequisitesPass(recipe)) continue;
            prerequisitePassed = true;
            if (!native.IsReady(recipe)) continue;

            var cost = native.LevelCost(recipe);
            if (cost is null || !native.HasEnough(cost)) continue;

            return SubmitAllVerified(uuid);
        }

        if (discovered && !prerequisitePassed)
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelActionResultCodes.ProgressionLocked,
                "spell leveling is not unlocked");
        }

        return SpellLevelSubmission.Rejected(
            SpellLevelActionResultCodes.LevelNotAffordable,
            "no ready spell has an affordable level cost");
    }

    private SpellLevelSubmission SubmitAllVerified(Guid uuid)
    {
        var native = _bindings!;
        var manager = _manager!;
        var evidence = NativeMutationVerifier.Execute(
            "Spell level all",
            uuid == Guid.Empty ? "all ready spells" : uuid.ToString("D"),
            "total mastery level positive delta",
            ReadTotalMasteryLevels,
            () => native.TryLevelAll(manager),
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
    private SpellLevelSubmission Complete<TState>(
        NativeMutationEvidence<TState> evidence,
        ResourceCharge[]? charges = null)
    {
        var submission = SpellLevelSubmission.Attempted(evidence, charges);
        if (!evidence.IsVerified && evidence.MutationWasAttempted)
            Block($"native spell-level mutation blocked until the next lifecycle: {evidence.Format()}");
        return submission;
    }

    private bool PrerequisitesPass(object recipe)
    {
        var native = _bindings!;
        return native.LevelingPrerequisites(recipe) is { } prerequisites &&
            native.PrerequisitesCheck(prerequisites);
    }

    private bool TryResolveRecipe(Guid uuid, out object recipe)
    {
        var native = _bindings!;
        foreach (var candidate in ReadRecipes())
        {
            if (candidate is null || candidate.GetType() != native.RecipeType) continue;
            if (native.RecipeId(candidate) != uuid) continue;
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

        if (_bindings is not { } native)
        {
            reason = _bindingFailure;
            return false;
        }

        try
        {
            var upgradeResolution = _registryResolver.Resolve(
                KnownEntities.UnlockLevelAllSpells.Uuid, native.UpgradeType);
            if (!upgradeResolution.IsResolved)
            {
                return HandleRegistryFailure(
                    EntityIdentityFormatter.Format(KnownEntities.UnlockLevelAllSpells.Uuid),
                    upgradeResolution,
                    out reason);
            }

            _levelAllUpgrade = upgradeResolution.Value;
            _levelAllResolution = upgradeResolution;

            _manager = native.Manager();
            if (_manager is null)
                return Retry("SpellManager is not ready", out reason);
            _availableRecipes = native.AvailableRecipes(_manager);
            if (_availableRecipes is null) return Retry("available spell recipes are not ready", out reason);

            _ = ReadRecipes();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Block($"spell-level contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private IList ReadRecipes() =>
        _bindings!.RecipeValues(_availableRecipes!) ??
        throw new InvalidOperationException("available spell recipe contents are unavailable");

    private long ReadTotalMasteryLevels()
    {
        var native = _bindings!;
        long total = 0;
        foreach (var recipe in ReadRecipes())
            if (recipe is not null && recipe.GetType() == native.RecipeType)
                total += Math.Max(0, native.MasteryLevel(recipe));
        return total;
    }

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
}
