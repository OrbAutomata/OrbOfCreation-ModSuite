using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-bound explicit-layout loadout-add boundary.</summary>
internal sealed class SpellWorkbenchGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private SpellWorkbenchNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellWorkbenchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
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

    internal SpellWorkbenchStagedLayout ReadStagedLayout()
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchPreflight.WrongThread,
                "The staged Spellcraft layout must be read on the Unity main thread.");
        if (_bindings is not { } native)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Magic > Spellbook"));
        try
        {
            var manager = native.ReadManager();
            if (manager is null)
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchPreflight.SelectionUnavailable,
                    "Spellcraft is not available in the current game state.");
            if (!TryReadStagedGlyphs(
                    native,
                    native.ReadGlyphValues(native.ReadCore(manager)),
                    out var core,
                    out var coreReason))
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchPreflight.SelectionUnavailable,
                    coreReason);
            if (!TryReadStagedGlyphs(
                    native,
                    native.ReadGlyphValues(native.ReadAugments(manager)),
                    out var augments,
                    out var augmentReason))
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchPreflight.SelectionUnavailable,
                    augmentReason);
            return SpellWorkbenchStagedLayout.Captured(core, augments);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchPreflight.ContractUnavailable,
                "The staged Spellcraft layout could not be read: " +
                exception.GetBaseException().Message);
        }
    }

    private static bool TryReadStagedGlyphs(
        SpellWorkbenchNativeBindings native,
        IList values,
        out SpellWorkbenchGlyphStack[] layout,
        out string reason)
    {
        var counts = new Dictionary<Guid, int>();
        var order = new List<Guid>();
        for (var index = 0; index < values.Count; index++)
        {
            var glyph = values[index];
            if (glyph is null || glyph.GetType() != native.GlyphType)
            {
                layout = Array.Empty<SpellWorkbenchGlyphStack>();
                reason = "The staged Spellcraft layout contains an invalid glyph.";
                return false;
            }
            var id = native.ReadIdentity(glyph);
            if (id == Guid.Empty)
            {
                layout = Array.Empty<SpellWorkbenchGlyphStack>();
                reason = "A staged Spellcraft glyph has no stable identity.";
                return false;
            }
            if (!counts.ContainsKey(id)) order.Add(id);
            counts.TryGetValue(id, out var count);
            if (count == int.MaxValue)
            {
                layout = Array.Empty<SpellWorkbenchGlyphStack>();
                reason = "A staged Spellcraft glyph count exceeds the supported range.";
                return false;
            }
            counts[id] = count + 1;
        }
        layout = new SpellWorkbenchGlyphStack[order.Count];
        for (var index = 0; index < order.Count; index++)
            layout[index] = new SpellWorkbenchGlyphStack(order[index], counts[order[index]]);
        reason = string.Empty;
        return true;
    }

    internal bool TryValidateStoredSpell(
        object spell,
        out Guid spellId,
        out Guid recipeId,
        out object? usageCost,
        out bool unique,
        out string reason,
        bool requireOwnedGlyphs = true)
    {
        spellId = Guid.Empty;
        recipeId = Guid.Empty;
        usageCost = null;
        unique = false;
        reason = string.Empty;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = "Spell loadout validation must run on the Unity main thread.";
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = _bindingFailure;
            return false;
        }
        if (spell is null || spell.GetType() != native.SpellType)
        {
            reason = "A saved spell has the wrong native type.";
            return false;
        }
        var guid = native.ReadSpellGuid(spell);
        if (guid is null || (spellId = native.ReadGuidValue(guid)) == Guid.Empty)
        {
            reason = "A saved spell has no stable instance identity.";
            return false;
        }
        var recipe = native.ReadSpellReference(spell);
        if (recipe is null || recipe.GetType() != native.RecipeType ||
            (recipeId = native.ReadIdentity(recipe)) == Guid.Empty)
        {
            reason = "A saved spell has no valid recipe identity.";
            return false;
        }
        var resolution = _registry.Resolve(recipeId, native.RecipeType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            !ReferenceEquals(resolution.Value, recipe))
        {
            reason = EntityIdentityFormatter.PlayerName(recipeId) +
                " is not the current spell recipe instance.";
            return false;
        }
        if (!native.IsDiscovered(recipe))
        {
            reason = EntityIdentityFormatter.PlayerName(recipeId) + " has not been discovered.";
            return false;
        }

        var glyphCounts = new Dictionary<Guid, int>();
        var core = native.ReadRecipeGlyphs(recipe);
        for (var index = 0; index < core.Count; index++)
        {
            var glyph = core[index];
            if (glyph is null)
            {
                reason = "A saved spell has a missing core glyph.";
                return false;
            }
            if (!TryValidateStoredGlyph(native, glyph, native.ReadIdentity(glyph), false,
                    requireOwnedGlyphs, out reason) ||
                !IncrementGlyph(native, glyph, glyphCounts, out reason))
                return false;
        }
        var augments = native.ReadSpellAugments(spell);
        for (var index = 0; index < augments.Count; index++)
        {
            var glyph = augments[index];
            if (glyph is null)
            {
                reason = "A saved spell has a missing augment glyph.";
                return false;
            }
            if (!TryValidateStoredGlyph(native, glyph, native.ReadIdentity(glyph), true,
                    requireOwnedGlyphs, out reason) ||
                !IncrementGlyph(native, glyph, glyphCounts, out reason))
                return false;
        }
        if (!native.MeetsNonLevelRequirements(augments, spell))
        {
            reason = "The saved spell's glyph layout no longer meets its duration/toggle requirements.";
            return false;
        }
        if (!native.HasMetUsageRequirements(recipe) && augments.Count == 0)
        {
            reason = "The saved spell no longer meets its usage requirements.";
            return false;
        }
        usageCost = native.GetUsageCost(spell);
        unique = native.IsUniqueSpell(spell);
        reason = string.Empty;
        return true;
    }

    private static bool TryValidateStoredGlyph(
        SpellWorkbenchNativeBindings native,
        object glyph,
        Guid glyphId,
        bool expectAugment,
        bool requireOwnedGlyphs,
        out string reason)
    {
        if (requireOwnedGlyphs)
            return TryValidateGlyph(native, glyph, glyphId, expectAugment, out reason);
        if (glyph.GetType() != native.GlyphType)
        {
            reason = "A saved spell component has the wrong native type.";
            return false;
        }
        if (native.IsGlyphAugment(glyph) != expectAugment)
        {
            reason = EntityIdentityFormatter.PlayerName(glyphId) +
                " has the wrong core/augment role in the saved spell.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool IncrementGlyph(
        SpellWorkbenchNativeBindings native,
        object glyph,
        Dictionary<Guid, int> counts,
        out string reason)
    {
        var id = native.ReadIdentity(glyph);
        counts.TryGetValue(id, out var count);
        count++;
        if (count > native.GetGlyphMaximumUsages(glyph))
        {
            reason = EntityIdentityFormatter.PlayerName(id) +
                " exceeds its live usable count in the saved spell.";
            return false;
        }
        counts[id] = count;
        reason = string.Empty;
        return true;
    }

    internal SpellWorkbenchSubmission Submit(in SpellWorkbenchAction action)
    {
        if (!TryResolveContext(
                action.LifecycleEpoch,
                action.SpellRecipeId,
                out var native,
                out var manager,
                out var recipe,
                out var preflight,
                out var contextReason))
            return SpellWorkbenchSubmission.Reject(preflight, contextReason);
        try
        {
            return CreateWithLayout(in action, native, manager, recipe);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellWorkbenchSubmission.Reject(SpellWorkbenchPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Magic > Spellbook", ex));
        }
    }

    internal SpellWorkbenchLoadPreview Preview(in SpellWorkbenchLoadPreviewRequest request)
    {
        if (!TryResolveContext(
                request.LifecycleEpoch,
                request.SpellRecipeId,
                out var native,
                out var manager,
                out var recipe,
                out var preflight,
                out var contextReason))
            return SpellWorkbenchLoadPreview.Refused(preflight, contextReason);
        try
        {
            if (!TryResolveGlyphLayout(
                    request.AugmentGlyphs,
                    expectAugment: true,
                    native,
                    out var augments,
                    out var augmentReason))
                return SpellWorkbenchLoadPreview.Refused(
                    SpellWorkbenchPreflight.SelectionUnavailable,
                    augmentReason);
            if (!TryAdmitLoad(native, manager, recipe, augments, out preflight, out var admitReason))
                return SpellWorkbenchLoadPreview.Refused(preflight, admitReason);
            var candidate = BuildCandidate(native, recipe, augments);
            return SpellWorkbenchLoadPreview.Admitted(
                request.SpellRecipeId,
                ReadUsageAllocation(native, native.GetUsageCost(candidate)));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchPreflight.ContractUnavailable,
                "Spell loadout preview failed while reading the live usage allocation: " +
                ex.GetBaseException().Message);
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

    /// <summary>Loads a discovered spell the way the Loadout list's own row loads it.</summary>
    /// <remarks>
    /// <para>
    /// <c>SpellManager.CreateRecipe</c> reads exactly one thing off the workbench — the augment
    /// stack — and nothing at all off the Recipe Book. So a load is: snapshot the player's augment
    /// staging, write the requested layout over it, press the row, put the staging back. The core
    /// selection is never written and never restored: the game empties it itself on the way out,
    /// which is what a player pressing that row sees too.
    /// </para>
    /// <para>
    /// Nothing here pays. The game charges nothing to put a spell in a slot; the only budget it
    /// weighs the load against is the usage allocation the loaded spell holds, and that is settled
    /// after the fact by the game's own recompute, never predicted here.
    /// </para>
    /// </remarks>
    private SpellWorkbenchSubmission CreateWithLayout(
        in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        if (!TryResolveGlyphLayout(
                action.AugmentGlyphs, expectAugment: true, native,
                out var augments, out var augmentReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.SelectionUnavailable, augmentReason);
        if (!TryAdmitLoad(native, manager, recipe, augments, out var refusal, out var refusalReason))
            return SpellWorkbenchSubmission.Reject(refusal, refusalReason);

        var expectedLayout = ReadIds(native, augments);
        var before = ReadMatchingInstanceIds(native, manager, recipe, expectedLayout);
        if (!TryCapturePermit(out var permitReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.MutationPermitUnavailable, permitReason);

        var nativeCalls = 0;
        var stage = SpellWorkbenchNativeStage.ClearSelection;
        if (!TryReadAugmentSelection(native, manager, out var previous, out var snapshotReason))
            return SpellWorkbenchSubmission.Reject(
                SpellWorkbenchPreflight.ContractUnavailable, snapshotReason);
        try
        {
            var staged = TryStageAugments(
                native, manager, augments, ref nativeCalls, out var stagingReason);
            stage = SpellWorkbenchNativeStage.ApplySelection;
            if (!staged)
            {
                return RestoreAugments(native, manager, previous, ref nativeCalls)
                    ? SpellWorkbenchSubmission.Reject(
                        SpellWorkbenchPreflight.StagedWriteFailed, stagingReason)
                    : FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                        SpellWorkbenchNativeStage.ApplySelection,
                        NativeMutationOutcome.PostconditionFailed, nativeCalls,
                        stagingReason +
                        " The player's own augment selection could not be restored either.");
            }

            if (!TryReadAugmentSelection(native, manager, out var selected, out var readBackReason))
            {
                return RestoreAugments(native, manager, previous, ref nativeCalls)
                    ? SpellWorkbenchSubmission.Reject(
                        SpellWorkbenchPreflight.StagedWriteFailed, readBackReason)
                    : FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                        SpellWorkbenchNativeStage.ApplySelection,
                        NativeMutationOutcome.PostconditionFailed, nativeCalls,
                        readBackReason +
                        " The player's own augment selection could not be restored either.");
            }
            if (!TryAdmitLoad(native, manager, recipe, selected, out refusal, out refusalReason))
            {
                if (RestoreAugments(native, manager, previous, ref nativeCalls))
                    return SpellWorkbenchSubmission.Reject(refusal, refusalReason);
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.ApplySelection,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The game stopped admitting this load between the check and the staging (" +
                    refusalReason + ") and the player's own augment selection could not be put " +
                    "back. No spell was loaded; the Spellcraft selection on screen is the one " +
                    "this call staged.");
            }

            stage = SpellWorkbenchNativeStage.Create;
            native.Create(manager, recipe);
            nativeCalls++;
            var restored = RestoreAugments(native, manager, previous, ref nativeCalls);
            if (!restored)
                return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The spell was loaded but the player's own augment selection could not be " +
                    "restored.");
            return HasNewMatchingInstance(
                    native, manager, recipe, expectedLayout, before)
                ? Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "A new spell with the exact requested augment layout is loaded.")
                : FaultAfterCommit(in action, SpellWorkbenchPreflight.VerificationFailed,
                    SpellWorkbenchNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls,
                    "The game returned from the load without a spell carrying the requested " +
                    "layout, so nothing is loaded and the player's own augment selection is " +
                    "back as it was. Read the loadout before calling again.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var restored = RestoreAugments(native, manager, previous, ref nativeCalls);
            if (restored && HasNewMatchingInstanceBestEffort(
                    native, manager, recipe, expectedLayout, before))
                return Verified(SpellWorkbenchNativeStage.Verification, nativeCalls,
                    "The exact layout was loaded before the native fault.");
            return FaultAfterCommit(in action, SpellWorkbenchPreflight.PostCommitFault,
                stage, NativeMutationOutcome.ExecutionThrew, nativeCalls,
                restored
                    ? "Loading the spell faulted without the requested exact layout: " +
                        ex.GetBaseException().Message
                    : "Loading the spell faulted and the player's own augment selection could " +
                        "not be restored: " + ex.GetBaseException().Message);
        }
    }

    /// <summary>Every fact the Loadout row's own button is disabled on, and nothing else.</summary>
    /// <remarks>
    /// <para>
    /// <c>UISpellRecipeButton.RenderContent</c> disables the row unless five things hold: the
    /// layout meets its duration/toggle requirements, the recipe's usage requirements are met or
    /// no augment is selected, the candidate's usage cost fits, the loadout has an empty spot, and
    /// the candidate is not a second copy of a loadout-unique spell. Those five are mirrored here
    /// from the same native reads, in that order.
    /// </para>
    /// <para>
    /// Ahead of them sit the two ceilings the game puts on the augment selection the row bakes
    /// from: a glyph cannot be stacked past its own usable count, and the selection holds at most
    /// <c>Max Spell Augment Slots</c> different augments. The suite writes that selection with one
    /// stack write, which is not gated the way the player's clicks are, so the ceilings are
    /// checked here instead of being silently exceeded.
    /// </para>
    /// <para>
    /// Preview and add share this, so a preview cannot answer available for a layout the add on
    /// identical arguments refuses.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The screen has to exist before any of its rows can be pressed, and locked is a different
    /// answer from unaffordable and from full. <c>ViewSO.IsAvailable()</c> is the game's own
    /// question — <c>prerequisites.Container.Check()</c> — so this is read, never inferred.
    /// </summary>
    private bool TryAdmitScreen(
        SpellWorkbenchNativeBindings native,
        out SpellWorkbenchPreflight refusal,
        out string reason)
    {
        var resolution = _registry.Resolve(
            KnownEntities.MagicSpellbookLoadout.Uuid, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
            return Refuse(SpellWorkbenchPreflight.ContractUnavailable,
                "The game's Magic > Spellbook > Loadout screen could not be read, so whether the " +
                "game would draw a row to load is unknown.",
                out refusal, out reason);
        if (!native.IsViewAvailable(view))
            return Refuse(SpellWorkbenchPreflight.ScreenLocked,
                "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no row to " +
                "load. Buy the Spellbook Loadout upgrade first.",
                out refusal, out reason);
        refusal = SpellWorkbenchPreflight.Proceeded;
        reason = string.Empty;
        return true;
    }

    private bool TryAdmitLoad(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        IList<object> augments,
        out SpellWorkbenchPreflight refusal,
        out string reason)
    {
        if (!TryAdmitScreen(native, out refusal, out reason)) return false;
        var recipeName = EntityIdentityFormatter.PlayerName(native.ReadIdentity(recipe));
        if (!native.IsDiscovered(recipe))
            return Refuse(SpellWorkbenchPreflight.DiscoveryUnavailable,
                recipeName + " is not discovered, so Magic > Spellbook > Loadout lists no row " +
                "for it. Discover it first, and the same call loads it.",
                out refusal, out reason);

        var augmentList = native.ReadAugments(manager);
        var slots = native.GetListMax(augmentList);
        var distinct = DistinctCount(native, augments);
        if (distinct > slots)
            return Refuse(SpellWorkbenchPreflight.AugmentSlotsExceeded,
                "This layout puts " + distinct + " different augments on " + recipeName +
                " and the game allows " + slots +
                " at once (Max Spell Augment Slots). Ask for " + slots +
                " different augments or fewer, or raise that limit first.",
                out refusal, out reason);

        var candidate = BuildCandidate(native, recipe, augments);
        var nativeAugments = NativeGlyphList(native, augments);
        if (!native.MeetsNonLevelRequirements(nativeAugments, candidate))
            return Refuse(SpellWorkbenchPreflight.GlyphRequirementsUnavailable,
                "The game refuses " + Describe(native, augments) + " on " + recipeName +
                ": augments that require a duration spell or a toggleable spell only attach to " +
                "one, and this spell is not the kind they need. Load it with a layout those " +
                "augments fit, or leave them out.",
                out refusal, out reason);
        if (!native.HasMetUsageRequirements(recipe) && augments.Count > 0)
            return Refuse(SpellWorkbenchPreflight.UsageRequirementsUnavailable,
                recipeName + " has not met its usage requirements yet, and the game only lets " +
                "that pass while no augment is selected. Load it with no augments, or meet the " +
                "requirement first.",
                out refusal, out reason);
        var usageCost = native.GetUsageCost(candidate);
        if (!native.HasEnough(usageCost))
        {
            var shortResourceId = ReadShortUsageResourceId(native, usageCost);
            return Refuse(
                SpellWorkbenchPreflight.UsageUnaffordable,
                shortResourceId == Guid.Empty
                    ? "Loading " + recipeName +
                        " would push the loadout past its usage allocation."
                    : "Loading " + recipeName + " would push " +
                        EntityIdentityFormatter.PlayerName(shortResourceId) +
                        " past its allocation. Remove a loaded spell, or raise that allocation.",
                out refusal, out reason);
        }
        var activeList = native.ReadActive(manager);
        if (!native.HasEmpty(activeList))
            return Refuse(SpellWorkbenchPreflight.LoadoutFull,
                "All " + native.ReadActiveValues(activeList).Count +
                " loadout slots hold a spell, so there is nowhere to put " + recipeName +
                ". Remove a loaded spell first; nothing was staged and nothing was spent.",
                out refusal, out reason);
        if (native.IsUniqueSpell(candidate) && HasActiveRecipe(native, manager, recipe))
            return Refuse(SpellWorkbenchPreflight.UniqueSpellConflict,
                recipeName + " is loadout-unique and one copy is already loaded. " +
                "Remove that copy first.",
                out refusal, out reason);
        refusal = SpellWorkbenchPreflight.Proceeded;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The throwaway spell the Loadout row builds to answer its own gates, built the same way.
    /// </summary>
    /// <remarks>
    /// <c>UISpellRecipeButton.AttachSpell</c> takes the recipe's selected level and the staged
    /// augment record and asks the resulting spell for its requirements and its usage cost. This
    /// is that, on the layout the caller asked for rather than on whatever is staged.
    /// </remarks>
    private static object BuildCandidate(
        SpellWorkbenchNativeBindings native,
        object recipe,
        IList<object> augments)
    {
        var candidate = native.CreateEmptySpell(recipe, 0);
        native.SetSpellLevel(candidate, native.GetSelectedSpellLevel(recipe));
        var record = native.CreateStackedRecord();
        SetStackedLayout(native, record, augments);
        native.SetSpellAugments(candidate, record);
        return candidate;
    }

    private static int DistinctCount(SpellWorkbenchNativeBindings native, IList<object> glyphs)
    {
        var seen = new HashSet<Guid>();
        for (var index = 0; index < glyphs.Count; index++)
            seen.Add(native.ReadIdentity(glyphs[index]));
        return seen.Count;
    }

    private static bool Refuse(
        SpellWorkbenchPreflight exact,
        string exactReason,
        out SpellWorkbenchPreflight refusal,
        out string reason)
    {
        refusal = exact;
        reason = exactReason;
        return false;
    }

    private static bool HasActiveRecipe(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        var recipeId = native.ReadIdentity(recipe);
        var active = native.ReadActiveValues(native.ReadActive(manager));
        for (var index = 0; index < active.Count; index++)
        {
            var spell = active[index];
            if (spell is null) continue;
            var reference = native.ReadSpellReference(spell);
            if (reference is not null && reference.GetType() == native.RecipeType &&
                native.ReadIdentity(reference) == recipeId) return true;
        }
        return false;
    }

    private static void SetStackedLayout(
        SpellWorkbenchNativeBindings native,
        object record,
        IList<object> augments)
    {
        var counts = new Dictionary<Guid, int>();
        var values = new Dictionary<Guid, object>();
        for (var index = 0; index < augments.Count; index++)
        {
            var id = native.ReadIdentity(augments[index]);
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
            values[id] = augments[index];
        }
        foreach (var pair in counts)
            native.SetStackedRecord(record, values[pair.Key], pair.Value);
    }

    private bool TryResolveGlyphLayout(
        SpellWorkbenchGlyphStack[] layout,
        bool expectAugment,
        SpellWorkbenchNativeBindings native,
        out List<object> glyphs,
        out string reason)
    {
        glyphs = new List<object>();
        var resolved = new Dictionary<Guid, object>();
        var totals = new Dictionary<Guid, int>();
        var order = new List<Guid>();
        for (var index = 0; index < layout.Length; index++)
        {
            var stack = layout[index];
            if (!totals.ContainsKey(stack.GlyphId)) order.Add(stack.GlyphId);
            totals.TryGetValue(stack.GlyphId, out var prior);
            var total = (long)prior + stack.Count;
            if (total > int.MaxValue)
            {
                reason = "The requested glyph count exceeds the supported native integer range.";
                return false;
            }
            totals[stack.GlyphId] = (int)total;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            var resolution = _registry.Resolve(glyphId, native.GlyphType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            {
                reason = resolution.IsResolved
                    ? GameActionAnswer.Replaced("glyph")
                    : resolution.Reason;
                return false;
            }
            var glyph = resolution.Value!;
            if (!TryValidateGlyph(native, glyph, glyphId, expectAugment, out reason))
                return false;
            var maximum = native.GetGlyphMaximumUsages(glyph);
            var requested = totals[glyphId];
            if (requested > maximum)
            {
                reason = "Requested " + requested + " uses of " +
                    EntityIdentityFormatter.PlayerName(glyphId) +
                    ", but the live usable count is " + maximum + ".";
                return false;
            }
            resolved[glyphId] = glyph;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            for (var count = 0; count < totals[glyphId]; count++)
                glyphs.Add(resolved[glyphId]);
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryValidateGlyph(
        SpellWorkbenchNativeBindings native,
        object glyph,
        Guid glyphId,
        bool expectAugment,
        out string reason)
    {
        if (glyph.GetType() != native.GlyphType)
        {
            reason = "The resolved spell component has the wrong native type.";
            return false;
        }
        if (native.IsGlyphAugment(glyph) != expectAugment)
        {
            reason = EntityIdentityFormatter.PlayerName(glyphId) +
                (expectAugment
                    ? " is not a spell augment."
                    : " is an augment and cannot be a core discovery component.");
            return false;
        }
        if (!native.IsGlyphAvailable(glyph))
        {
            reason = EntityIdentityFormatter.PlayerName(glyphId) +
                " is not available for this spell layout.";
            return false;
        }
        if (native.ReadGlyphLevel(glyph) <= 0)
        {
            reason = EntityIdentityFormatter.PlayerName(glyphId) +
                " is not owned; spell glyphs require an owned level above zero.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static SpellWorkbenchUsageAllocation[] ReadUsageAllocation(
        SpellWorkbenchNativeBindings native,
        object usageCost)
    {
        var totals = ReadUsageTotals(native, usageCost);
        var result = new SpellWorkbenchUsageAllocation[totals.Count];
        for (var index = 0; index < totals.Count; index++)
            result[index] = new SpellWorkbenchUsageAllocation(totals[index].Id, totals[index].Cost);
        return result;
    }

    private static Guid ReadShortUsageResourceId(
        SpellWorkbenchNativeBindings native,
        object usageCost)
    {
        var totals = ReadUsageTotals(native, usageCost);
        for (var index = 0; index < totals.Count; index++)
        {
            var value = totals[index];
            if (!native.HasResourceAmount(value.Resource, value.Cost))
                return value.Id;
        }
        return Guid.Empty;
    }

    private static List<(Guid Id, object Resource, BigDouble Cost)> ReadUsageTotals(
        SpellWorkbenchNativeBindings native,
        object usageCost)
    {
        var totals = new Dictionary<Guid, (object Resource, BigDouble Cost)>();
        var order = new List<Guid>();
        var rows = native.ReadCostEntries(usageCost);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null)
                throw new InvalidOperationException(
                    "The native usage allocation contained an empty cost row.");
            var resource = native.ReadCostResource(row);
            if (resource is null)
                throw new InvalidOperationException(
                    "The native usage allocation contained a cost without a resource.");
            var id = native.ReadIdentity(resource);
            if (id == Guid.Empty)
                throw new InvalidOperationException(
                    "The native usage allocation contained a resource without a stable identity.");
            if (!totals.ContainsKey(id)) order.Add(id);
            totals.TryGetValue(id, out var current);
            totals[id] = (resource, current.Cost + native.ReadCostValue(row));
        }
        var result = new List<(Guid Id, object Resource, BigDouble Cost)>(order.Count);
        for (var index = 0; index < order.Count; index++)
        {
            var id = order[index];
            var value = totals[id];
            result.Add((id, value.Resource, value.Cost));
        }
        return result;
    }

    /// <summary>
    /// The player's live augment staging, expanded to one entry per use.
    /// </summary>
    /// <remarks>
    /// Read from the stack, never from the value list beside it: <c>SetStack</c> feeds that list
    /// through <c>SetValue</c>, which truncates to <c>Max Spell Augment Slots</c> and drops
    /// multiplicity, while the stack is what <c>SpellManager.CreateRecipe</c> bakes the spell
    /// from. Comparing against the truncated mirror would call a landed write a failure.
    /// </remarks>
    private static bool TryReadAugmentSelection(
        SpellWorkbenchNativeBindings native,
        object manager,
        out List<object> augments,
        out string reason)
    {
        augments = new List<object>();
        var stack = native.ReadListStack(native.ReadAugments(manager));
        if (stack is null)
        {
            reason = "The live augment selection exposes no stack for the game to bake a spell " +
                "from, so no augment layout can be staged or read back.";
            return false;
        }
        var items = native.ReadStackedItems(stack);
        for (var index = 0; index < items.Count; index++)
        {
            var glyph = items[index];
            if (glyph is null || glyph.GetType() != native.GlyphType)
            {
                reason = "The live augment stack holds an entry that is not a glyph.";
                return false;
            }
            var quantity = native.ReadStackedQuantity(stack, glyph);
            if (quantity < 0)
            {
                reason = "The live augment stack reports a negative count for " +
                    EntityIdentityFormatter.PlayerName(native.ReadIdentity(glyph)) + ".";
                return false;
            }
            for (var copy = 0; copy < quantity; copy++) augments.Add(glyph);
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes one augment layout over the player's staging with the game's own stack write, and
    /// proves it landed by reading the stack back.
    /// </summary>
    /// <remarks>
    /// <c>SetStack</c> replaces the whole multiplicity record, so this both clears what was staged
    /// and stages what was asked for in one call. The read-back is the postcondition: a write that
    /// landed short is named here as the suite's own staging failure rather than travelling on to
    /// create a spell with augments nobody asked for.
    /// </remarks>
    private static bool TryStageAugments(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> augmentGlyphs,
        ref int nativeCalls,
        out string reason)
    {
        var record = native.CreateStackedRecord();
        SetStackedLayout(native, record, augmentGlyphs);
        native.SetListStack(native.ReadAugments(manager), record);
        nativeCalls++;
        if (!TryReadAugmentSelection(native, manager, out var staged, out reason)) return false;
        if (!SameCounts(native, staged, augmentGlyphs))
        {
            reason = "Staging the chosen augments into the game's augment selection did not " +
                "land: " + Describe(native, augmentGlyphs) + " was written and " +
                Describe(native, staged) + " came back.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool RestoreAugments(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> previous,
        ref int nativeCalls)
    {
        try
        {
            return TryStageAugments(native, manager, previous, ref nativeCalls, out _);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static string Describe(SpellWorkbenchNativeBindings native, IList<object> glyphs)
    {
        if (glyphs.Count == 0) return "no glyphs";
        var counts = new Dictionary<Guid, int>();
        var order = new List<Guid>();
        for (var index = 0; index < glyphs.Count; index++)
        {
            var id = native.ReadIdentity(glyphs[index]);
            if (!counts.ContainsKey(id)) order.Add(id);
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
        }
        var text = new System.Text.StringBuilder();
        for (var index = 0; index < order.Count; index++)
        {
            if (index > 0) text.Append(", ");
            var id = order[index];
            if (counts[id] > 1) text.Append(counts[id]).Append("x ");
            text.Append(EntityIdentityFormatter.PlayerName(id));
        }
        return text.ToString();
    }

    private static bool SameCounts(
        SpellWorkbenchNativeBindings native,
        IList<object> actual,
        IList<object> expected)
    {
        if (actual.Count != expected.Count) return false;
        var counts = new Dictionary<Guid, int>();
        for (var index = 0; index < expected.Count; index++)
        {
            var id = native.ReadIdentity(expected[index]);
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
        }
        for (var index = 0; index < actual.Count; index++)
        {
            var id = native.ReadIdentity(actual[index]);
            if (!counts.TryGetValue(id, out var count) || count == 0) return false;
            counts[id] = count - 1;
        }
        return true;
    }

    private static IList NativeGlyphList(
        SpellWorkbenchNativeBindings native,
        IList<object> glyphs)
    {
        var result = native.CreateGlyphList();
        for (var index = 0; index < glyphs.Count; index++) result.Add(glyphs[index]);
        return result;
    }

    private static List<object> Copy(IList source)
    {
        var result = new List<object>(source.Count);
        for (var index = 0; index < source.Count; index++)
            if (source[index] is { } value) result.Add(value);
        return result;
    }

    private static Guid[] ReadMatchingInstanceIds(SpellWorkbenchNativeBindings native,
        object manager, object targetRecipe, Guid[] expectedLayout)
    {
        var matching = new List<Guid>();
        var targetId = native.ReadIdentity(targetRecipe);
        var active = native.ReadActiveValues(native.ReadActive(manager));
        for (var index = 0; index < active.Count; index++)
        {
            var spell = active[index];
            if (spell is null) continue;
            var reference = native.ReadSpellReference(spell);
            if (reference is null || reference.GetType() != native.RecipeType ||
                native.ReadIdentity(reference) != targetId) continue;
            var container = native.ReadSpellGuid(spell);
            var instanceId = container is null ? Guid.Empty : native.ReadGuidValue(container);
            if (SameLayout(native, spell, expectedLayout))
                matching.Add(instanceId);
        }
        return matching.ToArray();
    }

    private static bool HasNewMatchingInstance(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        Guid[] expectedLayout,
        Guid[] before) =>
        HasNewInstance(before, ReadMatchingInstanceIds(native, manager, recipe, expectedLayout));

    private static bool HasNewMatchingInstanceBestEffort(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        Guid[] expectedLayout,
        Guid[] before)
    {
        try { return HasNewMatchingInstance(native, manager, recipe, expectedLayout, before); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static Guid[] ReadIds(SpellWorkbenchNativeBindings native, IList values)
    {
        var result = new Guid[values.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var value = values[index];
            result[index] = value is null ? Guid.Empty : native.ReadIdentity(value);
        }
        return result;
    }

    /// <summary>Whether a loaded spell carries exactly the augment layout that was asked for.</summary>
    /// <remarks>
    /// Read from <c>Spell.augmentGlyphRefs</c>, the stack the spell was baked from.
    /// <c>Spell.GetAugmentGlyphs()</c> hands back one entry per distinct glyph with the
    /// multiplicities discarded, so a two-of-one-glyph layout read back as one glyph and every
    /// such load reported itself as a fault it was not.
    /// </remarks>
    private static bool SameLayout(
        SpellWorkbenchNativeBindings native,
        object spell,
        Guid[] expected)
    {
        var counts = new Dictionary<Guid, int>();
        for (var index = 0; index < expected.Length; index++)
        {
            counts.TryGetValue(expected[index], out var count);
            counts[expected[index]] = count + 1;
        }
        var stack = native.ReadSpellAugmentStack(spell);
        if (stack is null) return counts.Count == 0;
        var items = native.ReadStackedItems(stack);
        for (var index = 0; index < items.Count; index++)
        {
            var value = items[index];
            if (value is null) return false;
            var quantity = native.ReadStackedQuantity(stack, value);
            if (quantity <= 0) continue;
            var id = native.ReadIdentity(value);
            if (!counts.TryGetValue(id, out var wanted) || wanted != quantity) return false;
            counts.Remove(id);
        }
        return counts.Count == 0;
    }

    private static bool HasNewInstance(Guid[] before, Guid[] after)
    {
        if (after.Length <= before.Length) return false;
        for (var index = 0; index < after.Length; index++)
        {
            if (after[index] == Guid.Empty) continue;
            var found = false;
            for (var prior = 0; prior < before.Length; prior++)
                if (after[index] == before[prior]) { found = true; break; }
            if (!found) return true;
        }
        return false;
    }

    private bool TryResolveContext(
        long requestedEpoch,
        Guid recipeId,
        out SpellWorkbenchNativeBindings native,
        out object manager,
        out object recipe,
        out SpellWorkbenchPreflight preflight,
        out string reason)
    {
        native = null!;
        manager = null!;
        recipe = null!;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            preflight = SpellWorkbenchPreflight.WrongThread;
            reason = GameActionAnswer.SuiteStopped();
            return false;
        }
        if (_bindings is not { } available)
        {
            preflight = SpellWorkbenchPreflight.ContractUnavailable;
            reason = GameActionAnswer.NotAttached("Magic > Spellbook");
            return false;
        }
        native = available;
        try
        {
            var currentEpoch = _readLifecycleEpoch();
            if (currentEpoch != requestedEpoch)
            {
                preflight = SpellWorkbenchPreflight.LifecycleReplaced;
                reason = GameActionAnswer.RunChanged();
                return false;
            }
            manager = native.ReadManager()!;
            if (manager is null)
            {
                preflight = SpellWorkbenchPreflight.ContractUnavailable;
                reason = "The spellbook is not loaded right now.";
                return false;
            }
            if (!TryResolveRecipe(native, recipeId, out recipe, out reason))
            {
                preflight = SpellWorkbenchPreflight.IdentityUnavailable;
                return false;
            }
            preflight = SpellWorkbenchPreflight.Proceeded;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            preflight = SpellWorkbenchPreflight.ContractUnavailable;
            reason = "Spell workbench context could not be read: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool TryResolveRecipe(SpellWorkbenchNativeBindings native, Guid id,
        out object recipe, out string reason)
    {
        recipe = null!;
        var matches = 0;
        foreach (var value in native.ReadRecipes())
        {
            if (value is null || value.GetType() != native.RecipeType || native.ReadIdentity(value) != id) continue;
            recipe = value;
            matches++;
        }
        if (matches == 1) { reason = string.Empty; return true; }
        reason = matches == 0
            ? "That spell recipe is not in this run."
            : "That id names more than one live spell recipe.";
        return false;
    }

    private static SpellWorkbenchSubmission Verified(SpellWorkbenchNativeStage stage,
        int nativeCalls, string reason)
    {
        return new SpellWorkbenchSubmission(SpellWorkbenchPreflight.Proceeded, stage,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(nativeCalls, 1, 1),
            reason);
    }

    private static SpellWorkbenchSubmission FaultAfterCommit(in SpellWorkbenchAction action,
        SpellWorkbenchPreflight preflight, SpellWorkbenchNativeStage stage,
        NativeMutationOutcome outcome, int nativeCalls, string reason)
    {
        var exactReason = $"Spell workbench action faulted after {stage} on " +
            $"recipe {EntityIdentityFormatter.PlayerName(action.SpellRecipeId)}: {reason}";
        return new SpellWorkbenchSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0), exactReason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit()) { reason = string.Empty; return true; }
        reason = _readOwnershipFailure();
        if (reason.Length == 0) reason = "The suite does not own the spell workbench action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolveType = _resolveType ?? ReflectionUtil.FindLoadedType;
        Func<string, bool> includeContract = _includeContract ?? (_ => true);
        if (SpellWorkbenchNativeBindings.TryCreate(resolveType, includeContract, out var bindings, out var reason))
        {
            _bindings = bindings;
            return;
        }
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or TargetInvocationException or NullReferenceException;
}
