using Xunit;

namespace OrbModding.GameContractTests;

public sealed class SpellCastContractTests
{
    [GameAssemblyFact]
    public void ToggleOffUsesTheSameNativeFireRouteAsTheVisibleSpellButton()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.True(assembly.MethodReferencesMethod("UISpellList", "OnSpellFire", "Spell", "CanFire"));
        Assert.True(assembly.MethodReferencesMethod("UISpellList", "OnSpellFire", "Spell", "Fire"));
        Assert.True(assembly.MethodReferencesMethod("SpellManager", "FireSpellIndex", "Spell", "CanFire"));
        Assert.True(assembly.MethodReferencesMethod("SpellManager", "FireSpellIndex", "Spell", "Fire"));
        Assert.True(assembly.MethodReferencesMethod("Spell", "Fire", "Spell", "IsCasting"));
        Assert.True(assembly.MethodReferencesMethod("Spell", "Fire", "SettingsManager", "CanCancelSpells"));
        Assert.True(assembly.MethodReferencesMethod("Spell", "Fire", "Spell", "EndCasting"));
    }

    [GameAssemblyFact]
    public void TheManualCastCounterIsWrittenOnlyByTheGamesOwnCastExecution()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Int32", assembly.GetFieldType("Spell", "numCasts"));
        Assert.True(assembly.MethodReferencesField("Spell", "ExecuteSpell", "Spell", "numCasts"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "ExecuteSpell", "Spell+SpellCastData", "IsManual"));
    }

    /// <summary>
    /// The counter moves when a cast finishes, not when one is pressed, so it cannot answer
    /// "did my press land" in the frame the press happened. Neither the button nor the cast entry
    /// point touches it; only the per-frame execution the cast timer runs into does.
    /// </summary>
    [GameAssemblyFact]
    public void TheCastCounterMovesWhenACastFinishesRatherThanWhenOneIsPressed()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.False(assembly.MethodReferencesField("Spell", "Fire", "Spell", "numCasts"));
        Assert.False(assembly.MethodReferencesField("Spell", "Cast", "Spell", "numCasts"));
        Assert.False(assembly.MethodReferencesMethod("Spell", "Cast", "Spell", "ExecuteSpell"));
        Assert.True(assembly.MethodReferencesMethod("Spell", "Cast", "Spell", "PrepCast"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "CheckExecuteSpell", "Spell", "ExecuteSpell"));
    }

    /// <summary>
    /// Both arms of <c>Spell.Fire</c>'s first branch answer a running spell with something other
    /// than a cast, which is why the boundary refuses a fire at one instead of pressing it.
    /// </summary>
    [GameAssemblyFact]
    public void FiringARunningSpellNeverStartsACast()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.True(assembly.MethodReferencesMethod("Spell", "Fire", "Spell", "CancelDoubleCast"));
        Assert.False(assembly.MethodReferencesMethod("Spell", "Fire", "Spell", "PrepCast"));
    }

    [Fact]
    public void ManifestNamesEveryNewToggleOffActionAndCaptureTouch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "auto-cast.spell-can-fire-action",
            "auto-cast.spell-is-casting-action",
            "auto-cast.spell-is-toggled-action",
            "auto-cast.settings-can-cancel-action",
            "auto-cast.settings-can-cancel-capture",
            "spell.num-casts",
        };
        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }
}
