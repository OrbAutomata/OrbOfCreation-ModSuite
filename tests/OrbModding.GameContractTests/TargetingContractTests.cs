using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class TargetingContractTests
{
    [GameAssemblyFact]
    public void TargetingBindings_PinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06000770, assembly.GetMethodToken("TargetingManager", "IsTargeting"));
        Assert.Equal(0x06000771, assembly.GetMethodToken("TargetingManager", "GetTargetingLink"));
        Assert.Equal(0x06003268, assembly.GetMethodToken("TargetingManager+TargetLink", "GetAllTargets"));
        Assert.Equal(0x0600326C, assembly.GetMethodToken("TargetingManager+TargetLink", "GetOwner"));
        Assert.Equal(0x06003269, assembly.GetMethodToken("TargetingManager+TargetLink", "GetTargetSelection"));
        Assert.Equal(0x04001A96, assembly.GetFieldToken("TargetingManager+TargetLink", "resultInfo"));
        Assert.Equal(0x06001CF2, assembly.GetMethodToken("ITooltipable", "GetName"));
        Assert.Equal(0x0600326B, assembly.GetMethodToken("TargetingManager+TargetLink", "CheckTarget"));
        Assert.Equal(0x06003265, assembly.GetMethodToken("TargetingManager+TargetLink", "HasTarget"));
        Assert.Equal(0x04001A94, assembly.GetFieldToken("TargetingManager+TargetLink", "target"));
        Assert.Equal(0x06001BFC, assembly.GetMethodToken("EffectResultInfo", "Cancel"));
        Assert.Equal(0x06001BFB, assembly.GetMethodToken("EffectResultInfo", "IsCancelled"));
    }

    [GameAssemblyFact]
    public void Randomize_IsAGetRandomThenSubmitTransactionWhileCloseDoesNotCancel()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var random = assembly.MethodReferenceOffset(
            "UITargetingInterface", "Randomize", "TargetingManager+TargetLink", "GetRandom");
        var submit = assembly.MethodReferenceOffset(
            "UITargetingInterface", "Randomize", "TargetingManager", "SubmitTarget");
        Assert.True(random >= 0);
        Assert.True(submit > random);
        Assert.False(assembly.MethodReferencesMethod(
            "UITargetingInterface", "Close", "TargetingManager", "RemoveRequest"));
    }

    [GameAssemblyFact]
    public void SubmitTarget_AssignsTheExactObjectBeforeRemovingTheCurrentRequest()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = assembly.GetMethodBodyDefinitionReferences("TargetingManager", "SubmitTarget")
            .Concat(assembly.GetMethodBodyMemberReferences("TargetingManager", "SubmitTarget"))
            .OrderBy(reference => reference.Offset)
            .ToArray();
        var assign = references.Single(reference =>
            reference.DeclaringType == "TargetingManager+TargetLink" &&
            reference.MemberName == "AssignTarget").Offset;
        var remove = references.Single(reference => reference.MemberName == "RemoveAt").Offset;
        Assert.True(remove > assign);
    }

    [GameAssemblyFact]
    public void Cancel_IsOwnedByEffectResultInfoAndRetiresItsTargetLinks()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("EffectResultInfo",
            assembly.GetFieldType("TargetingManager+TargetLink", "resultInfo"));
        Assert.True(assembly.MethodReferencesField(
            "EffectResultInfo", "Cancel", "EffectResultInfo", "cancelled"));
        Assert.True(assembly.MethodReferencesMethod(
            "EffectResultInfo", "Cancel", "TargetingManager", "RemoveRequest"));
    }

    [GameAssemblyFact]
    public void EligibleTargetIdentity_IsExactlyStructureSoInThisGameBuild()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(new[] { "StructureSO" },
            assembly.GetTypesImplementing("Targeting.ITargetable"));
    }
}
