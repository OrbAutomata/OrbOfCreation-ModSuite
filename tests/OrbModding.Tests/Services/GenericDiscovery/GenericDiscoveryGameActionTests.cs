using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.GenericDiscovery;

public sealed class GenericDiscoveryGameActionTests : IDisposable
{
    private const long Epoch = 71;
    private readonly IDictionary _registry = new Hashtable();

    /// <summary>
    /// The seven discovery screens the six discoverable kinds are drawn on, unlocked. A locked
    /// screen draws no rows at all, so every press that expects a row needs them open.
    /// </summary>
    private readonly ViewSO _spellbookUnlock = Screen(KnownEntities.MagicSpellbookLearn.Uuid);
    private readonly ViewSO _glyphcraft = Screen(KnownEntities.MagicGlyphsDiscover.Uuid);
    private readonly ViewSO _ritualsDiscover = Screen(KnownEntities.RitualsDiscover.Uuid);
    private readonly ViewSO _artifactCreate = Screen(KnownEntities.WorkshopArtifactCreate.Uuid);
    private readonly ViewSO _timeRuneCreate = Screen(KnownEntities.TimeTimeRuneCreate.Uuid);
    private readonly ViewSO _alchemyLearn = Screen(KnownEntities.AlchAlchemyDiscover.Uuid);
    private readonly ViewSO _conceptDiscover = Screen(KnownEntities.ScholarConceptDiscover.Uuid);

    public GenericDiscoveryGameActionTests()
    {
        _registry.Add(_spellbookUnlock.GetGuid(), _spellbookUnlock);
        _registry.Add(_glyphcraft.GetGuid(), _glyphcraft);
        _registry.Add(_ritualsDiscover.GetGuid(), _ritualsDiscover);
        _registry.Add(_artifactCreate.GetGuid(), _artifactCreate);
        _registry.Add(_timeRuneCreate.GetGuid(), _timeRuneCreate);
        _registry.Add(_alchemyLearn.GetGuid(), _alchemyLearn);
        _registry.Add(_conceptDiscover.GetGuid(), _conceptDiscover);
    }

    public void Dispose()
    {
        SpellManager.instance = null;
        SpellRecipeSO.All.Clear();
    }

    [Theory]
    [InlineData("AlchemyRecipeSO")]
    [InlineData("EquipmentSO")]
    [InlineData("GlyphSO")]
    [InlineData("RitualSO")]
    [InlineData("SpellRecipeSO")]
    [InlineData("TimeRuneSO")]
    public void Every_audited_concrete_type_pays_then_discovers_the_exact_target(string nativeType)
    {
        var target = Target(nativeType);
        var resource = Resource(90);
        Discoverable(target).GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(25, 0)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, nativeType);

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
        Assert.Equal(1, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, resource.GetTrueQuantity().CompareTo(new BigDouble(65, 0)));
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void Visibility_native_verdict_and_already_discovered_refuse_before_payment()
    {
        var invisible = Target("GlyphSO");
        SetVisible(invisible, false);
        Register(invisible);
        var unavailable = Target("RitualSO");
        SetCanDiscover(unavailable, false);
        Register(unavailable);
        var complete = Target("TimeRuneSO");
        SetDiscovered(complete, true);
        Register(complete);
        using var boundary = Boundary();

        var hidden = Submit(boundary, invisible, "GlyphSO");
        var blocked = Submit(boundary, unavailable, "RitualSO");
        var already = Submit(boundary, complete, "TimeRuneSO");

        Assert.Equal(GenericDiscoveryPreflight.NotVisible, hidden.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.DiscoveryUnavailable, blocked.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.AlreadyDiscovered, already.Preflight);
        Assert.Equal(0, Discoverable(invisible).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, Discoverable(unavailable).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, Discoverable(complete).GetDiscoverCost().PerformCalls);
    }

