using Xunit;

namespace OrbModding.GameContractTests;

public sealed class TooltipFeasibilityProbeTests
{
    [GameAssemblyFact]
    public void TooltipExplorerNativeShape_IsStructurallyReachable()
    {
        var assembly = GameAssemblyPaths.Require().ManagedDirectory + "/Assembly-CSharp.dll";
        using var metadata = new GameAssemblyMetadata(assembly);
        var tooltipItem = metadata.GetField("HoverTooltip", "tooltipItem");
        Assert.Equal("public", tooltipItem.Visibility);
        Assert.Equal("ITooltipable", tooltipItem.FieldType);

        var nested = metadata.GetField("HoverTooltip", "subTooltips");
        Assert.Equal("private", nested.Visibility);
        Assert.Equal("System.Collections.Generic.List`1<ITooltipable>", nested.FieldType);

        Assert.Single(metadata.GetMethods("HoverTooltip", "OpenTooltip"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetName"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetDisplayType"));
        Assert.Single(metadata.GetMethods("ITooltipable", "GetDescription"));
        Assert.Single(metadata.GetMethods("ITooltipable", "HasAltTooltips"));
        var nodes = Assert.Single(metadata.GetMethods("ITooltipable", "GetTooltipNodes"));
        Assert.Equal("System.Collections.Generic.List`1<TooltipNode>", nodes.ReturnType);
        var altNodes = Assert.Single(metadata.GetMethods("ITooltipable", "GetAltTooltipNodes"));
        Assert.Equal("System.Collections.Generic.List`1<TooltipNode>", altNodes.ReturnType);

        Assert.Equal("System.Collections.Generic.List`1<TooltipNode>",
            metadata.GetFieldType("TooltipNode", "children"));
        Assert.Equal("System.Collections.Generic.List`1<ITooltipable>",
            metadata.GetFieldType("TooltipNode", "subTooltips"));
        Assert.Equal("System.Func`1<System.String>",
            metadata.GetFieldType("TooltipNode", "textFn"));
        Assert.Equal("System.String", metadata.GetFieldType("TooltipNode", "text"));
        Assert.Equal("TooltipNode+NodeType", metadata.GetFieldType("TooltipNode", "nodeType"));
        Assert.Equal("TooltipNode+ParentType", metadata.GetFieldType("TooltipNode", "parentType"));
        Assert.Equal("ITooltipable", metadata.GetFieldType("TooltipNode", "tooltipable"));

        Assert.Equal("System.Collections.Generic.List`1<UITooltipContainer>",
            metadata.GetFieldType("UITooltipContainer", "globalTooltips"));
        Assert.Equal("ITooltipable", metadata.GetFieldType("UITooltipContainer", "item"));

        var viewText = metadata.GetField("UIViewRadioButton", "viewText");
        Assert.Equal("TMPro.TextMeshProUGUI", viewText.FieldType);
    }
}
