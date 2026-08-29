using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class RitualLifecycleContractTests
{
    [GameAssemblyFact]
    public void RitualListSelectionUsesTheVisibleToggleControl()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002658,
            assembly.GetMethodToken("UIRitualList", "ClickRitual", "RitualSO"));
        var references = References(assembly, "UIRitualList", "ClickRitual", "RitualSO");
        Assert.Contains(references, reference =>
            reference.DeclaringType == "AbstractVariable`1<RitualSO>" &&
            reference.MemberName == "ToggleValue");
    }

    [GameAssemblyFact]
    public void RitualStartingLevelUsesTheVisibleJumpStartControl()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600264E,
            assembly.GetMethodToken("UIRitual", "SetJumpStart", "System.Int32"));
        Assert.Contains(References(assembly, "UIRitual", "SetJumpStart", "System.Int32"),
            reference => reference.DeclaringType == "RitualSO" &&
                         reference.MemberName == "ChangeStartingLevel");
        Assert.Equal(0x06001367,
            assembly.GetMethodToken("RitualSO", "ChangeStartingLevel", "System.Int32"));
    }

    [GameAssemblyFact]
    public void RitualActivationButtonPricesAndPaysBeforeItsNativeCallback()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600264C,
            assembly.GetMethodToken("UIRitual", "RenderActivationButton"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIRitual", "RenderActivationButton", "RitualSO", "GetActivationCost"));
        Assert.Equal(0x06002204, assembly.GetMethodToken("UICostButton", "OnClick"));
        var references = References(assembly, "UICostButton", "OnClick");
        var payment = Offset(references, "ResourceCostList", "PerformCost");
        var callback = references.Where(reference => reference.MemberName == "Invoke")
            .Select(reference => reference.Offset)
            .DefaultIfEmpty(-1)
            .Max();
        Assert.True(payment >= 0);
        Assert.True(callback > payment);
    }

    [GameAssemblyFact]
    public void RitualActivationAndDurationCancellationOwnDifferentVisibleOutcomes()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x060006BF,
            assembly.GetMethodToken("RitualManager", "ActivateSelectedRitual"));
        Assert.Contains(References(assembly, "RitualManager", "ActivateSelectedRitual"),
            reference => reference.DeclaringType == "RitualManager" &&
                         reference.MemberName == "StartRitual");
        Assert.Contains(References(assembly, "RitualManager", "StartRitual", "RitualSO"),
            reference => reference.DeclaringType == "BattleManager" &&
                         reference.MemberName == "StartRitual");
        Assert.Equal(0x0600048E,
            assembly.GetMethodToken("BattleManager", "StartRitual", "RitualSO"));
        Assert.True(assembly.MethodReferencesMethod(
            "BattleManager", "StartRitual", "RitualSO", "Initiate"));

        Assert.Equal(0x06002653, assembly.GetMethodToken("UIRitual", "CancelRitual"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIRitual", "CancelRitual", "RitualSO", "Cancel"));
        Assert.Equal(0x06001369, assembly.GetMethodToken("RitualSO", "Cancel"));
    }

    [GameAssemblyFact]
    public void EndRitualClearsTheActiveBattleAfterOwningTheNativeOutcome()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "BattleManager", "EndRitual", "RitualSO", "End"));
        Assert.Contains(References(assembly, "BattleManager", "EndRitual"),
            reference => reference.DeclaringType == "AbstractVariable`1<RitualSO>" &&
                         reference.MemberName == "Clear");
        Assert.Contains(References(assembly, "BattleManager", "IsInCombat"),
            reference => reference.DeclaringType == "UntypedAbstractVariable" &&
                         reference.MemberName == "HasValue");
    }

    [Fact]
    public void ManifestNamesTheCompleteRitualLifecycleBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "ritual-lifecycle.ritual.type-action", "ritual-lifecycle.manager.type-action",
            "ritual-lifecycle.variable.type-action", "ritual-lifecycle.cost.type-action",
            "ritual-lifecycle.tuple.type-action", "ritual-lifecycle.resource.type-action",
            "ritual-lifecycle.battle-manager.type-action", "ritual-lifecycle.manager-instance-action",
            "ritual-lifecycle.manager-selected-action", "ritual-lifecycle.variable-toggle-action",
            "ritual-lifecycle.variable-is-item-action", "ritual-lifecycle.ritual-discovered-action",
            "ritual-lifecycle.ritual-force-level-action", "ritual-lifecycle.ritual-force-level-value-action",
            "ritual-lifecycle.ritual-selected-level-action", "ritual-lifecycle.ritual-max-selected-level-action",
            "ritual-lifecycle.ritual-change-level-action", "ritual-lifecycle.ritual-activation-cost-action",
            "ritual-lifecycle.cost-has-enough-action", "ritual-lifecycle.cost-perform-action",
            "ritual-lifecycle.cost-entries-action", "ritual-lifecycle.tuple-resource-action",
            "ritual-lifecycle.tuple-value-action", "ritual-lifecycle.resource-guid-action",
            "ritual-lifecycle.resource-has-amount-action", "ritual-lifecycle.manager-activate-action",
            "ritual-lifecycle.battle-manager-instance-action", "ritual-lifecycle.battle-active-action",
            "ritual-lifecycle.ritual-in-battle-action", "ritual-lifecycle.ritual-duration-kind-action",
            "ritual-lifecycle.ritual-duration-active-action", "ritual-lifecycle.ritual-cancel-action",
            "ritual-lifecycle.battle-active-ritual-action", "ritual-lifecycle.battle-end-ritual-action",
            "ritual-lifecycle.ritual-usage-requirements-capture",
            "ritual.completion-cost", "ritual.completion-cost-per-level",
        };

        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }

    /// <summary>
    /// The published starting-level floor is a suite constant, and this is what binds it to the
    /// control it quotes.
    /// </summary>
    /// <remarks>
    /// <c>RitualSO.ChangeStartingLevel</c> accepts 0; only the screen's value selector stops at 1,
    /// so the floor cannot be read from the setter behind it. That selector clamps in
    /// <c>UIValueSelectButton.SetClamp(min, max)</c>, which stores <c>minValue</c>, and
    /// <c>Decrement()</c> is <c>Math.Max(value - GetChange(), minValue)</c> — the floor is that
    /// stored minimum and nothing else. A game update that renamed the call, changed its arity, or
    /// stopped the decrement consulting <c>minValue</c> would leave the wire advertising a bound no
    /// control enforces; this is the assertion that fails first instead.
    /// </remarks>
    [GameAssemblyFact]
    public void TheStartingLevelFloorIsTheValueSelectorsOwnStoredMinimum()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Contains(
            assembly.GetMethods("UIValueSelectButton"),
            method => method.Name == "SetClamp" &&
                method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "UIValueSelectButton" &&
                method.ParameterTypes.SequenceEqual(
                    new[] { "System.Int32", "System.Int32" }));
        Assert.Equal("System.Int32", assembly.GetFieldType("UIValueSelectButton", "minValue"));
        Assert.Contains(
            References(assembly, "UIValueSelectButton", "SetClamp", "System.Int32", "System.Int32"),
            reference => reference.DeclaringType == "UIValueSelectButton" &&
                         reference.MemberName == "minValue");
        Assert.Contains(References(assembly, "UIValueSelectButton", "Decrement"),
            reference => reference.DeclaringType == "UIValueSelectButton" &&
                         reference.MemberName == "minValue");

        Assert.Contains(References(assembly, "UIRitual", "RenderContent"),
            reference => reference.DeclaringType == "UIValueSelectButton" &&
                         reference.MemberName == "SetClamp");
        Assert.True(assembly.MethodReferencesMethod(
            "UIRitual", "RenderContent", "RitualSO", "GetMaxSelectedLevel"));
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method,
        params string[] parameterTypes) =>
        assembly.GetMethodBodyDefinitionReferences(type, method, parameterTypes)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method, parameterTypes))
            .OrderBy(reference => reference.Offset)
            .ToArray();

    private static int Offset(
        MethodBodyDefinitionReference[] references,
        string type,
        string member) =>
        references.Where(reference =>
                reference.DeclaringType.StartsWith(type, StringComparison.Ordinal) &&
                reference.MemberName == member)
            .Select(reference => reference.Offset)
            .DefaultIfEmpty(-1)
            .Min();
}
