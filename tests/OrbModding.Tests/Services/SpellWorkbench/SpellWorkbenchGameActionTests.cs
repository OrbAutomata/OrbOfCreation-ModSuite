using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.SpellWorkbench;

public sealed class SpellWorkbenchGameActionTests : IDisposable
{
    private const long Epoch = 41;

    public SpellWorkbenchGameActionTests()
    {
        SpellRecipeSO.All.Clear();
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = new SpellManager();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(Epoch));
        LoadoutScreen();
    }

    /// <summary>
    /// Loading a spell writes only the augment stack, charges nothing, and hands the player's own
    /// staging back.
    /// </summary>
    /// <remarks>
    /// The core selection is the game's to clear: <c>CreateRecipe</c> empties it on the way out,
    /// so a load that put a snapshot back would leave the workbench in a state no button press
    /// produces.
    /// </remarks>
    [Fact]
    public void LoadoutAddBakesTheExactAugmentLayoutAndChargesNothing()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = new BigDouble(10) };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, new BigDouble(3)));
        var wallet = new ResourceSO { quantity = new BigDouble(10) };
        var augment = Augment(maximum: 2);
        var stagedAugment = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(),
            Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(2, equipped.GetQuantityOfGlyph(augment));
        Assert.Equal(new BigDouble(10), wallet.quantity);
        Assert.Empty(SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Equal(1, SpellManager.instance.selectedAugmentGlyphs.GetStacks(stagedAugment));
    }

    [Fact]
    public void LoadoutAddAggregatesDuplicateGlyphRowsAgainstTheLiveUsableCount()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 2),
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 1),
            }));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, result.Preflight);
        Assert.Contains("Requested 3 uses", result.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// The two requirement gates are the row's own, including the direction of the usage one.
    /// </summary>
    /// <remarks>
    /// <c>UISpellRecipeButton.RenderContent</c> disables the row when the usage requirements are
    /// unmet <em>and</em> an augment is selected — an unaugmented load of a recipe whose usage
    /// requirements are unmet is exactly what the button allows. The suite had that backwards and
    /// refused the load a player makes with one click while permitting the one the game blocks.
    /// </remarks>
    [Fact]
    public void LoadoutAddMirrorsTheRowsGlyphAndUsageRequirementGates()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        recipe.NativeUsageRequirementsMet = false;
        var duration = Augment();
        duration.requiresDuration = true;
        var plain = Augment();
        var stagedAugment = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        using var action = Action();

        var glyphResult = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(duration.GetGuid(), 1) }));
        var augmentedResult = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(plain.GetGuid(), 1) }));
        var bareResult = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.GlyphRequirementsUnavailable, glyphResult.Preflight);
        Assert.Equal(
            SpellWorkbenchPreflight.UsageRequirementsUnavailable, augmentedResult.Preflight);
        Assert.Equal(
            Name(recipe) + " has not met its usage requirements yet, and the game only lets that " +
            "pass while no augment is selected. Load it with no augments, or meet the " +
            "requirement first.",
            augmentedResult.Reason);
        Assert.True(bareResult.Verified, bareResult.Reason);
        Assert.Single(SpellManager.instance.activeSpells.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
    }

    [Fact]
    public void LoadoutAddChecksUsageBudgetAndUniqueCompatibilityBeforeItStages()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = BigDouble.Zero };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, BigDouble.One));
        using var action = Action();

        var budget = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        recipe.baseUsageCost = new ResourceCostList();
        recipe.NativeUniqueSpell = true;
        SpellManager.instance!.activeSpells.value.Add(recipe.CreateEmpty(0));
        var unique = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.UsageUnaffordable, budget.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UniqueSpellConflict, unique.Preflight);
    }

    /// <summary>
    /// The game's augment selection holds a fixed number of different augments, and a stack write
    /// is not gated the way the player's clicks are.
    /// </summary>
    /// <remarks>
    /// <c>StackableListVariable.Stack</c> refuses a new distinct augment once the list is at
    /// <c>Max Spell Augment Slots</c>, so a layout with more different augments than that is one
    /// the player cannot build. Writing it straight into the stack would have produced it anyway.
    /// </remarks>
    [Fact]
    public void LoadoutAddRefusesMoreDifferentAugmentsThanTheSelectionHolds()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        SpellManager.instance!.selectedAugmentGlyphs.maxSizeVariable = new IntVariable { Value = 1 };
        var first = Augment();
        var second = Augment();
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
                new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
            }));

        Assert.Equal(SpellWorkbenchPreflight.AugmentSlotsExceeded, result.Preflight);
        Assert.Equal(
            "This layout puts 2 different augments on " + Name(recipe) +
            " and the game allows 1 at once (Max Spell Augment Slots). Ask for 1 different " +
            "augments or fewer, or raise that limit first.",
            result.Reason);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// A pre-check that answers available for a layout the add on identical arguments refuses is
    /// worse than no pre-check: it turns a cautious caller into a confident wrong one.
    /// </summary>
    [Fact]
    public void LoadPreviewRefusesEverythingTheAddRefusesBeforeItStages()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = BigDouble.Zero };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, BigDouble.One));
        using var action = Action();

        var budgetPreview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        var budgetAdd = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        recipe.baseUsageCost = new ResourceCostList();
        recipe.NativeUniqueSpell = true;
        SpellManager.instance!.activeSpells.value.Add(recipe.CreateEmpty(0));
        var uniquePreview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        var uniqueAdd = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.False(budgetPreview.Available);
        Assert.Equal(budgetAdd.Preflight, budgetPreview.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UsageUnaffordable, budgetPreview.Preflight);
        Assert.False(uniquePreview.Available);
        Assert.Equal(uniqueAdd.Preflight, uniquePreview.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UniqueSpellConflict, uniquePreview.Preflight);
    }

    /// <summary>
    /// The preview answers the one budget a load is weighed against, per requested layout.
    /// </summary>
    [Fact]
    public void LoadPreviewNamesTheUsageAllocationForTheRequestedLayout()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var weight = new ResourceSO { name = "Spell Power", quantity = new BigDouble(10) };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(weight, new BigDouble(3)));
        using var action = Action(permit: false);

        var preview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(Augment().GetGuid(), 1) }));

        Assert.True(preview.Available, preview.Reason);
        var row = Assert.Single(preview.Usage);
        Assert.Equal(weight.GetGuid(), row.ResourceId);
        Assert.Equal(new BigDouble(3), row.Amount);
    }

    [Fact]
    public void LoadoutAddFaultsWhenTheGameReturnsWithoutTheRequestedSpell()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        SpellManager.instance!.SuppressCreation = true;
        var stagedAugment = Augment();
        SpellManager.instance.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.VerificationFailed, result.Preflight);
        Assert.Empty(SpellManager.instance.activeSpells.value);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
    }

    [Fact]
    public void LoadoutAddRejectsUnownedAugmentsAndLoadsTheOwnedOne()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var owned = Augment();
        var unowned = Augment();
        unowned.level = 0;
        using var action = Action();

        var refused = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(unowned.GetGuid(), 1) }));
        var committed = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(owned.GetGuid(), 1) }));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, refused.Preflight);
        Assert.Contains("not owned", refused.Reason);
        Assert.True(committed.Verified, committed.Reason);
        Assert.Single(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// A spell the game quotes a creation price for still loads, and nothing is spent.
    /// </summary>
    /// <remarks>
    /// The suite used to read <c>SpellManager.GetSpellCreateCost</c>, gate on it, and pay it. The
    /// Loadout row reads none of that: <c>CreateRecipe</c> takes no payment at all, so an add that
    /// paid was moving resources on a press that costs nothing — and refused loads the player
    /// makes with one click whenever that invented price was out of reach.
    /// </remarks>
    [Fact]
    public void LoadoutAddMovesNoResourcesForASpellWithACreationPrice()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var knowledge = new ResourceSO { name = "Knowledge", quantity = new BigDouble(2) };
        var augment = Augment();
        augment.creationCostMod = new ValueModifier(
            ValueModifier.ValueModifierType.Raw, new BigDouble(1000));
        var price = new ResourceCostList();
        price.costs.Add(new ResourceTuple(knowledge, new BigDouble(3)));
        SpellManager.instance!.CreateCostOverride = price;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(),
            Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, price.PerformCalls);
        Assert.Equal(new BigDouble(2), knowledge.quantity);
        Assert.Single(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void LoadPreviewUsesTheSubmittedLayoutWithoutChangingStagedSelection()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        var weight = new ResourceSO { name = "Spell Power", quantity = new BigDouble(100) };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(weight, new BigDouble(3)));
        var first = Augment();
        var second = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.Stack(first, 1);
        using var action = Action(permit: false);

        var firstPreview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(first.GetGuid(), 1) }));
        var secondPreview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(second.GetGuid(), 1) }));

        Assert.True(firstPreview.Available, firstPreview.Reason);
        Assert.True(secondPreview.Available, secondPreview.Reason);
        Assert.Equal(new BigDouble(3), Assert.Single(firstPreview.Usage).Amount);
        Assert.Equal(new BigDouble(3), Assert.Single(secondPreview.Usage).Amount);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { first }, SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Equal(1, SpellManager.instance.selectedAugmentGlyphs.GetStacks(first));
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void StagedLayoutReadAggregatesInOrderWithoutMutationOwnership()
    {
        var (_, core, _) = Recipe(discovered: true);
        var augment = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(core);
        SpellManager.instance.selectedCoreGlyphs.value.Add(core);
        SpellManager.instance.selectedAugmentGlyphs.value.Add(augment);
        using var action = Action(permit: false);

        var layout = action.ReadStagedLayout();

        Assert.True(layout.Available, layout.Reason);
        var coreRow = Assert.Single(layout.Core);
        Assert.Equal(core.GetGuid(), coreRow.GlyphId);
        Assert.Equal(2, coreRow.Count);
        var augmentRow = Assert.Single(layout.Augments);
        Assert.Equal(augment.GetGuid(), augmentRow.GlyphId);
        Assert.Equal(1, augmentRow.Count);
        Assert.Equal(new[] { core, core }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { augment }, SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void StagedLayoutReadFailsClosedWhenItsBindingSetIsIncomplete()
    {
        using var action = Action(include: name =>
            name != "spell-workbench.manager-selected-core-action");

        var layout = action.ReadStagedLayout();

        Assert.False(layout.Available);
        Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, layout.Preflight);
        Assert.Equal(
            "The suite could not attach to Magic > Spellbook in this run, so it will refuse " +
            "every press there until the run restarts.",
            layout.Reason);
    }




    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())));

        Assert.Equal(SpellWorkbenchPreflight.WrongThread, result.Preflight);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// A staging write that returns without landing is named as the suite's own failure, before
    /// anything is created.
    /// </summary>
    /// <remarks>
    /// The stack write the load stages with can return normally and write nothing. Without the
    /// read-back, the next thing that happens is a spell created with augments nobody asked for
    /// and a postcondition failure blaming the game for it.
    /// </remarks>
    [Fact]
    public void LoadoutAddNamesItsOwnStagingFailureWhenTheGameSilentlyRefusesTheWrite()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        SpellManager.instance!.selectedAugmentGlyphs.SuppressSetStack = true;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchPreflight.StagedWriteFailed, result.Preflight);
        Assert.Equal(
            "Staging the chosen augments into the game's augment selection did not land: 2x " +
            Name(augment) + " was written and no glyphs came back.",
            result.Reason);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// A full bar says how many slots it has and that nothing was spent, because "every slot is
    /// occupied" without a number leaves a caller unable to tell a full bar from a bar it cannot
    /// see.
    /// </summary>
    [Fact]
    public void LoadoutAddNamesTheSlotCountAndThatNothingWasStagedWhenTheBarIsFull()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var active = SpellManager.instance!.activeSpells;
        active.maxSizeVariable = new IntVariable { Value = 2 };
        active.value.Add(new Spell(new SpellRecipeSO()));
        active.value.Add(new Spell(new SpellRecipeSO()));
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.LoadoutFull, result.Preflight);
        Assert.Equal(
            "All 2 loadout slots hold a spell, so there is nowhere to put " + Name(recipe) +
            ". Remove a loaded spell first; nothing was staged and nothing was spent.",
            result.Reason);
        Assert.Equal(2, active.value.Count);
    }

    /// <summary>
    /// An augment stack the game will not expose is refused before anything is created.
    /// </summary>
    /// <remarks>
    /// A loaded spell is baked from the augment stack, and the plain list write never touches it.
    /// Staging with that write and not reading it back had one worst case: create a spell carrying
    /// none of the augments that were asked for and fail the suite's own postcondition afterwards.
    /// </remarks>
    [Fact]
    public void LoadoutAddRefusesBeforeLoadingWhenTheAugmentStackCannotBeStaged()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        SpellManager.instance!.selectedAugmentGlyphs.isStackable = false;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        Assert.Equal(
            "The live augment selection exposes no stack for the game to bake a spell from, so " +
            "no augment layout can be staged or read back.",
            result.Reason);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// The multiplicity a caller asked for reaches the loaded spell, and the load says so.
    /// </summary>
    /// <remarks>
    /// The suite verified its own postcondition against <c>Spell.GetAugmentGlyphs()</c>, which
    /// hands back one entry per distinct glyph. Every load of two-of-a-glyph therefore reported a
    /// fault on a spell the game had created exactly as asked — the layout is compared against the
    /// stack the spell was baked from instead.
    /// </remarks>
    [Fact]
    public void LoadoutAddVerifiesRepeatedAugmentsAgainstTheStackTheSpellIsBakedFrom()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 3);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 3) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(3, equipped.GetQuantityOfGlyph(augment));
        Assert.Equal(new[] { augment }, equipped.GetAugmentGlyphs());
        Assert.Empty(SpellManager.instance.selectedAugmentGlyphs.value);
    }

    /// <summary>
    /// A spell whose Recipe Book core the workbench cannot hold at once still loads in one call.
    /// </summary>
    /// <remarks>
    /// The workbench holds one core glyph at a time — <c>Max Spell Creation Slots</c> is authored
    /// at one — and the suite used to stage the recipe's whole authored core into it before
    /// pressing the row. The list truncated silently and every two- and three-book spell refused,
    /// which is 50 of the game's 65 recipes. The row reads no core at all.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LoadoutAddLoadsSpellsWhoseCoreTheWorkbenchCannotHoldAtOnce(int coreGlyphs)
    {
        var (recipe, first, second) = Recipe(discovered: true);
        if (coreGlyphs == 3) recipe.coreRecipe.Add(second);
        SpellManager.instance!.selectedCoreGlyphs.maxSizeVariable = new IntVariable { Value = 1 };
        SpellManager.instance.selectedCoreGlyphs.value.Add(first);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction( recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(coreGlyphs, recipe.coreRecipe.Count);
        Assert.Single(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        foreach (var missing in SpellWorkbenchNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(new SpellWorkbenchAction(
                recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
            Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutChangingSelection()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(SpellWorkbenchPreflight.LifecycleReplaced,
            stale.Submit(new SpellWorkbenchAction(
                recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
        Assert.Equal(SpellWorkbenchPreflight.MutationPermitUnavailable,
            unowned.Submit(new SpellWorkbenchAction(
                recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    private static (SpellRecipeSO Recipe, GlyphSO First, GlyphSO Second) Recipe(bool discovered = false)
    {
        var first = new GlyphSO
        {
            DisplayName = "Form",
            NativeAvailable = true,
            maxUsages = new ValueModifierRecord(new BigDouble(4)),
            level = 1,
        };
        var second = new GlyphSO
        {
            DisplayName = "Bolt",
            NativeAvailable = true,
            maxUsages = new ValueModifierRecord(new BigDouble(4)),
            level = 1,
        };
        var recipe = new SpellRecipeSO { discovered = discovered };
        recipe.coreRecipe.Add(first);
        recipe.coreRecipe.Add(second);
        SpellRecipeSO.All.Add(recipe);
        SpellManager.instance!.availableSpellRecipes.value.Add(recipe);
        IdScriptableObject.RuntimeLookup[first.GetGuid()] = first;
        IdScriptableObject.RuntimeLookup[second.GetGuid()] = second;
        return (recipe, first, second);
    }

    private static string Name(IdScriptableObject entity) => entity.GetGuid().ToString("D");

    private static SpellWorkbenchGlyphStack[] CoreLayout(GlyphSO first, GlyphSO second) =>
        new[]
        {
            new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
            new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
        };

    private static GlyphSO Augment(int maximum = 4)
    {
        var augment = new GlyphSO
        {
            DisplayName = "Bright",
            NativeAvailable = true,
            augmentsSpells = true,
            maxUsages = new ValueModifierRecord(new BigDouble(maximum)),
            level = 1,
        };
        IdScriptableObject.RuntimeLookup[augment.GetGuid()] = augment;
        return augment;
    }

    /// <summary>
    /// Locked, unaffordable and full are three different answers. Until the Spellbook Loadout
    /// upgrade is bought the game draws no row to press, so an empty loadout slot says nothing
    /// about whether the load exists.
    /// </summary>
    [Fact]
    public void LoadoutAddAndPreviewRefuseWhileTheLoadoutScreenIsLocked()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        LoadoutScreen(unlocked: false);
        using var action = Action();

        var add = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>()));
        var preview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.ScreenLocked, add.Preflight);
        Assert.Equal(
            "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no row to load. " +
            "Buy the Spellbook Loadout upgrade first.",
            add.Reason);
        Assert.Equal(SpellWorkbenchPreflight.ScreenLocked, preview.Preflight);
        Assert.Equal(add.Reason, preview.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// The screen the Loadout row lives on, unlocked. Every load goes through it, because a locked
    /// screen draws no row to press.
    /// </summary>
    private static ViewSO LoadoutScreen(bool unlocked = true)
    {
        var view = new ViewSO { available = unlocked };
        view.SetGuid(KnownEntities.MagicSpellbookLoadout.Uuid);
        IdScriptableObject.RuntimeLookup[view.GetGuid()] = view;
        return view;
    }

    private static SpellWorkbenchGameAction Action(
        long epoch = Epoch,
        bool permit = true,
        Func<string, bool>? include = null)
    {
        var action = new SpellWorkbenchGameAction(
            () => epoch,
            () => permit,
            () => "test ownership unavailable",
            name => typeof(SpellManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            include ?? (_ => true));
        if (include is null) Assert.True(action.BindingsAvailable, action.BindingFailure);
        return action;
    }

    public void Dispose()
    {
        SpellRecipeSO.All.Clear();
        IdScriptableObject.RuntimeLookup.Clear();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(Epoch));
        SpellManager.instance = null;
    }
}
