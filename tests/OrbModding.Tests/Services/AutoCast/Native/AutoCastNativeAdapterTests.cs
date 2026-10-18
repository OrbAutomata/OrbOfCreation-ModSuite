using System;
using System.Collections;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Native;

public sealed class AutoCastNativeAdapterTests : IDisposable
{
    private static readonly Guid RecipeId = Guid.Parse("5ab1e000-0000-4000-8000-000000000001");

    public AutoCastNativeAdapterTests()
    {
        SpellManager.instance = new SpellManager();
        TargetingManager.OpenRequests = 0;
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
        using var adapter = new AutoCastNativeAdapter();

        var submission = adapter.Fire(0, RecipeId, holdFullCharge: false);

        Assert.Equal(AutoCastPreflight.AlreadyCasting, submission.Preflight);
        Assert.Equal(
            "the spell is still starting its cast, so the press would start no cast",
            submission.Reason);
    }

    [Fact]
    public void An_unexpected_exception_from_a_native_read_escapes_the_fire()
    {
        var recipe = new SpellRecipeSO();
        recipe.SetGuid(RecipeId);
        var block = new InstantEffectBlock();
        block.effectScripts.Add(new UnreadableEffectScript());
        recipe.onCastEffects.Add(block);
        SpellManager.instance!.activeSpells.value.Add(new Spell(recipe));
        using var adapter = new AutoCastNativeAdapter();

        Assert.Throws<InvalidCastException>(() => adapter.Fire(0, RecipeId, holdFullCharge: false));
    }

    private sealed class UnreadableEffectScript : IInstantEffectScript, IEnumerable
    {
        public IEnumerator GetEnumerator() =>
            throw new InvalidCastException("injected effect graph read failure");
    }

    public void Dispose()
    {
        SpellManager.NativeCanCast = true;
        SpellManager.instance = null;
    }
}
