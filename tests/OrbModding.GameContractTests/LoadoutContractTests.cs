using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class LoadoutContractTests
{
    [GameAssemblyFact]
    public void PlayerLoadoutUiUsesTheAuditedWholeTransactionAndVisibleEditors()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600248b,
            assembly.GetMethodToken("UILoadoutList", "OnLoadoutClick", "PlayerLoadout"));
        Assert.True(assembly.MethodReferencesMethod(
            "UILoadoutList", "OnLoadoutClick", "LoadoutManager", "SetLoadout"));
        Assert.True(assembly.MethodReferencesMethod(
            "UILoadoutEditor", "ChangeName", "PlayerLoadout+LoadoutLabel", "SetName"));
        Assert.True(assembly.MethodReferencesMethod(
            "UILoadoutEditor", "ChangeIcon", "PlayerLoadout+LoadoutLabel", "SetIconIndex"));
        Assert.True(assembly.MethodReferencesMethod(
            "UILoadoutEditor", "ChangeColor", "PlayerLoadout+LoadoutLabel", "SetColorIndex"));
    }

    [GameAssemblyFact]
    public void SnapshotUiUsesSaveLoadAndClearCallbacksWithoutOverwrite()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x060026bc, assembly.GetMethodToken("UISnapshotLoadout", "ClearLoadout"));
        Assert.Equal(0x060026bd, assembly.GetMethodToken("UISnapshotLoadout", "SaveLoadout"));
        Assert.Equal(0x060026be, assembly.GetMethodToken("UISnapshotLoadout", "LoadLoadout"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISnapshotLoadout", "ClearLoadout", "SnapshotLoadout", "Clear"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISnapshotLoadoutList", "SaveAlchemySnapshot", "AlchemyInstanceListVariable", "CreateStackedRecord"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISnapshotLoadoutList", "LoadAlchemySnapshot", "AlchemyInstanceListVariable", "FromStackedRecord"));
        Assert.Equal(0x060026c7,
            assembly.GetMethodToken("UISnapshotLoadoutList", "SaveEquipmentSnapshot", "SnapshotLoadout"));
        Assert.Equal(0x060026c9,
            assembly.GetMethodToken("UISnapshotLoadoutList", "LoadEquipmentSnapshot", "SnapshotLoadout"));
        Assert.Equal(0x060007ba,
            assembly.GetMethodToken("StackableListVariable`1", "GetStackedRecord"));
        Assert.Equal(0x060007b0,
            assembly.GetMethodToken("StackableListVariable`1", "SetStack", "Stacked.StackedIdRecord`1<!0>"));
    }

    [GameAssemblyFact]
    public void RuntimeLoadoutMutationTokensStayPinned()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600060b, assembly.GetMethodToken("LoadoutManager", "CanSwapLoadouts"));
        Assert.Equal(0x06000610,
            assembly.GetMethodToken("LoadoutManager", "SetLoadout", "PlayerLoadout"));
        Assert.Equal(0x06000f0f,
            assembly.GetMethodToken("PlayerLoadout", "SetSaveEquipment", "System.Boolean"));
        Assert.Equal(0x06000f10,
            assembly.GetMethodToken("PlayerLoadout", "SetSaveAlchemy", "System.Boolean"));
        Assert.Equal(0x06000f94, assembly.GetMethodToken(
            "SnapshotLoadout`1", "SaveSnapshot", "Stacked.StackedIdRecord`1<!0>"));
        Assert.Equal(0x06000f95, assembly.GetMethodToken("SnapshotLoadout`1", "Clear"));
    }

    [Fact]
    public void ManifestNamesTheCompleteLoadoutBindingSet()
    {
        var expected = new[]
        {
            "loadout.manager.type-action", "loadout.player-list.type-action",
            "loadout.player.type-action", "loadout.label.type-action",
            "loadout.spell.type-action", "loadout.spell-recipe.type-action",
            "loadout.equipment.type-action", "loadout.alchemy-recipe.type-action",
            "loadout.alchemy-snapshot-list.type-action", "loadout.equipment-snapshot-list.type-action",
            "loadout.alchemy-snapshot.type-action", "loadout.equipment-snapshot.type-action",
            "loadout.alchemy-list.type-action", "loadout.equipment-list.type-action",
            "loadout.cost.type-action", "loadout.global-variables.type-action",
            "loadout.identity.type-action", "loadout.stacked-record.type-action",
            "loadout.manager-instance-action",
            "loadout.manager-player-list-action", "loadout.manager-alchemy-snapshots-action",
            "loadout.manager-equipment-snapshots-action", "loadout.manager-active-alchemy-action",
            "loadout.manager-active-equipment-action",
            "loadout.manager-can-swap-action", "loadout.manager-set-loadout-action",
            "loadout.manager-save-active-action", "loadout.list-values-action",
            "loadout.player-guid-action", "loadout.player-name-action",
            "loadout.player-selected-action", "loadout.player-equipment-enabled-action",
            "loadout.player-alchemy-enabled-action", "loadout.player-spells-action",
            "loadout.player-equipment-record-action", "loadout.player-alchemy-record-action",
            "loadout.player-label-action", "loadout.player-set-equipment-action",
            "loadout.player-set-alchemy-action", "loadout.label-name-action",
            "loadout.label-icon-action", "loadout.label-color-action",
            "loadout.label-set-name-action", "loadout.label-set-icon-action",
            "loadout.label-set-color-action", "loadout.global-custom-icons-action",
            "loadout.global-custom-colors-action", "loadout.identity-guid-action",
            "loadout.spell-guid-action", "loadout.spell-reference-action",
            "loadout.spell-empty-action", "loadout.alchemy-list-maximum-action",
            "loadout.equipment-list-maximum-action", "loadout.alchemy-snapshot-empty-action",
            "loadout.equipment-snapshot-empty-action", "loadout.alchemy-snapshot-clear-action",
            "loadout.equipment-snapshot-clear-action", "loadout.alchemy-snapshot-record-action",
            "loadout.equipment-snapshot-record-action", "loadout.alchemy-snapshot-save-action",
            "loadout.equipment-snapshot-save-action", "loadout.alchemy-active-record-action",
            "loadout.alchemy-active-set-action", "loadout.equipment-active-record-action",
            "loadout.equipment-active-set-action", "loadout.alchemy-record-entries-action",
            "loadout.equipment-record-entries-action", "loadout.cost-construct-action",
            "loadout.cost-add-action", "loadout.cost-subtract-action",
            "loadout.cost-multiply-action", "loadout.cost-enough-action",
        };
        var actual = NativeContractManifest.Load().Contracts
            .Where(contract => contract.Id.StartsWith("loadout.", StringComparison.Ordinal))
            .Select(contract => contract.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal), actual);
    }
}
