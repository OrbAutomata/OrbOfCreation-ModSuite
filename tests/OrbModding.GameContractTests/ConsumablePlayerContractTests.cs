using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ConsumablePlayerContractTests
{
    [GameAssemblyFact]
    public void ConsumablePlayerBindingsPinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x060009D1, assembly.GetMethodToken("ConsumableSO", "SetRandomization"));
        Assert.Equal(0x060009D2, assembly.GetMethodToken("ConsumableSO", "CancelUsage"));
        Assert.Equal(0x060009D3, assembly.GetMethodToken("ConsumableSO", "Discard"));
        Assert.Equal(0x06000DD1, assembly.GetMethodToken("ConsumableUsage", "GetResultInfo"));
        Assert.Equal(0x06001BFB, assembly.GetMethodToken("EffectResultInfo", "IsCancelled"));
        Assert.Equal(0x06001BFC, assembly.GetMethodToken("EffectResultInfo", "Cancel"));
        Assert.Equal(0x0600229A, assembly.GetMethodToken("UIConsumableRefList", "OnDrop"));
        Assert.Equal(0x0600229E,
            assembly.GetMethodToken("UIConsumableRefList", "DiscardConsumable"));
        Assert.Equal(0x060014ED,
            assembly.GetMethodToken("AbstractListVariable", "UpdateObservable"));

        Assert.Equal("ConsumableUsage", assembly.GetFieldType("ConsumableSO", "nextUsage"));
        Assert.Equal("Inventory", assembly.GetFieldType("Inventory", "_instance"));
        Assert.Equal(
            "ConsumableRefListVariable",
            assembly.GetFieldType("Inventory", "allConsumables"));
        Assert.Equal(
            "ConsumableRefListVariable",
            assembly.GetFieldType("Inventory", "hotBar"));
    }

    [GameAssemblyFact]
    public void CancelUsageCancelsAndRemovesTheExactPendingUsageBeforeAdvancingTheQueue()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "ConsumableSO", "CancelUsage");

        var resultInfo = Offset(references, "ConsumableUsage", "GetResultInfo");
        var cancel = Offset(references, "EffectResultInfo", "Cancel");
        var remove = references.Single(reference =>
            reference.MemberName == "Remove" &&
            reference.DeclaringType.Contains("ConsumableUsage", StringComparison.Ordinal)).Offset;
        var prepNext = Offset(references, "ConsumableSO", "PrepNextUsage");

        Assert.True(cancel > resultInfo, "The selected usage must own the cancelled result.");
        Assert.True(remove > cancel, "The usage must be cancelled before it leaves the list.");
        Assert.True(prepNext > remove, "The queue may advance only after exact usage removal.");
    }

    [GameAssemblyFact]
    public void ConsumableListDropValidatesSameListThenPublishesTheExactNativeOrder()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "UIConsumableRefList", "OnDrop");

        var listsMatch = Offset(references, "DragDropContext", "ListsMatch");
        var indicesMatch = Offset(references, "DragDropContext", "IndicesMatch");
        var swap = references.Single(reference =>
            reference.MemberName == "SwapPositions" &&
            reference.DeclaringType.Contains("ConsumableSO", StringComparison.Ordinal)).Offset;
        var update = Offset(references, "AbstractListVariable", "UpdateObservable");
        var setAt = references.Single(reference =>
            reference.MemberName == "SetAt" &&
            reference.DeclaringType.Contains("ConsumableSO", StringComparison.Ordinal)).Offset;

        Assert.True(indicesMatch > listsMatch);
        Assert.True(swap > indicesMatch);
        Assert.True(update > swap);
        Assert.True(setAt > update, "The hotbar rule tail must run after list publication.");
    }

    [GameAssemblyFact]
    public void DiscardAndRandomizationUiReachTheAuditedDataMutators()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var discard = References(assembly, "UIConsumableRefList", "DiscardConsumable");
        Assert.True(
            Offset(discard, "ConsumableSO", "Discard") >
            Offset(discard, "GlobalVariables", "GetMultiBuy"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIConsumableRefItem",
            "TurnRandomizationOn",
            "ConsumableSO",
            "SetRandomization"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIConsumableRefItem",
            "TurnRandomizationOff",
            "ConsumableSO",
            "SetRandomization"));
    }

    [Fact]
    public void ManifestNamesEveryB006SpecificActionAndCaptureTouch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "consumable-player.consumable-cancel-usage-action",
            "consumable-player.consumable-discard-action",
            "consumable-player.consumable-next-usage-action",
            "consumable-player.usage-result-info-action",
            "consumable-player.result-info-is-cancelled-action",
            "consumable-player.inventory-instance-action",
            "consumable-player.inventory-list-action",
            "consumable-player.hotbar-list-action",
            "consumable-player.list-value-action",
            "consumable-player.list-swap-action",
            "consumable-player.list-set-at-action",
            "consumable-player.list-update-action",
            "consumable-inventory.inventory-instance-capture",
            "consumable-inventory.inventory-list-capture",
            "consumable-inventory.hotbar-list-capture",
            "consumable-inventory.list-value-capture",
            "consumable-inventory.list-get-max-capture",
            "consumable-inventory.can-use-capture",
            "consumable.cost-has-enough-capture",
            "consumable.cost-value-capture",
        };

        Assert.All(expected, id =>
            Assert.Single(manifest.Contracts, contract => contract.Id == id));
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
