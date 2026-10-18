using System;
using System.Collections;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Doctrine-shaped re-drive of the native Scribe UI composite. All fallible suite-owned reads and
/// decisions finish before payment; the exact work admission is the one postcondition sentinel.
/// </summary>
internal sealed partial class AutoScribeOneShotCraftGameAction : IDisposable
{
    private readonly TypedRegistryResolver _registry;
    private readonly AutoScribeIdentityProfile _profile;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly int _mainThreadId;
    private AutoScribeNativeBindings? _bindings;
    private CraftingPlayerNativeBindings? _playerBindings;
    private string _bindingFailure = string.Empty;
    private string _playerBindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoScribeOneShotCraftGameAction(
        TypedRegistryResolver registry,
        AutoScribeIdentityProfile profile,
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal bool PlayerCraftingBindingsAvailable => _playerBindings is not null;
    internal string PlayerCraftingBindingFailure => _playerBindingFailure;
    internal bool IsQuarantined => _quarantineReason.Length != 0;
    internal string QuarantineReason => _quarantineReason;

    internal AutoScribeSubmission Submit(in AutoScribeCycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return AutoScribeSubmission.Fault(
                AutoScribeActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        long liveLifecycle;
        try
        {
            liveLifecycle = _readLifecycleEpoch();
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoScribeSubmission.Fault(
                AutoScribeActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Scholar > Scribe", ex));
        }
        if (liveLifecycle <= 0 || liveLifecycle != action.CollectedAtEpoch)
            return AutoScribeSubmission.Reject(
                CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());
        if (_quarantineReason.Length != 0)
            return AutoScribeSubmission.Fault(
                AutoScribeActionResultCodes.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return AutoScribeSubmission.Fault(
                AutoScribeActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("Scholar > Scribe", _bindingFailure));

        try
        {
            if (!TryResolveRelation(
                    in action,
                    native,
                    out var recipe,
                    out var scroll,
                    out var recipeType,
                    out var activeQueue,
                    out var reason,
                    out var rejection,
                    out var retryable))
                return retryable
                    ? AutoScribeSubmission.Reject(rejection, reason, retryable: true)
                    : AutoScribeSubmission.Fault(rejection, reason);
            if (!native.RecipeVisible(recipe))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.RecipeUnavailable,
                    $"The game is not showing {EntityIdentityFormatter.PlayerName(action.RecipeId)} yet.");
            if (!native.QueueHasRoom(activeQueue))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.QueueFull,
                    "ActiveScribeInstances.HasEmptySpot() refused before payment.");

            var craftLevel = FindHighestAffordableLevel(
                native,
                recipe,
                recipeType,
                action.Level);
            if (craftLevel < action.Level)
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.Unaffordable,
                    $"Recipe {EntityIdentityFormatter.PlayerName(action.RecipeId)} could not afford requested level " +
                    $"{action.Level} or any stronger level.");
            if (HasCompetingSupply(native, action.RecipeId, craftLevel, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.CompetingSupply,
                    reason);
            if (!TryValidateTarget(native, scroll, action.ScrollId, craftLevel, out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.TargetUnavailable,
                    reason);

            var level = new BigDouble(craftLevel, 0);
            var zero = BigDouble.Zero;
            var totalCost = Returned(
                native.RecipeTotalCost(recipe, zero, level),
                "CraftingRecipeSO.GetTotalCost");
            if (totalCost.GetType() != native.ResourceCostType)
                return AutoScribeSubmission.Fault(
                    AutoScribeActionResultCodes.ContractUnavailable,
                    "The game did not publish a price for this recipe, so nothing could be " +
                    "paid. Open Scholar > Scribe and read again.");
            if (!native.CostHasEnough(totalCost))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.Unaffordable,
                    $"GetTotalCost(0,{craftLevel}).HasEnough() refused recipe {EntityIdentityFormatter.PlayerName(action.RecipeId)}.");

            if (!TryCaptureMutationPermit(out reason))
                return AutoScribeSubmission.Reject(
                    AutoScribeActionResultCodes.MutationPermitUnavailable,
                    reason);

            // Payment is deliberately the final pre-native risk. From here onward no metadata is
            // discovered and no policy/configuration decision is made.
            return Execute(
                in action,
                native,
                recipe,
                activeQueue,
                craftLevel,
                level);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoScribeSubmission.Fault(
                AutoScribeActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Scholar > Scribe", ex));
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
    }

    private AutoScribeSubmission Execute(
        in AutoScribeCycleAction action,
        AutoScribeNativeBindings native,
        object recipe,
        object activeQueue,
        int craftLevel,
        BigDouble level)
    {
        var stage = "Payment";
        var nativeCalls = 1;
        object? instance = null;
        var instant = false;
        var admissionKnown = false;
        try
        {
            native.RecipePurchase(recipe, level, BigDouble.Zero);

            stage = "Construction";
            nativeCalls = 2;
            instance = native.ConstructInstance(recipe, level);
            if (instance.GetType() != native.InstanceType)
                throw new InvalidOperationException(
                    "CraftingInstance construction returned the wrong native type.");

            stage = "Initiation";
            nativeCalls = 3;
            native.InstanceInitiate(instance);

            stage = "Admission";
            instant = native.InstanceInstantCheck(instance);
            admissionKnown = true;
            nativeCalls = 4;
            if (instant)
                native.InstanceInstant(instance);
            else
                native.QueueAdd(activeQueue, instance);

            stage = "Verification";
            var verified = instant
                ? native.InstanceExpired(instance)
                : ContainsExactInstance(RequireList(
                    native.InstanceListValue(activeQueue),
                    "ActiveScribeInstances.value"), instance);
            if (!verified)
                return Quarantine(
                    in action,
                    AutoScribeActionResultCodes.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    GameActionAnswer.ChangeNotSeen("Scholar > Scribe"));
            return new AutoScribeSubmission(NativeActionOutcome.Commit(
                new NativeMutationCallOutcome(nativeCalls, 1, 1),
                "The exact crafted work reached its native destination."));
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            var landed = WorkObservedBestEffort(
                native,
                activeQueue,
                instance,
                admissionKnown,
                instant);
            if (landed)
                return new AutoScribeSubmission(NativeActionOutcome.Commit(
                    new NativeMutationCallOutcome(nativeCalls, 1, 1),
                    "The exact crafted work was observable after native code threw."));
            return Quarantine(
                in action,
                AutoScribeActionResultCodes.PostPaymentFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                GameActionAnswer.GameErrored("Scholar > Scribe", ex));
        }
        catch (Exception ex)
        {
            Quarantine(
                in action,
                AutoScribeActionResultCodes.PostPaymentFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                GameActionAnswer.GameErrored("Scholar > Scribe", ex));
            throw;
        }
    }

    private AutoScribeSubmission Quarantine(
        in AutoScribeCycleAction action,
        ServiceActionResultCode code,
        string stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        string reason)
    {
        _quarantineReason =
            $"Auto Scribe is quarantined for this lifecycle after {stage} on " +
            $"{EntityIdentityFormatter.PlayerName(action.RecipeId)}: {reason}";
        return new AutoScribeSubmission(NativeActionOutcome.Fault(
            code,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
            _quarantineReason));
    }

    private bool TryResolveRelation(
        in AutoScribeCycleAction action,
        AutoScribeNativeBindings native,
        out object recipe,
        out object scroll,
        out object recipeType,
        out object activeQueue,
        out string reason,
        out ServiceActionResultCode rejection,
        out bool retryable)
    {
        recipe = null!;
        scroll = null!;
        recipeType = null!;
        activeQueue = null!;
        retryable = false;
        if (!_profile.TryFindByRecipe(action.RecipeId, out var recipeRole) ||
            !_profile.TryFindByScroll(action.ScrollId, out var scrollRole) ||
            recipeRole.Ordinal != scrollRole.Ordinal ||
            recipeRole.Recipe?.Uuid != action.RecipeId ||
            recipeRole.Scroll.Uuid != action.ScrollId)
        {
            reason =
                $"Action recipe {EntityIdentityFormatter.PlayerName(action.RecipeId)} and Scroll {EntityIdentityFormatter.PlayerName(action.ScrollId)} do not identify " +
                "one audited Auto Scribe role.";
            rejection = AutoScribeActionResultCodes.RelationshipMismatch;
            return false;
        }

        var recipeResolution = _registry.Resolve(action.RecipeId, native.RecipeType);
        var scrollResolution = _registry.Resolve(action.ScrollId, native.ConsumableType);
        var enchantmentResolution = _registry.Resolve(
            recipeRole.Enchantment.Uuid,
            native.EnchantmentType);
        var typeResolution = _registry.Resolve(_profile.RecipeType.Uuid, native.RecipeTypeType);
        var registryResolution = _registry.Resolve(
            _profile.RecipeRegistry.Uuid,
            native.RecipeListType);
        var activeResolution = _registry.Resolve(
            _profile.ActiveInstances.Uuid,
            native.InstanceListType);
        var automaticResolution = _registry.Resolve(
            _profile.AutomaticInstances.Uuid,
            native.InstanceListType);
        if (!recipeResolution.IsResolved || !scrollResolution.IsResolved ||
            !enchantmentResolution.IsResolved || !typeResolution.IsResolved ||
            !registryResolution.IsResolved || !activeResolution.IsResolved ||
            !automaticResolution.IsResolved)
        {
            reason = FirstFailure(
                out retryable,
                recipeResolution,
                scrollResolution,
                enchantmentResolution,
                typeResolution,
                registryResolution,
                activeResolution,
                automaticResolution);
            rejection = AutoScribeActionResultCodes.IdentityUnavailable;
            return false;
        }

        recipe = recipeResolution.Value!;
        scroll = scrollResolution.Value!;
        recipeType = typeResolution.Value!;
        activeQueue = activeResolution.Value!;
        if (native.Identity(recipe) != action.RecipeId ||
            native.ConsumableIdentity(scroll) != action.ScrollId ||
            native.EnchantmentIdentity(enchantmentResolution.Value!) !=
                recipeRole.Enchantment.Uuid)
        {
            reason =
                "The live recipe, Scroll, or enchantment identity did not equal the action relation.";
            rejection = AutoScribeActionResultCodes.RelationshipMismatch;
            return false;
        }
        if (!ValidateRecipeRegistry(native, registryResolution.Value!, out reason) ||
            !ValidateRecipeRelation(
                native,
                recipe,
                recipeType,
                scroll,
                recipeRole.Enchantment.Uuid,
                out reason))
        {
            rejection = AutoScribeActionResultCodes.RelationshipMismatch;
            return false;
        }

        reason = string.Empty;
        rejection = CommonActionResultCodes.Committed;
        return true;
    }

    private bool ValidateRecipeRegistry(
        AutoScribeNativeBindings native,
        object registry,
        out string reason)
    {
        if (native.RecipeListValue(registry) is not { } values)
        {
            reason = "ScribeCraftingRecipes.value was not a live native list.";
            return false;
        }
        var expected = 0;
        for (var roleIndex = 0; roleIndex < _profile.Roles.Count; roleIndex++)
            if (_profile.Roles[roleIndex].IsProducible) expected++;
        if (values.Count != expected)
        {
            reason =
                $"ScribeCraftingRecipes contained {values.Count} recipes; exactly {expected} are audited.";
            return false;
        }
        for (var roleIndex = 0; roleIndex < _profile.Roles.Count; roleIndex++)
        {
            var role = _profile.Roles[roleIndex];
            if (!role.Recipe.HasValue) continue;
            var found = false;
            foreach (var value in values)
            {
                if (value is null || value.GetType() != native.RecipeType)
                {
                    reason =
                "The game's scribe recipe list holds an entry this build does not model.";
                    return false;
                }
                if (native.Identity(value) == role.Recipe.Value.Uuid)
                    found = true;
            }
            if (!found)
            {
                reason =
                    $"ScribeCraftingRecipes omitted audited recipe {EntityIdentityFormatter.PlayerName(role.Recipe.Value.Uuid)}.";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool ValidateRecipeRelation(
        AutoScribeNativeBindings native,
        object recipe,
        object recipeType,
        object scroll,
        Guid expectedEnchantment,
        out string reason)
    {
        if (native.CraftingTypes(recipe) is not { } types)
        {
            reason = "The game did not publish what this recipe scribes.";
            return false;
        }
        var typeCount = 0;
        foreach (var value in types)
        {
            if (!ReferenceEquals(value, recipeType))
            {
                reason = "The live recipe referenced a non-Scribe crafting type.";
                return false;
            }
            typeCount++;
        }
        if (typeCount != 1 ||
            !native.UseQuantityAsLevel(recipe) ||
            !native.IsLevelType(recipeType) ||
            !ReferenceEquals(
                Returned(native.RecipeMainType(recipe), "CraftingRecipeSO.GetMainType"),
                recipeType))
        {
            reason =
                "The live recipe did not prove exactly one levelled Scribe crafting-type relation.";
            return false;
        }

        var outputs = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.CompleteEffects(recipe),
                     "CraftingRecipeSO.completeEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts(blockValue) is not { } scripts)
            {
                reason = "The live recipe complete-effect graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || !native.InstantScriptType.IsInstanceOfType(script))
                {
                    reason = "The live recipe output graph contained an unknown script.";
                    return false;
                }
                if (script.GetType() != native.GainType) continue;
                outputs++;
                if (!ReferenceEquals(native.GainConsumable(script), scroll))
                {
                    reason = "The live Scribe recipe output was not the action Scroll.";
                    return false;
                }
            }
        }
        if (outputs != 1)
        {
            reason =
                $"The live Scribe recipe had {outputs} ConsumableGainEffect outputs; exactly one is required.";
            return false;
        }

        var requestCount = 0;
        var enchantCount = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects(scroll),
                     "ConsumableSO.onUseEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts(blockValue) is not { } scripts)
            {
                reason = "The live Scroll on-use graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || !native.InstantScriptType.IsInstanceOfType(script))
                {
                    reason = "The live Scroll on-use graph contained an unknown script.";
                    return false;
                }
                if (script.GetType() == native.RequestType) requestCount++;
                if (script.GetType() != native.EnchantScriptType) continue;
                enchantCount++;
                var enchantment = native.EnchantScriptEnchantment(script);
                if (enchantment is null || enchantment.GetType() != native.EnchantmentType ||
                    native.EnchantmentIdentity(enchantment) != expectedEnchantment)
                {
                    reason =
                        $"The live Scroll enchantment did not equal audited {EntityIdentityFormatter.PlayerName(expectedEnchantment)}.";
                    return false;
                }
            }
        }
        if (requestCount != 1 || enchantCount != 1)
        {
            reason =
                $"The live Scroll had {requestCount} target requests and {enchantCount} enchant effects; " +
                "exactly one of each is required.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private bool HasCompetingSupply(
        AutoScribeNativeBindings native,
        Guid recipeId,
        int level,
        out string reason)
    {
        foreach (var queueIdentity in new[]
        {
            _profile.ActiveInstances,
            _profile.AutomaticInstances,
        })
        {
            var resolution = _registry.Resolve(queueIdentity.Uuid, native.InstanceListType);
            if (!resolution.IsResolved)
            {
                reason = resolution.Format();
                return true;
            }
            if (native.InstanceListValue(resolution.Value!) is not { } work)
            {
                reason = $"{EntityIdentityFormatter.PlayerName(queueIdentity.Uuid)} did not expose its exact CraftingInstance list.";
                return true;
            }
            foreach (var value in work)
            {
                if (value is null) continue;
                if (value.GetType() != native.InstanceType)
                {
                    reason = $"{EntityIdentityFormatter.PlayerName(queueIdentity.Uuid)} contained a non-CraftingInstance value.";
                    return true;
                }
                if (native.InstanceRecipe(value) == recipeId &&
                    Level(native.InstanceQuantity(value)) >= level &&
                    !native.InstanceExpired(value))
                {
                    reason =
                        $"{EntityIdentityFormatter.PlayerName(queueIdentity.Uuid)} already supplies recipe {EntityIdentityFormatter.PlayerName(recipeId)} at level " +
                        $"{level} or higher.";
                    return true;
                }
            }
        }
        reason = string.Empty;
        return false;
    }

    private static bool TryValidateTarget(
        AutoScribeNativeBindings native,
        object scroll,
        Guid scrollId,
        int level,
        out string reason)
    {
        object? options = null;
        var requests = 0;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects(scroll),
                     "ConsumableSO.onUseEffects"))
        {
            if (blockValue is null || blockValue.GetType() != native.InstantBlockType ||
                native.EffectScripts(blockValue) is not { } scripts)
            {
                reason = "The live Scroll target graph changed type.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || script.GetType() != native.RequestType) continue;
                requests++;
                options = native.TargetOptions(script);
            }
        }
        if (requests != 1 || options is null || options.GetType() != native.OptionsType)
        {
            reason =
                $"Scroll {EntityIdentityFormatter.PlayerName(scrollId)} did not expose exactly one exact TargetSelectOptions.";
            return false;
        }
        var targeting = Returned(native.GetTargeting(options), "TargetSelectOptions.GetTargeting");
        if (targeting.GetType() != native.TargetType)
        {
            reason = $"Scroll {EntityIdentityFormatter.PlayerName(scrollId)} did not resolve the exact TargetStructure selector.";
            return false;
        }
        var scaling = Returned(native.ScalingBasic(new BigDouble(level, 0)), "ScalingInfo.Basic");
        if (scaling.GetType() != native.ScalingType ||
            native.GetRandomList(targeting, scaling) is not { } candidates)
        {
            reason = $"Scroll {EntityIdentityFormatter.PlayerName(scrollId)} target selection changed contract.";
            return false;
        }
        if (candidates.Count == 0)
        {
            reason = $"Scroll {EntityIdentityFormatter.PlayerName(scrollId)} has no valid live target at level {level}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static int FindHighestAffordableLevel(
        AutoScribeNativeBindings native,
        object recipe,
        object recipeType,
        int minimumLevel)
    {
        if (minimumLevel <= 0 ||
            !CanBuyAt(native, recipe, minimumLevel))
            return 0;

        var sharedMaximum = Math.Max(1, native.MaxStartingLevel(recipeType));
        var affordable = minimumLevel;
        if (sharedMaximum > affordable)
        {
            if (!CanBuyAt(native, recipe, sharedMaximum))
                return FindHighestAffordableBetween(
                    native,
                    recipe,
                    affordable,
                    sharedMaximum - 1);
            affordable = sharedMaximum;
        }
        if (affordable == int.MaxValue)
            return affordable;

        // Audited levelled Scribe costs grow monotonically. Bracket the first unaffordable level,
        // then refine the frontier without an unbounded linear scan.
        while (affordable < int.MaxValue)
        {
            var candidate = affordable > int.MaxValue / 2
                ? int.MaxValue
                : Math.Max(affordable + 1, affordable * 2);
            if (!CanBuyAt(native, recipe, candidate))
                return FindHighestAffordableBetween(
                    native,
                    recipe,
                    affordable,
                    candidate - 1);
            affordable = candidate;
        }
        return affordable;
    }

    private static int FindHighestAffordableBetween(
        AutoScribeNativeBindings native,
        object recipe,
        int affordable,
        int maximum)
    {
        var low = affordable + 1;
        var high = maximum;
        var found = affordable;
        while (low <= high)
        {
            var candidate = low + ((high - low) / 2);
            if (CanBuyAt(native, recipe, candidate))
            {
                found = candidate;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }
        return found;
    }

    private static bool CanBuyAt(
        AutoScribeNativeBindings native,
        object recipe,
        int level) =>
        native.RecipeCanBuyAt(recipe, new BigDouble(level, 0));

    private static bool ContainsExactInstance(IList work, object instance)
    {
        foreach (var value in work)
            if (ReferenceEquals(value, instance)) return true;
        return false;
    }

    private static bool WorkObservedBestEffort(
        AutoScribeNativeBindings native,
        object activeQueue,
        object? instance,
        bool admissionKnown,
        bool instant)
    {
        try
        {
            if (instance is null || !admissionKnown) return false;
            if (instant) return native.InstanceExpired(instance);
            return ContainsExactInstance(RequireList(
                native.InstanceListValue(activeQueue),
                "ActiveScribeInstances.value"), instance);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }

    private bool TryCaptureMutationPermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readOwnershipFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Auto Scribe no longer owns CraftingQueueSubmission.";
            return false;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            reason = GameActionAnswer.CouldNotRead("Scholar > Scribe", ex);
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (AutoScribeNativeBindings.TryCreate(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
        }
        else
        {
            _bindings = null;
            _bindingFailure = reason;
        }
        if (CraftingPlayerNativeBindings.TryCreate(out var player, out var playerReason))
        {
            _playerBindings = player;
            _playerBindingFailure = string.Empty;
        }
        else
        {
            _playerBindings = null;
            _playerBindingFailure = playerReason;
        }
    }

    private static string FirstFailure(
        out bool retryable,
        params TypedRegistryResolution[] resolutions)
    {
        TypedRegistryResolution? firstRetryable = null;
        for (var index = 0; index < resolutions.Length; index++)
        {
            var resolution = resolutions[index];
            if (resolution.IsResolved) continue;
            if (!resolution.IsRetryable)
            {
                retryable = false;
                return resolution.Format();
            }
            firstRetryable ??= resolution;
        }
        if (firstRetryable is not null)
        {
            retryable = true;
            return firstRetryable.Format();
        }
        retryable = false;
        return "An Auto Scribe live identity was unavailable.";
    }

    private static IList RequireList(object? value, string contract) =>
        value as IList ??
        throw new InvalidOperationException(contract + " was not a native list.");

    private static IEnumerable RequireEnumerable(object? value, string contract) =>
        value as IEnumerable ??
        throw new InvalidOperationException(contract + " was not enumerable.");

    private static object Returned(object? value, string member) =>
        value ?? throw new InvalidOperationException(member + " returned null.");

    private static int Level(BigDouble number)
    {
        var scalar = number.ToDouble();
        if (!double.IsFinite(scalar) || scalar < 1d || scalar > int.MaxValue)
            throw new InvalidOperationException("CraftingInstance quantity was not a valid level.");
        return (int)Math.Floor(scalar);
    }

}
