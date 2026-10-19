using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Native;

public sealed class AutoCastNativeAdapterTests : IDisposable
{
    private static readonly Guid RecipeId = Guid.Parse("5ab1e000-0000-4000-8000-000000000001");

    public AutoCastNativeAdapterTests()
    {
        SpellManager.instance = new SpellManager();
        TargetingManager.Reset();
        Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
        DecoyTargetingManager.Reset();
    }

    /// <summary>
    /// A fire at a spell that is itself still starting its cast names that spell, not another one.
    /// </summary>
    /// <remarks>
    /// <c>SpellManager.CanCastASpell()</c> is false while any loaded spell is readying, the pressed
    /// one included. A live round's second Psychic Blast fire, and a Beam Burst fire, answered
    /// "another spell is still casting" while the only spell casting was the one pressed.
    /// </remarks>
    [Fact]
    public void A_fire_at_a_spell_still_starting_its_own_cast_says_so()
    {
        var recipe = new SpellRecipeSO();
        recipe.SetGuid(RecipeId);
        SpellManager.instance!.activeSpells.value.Add(
            new Spell(recipe) { NativeReadyingCast = true });
        SpellManager.NativeCanCast = false;
        var adapter = new AutoCastNativeAdapter();

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(AutoCastActionResultCodes.SpellAlreadyCasting, submission.Outcome.Code);
        Assert.Equal(
            "the spell is still starting its cast, so the press would start no cast",
            submission.Outcome.Reason);
    }

