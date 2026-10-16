using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class AlchemyLoadoutContractTests
{
    [GameAssemblyFact]
    public void VisibleAlchemyListsRouteClicksAndDropsThroughTheAuditedLifecycle()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600217B, assembly.GetMethodToken("UIAlchemyRecipeList", "ClickItem", "AlchemyRecipeSO"));
        Assert.Equal(0x06002167, assembly.GetMethodToken("UIAlchemyInstanceList", "ClickItem", "AlchemyInstance"));
        Assert.Equal(0x06002168, assembly.GetMethodToken("UIAlchemyInstanceList", "OnDrop", "DragDropContext"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIAlchemyRecipeList", "ClickItem", "AlchemyInstanceListVariable", "EngageAlchemy"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIAlchemyInstanceList", "ClickItem", "AlchemyInstanceListVariable", "DisengageAlchemy"));
        var dropReferences = References(
            assembly, "UIAlchemyInstanceList", "OnDrop", "DragDropContext");
        Assert.Contains(dropReferences, reference =>
            reference.MemberName == "SwapPositions" &&
            reference.DeclaringType == "AbstractListVariable`1<AlchemyInstance>");
        Assert.Contains(dropReferences, reference =>
            reference.MemberName == "UpdateObservable" &&
            reference.DeclaringType == "AbstractListVariable");
    }

    [GameAssemblyFact]
    public void UiWrappersDelegateTheirStripSizedDecisionToExplicitCountCoreMethods()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06001604,
            assembly.GetMethodToken("AlchemyInstanceListVariable", "EngageAlchemy", "AlchemyRecipeSO"));
        var refs = References(assembly, "AlchemyInstanceListVariable", "EngageAlchemy", "AlchemyRecipeSO");

        Assert.True(Offset(refs, "AlchemyRecipeSO", "GetFreeUsageSlots") >= 0);
        Assert.True(Offset(refs, "AlchemyRecipeSO", "GetMaxUsageSlots") >= 0);
        Assert.True(Offset(refs, "AlchemyRecipeSO", "GetUsageCost") >= 0);
        Assert.True(Offset(refs, "ResourceCostList", "MaximumCostTimes") >= 0);
        Assert.True(Offset(refs, "GlobalVariables", "GetMultiBuy") >= 0);
        Assert.True(Offset(refs, "GlobalVariables", "GetMultiBuy") <
                    Offset(refs, "AlchemyInstanceListVariable", "AddAlchemyInstances"));
        Assert.Equal(0x06001606,
            assembly.GetMethodToken("AlchemyInstanceListVariable", "DisengageAlchemy", "AlchemyRecipeSO"));
        var removeRefs = References(assembly, "AlchemyInstanceListVariable", "DisengageAlchemy", "AlchemyRecipeSO");

        Assert.True(Offset(removeRefs, "GlobalVariables", "GetMultiBuy") >= 0);
        Assert.True(Offset(removeRefs, "GlobalVariables", "GetMultiBuy") <
                    Offset(removeRefs, "AlchemyInstanceListVariable", "RemoveAlchemyInstances"));
        Assert.Contains(References(assembly, "AlchemyInstanceListVariable", "AddAlchemyInstances",
                "AlchemyRecipeSO", "System.Int32"),
            reference => reference.DeclaringType == "AlchemyInstance" &&
                         reference.MemberName == "AddQuantity");
        Assert.Contains(References(assembly, "AlchemyInstanceListVariable", "RemoveAlchemyInstances",
                "AlchemyRecipeSO", "System.Int32"),
            reference => reference.DeclaringType == "AlchemyInstance" &&
                         reference.MemberName == "RemoveQuantity");
    }

    [Fact]
    public void ManifestNamesTheCompleteOrdinaryAlchemyLifecycleBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "alchemy-loadout.recipe.type-action", "alchemy-loadout.manager.type-action",
            "alchemy-loadout.list.type-action", "alchemy-loadout.instance.type-action",
            "alchemy-loadout.cost.type-action", "alchemy-loadout.manager-instance-action",
            "alchemy-loadout.manager-active-action", "alchemy-loadout.recipe-discovered-action",
            "alchemy-loadout.recipe-usage-cost-action", "alchemy-loadout.recipe-free-uses-action",
            "alchemy-loadout.recipe-maximum-uses-action", "alchemy-loadout.list-can-add-action",
            "alchemy-loadout.list-values-action", "alchemy-loadout.instance-reference-action",
            "alchemy-loadout.instance-queued-action", "alchemy-loadout.instance-remaining-free-action",
            "alchemy-loadout.instance-remaining-maximum-action", "alchemy-loadout.cost-maximum-times-action",
            "alchemy-loadout.cost-empty-action", "alchemy-loadout.list-add-count-action",
            "alchemy-loadout.list-remove-count-action",
        };
        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly, string type, string method, params string[] parameterTypes) =>
        assembly.GetMethodBodyDefinitionReferences(type, method, parameterTypes)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method, parameterTypes))
            .OrderBy(reference => reference.Offset)
            .ToArray();

    private static int Offset(MethodBodyDefinitionReference[] references, string type, string member) =>
        references.Where(reference =>
                reference.DeclaringType.StartsWith(type, StringComparison.Ordinal) &&
                reference.MemberName == member)
            .Select(reference => reference.Offset)
            .DefaultIfEmpty(-1)
            .Min();
}