    /// <summary>
    /// A hidden row's refusal names the Recipe Book it is waiting on rather than reciting the rule.
    /// The recipe names its core glyphs, each core glyph names the book it is the internal half of,
    /// and the book answers whether it is owned, so the first unowned one is the answer.
    /// </summary>
    [Fact]
    public void A_hidden_row_names_the_recipe_book_it_is_waiting_on()
    {
        var target = Target("SpellRecipeSO");
        SetVisible(target, false);
        Register(target);
        var core = Component();
        core.associatedRecipeBook = new RecipeBookSO { available = false };
        GlyphRecipe(target).Add(core);
        Register(core);
        using var boundary = Boundary();

        var hidden = Submit(boundary, target, "SpellRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.NotVisible, hidden.Preflight);
        Assert.Contains("recipe book, which is not owned.", hidden.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every book owned leaves one honest answer, and it is not the book: the row's own
    /// prerequisites are what is left.
    /// </summary>
    [Fact]
    public void A_hidden_row_whose_books_are_all_owned_says_so()
    {
        var target = Target("SpellRecipeSO");
        SetVisible(target, false);
        Register(target);
        var core = Component();
        core.associatedRecipeBook = new RecipeBookSO { available = true };
        GlyphRecipe(target).Add(core);
        Register(core);
        using var boundary = Boundary();

        var hidden = Submit(boundary, target, "SpellRecipeSO");

        Assert.Contains(
            "Every recipe book it is made of is owned, so what is left is its own prerequisites.",
            hidden.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unaffordable_cost_refuses_before_payment_or_discovery()
    {
        var target = Target("AlchemyRecipeSO");
        var resource = Resource(4);
        Discoverable(target).GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(5, 0)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.Unaffordable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.False(Discoverable(target).IsDiscovered());
    }

    /// <summary>
    /// A row the discovery page could never draw a button for is refused before payment.
    /// </summary>
    /// <remarks>
    /// <c>UIDiscoverablePage.IsGlyphSelectionValid</c> starts at <c>selectedGlyphs.Count &gt; 0</c>,
    /// and the selection is filled from the row's own glyph recipe. A discoverable that names no
    /// glyph can never make that count positive, so the button never validates for it.
    /// </remarks>
    [Fact]
    public void A_target_with_no_glyph_recipe_is_refused_before_payment()
    {
        var target = Target("GlyphSO");
        Register(target);
        using var boundary = Boundary();
        var action = new GenericDiscoveryAction(
            Discoverable(target).GetGuid(), "GlyphSO", Epoch);

        var result = boundary.Submit(in action);

        Assert.Equal(GenericDiscoveryPreflight.GlyphRecipeEmpty, result.Preflight);
        Assert.Contains("from glyphs and it names none", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.False(Discoverable(target).IsDiscovered());
    }

    /// <summary>
    /// A locked screen is refused in the screen's own words, before any row fact — on all six
    /// discoverable kinds.
    /// </summary>
    /// <remarks>
    /// <c>ViewSO.IsAvailable()</c> is the game's own question about the screen, and each discovery
    /// tree names the view it is drawn under in its authored <c>viewLocation</c>. Five kinds
    /// resolve to one view each; alchemy recipes resolve through the recipe's own alchemy type,
    /// because the same kind is drawn on two screens.
    /// </remarks>
    [Theory]
    [InlineData("GlyphSO", "Magic > Augments > Glyphcraft")]
    [InlineData("SpellRecipeSO", "Magic > Spellbook > Unlock")]
    [InlineData("RitualSO", "Rituals > Discover")]
    [InlineData("EquipmentSO", "Workshop > Artifacts > Create")]
    [InlineData("TimeRuneSO", "Time > Time Runes > Create")]
    [InlineData("AlchemyRecipeSO", "Alchemy > Alchemy > Learn")]
    public void A_locked_discovery_screen_refuses_before_any_row_fact(
        string nativeType, string path)
    {
        LockEveryScreen();
        SpellManager.instance = new SpellManager();
        var target = Target(nativeType);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, nativeType);

        Assert.Equal(GenericDiscoveryPreflight.ScreenLocked, result.Preflight);
        Assert.Equal(
            path + " is not unlocked yet, so the game draws no row to discover. Nothing was spent.",
            result.Reason);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
    }

    /// <summary>
    /// Alchemy recipes are the one discoverable kind drawn on two screens, and each row is gated on
    /// the one that draws it: locking Alchemy &gt; Alchemy &gt; Learn refuses an ordinary recipe
    /// while a Scholar concept still presses, and the other way round.
    /// </summary>
    /// <remarks>
    /// <c>AlchemyDiscoveryTree.viewLocation</c> ends at <c>AlchAlchemyDiscover</c> and
    /// <c>ConceptDiscoveryTree.viewLocation</c> ends at <c>ScholarConceptDiscover</c>, and the game
    /// separates the two by the recipe's own <c>AlchemyTypeSO</c>. Gating both on one view would
    /// refuse a press the other screen would have taken.
    /// </remarks>
    [Fact]
    public void Each_alchemy_screen_gates_only_the_recipes_it_draws()
    {
        var potion = AlchemyRecipe(AlchemyGameplayDomainClassifier.BrewingTypeUuid);
        potion.SetGuid(Guid.NewGuid());
        var concept = AlchemyRecipe(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid);
        concept.SetGuid(Guid.NewGuid());
        Register(potion);
        Register(concept);
        using var boundary = Boundary();

        _alchemyLearn.available = false;
        var shutLearn = Submit(boundary, potion, "AlchemyRecipeSO");
        var openConcepts = Submit(boundary, concept, "AlchemyRecipeSO");
        _alchemyLearn.available = true;
        _conceptDiscover.available = false;
        var openLearn = Submit(boundary, potion, "AlchemyRecipeSO");
        var shutConcepts = Submit(boundary, concept, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.ScreenLocked, shutLearn.Preflight);
        Assert.Equal(
            "Alchemy > Alchemy > Learn is not unlocked yet, so the game draws no row to " +
            "discover. Nothing was spent.",
            shutLearn.Reason);
        Assert.True(openConcepts.Verified, openConcepts.Reason);
        Assert.True(openLearn.Verified, openLearn.Reason);
        Assert.Equal(GenericDiscoveryPreflight.ScreenLocked, shutConcepts.Preflight);
        Assert.Equal(
            "Scholar > Concepts > Discover is not unlocked yet, so the game draws no row to " +
            "discover. Nothing was spent.",
            shutConcepts.Reason);
    }

    /// <summary>
    /// A recipe whose alchemy type belongs to neither screen is the suite's own fault, not a guess:
    /// naming either screen would refuse a press the other one would have taken.
    /// </summary>
    [Fact]
    public void An_alchemy_recipe_belonging_to_neither_screen_is_a_suite_fault()
    {
        var orphan = AlchemyRecipe(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        orphan.SetGuid(Guid.NewGuid());
        Register(orphan);
        using var boundary = Boundary();

        var result = Submit(boundary, orphan, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.ContractUnavailable, result.Preflight);
        Assert.EndsWith(
            "belongs to neither, so which screen would draw its row is unknown. Nothing was spent.",
            result.Reason,
            StringComparison.Ordinal);
        Assert.Equal(0, Discoverable(orphan).GetDiscoverCost().PerformCalls);
        Assert.False(Discoverable(orphan).IsDiscovered());
    }

    /// <summary>
    /// Discovering a spell pays once and lets the game load it, which is the same press.
    /// </summary>
    /// <remarks>
    /// <c>SpellRecipeSO.Discover()</c> is <c>discovered = true</c> plus
    /// <c>SpellManager.PostDiscoverRecipe</c>, which loads the new spell when the loadout has a
    /// free spot and its usage cost fits. <c>SpellManager.DiscoverRecipe</c> would run that second
    /// half twice and is not what the Discover button calls.
    /// </remarks>
    [Fact]
    public void A_discovered_spell_pays_once_and_the_game_loads_it()
    {
        SpellManager.instance = new SpellManager();
        var target = Target("SpellRecipeSO");
        var resource = Resource(90);
        Discoverable(target).GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(25, 0)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "SpellRecipeSO");

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
        Assert.Equal(1, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, resource.GetTrueQuantity().CompareTo(new BigDouble(65, 0)));
        var loaded = Assert.Single(SpellManager.instance.activeSpells.value);
        Assert.Same(target, loaded.get_reference());
    }

    /// <summary>A full loadout still discovers the spell; the game just leaves it unloaded.</summary>
    [Fact]
    public void A_discovered_spell_stays_unloaded_when_the_loadout_is_full()
    {
        SpellManager.instance = new SpellManager();
        SpellManager.instance.activeSpells.Maximum = 0;
        var target = Target("SpellRecipeSO");
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "SpellRecipeSO");

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void Wrong_type_stale_lifecycle_and_revoked_permit_all_refuse_before_mutation()
    {
        var target = Target("AlchemyRecipeSO");
        Register(target);
        var epoch = Epoch;
        var permit = true;
        using var boundary = Boundary(() => epoch, () => permit);

        var wrongType = Submit(boundary, target, "GlyphSO");
        var stale = Submit(boundary, target, "AlchemyRecipeSO", Epoch - 1);
        permit = false;
        var revoked = Submit(boundary, target, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.IdentityUnavailable, wrongType.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.LifecycleReplaced, stale.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.MutationPermitUnavailable, revoked.Preflight);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
    }

    [Fact]
    public void Missing_outcome_faults_without_persistent_action_state()
    {
        var target = Target("EquipmentSO");
        SetSuppressDiscovery(target, true);
        Register(target);
        using var boundary = Boundary();

        var failed = Submit(boundary, target, "EquipmentSO");
        var repeated = Submit(boundary, target, "EquipmentSO");
        SetSuppressDiscovery(target, false);
        boundary.InvalidateLifecycle();
        var retry = Submit(boundary, target, "EquipmentSO");

        Assert.Equal(GenericDiscoveryPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, failed.Outcome);
        Assert.Equal(GenericDiscoveryPreflight.VerificationFailed, repeated.Preflight);
        Assert.True(retry.Verified, retry.Reason);
    }

    [Fact]
    public void Exception_after_requested_outcome_commits()
    {
        var target = Target("RitualSO");
        SetThrowAfterDiscovery(target, true);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "RitualSO");

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
    }

    [Fact]
    public void Partial_payment_fault_without_discovery_fails_without_persistent_action_state()
    {
        var target = Target("TimeRuneSO");
        var first = Resource(10);
        var second = Resource(10);
        var cost = Discoverable(target).GetDiscoverCost();
        cost.costs.Add(new ResourceTuple(first, new BigDouble(2, 0)));
        cost.costs.Add(new ResourceTuple(second, new BigDouble(3, 0)));
        cost.ThrowAfterCostRows = 1;
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "TimeRuneSO");

        Assert.Equal(GenericDiscoveryPreflight.PostCommitFault, result.Preflight);
        Assert.Equal(GenericDiscoveryNativeStage.Payment, result.Stage);
        Assert.False(Discoverable(target).IsDiscovered());
    }

    [Fact]
    public void Unity_thread_is_revalidated_before_identity_or_payment()
    {
        var target = Target("GlyphSO");
        Register(target);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary, target, "GlyphSO"));

        Assert.Equal(GenericDiscoveryPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in GenericDiscoveryNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private GenericDiscoveryGameAction Boundary(
        Func<long>? epoch = null,
        Func<bool>? permit = null,
        Func<string, bool>? includeContract = null)
    {
        var readEpoch = epoch ?? (() => Epoch);
        var resolver = new TypedRegistryResolver(
            readEpoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value switch
            {
                IHasGuid item => item.GetGuid(),
                IdScriptableObject item => item.GetGuid(),
                _ => null,
            });
        return new GenericDiscoveryGameAction(
            readEpoch,
            permit ?? (() => true),
            static () => "GenericDiscovery ownership was revoked.",
            includeContract: includeContract,
            registry: resolver);
    }

    private GenericDiscoverySubmission Submit(
        GenericDiscoveryGameAction boundary,
        object target,
        string nativeType,
        long lifecycle = Epoch)
    {
        var recipe = GlyphRecipe(target);
        if (recipe.Count == 0)
        {
            var component = Component();
            recipe.Add(component);
            Register(component);
        }
        var action = new GenericDiscoveryAction(
            Discoverable(target).GetGuid(),
            nativeType,
            lifecycle);
        return boundary.Submit(in action);
    }

    private void Register(object target)
    {
        var guid = target switch
        {
            IHasGuid item => item.GetGuid(),
            IdScriptableObject item => item.GetGuid(),
            _ => throw new InvalidOperationException($"{target.GetType().Name} has no runtime identity."),
        };
        _registry.Add(guid, target);
    }

    private void LockEveryScreen()
    {
        _glyphcraft.available = false;
        _spellbookUnlock.available = false;
        _ritualsDiscover.available = false;
        _artifactCreate.available = false;
        _timeRuneCreate.available = false;
        _alchemyLearn.available = false;
        _conceptDiscover.available = false;
    }

    private static ViewSO Screen(Guid uuid)
    {
        var view = new ViewSO { available = true };
        view.SetGuid(uuid);
        return view;
    }

    /// <summary>
    /// An alchemy recipe filed under one alchemy type. The type is what says which of the two
    /// alchemy discovery screens draws the recipe, so every alchemy target names one.
    /// </summary>
    private static AlchemyRecipeSO AlchemyRecipe(Guid coreTypeUuid)
    {
        var recipe = new AlchemyRecipeSO { discovered = false };
        recipe.coreType.SetGuid(coreTypeUuid);
        return recipe;
    }

    private static object Target(string nativeType)
    {
        object value = nativeType switch
        {
            "AlchemyRecipeSO" =>
                AlchemyRecipe(AlchemyGameplayDomainClassifier.BrewingTypeUuid),
            "EquipmentSO" => new EquipmentSO { isCreated = false },
            "GlyphSO" => new GlyphSO { discovered = false },
            "RitualSO" => new RitualSO { discovered = false },
            "SpellRecipeSO" => new SpellRecipeSO { discovered = false },
            "TimeRuneSO" => new TimeRuneSO { discovered = false },
            _ => throw new ArgumentOutOfRangeException(nameof(nativeType)),
        };
        if (value is SpellRecipeSO recipe) recipe.uuid = Guid.NewGuid().ToString("D");
        else ((IdScriptableObject)value).SetGuid(Guid.NewGuid());
        return value;
    }

    private static IDiscoverable Discoverable(object target) =>
        Assert.IsAssignableFrom<IDiscoverable>(target);

    private static ResourceSO Resource(double amount)
    {
        var resource = new ResourceSO { quantity = new BigDouble(amount, 0) };
        resource.SetGuid(Guid.NewGuid());
        return resource;
    }

    private static GlyphSO Component()
    {
        var glyph = new GlyphSO { NativeAvailable = true };
        glyph.SetGuid(Guid.NewGuid());
        return glyph;
    }

    private static List<GlyphSO> GlyphRecipe(object target) => target switch
    {
        AlchemyRecipeSO item => item.glyphRecipe,
        EquipmentSO item => item.glyphRecipe,
        GlyphSO item => item.glyphRecipe,
        RitualSO item => item.glyphRecipe,
        SpellRecipeSO item => item.coreRecipe,
        TimeRuneSO item => item.glyphRecipe,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static void SetVisible(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.NativeDiscoverVisible = value; break;
            case EquipmentSO item: item.NativeDiscoverVisible = value; break;
            case GlyphSO item: item.NativeDiscoverVisible = value; break;
            case RitualSO item: item.NativeDiscoverVisible = value; break;
            case SpellRecipeSO item: item.NativeDiscoverVisible = value; break;
            case TimeRuneSO item: item.NativeDiscoverVisible = value; break;
        }
    }

    private static void SetCanDiscover(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.NativeCanDiscover = value; break;
            case EquipmentSO item: item.NativeCanDiscover = value; break;
            case GlyphSO item: item.NativeCanDiscover = value; break;
            case RitualSO item: item.NativeCanDiscover = value; break;
            case SpellRecipeSO item: item.NativeCanDiscover = value; break;
            case TimeRuneSO item: item.NativeCanDiscover = value; break;
        }
    }

    private static void SetDiscovered(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.discovered = value; break;
            case EquipmentSO item: item.isCreated = value; break;
            case GlyphSO item: item.discovered = value; break;
            case RitualSO item: item.discovered = value; break;
            case SpellRecipeSO item: item.discovered = value; break;
            case TimeRuneSO item: item.discovered = value; break;
        }
    }

    private static void SetSuppressDiscovery(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.SuppressDiscovery = value; break;
            case EquipmentSO item: item.SuppressDiscovery = value; break;
            case GlyphSO item: item.SuppressDiscovery = value; break;
            case RitualSO item: item.SuppressDiscovery = value; break;
            case SpellRecipeSO item: item.SuppressDiscovery = value; break;
            case TimeRuneSO item: item.SuppressDiscovery = value; break;
        }
    }

    private static void SetThrowAfterDiscovery(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.ThrowAfterDiscovery = value; break;
            case EquipmentSO item: item.ThrowAfterDiscovery = value; break;
            case GlyphSO item: item.ThrowAfterDiscovery = value; break;
            case RitualSO item: item.ThrowAfterDiscovery = value; break;
            case SpellRecipeSO item: item.ThrowAfterDiscovery = value; break;
            case TimeRuneSO item: item.ThrowAfterDiscovery = value; break;
        }
    }
}
