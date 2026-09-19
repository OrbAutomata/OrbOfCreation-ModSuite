using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The shape of the panel the screen draws for one hovered element, which is the shape the MCP
/// tooltip reader projects. Each of these was a hop the reader used to take on its own: a second
/// node list, a recursive sub-panel row, and a link followed instead of named.
/// </summary>
public sealed class TooltipContainerContractTests
{
    /// <summary>
    /// One panel, one node list. <c>Render</c> stores <c>IsUsingAltTooltip()</c> and then reads one
    /// of the two lists into <c>renderedTooltipNodes</c>, so the alt list replaces the main one and
    /// never joins it.
    /// </summary>
    [GameAssemblyFact]
    public void ThePanelRendersOneNodeListAndTheAltListReplacesTheMainOne()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var render = assembly.GetMethodBodyDefinitionReferences("UITooltip", "Render")
            .Concat(assembly.GetMethodBodyMemberReferences("UITooltip", "Render"))
            .ToArray();
        Assert.Contains(render, reference =>
            reference.DeclaringType == "UITooltip" && reference.MemberName == "IsUsingAltTooltip");
        Assert.Contains(render, reference =>
            reference.DeclaringType == "ITooltipable" && reference.MemberName == "GetTooltipNodes");
        Assert.Contains(render, reference =>
            reference.DeclaringType == "ITooltipable" &&
            reference.MemberName == "GetAltTooltipNodes");
        Assert.Single(
            render,
            reference => reference.DeclaringType == "UITooltip" &&
                reference.MemberName == "renderedTooltipNodes" &&
                reference.Offset > render.First(candidate =>
                    candidate.MemberName == "GetAltTooltipNodes").Offset);
    }

    /// <summary>
    /// Which list is drawn is a key the player is holding, not anything the panel stores: the
    /// suite reads it back off the container the screen already has up.
    /// </summary>
    [GameAssemblyFact]
    public void TheAltListIsTheMoreInfoKeyAndTheEntityHavingOne()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        foreach (var owner in new[] { "UITooltip", "UITooltipContainer" })
        {
            var decision = assembly.GetMethodBodyDefinitionReferences(owner, "IsUsingAltTooltip")
                .Concat(assembly.GetMethodBodyMemberReferences(owner, "IsUsingAltTooltip"))
                .ToArray();
            Assert.Contains(decision, reference =>
                reference.DeclaringType == "ITooltipable" &&
                reference.MemberName == "HasAltTooltips");
            Assert.Contains(decision, reference =>
                reference.DeclaringType == "InputManager" &&
                reference.MemberName == "ShowMoreInfo");
        }

        var answer = Assert.Single(
            assembly.GetMethods("UITooltipContainer", "IsUsingAltTooltip"));
        Assert.Equal("public", answer.Visibility);
        Assert.False(answer.IsStatic);
        Assert.Equal("System.Boolean", answer.ReturnType);
        Assert.Empty(answer.ParameterTypes);
    }

    /// <summary>
    /// The sub-panel row is one level and stops. The container holds the list and the code that
    /// instantiates a panel per entry; the panel it instantiates has neither, so a sub-panel's own
    /// sub-tooltips are never drawn beside it.
    /// </summary>
    [GameAssemblyFact]
    public void TheSubPanelRowIsDrawnByTheContainerAndNotByThePanelsItDraws()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.List`1<ITooltipable>",
            assembly.GetFieldType("UITooltipContainer", "subTooltips"));
        Assert.Single(assembly.GetMethods("UITooltipContainer", "RenderChildren"));

        Assert.DoesNotContain(
            assembly.GetFields("UITooltip"), field => field.Name == "subTooltips");
        Assert.Empty(assembly.GetMethods("UITooltip", "RenderChildren"));
    }

    /// <summary>
    /// A node tree is walked through <c>children</c> alone. <c>tooltipable</c> is not text in this
    /// panel at all — <c>Setup</c> hands it and the node's sub-tooltips to the hover that draws the
    /// player's <em>next</em> panel.
    /// </summary>
    [GameAssemblyFact]
    public void ANodeDrawsItsChildrenAndHandsItsLinkToTheNextHover()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var render = assembly.GetMethodBodyDefinitionReferences("UITooltipNode", "Render")
            .Concat(assembly.GetMethodBodyMemberReferences("UITooltipNode", "Render"))
            .ToArray();
        Assert.Contains(render, reference =>
            reference.DeclaringType == "TooltipNode" && reference.MemberName == "children");
        Assert.DoesNotContain(render, reference =>
            reference.DeclaringType == "TooltipNode" &&
            (reference.MemberName == "tooltipable" ||
                reference.MemberName == "subTooltips" ||
                reference.MemberName == "GetSubTooltips"));

        var setup = assembly.GetMethodBodyDefinitionReferences("UITooltipNode", "Setup")
            .Concat(assembly.GetMethodBodyMemberReferences("UITooltipNode", "Setup"))
            .ToArray();
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "TooltipNode" && reference.MemberName == "tooltipable");
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "TooltipNode" && reference.MemberName == "GetSubTooltips");
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "HoverTooltip" && reference.MemberName == "Setup");
    }
}
