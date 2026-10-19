using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Services.TestSupport;
using Xunit;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Tests.Services.SpellWorkbench;

[Collection(GameActionFaultLogCollection.Name)]
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

        Assert.Equal(SpellWorkbenchActionResultCodes.GlyphUsagesExceeded, result.Code);
        Assert.Equal(
            Name(augment) + " allows 2 uses per spell and this layout asks for 3 of 2. Ask for 2 " +
            "or fewer.",
            result.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// A glyph is socketable when the game says so. The game's socket gate is
    /// <c>GlyphSO.IsAvailable()</c> plus <c>GetMaxUsages()</c> headroom, and neither reads a level:
    /// a freshly discovered augment is available at level 0 with one usable copy, and the Loadout
    /// page draws its tile as clickable. The suite refused that press for an ownership predicate
    /// the game does not have, which blocked every augment discovered before Upgrade Glyphs exists.
    /// </summary>
    [Fact]
    public void LoadoutAddSocketsAnAvailableAugmentTheGameHasNeverLeveled()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 1);
        augment.level = 0;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(1, equipped.GetQuantityOfGlyph(augment));
    }

    /// <summary>
    /// The one glyph gate the game does keep, said in the game's own words. A resolved glyph was
    /// never missing, so the refusal names the screen that would offer it rather than answering
    /// "not found".
    /// </summary>
    [Fact]
    public void LoadoutAddRefusesAGlyphTheGameOffersNoCopyOf()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 1);
        augment.NativeAvailable = false;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));

        Assert.Equal(SpellWorkbenchActionResultCodes.GlyphUnavailable, result.Code);
        Assert.Equal(
            Name(augment) + " is not available yet, so Magic > Spellbook > Loadout offers no copy " +
            "of it to socket. Discover it, or meet the requirements it names, first.",
            result.Reason);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.GlyphRequirementsUnavailable, glyphResult.Code);
        Assert.Equal(
            SpellWorkbenchActionResultCodes.UsageRequirementsUnavailable, augmentedResult.Code);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.UsageUnaffordable, budget.Code);
        Assert.Equal(SpellWorkbenchActionResultCodes.UniqueSpellConflict, unique.Code);
    }

    /// <summary>
    /// A load that does not fit names the weight the game charges, the room left, and the gap.
    /// </summary>
    /// <remarks>
    /// Sanctify is authored at 6 and the game charged 12 with its "use effectively" requirements
    /// unmet; the refusal said only that the load "would push Spell Capacity past its allocation",
    /// so a player freeing room had to guess how much.
    /// </remarks>
    [Fact]
    public void LoadoutAddNamesTheWeightTheGameChargesAndTheRoomLeft()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var capacity = new ResourceSO
        {
            bandwidthResource = true,
            quantity = new BigDouble(24),
            maxQuantity = new ValueModifierRecord(new BigDouble(31)),
        };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(capacity, new BigDouble(6)));
        recipe.penaltyUsageCost = 6;
        recipe.NativeUsageRequirementsMet = false;
        using var action = Action();

        var refused = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchActionResultCodes.UsageUnaffordable, refused.Code);
        Assert.Equal(
            "Loading " + Name(recipe) + " would push " + Name(capacity) + " past its allocation. " +
            "It weighs 12 " + Name(capacity) + " and 7 is free. Remove loaded spells weighing 5 " +
            "more, or raise that allocation. → Loaded spells and their weights: world_list " +
            "category=spell-slots",
            refused.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.AugmentSlotsExceeded, result.Code);
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
        Assert.Equal(budgetAdd.Code, budgetPreview.Code);
        Assert.Equal(SpellWorkbenchActionResultCodes.UsageUnaffordable, budgetPreview.Code);
        Assert.False(uniquePreview.Available);
        Assert.Equal(uniqueAdd.Code, uniquePreview.Code);
        Assert.Equal(SpellWorkbenchActionResultCodes.UniqueSpellConflict, uniquePreview.Code);
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

    /// <summary>
    /// Augments are baked in, so pricing a new layout for a loaded spell on a full bar is pricing
    /// its replacement: the bar-full refusal answered a question the player was not asking.
    /// </summary>
    [Fact]
    public void LoadPreviewOnAFullBarPricesAReplacementOfTheLowestSlotCopy()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var capacity = Capacity(maximum: 12, used: 9);
        recipe.baseUsageCost.costs.Add(new ResourceTuple(capacity, new BigDouble(3)));
        var active = SpellManager.instance!.activeSpells;
        active.maxSizeVariable = new IntVariable { Value = 3 };
        active.value.Add(new Spell(new SpellRecipeSO()));
        active.value.Add(recipe.CreateEmpty(0));
        active.value.Add(recipe.CreateEmpty(0));
        var augment = Augment();
        var layout = new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) };
        using var action = Action();

        var preview = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, layout));
        var add = action.Submit(new SpellWorkbenchAction(recipe.GetGuid(), Epoch, layout));

        Assert.True(preview.Available, preview.Reason);
        Assert.Equal(Name(recipe) + " in slot 2 — remove it, then add", preview.Replaces);
        Assert.Equal(new BigDouble(3), Assert.Single(preview.Usage).Amount);
        Assert.Equal(SpellWorkbenchActionResultCodes.LoadoutFull, add.Code);
        Assert.Equal(3, active.value.Count);
        Assert.Empty(SpellManager.instance.selectedAugmentGlyphs.value);
    }

    /// <summary>
    /// The replaced copy's weight is free once it is removed, and nothing beyond it is.
    /// </summary>
    [Fact]
    public void LoadPreviewReplacementCountsTheCopysWeightAsFreeAndNoMore()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var capacity = Capacity(maximum: 6, used: 6);
        recipe.baseUsageCost.costs.Add(new ResourceTuple(capacity, new BigDouble(3)));
        var augment = Augment();
        augment.usageCost.costs.Add(new ResourceTuple(capacity, new BigDouble(2)));
        var active = SpellManager.instance!.activeSpells;
        active.maxSizeVariable = new IntVariable { Value = 1 };
        active.value.Add(recipe.CreateEmpty(0));
        using var action = Action();

        var same = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        var heavier = action.Preview(new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));

        Assert.True(same.Available, same.Reason);
        Assert.Equal(Name(recipe) + " in slot 1 — remove it, then add", same.Replaces);
        Assert.Equal(SpellWorkbenchActionResultCodes.UsageUnaffordable, heavier.Code);
        Assert.Equal(
            Name(recipe) + " in place of its copy in slot 1 would push " + Name(capacity) +
            " past its allocation, counting that copy's weight as free. It weighs 5 " +
            Name(capacity) + " and 3 is free. Remove loaded spells weighing 2 more, or raise " +
            "that allocation. → Loaded spells and their weights: world_list category=spell-slots",
            heavier.Reason);
    }

    /// <summary>
    /// The unique-copy gate is answered by the removal a replacement holds after; with a free slot
    /// the preview is another copy, which the game refuses for a unique spell.
    /// </summary>
    [Fact]
    public void LoadPreviewSkipsTheUniqueCopyGateOnlyWhenItPricesAReplacement()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        recipe.NativeUniqueSpell = true;
        var active = SpellManager.instance!.activeSpells;
        active.maxSizeVariable = new IntVariable { Value = 2 };
        active.value.Add(recipe.CreateEmpty(0));
        using var action = Action();
        var request = new SpellWorkbenchLoadPreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>());

        var freeSlot = action.Preview(request);
        active.value.Add(new Spell(new SpellRecipeSO()));
        var fullBar = action.Preview(request);
        var add = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchActionResultCodes.UniqueSpellConflict, freeSlot.Code);
        Assert.True(fullBar.Available, fullBar.Reason);
        Assert.Equal(Name(recipe) + " in slot 1 — remove it, then add", fullBar.Replaces);
        Assert.Equal(SpellWorkbenchActionResultCodes.LoadoutFull, add.Code);
    }

    private static ResourceSO Capacity(int maximum, int used) => new()
    {
        bandwidthResource = true,
        quantity = new BigDouble(used),
        maxQuantity = new ValueModifierRecord(new BigDouble(maximum)),
    };

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

        Assert.Equal(SpellWorkbenchActionResultCodes.VerificationFailed, result.Code);
        Assert.Empty(SpellManager.instance.activeSpells.value);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
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
        Assert.Equal(SpellWorkbenchActionResultCodes.ContractUnavailable, layout.Code);
        Assert.Equal(
            "The suite could not attach to Magic > Spellbook in this run, so it will refuse " +
            "every press there until the run restarts.",
            layout.Reason);
    }




    [Fact]
    public void An_unreadable_lifecycle_epoch_faults_before_the_native_call()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action(readEpoch: () => throw new InvalidOperationException("lifecycle unavailable"));

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.IsFault);
        Assert.Equal(SpellWorkbenchActionResultCodes.ContractUnavailable, result.Code);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    /// <summary>
    /// Staging the requested augments is a press. A load that stops because the staging did not
    /// land counts that press as an attempt, and refuses only once the player's own augment
    /// selection reads back as it was.
    /// </summary>
    [Fact]
    public void AStagingWriteThatDoesNotLandRefusesWithOneAttemptAndThePlayersSelectionBack()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        var playerAugment = Augment();
        SpellManager.instance!.selectedAugmentGlyphs.Stack(playerAugment, 1);
        SpellManager.instance.selectedAugmentGlyphs.SuppressSetStack = true;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.False(result.Verified || result.IsFault, result.Reason);
        Assert.Equal(SpellWorkbenchActionResultCodes.StagedWriteFailed, result.Code);
        Assert.Equal(1, result.CallOutcome.MutationAttempts);
        Assert.EndsWith(GameActionAnswer.Undone(), result.Reason, StringComparison.Ordinal);
        Assert.Equal(1, SpellManager.instance.selectedAugmentGlyphs.GetStacks(playerAugment));
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// When the load throws and putting the player's augments back throws too, the load's own
    /// exception stays the cause the answer's reference names, and the put-back's is written into
    /// the same log entry after it.
    /// </summary>
    [Fact]
    public void ALoadThatThrowsKeepsItsCauseWhenPuttingTheSelectionBackThrowsToo()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        SpellManager.instance!.selectedAugmentGlyphs.Stack(Augment(), 1);
        SpellManager.instance.ThrowAfterCreation = true;
        SpellManager.instance.selectedAugmentGlyphs.ThrowOnSetStackFromCall = 2;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchActionResultCodes.PostCommitFault, result.Code);
        var reference = Assert.Single(Regex.Matches(result.Reason, "MCP-[0-9A-F]{8}")
            .Select(match => match.Value));
        var entry = Assert.Single(logged, line => line.Contains(reference, StringComparison.Ordinal));
        var cause = entry.IndexOf("injected failure after creation", StringComparison.Ordinal);
        var putBack = entry.IndexOf("injected failure while writing the stack", StringComparison.Ordinal);
        Assert.InRange(cause, 0, int.MaxValue);
        Assert.True(putBack > cause, entry);
    }

    /// <summary>
    /// A load that throws and leaves the requested augments staged in place of the player's own
    /// says so, so the player's Spellcraft selection never changes without a word.
    /// </summary>
    [Fact]
    public void ALoadThatThrowsAndCannotPutTheSelectionBackSaysTheSelectionChanged()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        SpellManager.instance!.selectedAugmentGlyphs.Stack(Augment(), 1);
        SpellManager.instance.ThrowAfterCreation = true;
        SpellManager.instance.selectedAugmentGlyphs.SuppressSetStackFromCall = 2;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchActionResultCodes.PostCommitFault, result.Code);
        Assert.Contains(GameActionAnswer.GameErrored("Magic > Spellbook").Sentence, result.Reason, StringComparison.Ordinal);
        Assert.EndsWith(GameActionAnswer.NotUndone("Magic > Spellbook"), result.Reason, StringComparison.Ordinal);
        Assert.Equal(2, SpellManager.instance.selectedAugmentGlyphs.GetStacks(augment));
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())));

        Assert.Equal(SpellWorkbenchActionResultCodes.WrongThread, result.Code);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.StagedWriteFailed, result.Code);
        Assert.Equal(
            "Staging the chosen augments into the game's augment selection did not land: 2x " +
            Name(augment) + " was written and no glyphs came back. " + GameActionAnswer.Undone(),
            result.Reason);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// A load stages the requested augments over the player's own, then puts the player's back.
    /// When that second write does not land, the fault says what came back instead, so whoever
    /// reads it knows what the Spellcraft selection now holds.
    /// </summary>
    [Fact]
    public void ASelectionThatCannotBePutBackAfterTheLoadSaysWhatCameBack()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        var stagedAugment = Augment();
        SpellManager.instance!.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        SpellManager.instance.selectedAugmentGlyphs.SuppressSetStackFromCall = 2;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchActionResultCodes.PostCommitFault, result.Code);
        Assert.EndsWith(
            "The player's own augment selection could not be restored after the load; read the " +
            "loadout before calling again. Putting it back: Staging the chosen augments into the game's augment selection did " +
            "not land: " + Name(stagedAugment) + " was written and 2x " + Name(augment) +
            " came back.",
            result.Reason,
            StringComparison.Ordinal);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.LoadoutFull, result.Code);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.ContractUnavailable, result.Code);
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
            Assert.Equal(SpellWorkbenchActionResultCodes.ContractUnavailable, result.Code);
        }
    }

    [Theory]
    [InlineData("spell-workbench.recipe-get-glyphs-action")]
    [InlineData("spell-composition.spell-get-augment-glyphs-action")]
    public void LoadoutAddNeverNeedsAMemberThePressDoesNotRead(string unread)
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        using var action = Action(include: id => id != unread);

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(),
            Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(2, equipped.GetQuantityOfGlyph(augment));
    }

    [Fact]
    public void OverflowFromANativeReadIsAnsweredAsUnreadable()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        recipe.DiscoveredReadFailure = new OverflowException("injected discovery read overflow");
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchActionResultCodes.ContractUnavailable, result.Code);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void UnexpectedExceptionFromANativeReadEscapesInsteadOfReadingAsUnavailable()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        recipe.DiscoveredReadFailure = new NullReferenceException("injected discovery read failure");
        using var action = Action();

        Assert.Throws<NullReferenceException>(() => action.Submit(new SpellWorkbenchAction(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())));
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutChangingSelection()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced,
            stale.Submit(new SpellWorkbenchAction(
                recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())).Code);
        Assert.Equal(SpellWorkbenchActionResultCodes.MutationPermitUnavailable,
            unowned.Submit(new SpellWorkbenchAction(
                recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>())).Code);
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

        Assert.Equal(SpellWorkbenchActionResultCodes.ScreenLocked, add.Code);
        Assert.Equal(
            "Magic > Spellbook > Loadout is not unlocked yet, so the game draws no row to load. " +
            "Buy the Spellbook Loadout upgrade first.",
            add.Reason);
        Assert.Equal(SpellWorkbenchActionResultCodes.ScreenLocked, preview.Code);
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
        Func<string, bool>? include = null,
        Func<long>? readEpoch = null)
    {
        var action = new SpellWorkbenchGameAction(
            readEpoch ?? (() => epoch),
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
        GameActionFaultLog.ConfigureLog(null);
        SpellRecipeSO.All.Clear();
        IdScriptableObject.RuntimeLookup.Clear();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(Epoch));
        SpellManager.instance = null;
    }
}
