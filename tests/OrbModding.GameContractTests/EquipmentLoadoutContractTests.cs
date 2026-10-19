using Xunit;
using static OrbModding.GameContractTests.MethodBodyReferences;

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
                    Offset(refs, "StackableListVariable`1<EquipmentSO>", "Stack"));
        Assert.True(Offset(refs, "ResourceCostList", "MaximumCostTimes") <
                    Offset(refs, "StackableListVariable`1<EquipmentSO>", "Stack"));
        Assert.True(Offset(refs, "StackableListVariable`1<EquipmentSO>", "Stack") <
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
}
