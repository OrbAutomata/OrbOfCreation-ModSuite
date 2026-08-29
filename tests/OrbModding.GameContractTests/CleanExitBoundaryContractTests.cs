using Xunit;

namespace OrbModding.GameContractTests;

public sealed class CleanExitBoundaryContractTests
{
    [GameAssemblyFact]
    public void BothNativeQuitEntrypointsTerminateDirectlyWithoutAReceiptableTransition()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var directQuit = new byte[] { 0x28, 0x72, 0x03, 0x00, 0x0a, 0x2a };

        Assert.Equal(0x06002507,
            assembly.GetMethodToken("UIQuitButton", "QuitApplication"));
        Assert.Equal(0x06000702,
            assembly.GetMethodToken("SaveStateManager", "QuitGame"));
        Assert.Equal(directQuit,
            assembly.GetMethodBodyBytes("UIQuitButton", "QuitApplication"));
        Assert.Equal(directQuit,
            assembly.GetMethodBodyBytes("SaveStateManager", "QuitGame"));
        Assert.Empty(assembly.GetMethodBodyDefinitionReferences(
            "UIQuitButton", "QuitApplication"));
        Assert.Empty(assembly.GetMethodBodyDefinitionReferences(
            "SaveStateManager", "QuitGame"));
        AssertQuitOnly(assembly.GetMethodBodyMemberReferences(
            "UIQuitButton", "QuitApplication"));
        AssertQuitOnly(assembly.GetMethodBodyMemberReferences(
            "SaveStateManager", "QuitGame"));
    }

    private static void AssertQuitOnly(
        System.Collections.Generic.IReadOnlyList<MethodBodyDefinitionReference> references)
    {
        var reference = Assert.Single(references);
        Assert.Equal("UnityEngine.Application", reference.DeclaringType);
        Assert.Equal("Quit", reference.MemberName);
    }
}
