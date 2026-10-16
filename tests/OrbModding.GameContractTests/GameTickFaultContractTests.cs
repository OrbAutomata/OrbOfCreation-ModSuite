using System;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The tick-fault hooks swap exactly the audited interface calls for the observer, so the game
/// bodies they rewrite must still make exactly those calls, by <c>callvirt</c>, and nothing else
/// of the same target.
/// </summary>
public sealed class GameTickFaultContractTests
{
    private const byte Callvirt = 0x6F;

    [GameAssemblyFact]
    public void TheElementIteratorTicksEachElementThroughTwoInterfaceCalls()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var opcodes = assembly.GetMethodReferenceOpcodes(
            "GameManager+GameElementIterator",
            "Increment",
            new[] { "System.Single" },
            "IIncrement",
            "Increment",
            "System.Single");

        Assert.Equal(new[] { Callvirt, Callvirt }, opcodes);
    }

    [GameAssemblyFact]
    public void TheViewManagerChecksEachViewThroughOneVirtualCall()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var opcodes = assembly.GetMethodReferenceOpcodes(
            "ViewManager",
            "CheckViews",
            Array.Empty<string>(),
            "ManagedView",
            "CheckViewVisibility");

        Assert.Equal(new[] { Callvirt }, opcodes);
    }

    [GameAssemblyFact]
    public void TheFixedTickReachesElementsOnlyThroughTheIterator()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod("GameManager", "FixedUpdate", "GameManager", "IncrementElements"));
        Assert.True(assembly.MethodReferencesMethod(
            "GameManager", "IncrementElements", "GameManager+GameElementIterator", "Increment"));
        Assert.True(assembly.MethodReferencesMethod("ViewManager", "Update", "ViewManager", "CheckViews"));
    }
}
