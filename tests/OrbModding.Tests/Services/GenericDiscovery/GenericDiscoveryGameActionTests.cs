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
    /// The two discovery screens whose owning view the suite pins, unlocked. A locked screen draws
    /// no rows at all, so every press that expects a row needs them open.
    /// </summary>
    private readonly ViewSO _spellbookUnlock = Screen(KnownEntities.MagicSpellbookLearn.Uuid);
    private readonly ViewSO _glyphcraft = Screen(KnownEntities.MagicGlyphsDiscover.Uuid);

    public GenericDiscoveryGameActionTests()
    {
        _registry.Add(_spellbookUnlock.GetGuid(), _spellbookUnlock);
        _registry.Add(_glyphcraft.GetGuid(), _glyphcraft);
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
    /// A locked screen is refused in the screen's own words, before any row fact — and only the two
    /// screens the suite pins are gated at all.
    /// </summary>
    /// <remarks>
    /// <c>ViewSO.IsAvailable()</c> is the game's own question about the screen. The other discovery
    /// pages keep the answer their rows already give, because nothing pins which view owns them and
    /// guessing one would refuse a press the game would have taken.
    /// </remarks>
    [Fact]
    public void A_locked_discovery_screen_refuses_before_any_row_fact()
    {
        _glyphcraft.available = false;
        _spellbookUnlock.available = false;
        SpellManager.instance = new SpellManager();
        var glyph = Target("GlyphSO");
        Register(glyph);
        var recipe = Target("SpellRecipeSO");
        Register(recipe);
        var potion = Target("AlchemyRecipeSO");
        Register(potion);
        using var boundary = Boundary();

        var glyphcraft = Submit(boundary, glyph, "GlyphSO");
        var spellbook = Submit(boundary, recipe, "SpellRecipeSO");
        var alchemy = Submit(boundary, potion, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.ScreenLocked, glyphcraft.Preflight);
        Assert.Equal(
            "Magic > Augments > Glyphcraft is not unlocked yet, so the game draws no row to " +
            "discover. Nothing was spent.",
            glyphcraft.Reason);
        Assert.Equal(GenericDiscoveryPreflight.ScreenLocked, spellbook.Preflight);
        Assert.Equal(
            "Magic > Spellbook > Unlock is not unlocked yet, so the game draws no row to " +
            "discover. Nothing was spent.",
            spellbook.Reason);
        Assert.True(alchemy.Verified, alchemy.Reason);
        Assert.Equal(0, Discoverable(glyph).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, Discoverable(recipe).GetDiscoverCost().PerformCalls);
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

    private static ViewSO Screen(Guid uuid)
    {
        var view = new ViewSO { available = true };
        view.SetGuid(uuid);
        return view;
    }

    private static object Target(string nativeType)
    {
        object value = nativeType switch
        {
            "AlchemyRecipeSO" => new AlchemyRecipeSO { discovered = false },
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
