using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class SpellLoadoutContractTests
{
    [GameAssemblyFact]
    public void SpellLoadoutBindings_PinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06001027, assembly.GetMethodToken("Spell", "IsEmpty"));
        Assert.Equal(0x06001038, assembly.GetMethodToken("Spell", "CanRemove"));
        Assert.Equal(0x0600074C, assembly.GetMethodToken("SpellManager", "RemoveSpell"));
        Assert.Equal(0x060014ED, assembly.GetMethodToken("AbstractListVariable", "UpdateObservable"));

        Assert.Contains(
            assembly.GetMethods("AbstractListVariable`1", "SwapPositions"),
            method => method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "System.Void" &&
                method.ParameterTypes.SequenceEqual(new[] { "System.Int32", "System.Int32" }));
    }

    /// <summary>
    /// The gate the removal actually applies, and the predicate it never consults. <c>CanRemove</c>
    /// reads charge availability and casting, which reads like the removal rule and is not it: the
    /// game asks for full charges, and refusing costs the player a cooldown switch.
    /// </summary>
    [GameAssemblyFact]
    public void RemoveSpell_GatesItselfOnFullChargesAndNoCastInProgress()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "SpellManager", "RemoveSpell");

        var empty = Offset(references, "Spell", "IsEmpty");
        var atMaxCharges = Offset(references, "Spell", "IsAtMaxCharges");
        var casting = Offset(references, "Spell", "IsCasting");
        var readyingCast = Offset(references, "Spell", "IsReadyingCast");
        var remove = references.Single(reference => reference.MemberName == "Remove").Offset;

        Assert.True(atMaxCharges > empty, "The charge gate must follow the empty-slot guard.");
        Assert.True(casting > atMaxCharges, "The casting guard must follow the charge gate.");
        Assert.True(readyingCast > casting, "The ready-to-cast guard must follow the casting guard.");
        Assert.True(remove > readyingCast, "The list removal must follow every gate.");
        Assert.DoesNotContain(
            references,
            reference => reference.DeclaringType == "Spell" && reference.MemberName == "CanRemove");
    }

    [GameAssemblyFact]
    public void RemoveGateMembers_KeepTheShapeTheBoundaryBinds()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Contains(
            assembly.GetMethods("Spell", "IsAtMaxCharges"),
            method => method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "System.Boolean" &&
                method.ParameterTypes.Count == 0);
        Assert.Contains(
            assembly.GetMethods("Spell", "IsReadyingCast"),
            method => method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "System.Boolean" &&
                method.ParameterTypes.Count == 0);
    }

    /// <summary>
    /// The refusal is not a no-op: the game flips the spell to a time-based cooldown on its way
    /// out, which is why the boundary answers instead of calling and hoping.
    /// </summary>
    [GameAssemblyFact]
    public void RefusedRemoval_SwitchesTheSpellToATimeBasedCooldown()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "SpellManager", "RemoveSpell");

        var atMaxCharges = Offset(references, "Spell", "IsAtMaxCharges");
        var addFlags = references.First(reference => reference.MemberName == "AddFlags").Offset;
        var remove = references.Single(reference => reference.MemberName == "Remove").Offset;

        Assert.True(addFlags > atMaxCharges, "The cooldown switch belongs to the refused branch.");
        Assert.True(addFlags < remove, "The refused branch runs before the removal branch.");
    }

    [GameAssemblyFact]
    public void RemoveSpell_RemovesTheExactInstanceThenDestroysAndRecomputesWeight()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "SpellManager", "RemoveSpell");

        var remove = references.Single(reference => reference.MemberName == "Remove");
        var destroy = references.Single(reference =>
            reference.DeclaringType == "Spell" && reference.MemberName == "Destroy");
        var recompute = references.Single(reference =>
            reference.DeclaringType == "SpellManager" &&
            reference.MemberName == "RecomputeSpellWeight");

        Assert.True(destroy.Offset > remove.Offset, "The removed spell must be destroyed after list removal.");
        Assert.True(recompute.Offset > destroy.Offset, "Weight must be recomputed after destroying the removed spell.");
    }

    [GameAssemblyFact]
    public void SpellListDrop_ValidatesIdentityThenSwapsAndPublishesTheNewOrder()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "UISpellList", "OnDrop");

        var listsMatch = Offset(references, "DragDropContext", "ListsMatch");
        var indicesMatch = Offset(references, "DragDropContext", "IndicesMatch");
        var swap = references.Single(reference =>
            reference.MemberName == "SwapPositions" &&
            reference.DeclaringType == "AbstractListVariable`1<Spell>").Offset;
        var update = Offset(references, "AbstractListVariable", "UpdateObservable");

        Assert.True(indicesMatch > listsMatch, "The UI must reject cross-list drops before comparing indices.");
        Assert.True(swap > indicesMatch, "The exact slot swap must follow both identity guards.");
        Assert.True(update > swap, "The reordered list must notify observers only after the swap.");
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Concat(assembly.GetMethodBodyMemberReferences(typeName, methodName))
            .OrderBy(reference => reference.Offset)
            .ToArray();

    private static int Offset(
        MethodBodyDefinitionReference[] references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType && reference.MemberName == memberName)
        .Offset;
}
