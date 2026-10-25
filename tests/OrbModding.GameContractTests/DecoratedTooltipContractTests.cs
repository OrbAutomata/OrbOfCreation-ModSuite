using Xunit;
using static OrbModding.GameContractTests.MethodBodyReferences;

namespace OrbModding.GameContractTests;

/// <summary>
/// A panel that builds its tooltip out of several facts hands the screen an
/// <c>OverwriteTooltip</c> around the entity rather than the entity itself. The wrapper forwards
/// the name, so a row's name never gave the substitution away — only its missing id did.
/// </summary>
public sealed class DecoratedTooltipContractTests
{
    [GameAssemblyFact]
    public void TheDecoratorKeepsTheEntityItWasBuiltAround()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("ITooltipable", assembly.GetFieldType("OverwriteTooltip", "tooltipable"));
        Assert.Contains(
            References(assembly, "OverwriteTooltip", "GetName"),
            reference => reference.DeclaringType == "OverwriteTooltip" &&
                reference.MemberName == "tooltipable");
    }

    [GameAssemblyFact]
    public void TheGlyphListWrapsItsOwnGlyphToAddTheEquippedQuantity()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var tooltip = References(assembly, "UIGlyphListItem", "GetTotalModifierTooltip");
        Assert.Contains(tooltip, reference =>
            reference.DeclaringType == "OverwriteTooltip" && reference.MemberName == ".ctor");
        Assert.Contains(tooltip, reference => reference.MemberName == "item");

        var setup = References(assembly, "UIGlyphListItem", "PostSetup");
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "UIGlyphListItem" &&
            reference.MemberName == "GetTotalModifierTooltip");
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "HoverTooltip" && reference.MemberName == "Setup");
    }

    /// <summary>
    /// A decorator's own node list is either a list it was handed or a function it was handed, and
    /// the panel draws whichever was set ahead of the entity's own list. That is what makes a
    /// wrapper around a glossary word a panel of its own rather than the word's definition.
    /// </summary>
    [GameAssemblyFact]
    public void TheDecoratorDrawsTheNodeListItWasHandedAheadOfTheEntitys()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var draw = References(assembly, "OverwriteTooltip", "GetTooltipNodes");
        var handed = Assert.Single(draw, reference =>
            reference.DeclaringType == "OverwriteTooltip" && reference.MemberName == "tooltipNodes");
        var computed = Assert.Single(draw, reference =>
            reference.DeclaringType == "OverwriteTooltip" &&
            reference.MemberName == "overwriteTooltipNodes");
        var own = Assert.Single(draw, reference =>
            reference.DeclaringType == "ITooltipable" && reference.MemberName == "GetTooltipNodes");
        Assert.True(handed.Offset < computed.Offset && computed.Offset < own.Offset);

        Assert.Contains(
            assembly.GetMethodBodyDefinitionReferences(
                "OverwriteTooltip", "SetTooltipNodes", "System.Collections.Generic.List`1<TooltipNode>"),
            reference => reference.MemberName == "tooltipNodes");
        Assert.Contains(
            assembly.GetMethodBodyDefinitionReferences(
                "OverwriteTooltip",
                "SetTooltipNodes",
                "System.Func`1<System.Collections.Generic.List`1<TooltipNode>>"),
            reference => reference.MemberName == "overwriteTooltipNodes");
    }

    /// <summary>
    /// Those numbers are built here and nowhere else.
    /// </summary>
    [GameAssemblyFact]
    public void AChanceRowHangsTheRatingChangeWordFilledWithItsNumbersBesideTheStatistic()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var preview = References(assembly, "RatingReport", "CreateRatingUpgradeTooltip");
        Assert.Contains(preview, reference =>
            reference.DeclaringType == "GlobalVariables" && reference.MemberName == "GetAttribute");
        Assert.Contains(preview, reference =>
            reference.DeclaringType == "OverwriteTooltip" && reference.MemberName == ".ctor");
        Assert.Contains(preview, reference =>
            reference.DeclaringType == "OverwriteTooltip" && reference.MemberName == "SetTooltipNodes");
        Assert.Equal("AttributeSO", Assert.Single(assembly.GetMethods("GlobalVariables", "GetAttribute")).ReturnType);

        var row = References(assembly, "NumberVariable+PersistentEffect", "GetTooltipNodes");
        Assert.Contains(row, reference =>
            reference.DeclaringType == "RatingReport" &&
            reference.MemberName == "CreateRatingUpgradeTooltip");
        Assert.Contains(row, reference =>
            reference.DeclaringType == "TooltipNode" && reference.MemberName == "AddSubTooltipable");
    }

    [Fact]
    public void ManifestNamesTheDecoratorsOwnNodeLists()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Single(
            manifest.Contracts,
            contract => contract.Id == "game-mcp-tooltip-panel.overwrite-tooltip-nodes-action");
        Assert.Single(
            manifest.Contracts,
            contract => contract.Id == "game-mcp-tooltip-panel.overwrite-tooltip-node-source-action");
    }

    [Fact]
    public void ManifestNamesTheDecoratorsEntity()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Single(
            manifest.Contracts,
            contract => contract.Id == "game-mcp-tooltip-catalog.overwrite-tooltipable-action");
    }
}
