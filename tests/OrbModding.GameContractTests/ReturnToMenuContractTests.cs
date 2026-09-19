using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ReturnToMenuContractTests
{
    [Fact]
    public void ManifestNamesTheCompleteLiveControlBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "return-to-menu.button.type-action",
            "return-to-menu.button-action",
            "return-to-menu.screen-flash.type-action",
            "return-to-menu.screen-flash-instance-action",
            "return-to-menu.screen-flash-active-action",
            "return-to-menu.button-component-action",
            "return-to-menu.component-game-object-action",
            "return-to-menu.behaviour-enabled-action",
            "return-to-menu.game-object-active-action",
            "return-to-menu.selectable-interactable-action",
            "return-to-menu.object-name-action",
            "return-to-menu.modal.type-action",
            "return-to-menu.modal-open-action",
            "return-to-menu.modal-activator.type-action",
            "return-to-menu.activator-created-modal-action",
            "return-to-menu.activator-modal-created-action",
            "return-to-menu.activator-button-component-action",
            "return-to-menu.activator-open-action",
            "return-to-menu.component-transform-action",
            "return-to-menu.transform-child-of-action",
        };

        Assert.All(expected, id => Assert.Single(
            manifest.Contracts, contract => contract.Id == id));
    }

    [GameAssemblyFact]
    public void BackToMenuBindingsAndStartDestinationStayTokenExact()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600280d, assembly.GetMethodToken("UIBackToMenuButton", "BackToMenu"));
        Assert.Equal("UnityEngine.UI.Button", assembly.GetFieldType("UIBackToMenuButton", "button"));
        Assert.Equal(0x0400148c, assembly.GetFieldToken("UIBackToMenuButton", "manualSave"));
        Assert.Equal(0x060006ff, assembly.GetMethodToken("SaveStateManager", "BackToMainMenu"));
        Assert.Equal(0x06000700, assembly.GetMethodToken("SaveStateManager", "AnimateChangeScene",
            "System.String"));
        Assert.Equal(0x0400130a, assembly.GetFieldToken("UIScreenFlash", "instance"));
        Assert.Equal(0x04001308, assembly.GetFieldToken("UIScreenFlash", "isActive"));
        Assert.Equal(0x060026a3, assembly.GetMethodToken("UIScreenFlash", "FadeIn",
            "System.Single", "System.Single"));
        Assert.Equal(
            new byte[] { 0x02, 0x72, 0xcd, 0x3a, 0x00, 0x70, 0x28, 0x00, 0x07, 0x00, 0x06, 0x2a },
            assembly.GetMethodBodyBytes("SaveStateManager", "BackToMainMenu"));
    }

    /// <summary>
    /// The panel that holds the control is found by identity, not by caption: its activator names
    /// the modal it created, and the modal's own transform is what the control hangs under.
    /// </summary>
    [GameAssemblyFact]
    public void PanelActivatorOwnsTheModalItOpensAndOpeningIsOneVisibleCallback()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("UIModal", assembly.GetFieldType("UIModalActivator", "createdModal"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("UIModalActivator", "modalCreated"));
        Assert.Equal("UnityEngine.UI.Button", assembly.GetFieldType("UIModalActivator", "button"));

        var open = assembly.GetMethodBodyDefinitionReferences("UIModalActivator", "OpenModal");
        Assert.Contains(open, reference =>
            reference.DeclaringType == "UIModalActivator" && reference.MemberName == "modalCreated");
        Assert.Contains(open, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "Open");

        // The clone under contentArea is why a closed panel leaves zero live controls and an open
        // one leaves exactly one.
        var content = assembly.GetMethodBodyDefinitionReferences(
            "UIModal", "SetContent", "UnityEngine.RectTransform");
        Assert.Contains(content, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "contentArea");
    }

    [GameAssemblyFact]
    public void VisibleButtonSavesBeforeSchedulingTheOneGameWrittenTransitionSentinel()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var ui = assembly.GetMethodBodyDefinitionReferences("UIBackToMenuButton", "BackToMenu");
        Assert.True(Offset(ui, "VoidEventChannel", "Raise") <
            Offset(ui, "SaveStateManager", "BackToMainMenu"));

        var manager = assembly.GetMethodBodyDefinitionReferences(
            "SaveStateManager", "AnimateChangeScene", "System.String");
        Assert.True(Offset(manager, "UIScreenFlash", "FadeIn") <
            Offset(manager, "UIScreenFlash", "SetLoadingAnim"));
        Assert.True(Offset(manager, "UIScreenFlash", "SetLoadingAnim") <
            Offset(manager, "UIScreenFlash", "OnAnimComplete"));

        var fade = assembly.GetMethodBodyDefinitionReferences(
            "UIScreenFlash", "FadeIn", "System.Single", "System.Single");
        Assert.True(Offset(fade, "UIScreenFlash", "isActive") <
            Offset(fade, "UIScreenFlash", "AnimateAlpha"));
    }

    private static int Offset(
        System.Collections.Generic.IReadOnlyList<MethodBodyDefinitionReference> references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType &&
            reference.MemberName == memberName).Offset;
}
