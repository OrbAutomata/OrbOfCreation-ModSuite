using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class SpellManager
{
    public static SpellManager? instance;
    public static bool NativeCanCast { get; set; } = true;
    public static Exception? CanCastASpellFailure { get; set; }
    public SpellRecipeListVariable availableSpellRecipes = new SpellRecipeListVariable();
    public ResourceListVariable spellWeightVariables = new ResourceListVariable();
    public GlyphListVariable selectedCoreGlyphs = new GlyphListVariable();
    public GlyphListVariable selectedAugmentGlyphs = new GlyphListVariable();
    public SpellListVariable activeSpells = new SpellListVariable();
    public bool SuppressSelectionResolution { get; set; }
    public bool SuppressCreation { get; set; }
    public bool CreateEmptyIdentity { get; set; }
    public bool ThrowAfterCreation { get; set; }
    public bool SuppressRemoval { get; set; }
    public bool ThrowBeforeRemoval { get; set; }
    public bool ThrowAfterRemoval { get; set; }
    public Exception? ThrowAfterFireSpellIndex { get; set; }
    public int RemoveCalls { get; private set; }

    /// <summary>How often the native gate refused a removal the caller asked for anyway.</summary>
    public int RefusedRemovals { get; private set; }
    public int TryLevelAllCalls { get; private set; }
    public bool SuppressLevelAll { get; set; }
    public ResourceCostList? CreateCostOverride { get; set; }
    public Func<IReadOnlyList<GlyphSO>, ResourceCostList>? CreateCostResolver { get; set; }

    public static bool CanCastASpell() => CanCastASpellFailure is null ? NativeCanCast : throw CanCastASpellFailure;
    public static ResourceCostList GetUsageCostOfSpell(Spell spell) => spell.GetUsageCost();

    /// <summary>
    /// The game's own matcher: an empty list resolves to nothing, augments are stripped, candidates
    /// are the recipes whose core is the same length, each is then filtered by whether it contains
    /// the glyph — membership, not position and not multiplicity — and the first survivor wins.
    /// </summary>
    /// <remarks>
    /// Ordered reference equality, which this stub used to apply, cannot produce the case the wire
    /// actually met: two recipes are candidates for one layout and the earlier one in the registry
    /// takes it, so the caller's recipe is unreachable through a layout that looks exactly right.
    /// </remarks>
    public SpellRecipeSO? GetSpellFromRecipe(List<GlyphSO> glyphs)
    {
        if (SuppressSelectionResolution || glyphs.Count == 0) return null;
        var coreGlyphs = glyphs.Where(glyph => !glyph.IsSpellAugment()).ToList();
        var candidates = availableSpellRecipes.value
            .Where(recipe => recipe.coreRecipe.Count == coreGlyphs.Count)
            .ToList();
        foreach (var glyph in coreGlyphs)
            candidates = candidates.Where(recipe => recipe.coreRecipe.Contains(glyph)).ToList();
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public ResourceCostList GetSpellCreateCost(List<GlyphSO> glyphs)
    {
        var recipe = GetSpellFromRecipe(glyphs);
        if (recipe is null) return new ResourceCostList();
        return CreateCostResolver?.Invoke(glyphs) ?? CreateCostOverride ?? GlyphSO.GetCreationCostOfList(
            new ResourceCostList(), glyphs.Where(glyph => glyph.IsSpellAugment()));
    }

    public void CreateSpell()
    {
        var recipe = GetSpellFromRecipe(selectedCoreGlyphs.GetFilledElements());
        if (recipe is null) return;
        CreateRecipe(recipe);
    }

    public void CreateRecipe(SpellRecipeSO recipe)
    {
        if (!recipe.IsDiscovered() || SuppressCreation || !activeSpells.HasEmptySpot()) return;
        var spell = recipe.CreateEmpty(0);
        spell.SetLevel(recipe.GetSelectedSpellLevel());
        // The stack, not the value list: an augment that only ever reached `value` is not in the
        // spell the game bakes, however plainly it shows on the staging screen.
        var staged = selectedAugmentGlyphs.GetStackedRecord();
        spell.SetAugmentGlyphs(staged is null
            ? new Stacked.StackedIdRecord<GlyphSO>()
            : new Stacked.StackedIdRecord<GlyphSO>(staged));
        if (CreateEmptyIdentity) spell.guidContainer = new GuidContainer(Guid.Empty);
        AddSpell(spell);
        selectedCoreGlyphs.Empty();
        if (ThrowAfterCreation) throw new InvalidOperationException("injected failure after creation");
    }

    public void PostDiscoverRecipe(SpellRecipeSO recipe)
    {
        var spell = recipe.CreateEmpty(0);
        if (activeSpells.HasEmptySpot() && spell.get_reference()!.GetUsageCost().HasEnough())
            AddSpell(spell);
    }

    private void AddSpell(Spell spell) => activeSpells.Add(spell);

    /// <summary>
    /// The game's own removal, gate included: an empty slot is a no-op, and a spell short of full
    /// charges or in the middle of a cast is refused. The refusal is not free — the game switches
    /// the spell to a time-based cooldown on its way out — so a caller that skips the gate and
    /// calls anyway leaves the loadout changed without removing anything.
    /// </summary>
    public void RemoveSpell(Spell spell)
    {
        RemoveCalls++;
        if (ThrowBeforeRemoval) throw new InvalidOperationException("injected failure before spell removal");
        if (spell.IsEmpty()) return;
        if (!spell.IsAtMaxCharges() || spell.IsCasting() || spell.IsReadyingCast())
        {
            RefusedRemovals++;
            spell.SwitchedToTimeBasedCooldown = true;
            return;
        }
        if (!SuppressRemoval) activeSpells.Remove(spell);
        if (ThrowAfterRemoval) throw new InvalidOperationException("injected failure after spell removal");
    }

    public void FireSpellIndex(int index)
    {
        var spell = activeSpells[index];
        spell.GetType().GetMethod(
            "Fire",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null)?.Invoke(spell, Array.Empty<object>());
        if (ThrowAfterFireSpellIndex is { } failure) throw failure;
    }

    public void TryLevelAllSpells()
    {
        TryLevelAllCalls++;
        if (SuppressLevelAll) return;
        foreach (var recipe in availableSpellRecipes.value)
        {
            while (recipe.IsDiscovered() && recipe.levelingPrerequisites.Check() &&
                   recipe.IsReadyToLevelMastery() && recipe.GetLevelCost().HasEnough())
            {
                recipe.GetLevelCost().PerformCost();
                recipe.PurchaseLevel();
            }
        }
    }
}
