using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped discovery transaction — one press of a discovery screen's Discover button.
/// </summary>
/// <remarks>
/// <para>
/// The ladder mirrors the button the player presses. <c>UICostButton.OnClick</c> refuses unless the
/// row shows no error and <c>costList.HasEnough()</c>, then pays that same list and invokes
/// <c>UIDiscoverablePage.HandleClick</c>, which re-asks <c>IsGlyphSelectionValid()</c> and calls
/// <c>IDiscoverable.Discover()</c>. Payment before the call is the button's own order, kept here.
/// </para>
/// <para>
/// Nothing stages a selection. The page's selection lists exist only so the page can turn a click
/// on a row back into the discoverable the caller already named, and the discover path never reads
/// them; the recipe books the row's visibility depends on are read by the game inside
/// <c>IsDiscoverVisible()</c>, which is why no book is ever this action's argument.
/// </para>
/// </remarks>
internal sealed class GenericDiscoveryGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private GenericDiscoveryNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal GenericDiscoveryGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch,
            identity.Read,
            identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal GenericDiscoverySubmission Submit(in GenericDiscoveryAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("the discovery screen this is on"));

        long currentEpoch;
        try
        {
            currentEpoch = _readLifecycleEpoch();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != currentEpoch)
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            if (!native.SupportedTypes.TryGetValue(action.ExpectedNativeType, out var expectedType))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.UnsupportedType,
                    "Native type " + action.ExpectedNativeType +
                    " is not in the audited generic discovery family.");
            var resolution = _registry.Resolve(action.TargetId, expectedType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? GameActionAnswer.Replaced("discovery")
                        : resolution.Reason);
            var target = resolution.Value!;
            if (!native.DiscoverableType.IsInstanceOfType(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.IdentityUnavailable,
                    "The exact registered " + action.ExpectedNativeType +
                    " does not implement IDiscoverable at the action boundary.");
            var name = EntityIdentityFormatter.PlayerName(action.TargetId);
            if (!TryAdmitScreen(native, action.ExpectedNativeType, out var screenRefusal))
                return screenRefusal;
            if (native.GetGlyphRecipe(target).Count == 0)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.GlyphRecipeEmpty,
                    "The game builds " + name + " from glyphs and it names none, so no " +
                    "discovery screen ever draws a Discover button for it. Nothing was spent.");

            if (native.IsDiscovered(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.AlreadyDiscovered,
                    name + " is already discovered, so its row no longer offers the button. " +
                    "Nothing was spent.");
            if (!native.IsVisible(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.NotVisible,
                    name + " is not drawn on its discovery screen yet, so there is no row to " +
                    "press. " + MissingBook(native, target) + " Nothing was spent.");
            if (!native.CanDiscover(target))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.DiscoveryUnavailable,
                    name + " is drawn but its Discover button reads \"Has Requirements\", so " +
                    "the game refuses the press. Nothing was spent.");

            var cost = native.GetCost(target);
            if (cost is null || cost.GetType() != native.CostType)
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.ContractUnavailable,
                    "The game's discovery price for " + name +
                    " could not be read, so whether the button would take the press is unknown.");
            if (!native.HasEnough(cost))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.Unaffordable,
                    "The discovery price of " + name +
                    " is more than you hold, and the button only takes a press you can pay for. " +
                    "Nothing was spent.");
            if (!TryCapturePermit(out var permitReason))
                return GenericDiscoverySubmission.Reject(
                    GenericDiscoveryPreflight.MutationPermitUnavailable,
                    permitReason);

            return Execute(in action, native, target, cost);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("the discovery screen this is on", exception));
        }
    }

    /// <summary>
    /// Which Recipe Book a hidden row is waiting on, named. The refusal used to recite the general
    /// rule — "its own prerequisites and every recipe book it belongs to" — which left the caller to
    /// go and work out which book that was. The recipe names its core glyphs, each core glyph names
    /// the book it is the internal half of, and a book answers whether it is owned, so the first
    /// unowned one is the answer.
    /// </summary>
    private static string MissingBook(GenericDiscoveryNativeBindings native, object target)
    {
        var recipe = native.GetGlyphRecipe(target);
        for (var index = 0; index < recipe.Count; index++)
        {
            var glyph = recipe[index];
            if (glyph is null) continue;
            var book = native.GetGlyphRecipeBook(glyph);
            if (book is null || native.IsRecipeBookOwned(book)) continue;
            return "It needs the " +
                EntityIdentityFormatter.PlayerName(native.GetRecipeBookId(book)) +
                " recipe book, which is not owned.";
        }
        return "Every recipe book it is made of is owned, so what is left is its own " +
            "prerequisites.";
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

    /// <summary>
    /// A locked screen draws no rows at all, so it is a different answer from a row the game does
    /// not draw and from a price you cannot pay. <c>ViewSO.IsAvailable()</c> is the game's own
    /// question about the screen, so this is read, never inferred.
    /// </summary>
    /// <remarks>
    /// Each discovery tree names the view it is drawn under, as an authored
    /// <c>DiscoveryTreeSO.viewLocation</c> breadcrumb whose last element is that view, so five of
    /// the six discoverable kinds are pinned by the game's own authoring rather than inferred.
    /// <c>AlchemyRecipeSO</c> is the exception and stays ungated: its rows are drawn on two
    /// screens, because <c>ConceptDiscoveryTree</c> ends at <c>ScholarConceptDiscover</c> while
    /// <c>AlchemyDiscoveryTree</c> ends at <c>AlchAlchemyDiscover</c>, and naming either one here
    /// would refuse a row the other screen is happily drawing. Its rows keep the answer they
    /// already give: <c>IsDiscoverVisible()</c> is false while the game does not draw them.
    /// </remarks>
    private bool TryAdmitScreen(
        GenericDiscoveryNativeBindings native,
        string expectedNativeType,
        out GenericDiscoverySubmission refusal)
    {
        refusal = default;
        Guid screen;
        string path;
        switch (expectedNativeType)
        {
            case "SpellRecipeSO":
                screen = KnownEntities.MagicSpellbookLearn.Uuid;
                path = "Magic > Spellbook > Unlock";
                break;
            case "GlyphSO":
                screen = KnownEntities.MagicGlyphsDiscover.Uuid;
                path = "Magic > Augments > Glyphcraft";
                break;
            case "RitualSO":
                screen = KnownEntities.RitualsDiscover.Uuid;
                path = "Rituals > Discover";
                break;
            case "EquipmentSO":
                screen = KnownEntities.WorkshopArtifactCreate.Uuid;
                path = "Workshop > Artifacts > Create";
                break;
            case "TimeRuneSO":
                screen = KnownEntities.TimeTimeRuneCreate.Uuid;
                path = "Time > Time Runes > Create";
                break;
            default:
                return true;
        }
        var resolution = _registry.Resolve(screen, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
        {
            refusal = GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ContractUnavailable,
                "The game's " + path + " screen could not be read, so whether the game would " +
                "draw a row to discover is unknown.");
            return false;
        }
        if (!native.IsViewAvailable(view))
        {
            refusal = GenericDiscoverySubmission.Reject(
                GenericDiscoveryPreflight.ScreenLocked,
                path + " is not unlocked yet, so the game draws no row to discover. " +
                "Nothing was spent.");
            return false;
        }
        return true;
    }

    private GenericDiscoverySubmission Execute(
        in GenericDiscoveryAction action,
        GenericDiscoveryNativeBindings native,
        object target,
        object cost)
    {
        var stage = GenericDiscoveryNativeStage.Payment;
        var nativeCalls = 0;
        try
        {
            nativeCalls = 1;
            native.PerformCost(cost);
            stage = GenericDiscoveryNativeStage.Discover;
            nativeCalls = 2;
            native.Discover(target);
            stage = GenericDiscoveryNativeStage.Verification;
            return native.IsDiscovered(target)
                ? Verified(nativeCalls)
                : Fault(
                    in action,
                    GenericDiscoveryPreflight.VerificationFailed,
                    stage,
                    NativeMutationOutcome.PostconditionFailed,
                    nativeCalls,
                    "The requested target remained undiscovered after the native callback.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (IsDiscoveredBestEffort(native, target))
                return Verified(nativeCalls);
            return Fault(
                in action,
                GenericDiscoveryPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                nativeCalls,
                "Native generic discovery threw before the requested discovered outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static GenericDiscoverySubmission Verified(
        int nativeCalls) =>
        new(
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(nativeCalls, 1, 1),
            "The exact requested UUID is discovered.");

    private static GenericDiscoverySubmission Fault(
        in GenericDiscoveryAction action,
        GenericDiscoveryPreflight preflight,
        GenericDiscoveryNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        string reason)
    {
        var exactReason = "Generic discovery " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.TargetId) + ": " + reason;
        return new GenericDiscoverySubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0),
            exactReason);
    }

    private static bool IsDiscoveredBestEffort(
        GenericDiscoveryNativeBindings native,
        object target)
    {
        try { return native.IsDiscovered(target); }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    private bool TryCapturePermit(out string reason)
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
                reason = "The suite no longer owns GenericDiscovery.";
            return false;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            reason = "The generic discovery mutation permit could not be captured: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (GenericDiscoveryNativeBindings.TryCreate(
                out var bindings,
                out var reason,
                _resolveType,
                _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is not
        StackOverflowException and not
        OutOfMemoryException and not
        AccessViolationException;
}
