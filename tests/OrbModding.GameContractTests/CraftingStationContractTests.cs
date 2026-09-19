using System;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class CraftingStationContractTests
{
    [GameAssemblyFact]
    public void BrewingStationSelectorsUseTheRuntimeStationCallbacks()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002674, assembly.GetMethodToken("UIBrewingStation", "PostSetup"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "PostSetup", "CraftingStructureSO+TypeListElement", "GetElements"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "PostSetup", "CraftingStructure", "GetOutputList"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "<PostSetup>b__22_0", "UIBrewingStation", "SetIngredient"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "<PostSetup>b__22_1", "UIBrewingStation", "SetIngredient"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "SetIngredient", "CraftingStructure", "SetIngredient"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "SetOutput", "CraftingStructure", "SetOutput"));
    }

    [GameAssemblyFact]
    public void BrewingStationLevelAndActivationUseTheVisibleControls()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002682, assembly.GetMethodToken("UIBrewingStation", "ToggleBrewing"));
        Assert.Equal(0x0600267F,
            assembly.GetMethodToken("UIBrewingStation", "ChangeSelectedLevel", "System.Int32"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "ToggleBrewing", "CraftingStructure", "IsActive"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "ToggleBrewing", "CraftingStructure", "SetActive"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIBrewingStation", "ChangeSelectedLevel", "CraftingStructure", "SetSelectedLevel"));
    }

    [GameAssemblyFact]
    public void RuntimeStationMutationTokensStayPinned()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000E12, assembly.GetMethodToken(
            "CraftingStructure", "SetIngredient", "System.Int32", "CraftingStructureSO+TypeElement"));
        Assert.Equal(0x06000E14, assembly.GetMethodToken(
            "CraftingStructure", "SetOutput", "CraftingStructureSO+TypeElement"));
        Assert.Equal(0x06000E1F,
            assembly.GetMethodToken("CraftingStructure", "SetSelectedLevel", "System.Int32"));
        Assert.Equal(0x06000E1B,
            assembly.GetMethodToken("CraftingStructure", "SetActive", "System.Boolean"));
    }

    [Fact]
    public void ManifestNamesTheCompleteBrewingStationBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "crafting-station.structure.type-action",
            "crafting-station.station.type-action",
            "crafting-station.instance-list.type-action",
            "crafting-station.list-element.type-action",
            "crafting-station.element.type-action",
            "crafting-station.tooltipable-interface.type-action",
            "crafting-station.tooltipable-object.type-action",
            "crafting-station.tooltipable-guid-action",
            "crafting-station.structure-all-action",
            "crafting-station.structure-instances-action",
            "crafting-station.instance-list-get-all-action",
            "crafting-station.structure-ingredient-lists-action",
            "crafting-station.list-element-elements-action",
            "crafting-station.element-tooltipable-action",
            "crafting-station.element-available-action",
            "crafting-station.station-reference-action",
            "crafting-station.station-guid-action",
            "crafting-station.station-ingredient-action",
            "crafting-station.station-output-action",
            "crafting-station.station-output-list-action",
            "crafting-station.station-output-visible-action",
            "crafting-station.station-loaded-action",
            "crafting-station.station-active-action",
            "crafting-station.station-level-action",
            "crafting-station.station-min-level-action",
            "crafting-station.station-max-level-action",
            "crafting-station.station-set-ingredient-action",
            "crafting-station.station-set-output-action",
            "crafting-station.station-set-level-action",
            "crafting-station.station-set-active-action",
            "crafting-station.guid-container.type-capture",
            "crafting-station.cost.type-capture",
            "crafting-station.cost-entry.type-capture",
            "crafting-station.structure-guid-capture",
            "crafting-station.station-recipe-id-capture",
            "crafting-station.guid-container-value-capture",
            "crafting-station.station-current-drain-capture",
            "crafting-station.cost-entries-capture",
            "crafting-station.cost-entry-resource-capture",
            "crafting-station.cost-entry-value-capture",
        };

        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }
}
