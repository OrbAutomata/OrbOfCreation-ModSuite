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

    [Fact]
    public void ManifestNamesTheAuthoredPanelTitle()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Single(
            manifest.Contracts,
            contract => contract.Id == "modal-open.activator-title-action");
    }
}
