using System;
using System.Linq;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common.Runtime.World;
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
    }

    [Fact]
    public void DiscoverUsesTheNativePipelineAndVerifiesTheTargetOutcome()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified);
        Assert.True(recipe.discovered);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
        Assert.Single(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void DiscoveryResolvesTheExactSubmittedCoreCompositionBeforePayment()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover,
            recipe.GetGuid(),
            Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
                new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
            },
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified, result.Reason);
        Assert.True(recipe.discovered);
    }

    /// <summary>
    /// A component sequence the game reads as a different recipe is refused before anything is
    /// staged, and the player's own selection is left where it was.
    /// </summary>
    /// <remarks>
    /// The mismatch is a glyph the recipe's core does not contain rather than a reordering: the
    /// game matches a core by count and membership, so <c>[second, first]</c> resolves to exactly
    /// the same recipe as <c>[first, second]</c> and is no mismatch at all.
    /// </remarks>
    [Fact]
    public void DiscoveryMismatchRefusesWithoutDirtyingTheExistingUiSelection()
    {
        var (recipe, first, _) = Recipe();
        var stranger = new GlyphSO
        {
            DisplayName = "Ember",
            NativeAvailable = true,
            maxUsages = new ValueModifierRecord(new BigDouble(4)),
            level = 1,
        };
        IdScriptableObject.RuntimeLookup[stranger.GetGuid()] = stranger;
        SpellManager.instance!.selectedCoreGlyphs.value.Add(first);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover,
            recipe.GetGuid(),
            Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
                new SpellWorkbenchGlyphStack(stranger.GetGuid(), 1),
            },
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.WrongSelection, result.Preflight);
        Assert.Equal(new[] { first }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.False(recipe.discovered);
    }

    [Fact]
    public void LoadoutAddBakesTheExactGlyphLayoutAndPaysOnlyAfterAdmission()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        recipe.NativeUsageRequirementsMet = false;
        var usageResource = new ResourceSO { quantity = new BigDouble(10) };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, new BigDouble(3)));
        var paymentResource = new ResourceSO { quantity = new BigDouble(10) };
        var createCost = new ResourceCostList();
        createCost.costs.Add(new ResourceTuple(paymentResource, new BigDouble(2)));
        SpellManager.instance!.CreateCostOverride = createCost;
        var augment = new GlyphSO
        {
            DisplayName = "Bright",
            NativeAvailable = true,
            augmentsSpells = true,
            maxUsages = new ValueModifierRecord(new BigDouble(2)),
            level = 1,
        };
        IdScriptableObject.RuntimeLookup[augment.GetGuid()] = augment;
        var stagedAugment = Augment();
        SpellManager.instance.selectedCoreGlyphs.value.Add(stagedCore);
        // Staged the way the game stages: the stack is what a created spell is baked from,
        // and a glyph that only ever reached the value list beside it is not staged at all.
        SpellManager.instance.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(new[] { augment, augment }, equipped.GetAugmentGlyphs());
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Equal(1, createCost.PerformCalls);
    }

    [Fact]
    public void LoadoutAddAggregatesDuplicateGlyphRowsAgainstTheLiveUsableCount()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[]
            {
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 2),
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 1),
            }));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, result.Preflight);
        Assert.Contains("Requested 3 uses", result.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void LoadoutAddRefusesUnmetGlyphAndRecipeRequirementsBeforePayment()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        recipe.NativeUsageRequirementsMet = false;
        var augment = Augment();
        augment.requiresDuration = true;
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        var stagedAugment = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.value.Add(stagedAugment);
        using var action = Action();

        var glyphResult = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));
        var recipeResult = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.GlyphRequirementsUnavailable, glyphResult.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UsageRequirementsUnavailable, recipeResult.Preflight);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
    }

    [Fact]
    public void LoadoutAddChecksUsageBudgetAndUniqueCompatibilityBeforePayment()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = BigDouble.Zero };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, BigDouble.One));
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        using var action = Action();

        var budget = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));
        recipe.baseUsageCost = new ResourceCostList();
        recipe.NativeUniqueSpell = true;
        SpellManager.instance.activeSpells.value.Add(recipe.CreateEmpty(0));
        var unique = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.UsageUnaffordable, budget.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UniqueSpellConflict, unique.Preflight);
        Assert.Equal(0, payment.PerformCalls);
    }

    /// <summary>
    /// A pre-check that answers priced-and-affordable for a layout the add on identical arguments
    /// refuses is worse than no pre-check: it turns a cautious caller into a confident wrong one.
    /// </summary>
    [Fact]
    public void PricePreviewRefusesEverythingTheAddRefusesBeforeItStages()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = BigDouble.Zero };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, BigDouble.One));
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        using var action = Action();

        var budgetPreview = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        var budgetAdd = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        recipe.baseUsageCost = new ResourceCostList();
        recipe.NativeUniqueSpell = true;
        SpellManager.instance.activeSpells.value.Add(recipe.CreateEmpty(0));
        var uniquePreview = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch, Array.Empty<SpellWorkbenchGlyphStack>()));
        var uniqueAdd = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.False(budgetPreview.Available);
        Assert.Equal(budgetAdd.Preflight, budgetPreview.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UsageUnaffordable, budgetPreview.Preflight);
        Assert.False(uniquePreview.Available);
        Assert.Equal(uniqueAdd.Preflight, uniquePreview.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UniqueSpellConflict, uniquePreview.Preflight);
        Assert.Equal(0, payment.PerformCalls);
    }

    /// <summary>
    /// The price is still an answer, not a refusal: a caller asking what a layout costs gets the
    /// costs and an honest <c>affordable: false</c> rather than a shut door.
    /// </summary>
    [Fact]
    public void PricePreviewStillPricesALayoutTheCallerCannotAffordYet()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var knowledge = new ResourceSO { name = "Knowledge", quantity = new BigDouble(2) };
        var price = new ResourceCostList();
        price.costs.Add(new ResourceTuple(knowledge, new BigDouble(3)));
        SpellManager.instance!.CreateCostResolver = _ => price;
        using var action = Action(permit: false);

        var preview = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(Augment().GetGuid(), 1) }));

        Assert.True(preview.Available, preview.Reason);
        Assert.False(preview.Affordable);
        Assert.Equal(knowledge.GetGuid(), preview.ShortResourceId);
    }

    [Fact]
    public void LoadoutAddFaultsWhenPaymentRunsWithoutTheExactRequestedOutcome()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.SuppressCreation = true;
        var stagedAugment = Augment();
        SpellManager.instance.selectedCoreGlyphs.value.Add(stagedCore);
        // Staged the way the game stages: the stack is what a created spell is baked from,
        // and a glyph that only ever reached the value list beside it is not staged at all.
        SpellManager.instance.selectedAugmentGlyphs.Stack(stagedAugment, 1);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(1, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { stagedAugment }, SpellManager.instance.selectedAugmentGlyphs.value);
    }

    [Fact]
    public void DiscoveryThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();
        SpellManager.instance!.ThrowAfterDiscovery = true;

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified);
    }

    [Fact]
    public void LoadoutAddUsesTheScreenPriceResolverAndRejectsUnownedAugments()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var owned = Augment();
        var unowned = Augment();
        unowned.level = 0;
        using var action = Action();

        var refused = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(unowned.GetGuid(), 1) }));
        var committed = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(owned.GetGuid(), 1) }));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, refused.Preflight);
        Assert.Contains("not owned", refused.Reason);
        Assert.True(committed.Verified, committed.Reason);
        Assert.Single(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void RequestedDiscoveryComponentsRequireOwnershipButDiscoveredRecipeCoresAreBaked()
    {
        var (undiscovered, first, second) = Recipe();
        first.level = 0;
        using var discoveryAction = Action();

        var discovery = discoveryAction.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover,
            undiscovered.GetGuid(),
            Epoch,
            CoreLayout(first, second),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        discoveryAction.Dispose();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = new SpellManager();
        IdScriptableObject.RuntimeLookup.Clear();
        var (discovered, core, _) = Recipe(discovered: true);
        core.level = 0;
        using var loadoutAction = Action();
        var loadout = loadoutAction.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            discovered.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, discovery.Preflight);
        Assert.Contains("not owned", discovery.Reason);
        Assert.True(loadout.Verified, loadout.Reason);
        Assert.Single(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void LoadoutAddUsesTheScreenPriceAndNamesTheShortResourceBeforePayment()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var knowledge = new ResourceSO
        {
            name = "Knowledge",
            quantity = new BigDouble(2),
        };
        var price = new ResourceCostList();
        price.costs.Add(new ResourceTuple(knowledge, new BigDouble(3)));
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Bound(
            Epoch,
            new[]
            {
                new EntityIdentityName(
                    knowledge.GetGuid(), nameof(ResourceSO), "Knowledge", "Knowledge"),
            }));
        SpellManager.instance!.CreateCostOverride = price;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.Unaffordable, result.Preflight);
        Assert.Contains("Knowledge", result.Reason);
        Assert.Equal(0, price.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void PricePreviewUsesTheSubmittedLayoutWithoutChangingStagedSelection()
    {
        var (recipe, stagedCore, _) = Recipe(discovered: true);
        var knowledge = new ResourceSO { name = "Knowledge", quantity = new BigDouble(100) };
        var first = Augment();
        var second = Augment();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(stagedCore);
        SpellManager.instance.selectedAugmentGlyphs.value.Add(first);
        SpellManager.instance.CreateCostResolver = glyphs =>
        {
            var price = new ResourceCostList();
            price.costs.Add(new ResourceTuple(
                knowledge,
                glyphs.Contains(second) ? new BigDouble(7) : new BigDouble(3)));
            return price;
        };
        using var action = Action(permit: false);

        var firstPreview = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(first.GetGuid(), 1) }));
        var secondPreview = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(second.GetGuid(), 1) }));

        Assert.True(firstPreview.Available, firstPreview.Reason);
        Assert.True(secondPreview.Available, secondPreview.Reason);
        Assert.Equal(new BigDouble(3), Assert.Single(firstPreview.Costs).Cost);
        Assert.Equal(new BigDouble(7), Assert.Single(secondPreview.Costs).Cost);
        Assert.Equal(new[] { stagedCore }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Equal(new[] { first }, SpellManager.instance.selectedAugmentGlyphs.value);
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
        Assert.Contains("complete spell workbench binding set", layout.Reason);
    }

    [Fact]
    public void PricePreviewNamesTheShortResourceAndRefusesASelectionThatDoesNotResolve()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var knowledge = new ResourceSO { name = "Knowledge", quantity = new BigDouble(2) };
        var augment = Augment();
        var price = new ResourceCostList();
        price.costs.Add(new ResourceTuple(knowledge, new BigDouble(3)));
        SpellManager.instance!.CreateCostResolver = _ => price;
        using var action = Action(permit: false);

        var unaffordable = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));
        SpellManager.instance.SuppressSelectionResolution = true;
        var unresolved = action.Preview(new SpellWorkbenchPricePreviewRequest(
            recipe.GetGuid(), Epoch,
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));

        Assert.True(unaffordable.Available, unaffordable.Reason);
        Assert.False(unaffordable.Affordable);
        Assert.Equal(knowledge.GetGuid(), unaffordable.ShortResourceId);
        Assert.Equal(SpellWorkbenchPreflight.RecipeNotOffered, unresolved.Preflight);
        Assert.Contains("resolves to no spell at all", unresolved.Reason);
        Assert.Equal(0, price.PerformCalls);
    }

    [Fact]
    public void LoadoutAddRevalidatesThatTheExactSubmittedLayoutStillResolves()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.SuppressSelectionResolution = true;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.RecipeNotOffered, result.Preflight);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void PortableCreationCostCombinerDependsOnTheSubmittedGlyphs()
    {
        var resource = new ResourceSO();
        var starting = new ResourceCostList();
        starting.costs.Add(new ResourceTuple(resource, new BigDouble(10)));
        var free = new GlyphSO
        {
            creationCostMod = new ValueModifier(
                ValueModifier.ValueModifierType.Raw,
                BigDouble.Zero),
        };
        var expensive = new GlyphSO
        {
            creationCostMod = new ValueModifier(
                ValueModifier.ValueModifierType.Raw,
                new BigDouble(5)),
        };

        var freePrice = GlyphSO.GetCreationCostOfList(starting, new[] { free });
        var expensivePrice = GlyphSO.GetCreationCostOfList(starting, new[] { expensive });

        Assert.Equal(new BigDouble(10), Assert.Single(freePrice.costs).GetValue());
        Assert.Equal(new BigDouble(15), Assert.Single(expensivePrice.costs).GetValue());
    }

    [Theory]
    [InlineData(false, true, (int)SpellWorkbenchPreflight.DiscoveryUnavailable)]
    [InlineData(true, false, (int)SpellWorkbenchPreflight.RecipeUnavailable)]
    public void DiscoveryRevalidatesNativePrerequisitesBeforeMutation(
        bool canDiscover, bool creatable, int expected)
    {
        var (recipe, first, second) = Recipe();
        recipe.NativeCanDiscover = canDiscover;
        recipe.NativeIsCreatable = creatable;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal((SpellWorkbenchPreflight)expected, result.Preflight);
        Assert.False(recipe.discovered);
    }

    [Fact]
    public async Task OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = await Task.Run(() => action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())));

        Assert.Equal(SpellWorkbenchPreflight.WrongThread, result.Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    /// <summary>
    /// A staged core the game's own list refuses to take is named as the suite's staging failure,
    /// not as a layout the caller got wrong.
    /// </summary>
    /// <remarks>
    /// An authored-immutable list returns from every write without saying so, so the layout the
    /// second admission then re-reads is empty and resolves to nothing. That produced a refusal
    /// telling the caller its glyphs did not resolve, on a call whose glyphs were never in doubt.
    /// </remarks>
    [Fact]
    public void LoadoutAddNamesItsOwnStagingFailureWhenTheGameSilentlyRefusesTheWrite()
    {
        var (recipe, first, second) = Recipe(discovered: true);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.selectedCoreGlyphs.isStatic = true;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.StagedWriteFailed, result.Preflight);
        Assert.Equal(
            "Staging this spell's core into the game's Spellcraft selection did not land: " +
            Name(first) + ", " + Name(second) + " was written and no glyphs came back.",
            result.Reason);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>A staged core the game truncates is refused with both counts named.</summary>
    [Fact]
    public void LoadoutAddNamesWhatItWroteAndWhatCameBackWhenTheStagedCoreIsShort()
    {
        var (recipe, first, second) = Recipe(discovered: true);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.selectedCoreGlyphs.isFilled = true;
        SpellManager.instance.selectedCoreGlyphs.maxSizeVariable = new IntVariable { Value = 1 };
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.StagedWriteFailed, result.Preflight);
        Assert.Equal(
            "Staging this spell's core into the game's Spellcraft selection did not land: " +
            Name(first) + ", " + Name(second) + " was written and " + Name(first) + " came back.",
            result.Reason);
        Assert.Equal(0, payment.PerformCalls);
    }

    /// <summary>
    /// A layout the game hands to another spell says which spell, rather than claiming it resolves
    /// to nothing.
    /// </summary>
    /// <remarks>
    /// The matcher keeps every recipe whose core is the same length and then filters by membership,
    /// so a core of two of the same glyph is a candidate for a two-glyph recipe holding that glyph
    /// and one other. The registry order decides, and the caller can do nothing about it — which is
    /// exactly why the old sentence, which named neither spell, bought a retry loop.
    /// </remarks>
    [Fact]
    public void LoadoutAddNamesTheSpellALayoutActuallyResolvesTo()
    {
        var (target, first, second) = Recipe(discovered: true);
        target.coreRecipe.Clear();
        target.coreRecipe.Add(first);
        target.coreRecipe.Add(first);
        var rival = new SpellRecipeSO { discovered = true };
        rival.coreRecipe.Add(first);
        rival.coreRecipe.Add(second);
        SpellRecipeSO.All.Add(rival);
        SpellManager.instance!.availableSpellRecipes.value.Insert(0, rival);
        var payment = new ResourceCostList();
        SpellManager.instance.CreateCostOverride = payment;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, target.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.LayoutResolvedElsewhere, result.Preflight);
        Assert.Equal(
            "This glyph layout resolves to " + Name(rival) + ", not " + Name(target) +
            ": the game matches a layout by core-glyph count and membership and takes the first " +
            "recipe that fits, so this layout cannot reach the requested spell.",
            result.Reason);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// A recipe the game does not offer to layout matching says so, instead of blaming the layout.
    /// </summary>
    /// <remarks>
    /// The suite resolves the requested recipe out of the whole registry and the game resolves a
    /// layout against the recipes it currently offers — two different sets. A recipe in one and not
    /// the other is unreachable by any layout, and no sentence on the wire used to say that.
    /// </remarks>
    [Fact]
    public void LoadoutAddNamesTheCraftableRegistryGapRatherThanTheLayout()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        SpellManager.instance!.availableSpellRecipes.value.Remove(recipe);
        var payment = new ResourceCostList();
        SpellManager.instance.CreateCostOverride = payment;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.RecipeNotOffered, result.Preflight);
        Assert.Equal(
            "This glyph layout resolves to no spell at all: the game matches layouts against the " +
            "recipes it currently offers, and " + Name(recipe) + " is not among them.",
            result.Reason);
        Assert.Equal(0, payment.PerformCalls);
    }

    /// <summary>
    /// An augment stack the game will not expose is refused before payment, not after it.
    /// </summary>
    /// <remarks>
    /// A created spell is baked from the augment stack, and the plain list write never touches it.
    /// Staging with that write and not reading it back therefore had one worst case: pay the
    /// creation price, create a spell carrying none of the augments that were paid for, and fail
    /// the suite's own postcondition afterwards. Nothing is spent here.
    /// </remarks>
    [Fact]
    public void LoadoutAddRefusesBeforePayingWhenTheAugmentStackCannotBeStaged()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.selectedAugmentGlyphs.isStackable = false;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        Assert.Equal(
            "The live augment selection exposes no stack for the game to bake a spell from, so " +
            "no augment layout can be staged or read back.",
            result.Reason);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    /// <summary>
    /// The augments a caller asked for reach the created spell through the stack the game bakes
    /// from, and the value list beside it carries one entry per distinct glyph as the game does.
    /// </summary>
    [Fact]
    public void LoadoutAddStagesAugmentsIntoTheStackTheCreatedSpellIsBakedFrom()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 3);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        var observed = 0;
        SpellManager.instance.CreateCostResolver = _ =>
        {
            observed = SpellManager.instance!.selectedAugmentGlyphs.GetStacks(augment);
            return payment;
        };
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 3) }));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(3, observed);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(new[] { augment, augment, augment }, equipped.GetAugmentGlyphs());
        Assert.Empty(SpellManager.instance.selectedAugmentGlyphs.value);
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        var (recipe, first, second) = Recipe();
        foreach (var missing in SpellWorkbenchNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));
            Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutChangingSelection()
    {
        var (recipe, first, second) = Recipe();
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(SpellWorkbenchPreflight.LifecycleReplaced,
            stale.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
        Assert.Equal(SpellWorkbenchPreflight.MutationPermitUnavailable,
            unowned.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
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
