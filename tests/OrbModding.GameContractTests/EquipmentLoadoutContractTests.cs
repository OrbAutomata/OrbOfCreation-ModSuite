using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class EquipmentLoadoutContractTests
{
    [GameAssemblyFact]
    public void Equipment_manager_object_overloads_and_loadout_members_keep_the_audited_tokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000517, assembly.GetMethodToken("EquipmentManager", "EquipItem", "EquipmentSO"));
        Assert.Equal(0x06000519, assembly.GetMethodToken("EquipmentManager", "UnEquipItem", "EquipmentSO"));
        Assert.Equal(0x060007AF, assembly.GetMethodToken("StackableListVariable`1", "GetStacks"));
        Assert.Equal(0x06001523, assembly.GetMethodToken("AbstractListVariable`1", "GetMax"));
        Assert.Equal(0x06001524, assembly.GetMethodToken("AbstractListVariable`1", "IsAtMax"));
        Assert.Equal(0x0600166A, assembly.GetMethodToken("EquipmentListVariable", "GetTypesEquipped"));
        Assert.Equal(0x06000B6F, assembly.GetMethodToken("EquipmentTypeSO", "GetMaxTypeSlots"));
    }

    [GameAssemblyFact]
    public void Equip_manager_computes_live_multi_buy_stack_and_usage_affordability_before_stacking()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var refs = References(assembly, "EquipmentManager", "EquipItem", "EquipmentSO");

        Assert.True(Offset(refs, "ResourceCostList", "HasEnough") <
                    Offset(refs, "GlobalVariables", "GetMultiBuy"));
        Assert.True(Offset(refs, "GlobalVariables", "GetMultiBuy") <
                    Offset(refs, "StackableListVariable`1", "Stack"));
        Assert.True(Offset(refs, "ResourceCostList", "MaximumCostTimes") <
                    Offset(refs, "StackableListVariable`1", "Stack"));
        Assert.True(Offset(refs, "StackableListVariable`1", "Stack") <
                    Offset(refs, "EquipmentSO", "Equip"));
    }

    [GameAssemblyFact]
    public void Ui_admission_adds_the_equipment_type_room_guard_the_manager_does_not_own()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x060023C3, assembly.GetMethodToken("UIEquipmentItem", "CanEquip"));
        Assert.Equal(0x060023C4, assembly.GetMethodToken("UIEquipmentItem", "HasTypeRoom"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIEquipmentItem", "CanEquip", "UIEquipmentItem", "HasTypeRoom"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIEquipmentItem", "HasTypeRoom", "EquipmentListVariable", "GetTypesEquipped"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIEquipmentItem", "HasTypeRoom", "EquipmentTypeSO", "GetMaxTypeSlots"));
    }

    [Fact]
    public void Manifest_names_every_equipment_action_and_shared_decision_touch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "equipment-loadout.equipment.type-action", "equipment-loadout.manager.type-action",
            "equipment-loadout.list.type-action", "equipment-loadout.equipment-type.type-action",
            "equipment-loadout.cost.type-action", "equipment-loadout.manager-instance-action",
            "equipment-loadout.manager-equipped-list-action", "equipment-loadout.equipment-created-action",
            "equipment-loadout.equipment-type-field-action", "equipment-loadout.equipment-maximum-action",
            "equipment-loadout.equipment-cost-action", "equipment-loadout.list-stacks-action",
            "equipment-loadout.list-maximum-action", "equipment-loadout.list-at-maximum-action",
            "equipment-loadout.list-values-action", "equipment-loadout.list-type-count-action",
            "equipment-loadout.type-maximum-action", "equipment-loadout.cost-enough-action",
            "equipment-loadout.cost-maximum-times-action", "equipment-loadout.manager-equip-action",
            "equipment-loadout.manager-unequip-action",
        };
        Assert.All(expected, id => Assert.Single(manifest.Contracts, contract => contract.Id == id));
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
