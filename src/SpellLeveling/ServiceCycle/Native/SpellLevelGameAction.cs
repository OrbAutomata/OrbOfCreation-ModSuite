using System;
using System.Collections;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>The shared lifecycle action for single and native-batch spell mastery purchases.</summary>
internal sealed class SpellLevelGameAction : NativeGameAction<SpellLevelNativeBindings>, ISpellLevelCapabilityPort
{
    private readonly TypedRegistryResolver _registryResolver;

    internal const string Feature = "Spell Leveling";
    private static readonly GameActionCodes Codes = new(
        CommonActionResultCodes.AdapterFault,
        CommonActionResultCodes.AdapterFault,
        SpellLevelActionResultCodes.ActionFamilyUnavailable,
        CommonActionResultCodes.AdapterFault,
        CommonActionResultCodes.AdapterFault);

    internal SpellLevelGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryPermit,
        Func<string> readOwnershipFailure,
        TypedRegistryResolver? registryResolver = null,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base(Feature, "Spellbook", Codes, readLifecycleEpoch,
            () => AutomationCycleGuard.Owns(tryPermit), readOwnershipFailure,
            (out SpellLevelNativeBindings? bindings, out string reason) =>
                SpellLevelNativeBindings.TryCreate(out bindings, out reason, resolveType, includeContract))
    {
        if (tryPermit is null) throw new ArgumentNullException(nameof(tryPermit));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public SpellLevelSubmission Submit(in SpellLevelCycleAction action)
    {
        if (action.CollectedAtEpoch <= 0)
            return SpellLevelSubmission.Rejected(CommonActionResultCodes.LifecycleReplaced, GameActionAnswer.RunChanged());
        if (!TryEnter(action.CollectedAtEpoch, out var native, out var refusal))
            return SpellLevelSubmission.FromAdmission(refusal);
        if (!TryPermit(out refusal)) return SpellLevelSubmission.FromAdmission(refusal);
        var kind = action.Kind;
        var uuid = action.Uuid;
        if (!TryReadContext(native, out var manager, out var availableRecipes, out var levelAllUpgrade, out var reason, out var cause))
            return SpellLevelSubmission.ContractUnavailable(
                cause is null ? reason : GameActionFaultLog.Reason(reason.Sentence, cause, Feature));

        try
        {
            // A native batch is admitted from the live set, so one unaffordable spell never vetoes
            // a later spell that the game's own batch can level.
            if (kind == SpellLevelActionKind.All)
                return SubmitAll(native, manager, availableRecipes, levelAllUpgrade, uuid);

            if (!TryResolveRecipe(native, availableRecipes, uuid, out var recipe))
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

            if (!PrerequisitesPass(native, recipe))
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

            return SubmitSingle(native, uuid, recipe, cost);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return SpellLevelSubmission.ContractUnavailable(
                GameActionFaultLog.Reason("spell-level mutation failed.", ex, Feature));
        }
    }

    /// <summary>
    /// What the game says the feature can currently do. The <c>All</c> half is snapshot-derivable and
    /// the worker derives it; this exists for the <c>Locked</c> half, which is not. A failure is a
    /// stable sentence, and what the game threw, if anything, is handed back unrecorded as its cause.
    /// </summary>
    public bool TryReadCapability(
        out AutoSpellLevelCapability capability,
        out GameActionReason failure,
        out Exception? cause)
    {
        capability = AutoSpellLevelCapability.Locked;
        cause = null;
        if (!TryEnter(out var native, out var refusal))
        {
            failure = refusal.Cause;
            return false;
        }
        if (!TryReadContext(native, out _, out var availableRecipes, out var levelAllUpgrade, out failure, out cause))
            return false;

        try
        {
            if (native.PurchasedLevel(levelAllUpgrade) > 0)
            {
                capability = AutoSpellLevelCapability.All;
                return true;
            }

            foreach (var recipe in ReadRecipes(native, availableRecipes))
            {
                if (recipe is null || recipe.GetType() != native.RecipeType) continue;
                if (!native.IsDiscovered(recipe)) continue;
                if (!PrerequisitesPass(native, recipe)) continue;
                capability = AutoSpellLevelCapability.Single;
                return true;
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            failure = "spell-level capability check failed.";
            cause = ex;
            return false;
        }
    }

    private SpellLevelSubmission SubmitSingle(SpellLevelNativeBindings native, Guid uuid, object recipe, object cost)
    {
#if SERVICE_CYCLE_PROFILE
        var charges = ReadCharges(native, cost);
#endif
        var submission = Mutate(
            "Spell level single",
            uuid.ToString("D"),
            $"{Feature} paid for a level of {EntityIdentityFormatter.PlayerName(uuid)}",
            "and the level did not rise",
            () => native.MasteryLevel(recipe),
            () =>
            {
                native.PerformCost(cost);
                native.PurchaseLevel(recipe);
            });
#if SERVICE_CYCLE_PROFILE
        submission = submission.WithCharges(charges);
#endif
        return submission;
    }

    /// <summary>
    /// Presses the game and verifies one sentinel: the level rose, and any rise commits. A press that
    /// did not raise it, a throw included, is a paid-without-level fault whose exception goes whole to
    /// the suite log. An expected throw reading the level before the press is a fault with nothing
    /// paid; an unexpected one escapes.
    /// </summary>
    private static SpellLevelSubmission Mutate(
        string feature,
        string identity,
        string pressed,
        string noRise,
        Func<long> readLevel,
        Action press)
    {
        var pressedGame = false;
        try
        {
            var evidence = NativeMutationVerifier.Execute(
                feature,
                identity,
                "mastery level increased",
                readLevel,
                () =>
                {
                    pressedGame = true;
                    press();
                },
                static (before, after) => after > before);
            return SpellLevelSubmission.Attempted(evidence, pressed, noRise);
        }
        catch (Exception ex) when (pressedGame && !ExceptionDetail.IsProcessFatal(ex))
        {
            return SpellLevelSubmission.ThrewAfterPayment(pressed, ex);
        }
    }

#if SERVICE_CYCLE_PROFILE
    private ResourceCharge[]? ReadCharges(SpellLevelNativeBindings native, object cost)
    {
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
#endif

    private SpellLevelSubmission SubmitAll(SpellLevelNativeBindings native, object manager, object availableRecipes, object levelAllUpgrade, Guid uuid)
    {
        // The capability is re-read rather than trusted: the worker decided All from a snapshot, and
        // an upgrade refunded or reset since then would make this a call the game refuses.
        if (native.PurchasedLevel(levelAllUpgrade) <= 0)
        {
            return SpellLevelSubmission.Rejected(
                SpellLevelActionResultCodes.LevelAllLocked,
                "the level-all upgrade is no longer committed");
        }

        var discovered = false;
        var prerequisitePassed = false;
        foreach (var recipe in ReadRecipes(native, availableRecipes))
        {
            if (recipe is null || recipe.GetType() != native.RecipeType) continue;
            if (!native.IsDiscovered(recipe)) continue;
            discovered = true;
            if (!PrerequisitesPass(native, recipe)) continue;
            prerequisitePassed = true;
            if (!native.IsReady(recipe)) continue;

            var cost = native.LevelCost(recipe);
            if (cost is null || !native.HasEnough(cost)) continue;

            return SubmitAllVerified(native, manager, availableRecipes, uuid);
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

    private SpellLevelSubmission SubmitAllVerified(SpellLevelNativeBindings native, object manager, object availableRecipes, Guid uuid)
    {
        return Mutate(
            "Spell level all",
            uuid == Guid.Empty ? "all ready spells" : uuid.ToString("D"),
            $"{Feature} pressed level all",
            "and no spell's level rose",
            () => ReadTotalMasteryLevels(native, availableRecipes),
            () => native.TryLevelAll(manager));
    }

    private static bool PrerequisitesPass(SpellLevelNativeBindings native, object recipe)
    {
        return native.LevelingPrerequisites(recipe) is { } prerequisites &&
            native.PrerequisitesCheck(prerequisites);
    }

    private static bool TryResolveRecipe(SpellLevelNativeBindings native, object availableRecipes, Guid uuid, out object recipe)
    {
        foreach (var candidate in ReadRecipes(native, availableRecipes))
        {
            if (candidate is null || candidate.GetType() != native.RecipeType) continue;
            if (native.RecipeId(candidate) != uuid) continue;
            recipe = candidate;
            return true;
        }

        recipe = null!;
        return false;
    }

    // Native instances are local to this attempt; only the compiled binding set lives across calls.
    private bool TryReadContext(
        SpellLevelNativeBindings native,
        out object manager,
        out object availableRecipes,
        out object levelAllUpgrade,
        out GameActionReason reason,
        out Exception? cause)
    {
        manager = availableRecipes = levelAllUpgrade = null!;
        cause = null;
        try
        {
            var resolution = _registryResolver.Resolve(KnownEntities.UnlockLevelAllSpells.Uuid, native.UpgradeType);
            if (!resolution.IsResolved)
            {
                reason = resolution.FormatCause().Around(
                    EntityIdentityFormatter.Format(KnownEntities.UnlockLevelAllSpells.Uuid) + " resolution failed. ");
                return false;
            }
            levelAllUpgrade = resolution.Value!;
            manager = native.Manager()!;
            if (manager is null) return Retry("The spell list is not ready", out reason);
            availableRecipes = native.AvailableRecipes(manager)!;
            if (availableRecipes is null) return Retry("available spell recipes are not ready", out reason);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            cause = ex;
            return Retry("spell-level contract initialization failed.", out reason);
        }
    }

    private static IList ReadRecipes(SpellLevelNativeBindings native, object availableRecipes) =>
        native.RecipeValues(availableRecipes) ??
        throw new InvalidOperationException("available spell recipe contents are unavailable");

    private static long ReadTotalMasteryLevels(SpellLevelNativeBindings native, object availableRecipes)
    {
        long total = 0;
        foreach (var recipe in ReadRecipes(native, availableRecipes))
            if (recipe is not null && recipe.GetType() == native.RecipeType)
                total += Math.Max(0, native.MasteryLevel(recipe));
        return total;
    }

    private static bool Retry(GameActionReason message, out GameActionReason reason)
    {
        reason = message;
        return false;
    }
}
