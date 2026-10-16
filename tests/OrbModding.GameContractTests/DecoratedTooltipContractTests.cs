using System.Linq;
using Xunit;

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
            assembly.GetMethodBodyDefinitionReferences("OverwriteTooltip", "GetName")
                .Concat(assembly.GetMethodBodyMemberReferences("OverwriteTooltip", "GetName")),
            reference => reference.DeclaringType == "OverwriteTooltip" &&
                reference.MemberName == "tooltipable");
    }

    /// <summary>
    /// The glyph list is the case a live round caught: every icon on the loadout page is a glyph
    /// the world publishes, and every one of them reached the wire with no id on it.
    /// </summary>
    [GameAssemblyFact]
    public void TheGlyphListWrapsItsOwnGlyphToAddTheEquippedQuantity()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var tooltip = assembly
            .GetMethodBodyDefinitionReferences("UIGlyphListItem", "GetTotalModifierTooltip")
            .Concat(assembly.GetMethodBodyMemberReferences(
                "UIGlyphListItem", "GetTotalModifierTooltip"))
            .ToArray();
        Assert.Contains(tooltip, reference =>
            reference.DeclaringType == "OverwriteTooltip" && reference.MemberName == ".ctor");
        Assert.Contains(tooltip, reference => reference.MemberName == "item");

        var setup = assembly.GetMethodBodyDefinitionReferences("UIGlyphListItem", "PostSetup")
            .Concat(assembly.GetMethodBodyMemberReferences("UIGlyphListItem", "PostSetup"))
            .ToArray();
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "UIGlyphListItem" &&
            reference.MemberName == "GetTotalModifierTooltip");
        Assert.Contains(setup, reference =>
            reference.DeclaringType == "HoverTooltip" && reference.MemberName == "Setup");
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