    [Fact]
    public void An_unexpected_exception_from_the_target_preflight_escapes_the_fire()
    {
        var recipe = Recipe(Block(Request(new UnreadableSelection())));
        var spell = Equip(recipe);
        var adapter = new AutoCastNativeAdapter();

        Assert.Throws<InvalidCastException>(() => adapter.Fire(0, RecipeId, holdFullCharge: false));
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void An_unexpected_exception_from_the_targeting_read_escapes_the_fire()
    {
        var spell = Equip(Recipe());
        TargetingManager.IsTargetingFault = new NullReferenceException("injected targeting read failure");
        var adapter = new AutoCastNativeAdapter();

        Assert.Throws<NullReferenceException>(() => adapter.Fire(0, RecipeId, holdFullCharge: false));
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_auto_cast_binding_set()
    {
        foreach (var missing in AutoCastNativeBindings.ContractIds)
        {
            SpellManager.instance = new SpellManager();
            var spell = Equip(Recipe());
            var adapter = new AutoCastNativeAdapter(resolveType: null, includeContract: id => id != missing);

            foreach (var submission in new[]
                     {
                         adapter.Fire(0, RecipeId, holdFullCharge: false),
                         adapter.ReleaseCharge(0, RecipeId),
                         adapter.ToggleOff(0, RecipeId),
                     })
            {
                Assert.Equal(CommonActionResultCodes.AdapterFault, submission.Outcome.Code);
                Assert.False(submission.Outcome.ToResult().HasNativeEvidence);
                Assert.Contains(missing, submission.Outcome.Reason, StringComparison.Ordinal);
            }

            Assert.Null(adapter.IsRitualRunning());
            Assert.Equal(0, spell.FireCalls);
        }
    }

    /// <summary>
    /// The ritual reading binds with the rest of the set, so a battle member the game lacks stops the
    /// casts at bind time instead of leaving a ritual-conditioned cast to find out per call.
    /// </summary>
    [Fact]
    public void A_battle_member_the_game_lacks_disables_casting_at_bind_time()
    {
        var spell = Equip(Recipe());
        var adapter = new AutoCastNativeAdapter(
            resolveType: null, includeContract: id => id != "ritual-lifecycle.battle-active-action");

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(CommonActionResultCodes.AdapterFault, submission.Outcome.Code);
        Assert.Contains("ritual-lifecycle.battle-active-action", submission.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(0, spell.FireCalls);
    }

    /// <summary>
    /// The submit binds the overload taking <c>ITargetable</c>, not whichever one-parameter
    /// <c>SubmitTarget</c> reflection lists first.
    /// </summary>
    [Fact]
    public void The_target_submit_binds_the_overload_taking_a_targetable()
    {
        var spell = Equip(Recipe());
        spell.RequestsOnFire = 1;
        var target = new Target();
        TargetingManager.AvailableTarget = target;
        var adapter = new AutoCastNativeAdapter(DecoyTargetingTypes, includeContract: null);

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(CommonActionResultCodes.Committed, submission.Outcome.Code);
        Assert.Equal(0, DecoyTargetingManager.ObjectOverloadCalls);
        Assert.Equal(new object[] { target }, TargetingManager.SubmittedTargets);
    }

    /// <summary>
    /// The target comes from <c>TargetLink.GetRandom</c> as the game declares it, whatever the
    /// runtime type of the link the game hands back.
    /// </summary>
    [Fact]
    public void The_target_comes_from_the_declared_target_link_draw()
    {
        var spell = Equip(Recipe());
        spell.RequestsOnFire = 1;
        var offered = new Target();
        TargetingManager.AvailableTarget = offered;
        DecoyTargetingManager.HandOutDerivedLinks = true;
        var adapter = new AutoCastNativeAdapter(DecoyTargetingTypes, includeContract: null);

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(CommonActionResultCodes.Committed, submission.Outcome.Code);
        Assert.Equal(new object[] { offered }, TargetingManager.SubmittedTargets);
    }

    /// <summary>
    /// The preflight reads the target requests on the recipe's cast-effect blocks, the one place the
    /// game puts the requests a cast opens, and answers each shape the name walk it replaced answered.
    /// </summary>
    /// <remarks>
    /// A request nested inside a chance script's own blocks sat past the old walk's depth bound, so it
    /// never counted, and it still does not.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EffectShapes))]
    public void The_target_preflight_answers_every_effect_shape_as_before(string shape, bool casts)
    {
        var spell = Equip(EffectShape(shape));
        var adapter = new AutoCastNativeAdapter();

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(
            casts ? CommonActionResultCodes.Committed : AutoCastActionResultCodes.NoValidTarget,
            submission.Outcome.Code);
        Assert.Equal(casts ? 1 : 0, spell.FireCalls);
    }

    public static TheoryData<string, bool> EffectShapes() => new()
    {
        { "no effects", true },
        { "effects without a request", true },
        { "a request with a target", true },
        { "a request without a target", false },
        { "a second block's request without a target", false },
        { "empty block and script entries", true },
        { "a request inside a chance script", true },
    };

    private static SpellRecipeSO EffectShape(string shape) => shape switch
    {
        "no effects" => Recipe(),
        "effects without a request" => Recipe(Block(new Combat.CombatEffect())),
        "a request with a target" => Recipe(Block(new Combat.CombatEffect(), Request(InRange()))),
        "a request without a target" => Recipe(Block(Request(new Targeting.TargetStructure()))),
        "a second block's request without a target" =>
            Recipe(Block(Request(InRange())), Block(Request(new Targeting.TargetStructure()))),
        "empty block and script entries" => Recipe(null, Block(null, Request(InRange()))),
        "a request inside a chance script" => Recipe(Block(new ChanceEffectScript
        {
            effectBlocks = { Block(Request(new Targeting.TargetStructure())) },
        })),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private static Spell Equip(SpellRecipeSO recipe)
    {
        var spell = new Spell(recipe);
        SpellManager.instance!.activeSpells.value.Add(spell);
        return spell;
    }

    private static SpellRecipeSO Recipe(params InstantEffectBlock?[] blocks)
    {
        var recipe = new SpellRecipeSO();
        recipe.SetGuid(RecipeId);
        recipe.onCastEffects.AddRange(blocks!);
        return recipe;
    }

    private static InstantEffectBlock Block(params IInstantEffectScript?[] scripts)
    {
        var block = new InstantEffectBlock();
        block.effectScripts.AddRange(scripts!);
        return block;
    }

    private static RequestTargetEffectScript Request(Targeting.BaseTargetSelection selection) =>
        new() { targetOptions = { Targeting = selection } };

    private static Targeting.TargetStructure InRange() => new() { Candidates = { new Target() } };

    private static Type? DecoyTargetingTypes(string name) =>
        name == "TargetingManager" ? typeof(DecoyTargetingManager) : ReflectionUtil.FindLoadedType(name);

    private sealed class Target : Targeting.ITargetable
    {
    }

    private sealed class UnreadableSelection : Targeting.BaseTargetSelection
    {
        public override bool HasValidTargetsLeft(ScalingInfo scaling) =>
            throw new InvalidCastException("injected target preflight failure");
    }

    private static class DecoyTargetingManager
    {
        public static int ObjectOverloadCalls { get; private set; }
        public static bool HandOutDerivedLinks { get; set; }

        public static void Reset()
        {
            ObjectOverloadCalls = 0;
            HandOutDerivedLinks = false;
        }

        public static void SubmitTarget(object target) => ObjectOverloadCalls++;

        public static bool IsTargeting() => TargetingManager.IsTargeting();

        public static TargetingManager.TargetLink? GetTargetingLink() =>
            HandOutDerivedLinks && TargetingManager.GetTargetingLink() is not null
                ? new DerivedTargetLink(TargetingManager.AvailableTarget!)
                : TargetingManager.GetTargetingLink();

        public static void SubmitTarget(Targeting.ITargetable target) => TargetingManager.SubmitTarget(target);
    }

    private sealed class DerivedTargetLink : TargetingManager.TargetLink
    {
        public DerivedTargetLink(Targeting.ITargetable offered) : base(offered)
        {
        }

        public new Targeting.ITargetable GetRandom() => new Target();
    }

    public void Dispose()
    {
        SpellManager.NativeCanCast = true;
        SpellManager.instance = null;
        TargetingManager.Reset();
        DecoyTargetingManager.Reset();
    }
}
