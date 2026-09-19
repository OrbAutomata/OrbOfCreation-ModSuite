using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The top-right chrome is not a screen strip: each icon is a <c>UIModalActivator</c> that puts a
/// prepared panel up, and the title it is authored with is the title the open panel wears.
/// </summary>
public sealed class ModalOpenContractTests
{
    [GameAssemblyFact]
    public void TheAuthoredTitleIsTheOneThePreparedPanelWears()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.String", assembly.GetFieldType("UIModalActivator", "modalTitle"));
        var start = assembly.GetMethodBodyDefinitionReferences("UIModalActivator", "Start")
            .Concat(assembly.GetMethodBodyMemberReferences("UIModalActivator", "Start"))
            .OrderBy(reference => reference.Offset)
            .ToArray();
        Assert.Contains(start, reference =>
            reference.DeclaringType == "UIModalActivator" && reference.MemberName == "modalTitle");
        Assert.Contains(start, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "PrepModal");
        Assert.Contains(start, reference =>
            reference.DeclaringType == "UIModalActivator" && reference.MemberName == "modalCreated");
    }

    /// <summary>
    /// The open is not the button's own listener. The game wires the icon to <c>ToggleModal</c>,
    /// whose answer depends on what the panel is already doing; <c>OpenModal</c> is the same press
    /// with one outcome, which is what a verb that reports a post-state needs.
    /// </summary>
    [GameAssemblyFact]
    public void OpeningIsTheOneDirectionOfTheControlTheIconToggles()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var toggle = assembly.GetMethodBodyDefinitionReferences("UIModalActivator", "ToggleModal");
        Assert.Contains(toggle, reference =>
            reference.DeclaringType == "UIModalActivator" && reference.MemberName == "OpenModal");
        var open = assembly.GetMethodBodyDefinitionReferences("UIModalActivator", "OpenModal")
            .Concat(assembly.GetMethodBodyMemberReferences("UIModalActivator", "OpenModal"))
            .ToArray();
        Assert.Contains(open, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "Open");
    }

    /// <summary>
    /// Closing a panel hides it; it does not deactivate it. <c>SetElementVisibility(false)</c> drops
    /// the canvas group's raycasts, interactivity and alpha and flips <c>isOpen</c>, and never
    /// touches the GameObject — so every control inside a panel the player closed stays active in
    /// the hierarchy, and liveness alone cannot say what this screen offers.
    /// </summary>
    [GameAssemblyFact]
    public void AClosedPanelIsHiddenRatherThanDeactivated()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var visibility = assembly
            .GetMethodBodyMemberReferences("UIModal", "SetElementVisibility")
            .Concat(assembly.GetMethodBodyDefinitionReferences("UIModal", "SetElementVisibility"))
            .ToArray();
        Assert.Contains(visibility, reference =>
            reference.DeclaringType == "UnityEngine.CanvasGroup" &&
            reference.MemberName == "set_alpha");
        Assert.Contains(visibility, reference =>
            reference.DeclaringType == "UnityEngine.CanvasGroup" &&
            reference.MemberName == "set_blocksRaycasts");
        Assert.Contains(visibility, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "isOpen");
        Assert.DoesNotContain(visibility, reference =>
            reference.MemberName == "SetActive");
    }

    /// <summary>
    /// What an open does is scene data. <c>PerformOpen</c> invokes the panel's own
    /// <c>onOpen</c> event and its content element's, and a UnityEvent's listeners are authored in
    /// the scene — no read of this build can say what opening a given panel runs. That is why the
    /// suite presses a named set of chrome instead of inferring which opens are inert.
    /// </summary>
    [GameAssemblyFact]
    public void WhatAnOpenRunsIsSceneWiredRatherThanReadable()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var opened = assembly.GetMethodBodyMemberReferences("UIModal", "PerformOpen")
            .Concat(assembly.GetMethodBodyDefinitionReferences("UIModal", "PerformOpen"))
            .ToArray();
        Assert.Contains(opened, reference =>
            reference.DeclaringType == "UIModal" && reference.MemberName == "onOpen");
        Assert.Contains(opened, reference =>
            reference.DeclaringType == "UnityEngine.Events.UnityEvent" &&
            reference.MemberName == "Invoke");
        Assert.Equal("UnityEngine.Events.UnityEvent", assembly.GetFieldType("UIModal", "onOpen"));

        // The developer surface the suite will not put on the board, named once in the build.
        Assert.True(assembly.HasType("DevConsoleEngine"));
        Assert.Contains(
            assembly.GetMethods("DevConsoleEngine", "RunCommand"),
            method => method.ParameterTypes.SequenceEqual(new[] { "System.String" }));
    }

    [Fact]
    public void ManifestNamesTheAuthoredPanelTitle()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Single(
            manifest.Contracts,
            contract => contract.Id == "modal-open.activator-title-action");
    }
}
