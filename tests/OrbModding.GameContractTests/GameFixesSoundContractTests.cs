using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The sound fixes patch the game's audio pool and the loops that hold it. Each test pins the game
/// fact one patch relies on, so a game update that moves it fails here before it reaches a player.
/// </summary>
public sealed class GameFixesSoundContractTests
{
    private const byte Call = 0x28;
    private const byte Callvirt = 0x6F;
    private const byte BrS = 0x2B;
    private const byte LdlocS = 0x11;
    private const byte Ldloc1 = 0x07;
    private const byte BltS = 0x32;

    /// <summary>Mono's JIT inlines a method only when its IL body is shorter than this.</summary>
    private const int MonoInlineLimit = 20;

    [GameAssemblyFact]
    public void OnlyPlayAndPlayLoopTakeAnElementFromThePoolAndBothInitializeIt()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        MethodBodyReferences.AssertMethod(assembly, "SoundManager", "GetAudioElement", false, "AudioElement");
        Assert.Equal(
            new[] { "SoundManager.Play", "SoundManager.PlayLoop" },
            graph.CallSitesOf("SoundManager", "GetAudioElement"));
        foreach (var play in new[] { "Play", "PlayLoop" })
        {
            var references = MethodBodyReferences.References(assembly, "SoundManager", play);
            Assert.True(
                MethodBodyReferences.Offset(references, "SoundManager", "GetAudioElement") <
                MethodBodyReferences.Offset(references, "AudioElement", "Initialize"));
        }
    }

    [GameAssemblyFact]
    public void SoundManagerDeclaresOnePlayWhoseSecondArgumentIsTheVolume()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var play = Assert.Single(assembly.GetMethods("SoundManager", "Play"));
        Assert.True(play.IsStatic);
        Assert.Equal("AudioElement", play.ReturnType);
        Assert.Equal(new[] { "UnityEngine.AudioClip", "System.Single" }, play.ParameterTypes);
        Assert.True(assembly.MethodReferencesMethod("SoundManager", "Play", "AudioElement", "SetVolume"));
        Assert.False(assembly.MethodReferencesMethod("SoundManager", "Play", "AudioElement", "SetDelay"));
    }

    [GameAssemblyFact]
    public void ThePoolIsClonedFromThePublicPrefabOfTheOneSoundManager()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("AudioElement", assembly.GetFieldType("SoundManager", "audioElement"));
        Assert.Equal("SoundManager", assembly.GetFieldType("SoundManager", "instance"));
        Assert.True(assembly.MethodReferencesField("SoundManager", "PrepareAudioElements", "SoundManager", "audioElement"));
    }

    [GameAssemblyFact]
    public void AnElementStartsItsSourceOnlyThroughPlayAndForgetsAFadeThroughCancelFade()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        MethodBodyReferences.AssertMethod(assembly, "AudioElement", "Play", false, "AudioElement");
        MethodBodyReferences.AssertMethod(assembly, "AudioElement", "CancelFade", false, "AudioElement");
        MethodBodyReferences.AssertMethod(assembly, "AudioElement", "FadeOutDestroy", false, "AudioElement", "System.Single");
        var starts = new[] { "Play", "PlayDelayed", "PlayScheduled", "PlayOneShot" }
            .SelectMany(start => graph.CallSitesOf("UnityEngine.AudioSource", start))
            .Where(caller => caller.StartsWith("AudioElement", StringComparison.Ordinal));
        Assert.Equal(new[] { "AudioElement.Play" }, starts);
        Assert.Contains("AudioElement+<StartPlayingEnumerator>d__36.MoveNext", graph.CallSitesOf("AudioElement", "Play"));
        Assert.Empty(graph.CallSitesOf("AudioElement", "DestroyAudio"));
    }

    [GameAssemblyFact]
    public void TheOverflowElementMutesASourceOnlyAwakeSetsAndTheGameNeverSetsMute()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        var source = assembly.GetField("AudioElement", "audioSource");
        Assert.Equal("private", source.Visibility);
        Assert.False(source.IsStatic);
        Assert.Equal("UnityEngine.AudioSource", source.FieldType);
        Assert.Equal(
            new[] { "AudioElement.Awake" },
            assembly.GetFieldUseSites(new[] { ("AudioElement", "audioSource") })
                .Where(site => site.Use == "store")
                .Select(site => site.MethodOwner + "." + site.MethodName));
        Assert.Empty(graph.CallSitesOf("UnityEngine.AudioSource", "set_mute"));
    }

    [GameAssemblyFact]
    public void OnlyTheChargeAndResearchLoopsLoopASoundTheyPlayedAndSetLoopingIsNeverInlined()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var graph = GameCallGraph.Build(assembly);

        MethodBodyReferences.AssertMethod(assembly, "AudioElement", "SetLooping", false, "AudioElement", "System.Boolean");
        Assert.Equal(
            new[]
            {
                "MusicManager.FadeInAmbiance",
                "MusicManager.PlayRandomSong",
                "SoundManager.Play",
                "SoundManager.PlayLoop",
                "Spell.StartCharging",
                "UIResearchItem.LoopResearchSound",
            },
            graph.CallSitesOf("AudioElement", "SetLooping"));
        Assert.True(assembly.MethodReferencesField("UIResearchItem", "LoopResearchSound", "UIResearchItem", "loopingSound"));
        Assert.True(assembly.GetMethodBodyBytes("AudioElement", "SetLooping").Length >= MonoInlineLimit);
    }

    [GameAssemblyFact]
    public void ASpellsLoopsAreStoredOnlyWhereTheGameStartsOrReleasesThem()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("AudioElement", assembly.GetFieldType("Spell", "loopingElement"));
        Assert.Equal("AudioElement", assembly.GetFieldType("Spell", "chargeFxElement"));
        MethodBodyReferences.AssertMethod(assembly, "Spell", "InitializePersistence", false, "System.Void");
        MethodBodyReferences.AssertMethod(assembly, "Spell", "StartCharging", false, "System.Void");
        var stores = assembly.GetFieldUseSites(new[] { ("Spell", "loopingElement"), ("Spell", "chargeFxElement") })
            .Where(site => site.Use == "store")
            .Select(site => site.FieldName + " <- " + site.MethodOwner + "." + site.MethodName)
            .OrderBy(store => store, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "chargeFxElement <- Spell.DestroyChargeLoop",
                "chargeFxElement <- Spell.StartCharging",
                "loopingElement <- Spell.EndCasting",
                "loopingElement <- Spell.InitializePersistence",
            },
            stores);
    }

    [GameAssemblyFact]
    public void ResearchAndBrewingItemsReachTheRenderGroupDisableAndReleaseTheirOwnLoops()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var disable = Assert.Single(assembly.GetMethods("UIRenderGroupElement", "OnDisable"));
        Assert.Equal("private", disable.Visibility);
        Assert.False(disable.IsStatic);
        Assert.Empty(disable.ParameterTypes);
        foreach (var (item, release) in new[] { ("UIResearchItem", "DestroyResearchSound"), ("UIBrewingStation", "DestroyLoopingAudio") })
        {
            Assert.Equal("UIGenericItem`1", Generic(assembly.GetBaseType(item)));
            Assert.Equal("UIRenderGroupElement", assembly.GetBaseType("UIGenericItem`1"));
            Assert.Empty(assembly.GetMethods(item, "OnDisable"));
            MethodBodyReferences.AssertMethod(assembly, item, release, false, "System.Void");
            Assert.True(assembly.MethodReferencesMethod(item, release, "AudioElement", "FadeOutDestroy"));
        }
        Assert.Empty(assembly.GetMethods("UIGenericItem`1", "OnDisable"));
    }

    [GameAssemblyFact]
    public void ThePowerBuildLoopPlaysOneSoundPerFireBoundedByLocalOne()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            new[] { Call },
            assembly.GetMethodReferenceOpcodes("StructureSO", "CompleteQueuedQuantity", Array.Empty<string>(),
                "GlobalVariables", "GetPowerBuildSfx"));
        var il = assembly.GetMethodBodyBytes("StructureSO", "CompleteQueuedQuantity");
        var references = MethodBodyReferences.References(assembly, "StructureSO", "CompleteQueuedQuantity");
        var call = MethodBodyReferences.Offset(references, "GlobalVariables", "GetPowerBuildSfx") - 1;
        Assert.Equal(Call, il[call]);
        Assert.Equal(Callvirt, il[call + 5]);
        Assert.Equal(assembly.GetMethodToken("AudioInstance", "Play"), BitConverter.ToInt32(il, call + 6));

        Assert.Equal(BrS, il[call - 2]);
        var compare = call + (sbyte)il[call - 1];
        Assert.Equal(LdlocS, il[compare]);
        Assert.Equal(Ldloc1, il[compare + 2]);
        Assert.Equal(BltS, il[compare + 3]);
        Assert.Equal(call, compare + 5 + (sbyte)il[compare + 4]);
    }

    private static string Generic(string typeName)
    {
        var argument = typeName.IndexOf('<', StringComparison.Ordinal);
        return argument < 0 ? typeName : typeName.Substring(0, argument);
    }
}
