using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-bound explicit-layout loadout-add boundary.</summary>
internal sealed class SpellWorkbenchGameAction : NativeGameAction<SpellWorkbenchNativeBindings>
{
    private const string Screen = "Magic > Spellbook";

    private static readonly GameActionCodes Codes = new(
        SpellWorkbenchActionResultCodes.WrongThread,
        SpellWorkbenchActionResultCodes.ContractUnavailable,
        SpellWorkbenchActionResultCodes.MutationPermitUnavailable,
        SpellWorkbenchActionResultCodes.PostCommitFault,
        SpellWorkbenchActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal SpellWorkbenchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Spell workbench action", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out SpellWorkbenchNativeBindings? bindings, out string reason) =>
                SpellWorkbenchNativeBindings.TryCreate(resolveType ?? ReflectionUtil.FindLoadedType,
                    includeContract ?? (_ => true), out bindings, out reason))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal SpellWorkbenchStagedLayout ReadStagedLayout()
    {
        if (!TryEnter(out var native, out var refusal))
            return SpellWorkbenchStagedLayout.Unavailable(refusal.Code, refusal.Reason);
        try
        {
            var manager = native.ReadManager();
            if (manager is null)
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchActionResultCodes.SelectionUnavailable,
                    "Spellcraft is not available in the current game state.");
            if (!TryReadStagedGlyphs(
                    native,
                    native.ReadGlyphValues(native.ReadCore(manager)),
                    out var core,
                    out var coreReason))
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchActionResultCodes.SelectionUnavailable,
                    coreReason);
            if (!TryReadStagedGlyphs(
                    native,
                    native.ReadGlyphValues(native.ReadAugments(manager)),
                    out var augments,
                    out var augmentReason))
                return SpellWorkbenchStagedLayout.Unavailable(
                    SpellWorkbenchActionResultCodes.SelectionUnavailable,
                    augmentReason);
            return SpellWorkbenchStagedLayout.Captured(core, augments);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead(Screen, exception));
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

    internal NativeActionOutcome Submit(in SpellWorkbenchAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, native =>
            TryResolveLoad(native, request.SpellRecipeId, out var manager, out var recipe,
                out var refusal, out var reason)
                ? CreateWithLayout(in request, native, manager, recipe)
                : NativeActionOutcome.Reject(refusal, reason));
    }

    internal SpellWorkbenchLoadPreview Preview(in SpellWorkbenchLoadPreviewRequest request)
    {
        var asked = request;
        return Admit(request.LifecycleEpoch,
            static outcome => SpellWorkbenchLoadPreview.Refused(outcome.Code, outcome.Reason),
            native => PreviewLoad(in asked, native));
    }

    private SpellWorkbenchLoadPreview PreviewLoad(
        in SpellWorkbenchLoadPreviewRequest request,
        SpellWorkbenchNativeBindings native)
    {
        if (!TryResolveLoad(native, request.SpellRecipeId, out var manager, out var recipe,
                out var preflight, out var contextReason))
            return SpellWorkbenchLoadPreview.Refused(preflight, contextReason);
        if (!TryResolveGlyphLayout(
                request.AugmentGlyphs,
                expectAugment: true,
                native,
                out var augments,
                out var augmentRefusal,
                out var augmentReason))
            return SpellWorkbenchLoadPreview.Refused(augmentRefusal, augmentReason);
        var replaced = native.HasEmpty(native.ReadActive(manager))
            ? null
            : FindLoadedCopy(native, manager, recipe);
        if (!TryAdmitLoad(native, manager, recipe, augments, replaced,
                out preflight, out var admitReason))
            return SpellWorkbenchLoadPreview.Refused(preflight, admitReason);
        var candidate = BuildCandidate(native, recipe, augments);
        return SpellWorkbenchLoadPreview.Admitted(
            request.SpellRecipeId,
            ReadUsageAllocation(native, native.GetUsageCost(candidate)),
            replaced is { } copy
                ? EntityIdentityFormatter.PlayerName(request.SpellRecipeId) + " in slot " +
                  (copy.Slot + 1) + " — remove it, then add"
                : string.Empty);
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
    /// <para>
    /// The player's staging is part of the outcome: a load commits only when the new spell is
    /// there and the staging reads back as it was.
    /// </para>
    /// </remarks>
    private NativeActionOutcome CreateWithLayout(
        in SpellWorkbenchAction action,
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe)
    {
        if (!TryResolveGlyphLayout(
                action.AugmentGlyphs, expectAugment: true, native,
                out var augments, out var augmentRefusal, out var augmentReason))
            return NativeActionOutcome.Reject(augmentRefusal, augmentReason);
        if (!TryAdmitLoad(native, manager, recipe, augments, null, out var refusal, out var refusalReason))
            return NativeActionOutcome.Reject(refusal, refusalReason);

        var expectedLayout = ReadIds(native, augments);
        var before = ReadMatchingInstanceIds(native, manager, recipe, expectedLayout);
        if (!TryPermit(out var permitRefusal)) return permitRefusal;
        if (!TryReadAugmentSelection(native, manager, out var previous, out var snapshotReason))
            return NativeActionOutcome.Reject(
                SpellWorkbenchActionResultCodes.ContractUnavailable, snapshotReason);
        return Execute(EntityIdentityFormatter.PlayerName(action.SpellRecipeId),
            "A new spell with the exact requested augment layout is loaded.", false,
            () => HasNewInstance(before, ReadMatchingInstanceIds(native, manager, recipe, expectedLayout)) &&
                Restored(native, manager, previous),
            () => Load(native, manager, recipe, augments, previous),
            static (_, landed) => landed,
            putBack: new NativePutBack(
                () => WriteAugments(native, manager, previous),
                () => Restored(native, manager, previous)));
    }

    /// <summary>
    /// Every press of a load: stage the requested augments, prove they staged, press the row, and
    /// stage the player's own augments back.
    /// </summary>
    /// <remarks>
    /// The staging write is a press. A load that stops after it refuses only once the player's
    /// staging reads back as it was, and that refusal counts the press as an attempt. A throw
    /// anywhere here is put back by the shell.
    /// </remarks>
    private static NativeStop? Load(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        IList<object> augments,
        IList<object> previous)
    {
        WriteAugments(native, manager, augments);
        if (!TryReadBack(native, manager, augments, out var reason))
        {
            WriteAugments(native, manager, previous);
            return NativeStop.Refused(SpellWorkbenchActionResultCodes.StagedWriteFailed, reason,
                () => Restored(native, manager, previous));
        }
        native.Create(manager, recipe);
        WriteAugments(native, manager, previous);
        if (TryReadBack(native, manager, previous, out var unrestored)) return null;
        return NativeStop.Fault(SpellWorkbenchActionResultCodes.PostCommitFault,
            "The player's own augment selection could not be restored after the load; read " +
            "the loadout before calling again. Putting it back: " + unrestored);
    }

    private static bool Restored(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> previous) =>
        TryReadBack(native, manager, previous, out _);

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
    /// identical arguments refuses. The one exception is a preview on a full bar of a recipe that
    /// is already loaded: augments are baked in, so changing a loaded spell is a removal and an
    /// add, and the preview prices that replacement of the lowest-slot copy. It skips the empty
    /// spot and the unique-copy gates, which that removal answers, and counts the copy's usage as
    /// free.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The screen has to exist before any of its rows can be pressed, and locked is a different
    /// answer from unaffordable and from full. <c>ViewSO.IsAvailable()</c> is the game's own
    /// question — <c>prerequisites.Container.Check()</c> — so this is read, never inferred.
    /// </summary>
    private bool TryAdmitScreen(
        SpellWorkbenchNativeBindings native,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        var resolution = _registry.Resolve(
            KnownEntities.MagicSpellbookLoadout.Uuid, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
            return Refuse(SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The game's Magic > Spellbook > Loadout screen could not be read, so whether the " +
                "game would draw a row to load is unknown.",
                out refusal, out reason);
        if (!native.IsViewAvailable(view))
            return Refuse(SpellWorkbenchActionResultCodes.ScreenLocked,
                "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no row to " +
                "load. Buy the Spellbook Loadout upgrade first.",
                out refusal, out reason);
        refusal = CommonActionResultCodes.Committed;
        reason = string.Empty;
        return true;
    }

    private bool TryAdmitLoad(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe,
        IList<object> augments,
        LoadedCopy? replaced,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        if (!TryAdmitScreen(native, out refusal, out reason)) return false;
        var recipeName = EntityIdentityFormatter.PlayerName(native.ReadIdentity(recipe));
        if (!native.IsDiscovered(recipe))
            return Refuse(SpellWorkbenchActionResultCodes.DiscoveryUnavailable,
                recipeName + " is not discovered, so Magic > Spellbook > Loadout lists no row " +
                "for it. Discover it first, and the same call loads it.",
                out refusal, out reason);

        var augmentList = native.ReadAugments(manager);
        var slots = native.GetListMax(augmentList);
        var distinct = DistinctCount(native, augments);
        if (distinct > slots)
            return Refuse(SpellWorkbenchActionResultCodes.AugmentSlotsExceeded,
                "This layout puts " + distinct + " different augments on " + recipeName +
                " and the game allows " + slots +
                " at once (Max Spell Augment Slots). Ask for " + slots +
                " different augments or fewer, or raise that limit first.",
                out refusal, out reason);

        var candidate = BuildCandidate(native, recipe, augments);
        var nativeAugments = NativeGlyphList(native, augments);
        if (!native.MeetsNonLevelRequirements(nativeAugments, candidate))
            return Refuse(SpellWorkbenchActionResultCodes.GlyphRequirementsUnavailable,
                "The game refuses " + Describe(native, augments) + " on " + recipeName +
                ": augments that require a duration spell or a toggleable spell only attach to " +
                "one, and this spell is not the kind they need. Load it with a layout those " +
                "augments fit, or leave them out.",
                out refusal, out reason);
        if (!native.HasMetUsageRequirements(recipe) && augments.Count > 0)
            return Refuse(SpellWorkbenchActionResultCodes.UsageRequirementsUnavailable,
                recipeName + " has not met its usage requirements yet, and the game only lets " +
                "that pass while no augment is selected. Load it with no augments, or meet the " +
                "requirement first.",
                out refusal, out reason);
        var usageCost = native.GetUsageCost(candidate);
        if (replaced is { } copy)
        {
            var released = ReadUsageTotals(native, native.GetUsageCost(copy.Spell));
            if (TryReadShortUsage(native, usageCost, released, out var shortId, out var shortNeeded,
                    out var shortFree))
                return Refuse(
                    SpellWorkbenchActionResultCodes.UsageUnaffordable,
                    recipeName + " in place of its copy in slot " + (copy.Slot + 1) +
                        " would push " + EntityIdentityFormatter.PlayerName(shortId) +
                        " past its allocation, counting that copy's weight as free. " +
                        WeightShortfall(EntityIdentityFormatter.PlayerName(shortId),
                            shortNeeded, shortFree),
                    out refusal, out reason);
        }
        else if (!native.HasEnough(usageCost))
        {
            return Refuse(
                SpellWorkbenchActionResultCodes.UsageUnaffordable,
                TryReadShortUsage(native, usageCost, null, out var shortResourceId, out var needed,
                        out var free)
                    ? "Loading " + recipeName + " would push " +
                        EntityIdentityFormatter.PlayerName(shortResourceId) +
                        " past its allocation. " +
                        WeightShortfall(EntityIdentityFormatter.PlayerName(shortResourceId),
                            needed, free)
                    : "Loading " + recipeName +
                        " would push the loadout past its usage allocation.",
                out refusal, out reason);
        }
        var activeList = native.ReadActive(manager);
        if (replaced is null && !native.HasEmpty(activeList))
            return Refuse(SpellWorkbenchActionResultCodes.LoadoutFull,
                "All " + native.ReadActiveValues(activeList).Count +
                " loadout slots hold a spell, so there is nowhere to put " + recipeName +
                ". Remove a loaded spell first; nothing was staged and nothing was spent.",
                out refusal, out reason);
        if (replaced is null && native.IsUniqueSpell(candidate) &&
            HasActiveRecipe(native, manager, recipe))
            return Refuse(SpellWorkbenchActionResultCodes.UniqueSpellConflict,
                recipeName + " is loadout-unique and one copy is already loaded. " +
                "Remove that copy first.",
                out refusal, out reason);
        refusal = CommonActionResultCodes.Committed;
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
        ServiceActionResultCode exact,
        string exactReason,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        refusal = exact;
        reason = exactReason;
        return false;
    }

    private static bool HasActiveRecipe(
        SpellWorkbenchNativeBindings native,
        object manager,
        object recipe) =>
        FindLoadedCopy(native, manager, recipe) is not null;

    /// <summary>The lowest-slot loaded copy of this recipe, which a preview on a full bar prices a replacement for.</summary>
    private static LoadedCopy? FindLoadedCopy(
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
                native.ReadIdentity(reference) == recipeId) return new LoadedCopy(spell, index);
        }
        return null;
    }

    private sealed record LoadedCopy(object Spell, int Slot);

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
        out ServiceActionResultCode refusal,
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
                return Refuse(SpellWorkbenchActionResultCodes.SelectionUnavailable,
                    "The requested glyph count exceeds the supported native integer range.",
                    out refusal, out reason);
            }
            totals[stack.GlyphId] = (int)total;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            var resolution = _registry.Resolve(glyphId, native.GlyphType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            {
                return Refuse(SpellWorkbenchActionResultCodes.SelectionUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("glyph") : resolution.Reason,
                    out refusal, out reason);
            }
            var glyph = resolution.Value!;
            if (!TryValidateGlyph(native, glyph, glyphId, expectAugment, out refusal, out reason))
                return false;
            var maximum = native.GetGlyphMaximumUsages(glyph);
            var requested = totals[glyphId];
            if (requested > maximum)
            {
                var glyphName = EntityIdentityFormatter.PlayerName(glyphId);
                return Refuse(SpellWorkbenchActionResultCodes.GlyphUsagesExceeded,
                    maximum <= 0
                        ? glyphName + " has no usable copy in this run, so no layout can hold one."
                        : glyphName + " allows " + maximum + (maximum == 1 ? " use" : " uses") +
                          " per spell and this layout asks for " + requested + " of " + maximum +
                          ". Ask for " + maximum + " or fewer.",
                    out refusal, out reason);
            }
            resolved[glyphId] = glyph;
        }
        for (var index = 0; index < order.Count; index++)
        {
            var glyphId = order[index];
            for (var count = 0; count < totals[glyphId]; count++)
                glyphs.Add(resolved[glyphId]);
        }
        refusal = CommonActionResultCodes.Committed;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The glyph tile's own gate, and nothing beside it. <c>UIGlyphListItem.GetInteractable()</c>
    /// reduces to <c>GlyphSO.IsAvailable()</c> plus <c>GetMaxUsages()</c> headroom; neither
    /// <c>GlyphSO.level</c> nor <c>GetLevel()</c> appears anywhere in it. A freshly discovered
    /// augment is available at level 0 with one usable copy, and the Loadout page lets a player
    /// socket it — the suite refused that press for an ownership predicate the game does not have.
    /// </summary>
    private static bool TryValidateGlyph(
        SpellWorkbenchNativeBindings native,
        object glyph,
        Guid glyphId,
        bool expectAugment,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        if (glyph.GetType() != native.GlyphType)
        {
            return Refuse(SpellWorkbenchActionResultCodes.SelectionUnavailable,
                "The resolved spell component has the wrong native type.",
                out refusal, out reason);
        }
        if (native.IsGlyphAugment(glyph) != expectAugment)
        {
            return Refuse(SpellWorkbenchActionResultCodes.SelectionUnavailable,
                EntityIdentityFormatter.PlayerName(glyphId) +
                (expectAugment
                    ? " is not a spell augment."
                    : " is an augment and cannot be a core discovery component."),
                out refusal, out reason);
        }
        if (!native.IsGlyphAvailable(glyph))
        {
            return Refuse(SpellWorkbenchActionResultCodes.GlyphUnavailable,
                EntityIdentityFormatter.PlayerName(glyphId) +
                " is not available yet, so Magic > Spellbook > Loadout offers no copy of it to " +
                "socket. Discover it, or meet the requirements it names, first.",
                out refusal, out reason);
        }
        refusal = CommonActionResultCodes.Committed;
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

    /// <remarks>
    /// <paramref name="released"/> is the usage a replaced copy gives back. Spell weight is a
    /// bandwidth allocation, and <c>ResourceSO.HasUsageMissing</c> compares the snapped room left
    /// against the snapped weight, so the room after that removal is compared the same way.
    /// </remarks>
    private static bool TryReadShortUsage(
        SpellWorkbenchNativeBindings native,
        object usageCost,
        List<(Guid Id, object Resource, BigDouble Cost)>? released,
        out Guid resourceId,
        out BigDouble needed,
        out BigDouble free)
    {
        var totals = ReadUsageTotals(native, usageCost);
        for (var index = 0; index < totals.Count; index++)
        {
            var value = totals[index];
            var credit = ReleasedUsage(released, value.Id);
            var room = native.ReadResourceMissing(value.Resource) + credit;
            var fits = credit > BigDouble.Zero
                ? OrbGameMath.SnapFloorToInt(room) >= OrbGameMath.SnapFloorToInt(value.Cost)
                : native.HasResourceAmount(value.Resource, value.Cost);
            if (fits) continue;
            resourceId = value.Id;
            needed = value.Cost;
            free = room;
            return true;
        }
        resourceId = Guid.Empty;
        needed = BigDouble.Zero;
        free = BigDouble.Zero;
        return false;
    }

    private static BigDouble ReleasedUsage(
        List<(Guid Id, object Resource, BigDouble Cost)>? released,
        Guid resourceId)
    {
        if (released is null) return BigDouble.Zero;
        for (var index = 0; index < released.Count; index++)
            if (released[index].Id == resourceId) return released[index].Cost;
        return BigDouble.Zero;
    }

    /// <summary>
    /// A spell that does not fit the weight left on the bar, in the numbers the Loadout tile and the
    /// allocation's own counter print, and the gap a removal has to close.
    /// </summary>
    /// <remarks>
    /// <c>ResourceSO.HasUsageMissing</c> snaps both sides to whole numbers before comparing, so the
    /// numbers here are snapped the same way.
    /// </remarks>
    internal static string WeightShortfall(string resourceName, BigDouble needed, BigDouble free)
    {
        var weight = OrbGameMath.SnapFloorToInt(needed);
        var left = OrbGameMath.SnapFloorToInt(free);
        return "It weighs " + weight.ToString(CultureInfo.InvariantCulture) + " " + resourceName +
            " and " + left.ToString(CultureInfo.InvariantCulture) + " is free. Remove loaded " +
            "spells weighing " + (weight - left).ToString(CultureInfo.InvariantCulture) +
            " more, or raise that allocation. " + LoadedSpellsPointer;
    }

    /// <summary>Where the loaded spells and the weight each one frees are read.</summary>
    internal const string LoadedSpellsPointer =
        "→ Loaded spells and their weights: world_list category=spell-slots";

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

    /// <summary>Writes one augment layout over the player's staging with the game's own stack write.</summary>
    /// <remarks>
    /// <c>SetStack</c> replaces the whole multiplicity record, so this both clears what was staged
    /// and stages what was asked for in one call.
    /// </remarks>
    private static void WriteAugments(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> augmentGlyphs)
    {
        var record = native.CreateStackedRecord();
        SetStackedLayout(native, record, augmentGlyphs);
        native.SetListStack(native.ReadAugments(manager), record);
    }

    /// <summary>Whether the live staging reads back as exactly <paramref name="expected"/>.</summary>
    private static bool TryReadBack(
        SpellWorkbenchNativeBindings native,
        object manager,
        IList<object> expected,
        out string reason)
    {
        if (!TryReadAugmentSelection(native, manager, out var staged, out reason)) return false;
        if (SameCounts(native, staged, expected)) return true;
        reason = "Staging the chosen augments into the game's augment selection did not " +
            "land: " + Describe(native, expected) + " was written and " +
            Describe(native, staged) + " came back.";
        return false;
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

    private static bool TryResolveLoad(
        SpellWorkbenchNativeBindings native,
        Guid recipeId,
        out object manager,
        out object recipe,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        recipe = null!;
        manager = native.ReadManager()!;
        if (manager is null)
        {
            refusal = SpellWorkbenchActionResultCodes.ContractUnavailable;
            reason = "The spellbook is not loaded right now.";
            return false;
        }
        if (!TryResolveRecipe(native, recipeId, out recipe, out reason))
        {
            refusal = SpellWorkbenchActionResultCodes.IdentityUnavailable;
            return false;
        }
        refusal = CommonActionResultCodes.Committed;
        return true;
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
}
