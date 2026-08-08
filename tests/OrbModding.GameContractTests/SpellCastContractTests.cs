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
