using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The rule behind every printed magnitude, pinned member by member against the audited copy so
/// the suite's mirror of it cannot silently fall out of step with the screen.
/// </summary>
/// <remarks>
/// <para>
/// The suite mirrors <c>ValueModifier.ToStringValue</c> and the
/// <c>Utils.BeautifyNumber</c> chain under it rather than calling them:
/// the chain ends in <c>SettingsManager.GetNumberDisplayOption</c>, which asks
/// <c>UnityEngine.Application.isPlaying</c>, so there is no process outside the game in which the
/// game's own answer can be obtained and compared. What can be proved from here is the rule, and
/// the rule is what the mirror reproduces: which members the body calls, in which order, and which
/// branch each one is.
/// </para>
/// <para>
/// The per-branch output the mirror produces from that rule is pinned in the portable gate by
/// <c>GameNumberSpellingTests</c> and <c>GameModifierSpellingTests</c>. Together they are the two
/// halves: this says the game still computes it this way, those say the suite still spells it that
/// way.
/// </para>
/// </remarks>
public sealed class NumberSpellingContractTests
{
    /// <summary>
    /// The three-argument overload is the one with a decimal threshold, and it spends that
    /// threshold exactly where the mirror does: a recursion for the sign, then
    /// <c>HasDecimals</c> and <c>ToDecimalPlace</c> for the window just above the threshold, then
    /// the notation switch for everything else.
    /// </summary>
    [GameAssemblyFact]
    public void TheThresholdOverloadSpendsItsThresholdWhereTheMirrorDoes()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var overload = Assert.Single(
            assembly.GetMethods("Utils", "BeautifyNumber"),
            method => method.ParameterTypes.SequenceEqual(
                new[] { "BigDouble", "System.Boolean", "BigDouble" }));
        Assert.Equal("System.String", overload.ReturnType);
        Assert.True(overload.IsStatic);

        Assert.Equal(
            new[]
            {
                "Utils.BeautifyNumber",
                "Utils.HasDecimals",
                "Utils.ToDecimalPlace",
                "Utils.BeautifyNumberSwitch",
            },
            assembly.GetMethodBodyDefinitionReferences(
                    "Utils", "BeautifyNumber", "BigDouble", "System.Boolean", "BigDouble")
                .Where(reference => reference.DeclaringType == "Utils")
                .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                .ToArray());
    }

    /// <summary>
    /// The notation the suite pins its own settings to is still one of the switch's arms, and it is
    /// still the arm that narrows a number's decimals as the number grows.
    /// </summary>
    /// <remarks>
    /// <c>AgentSettingsNormalization</c> writes <c>Scientific</c> into the game's own
    /// <c>numDisplay</c>, so this one arm is the whole of what the mirror has to reproduce. If the
    /// switch stopped dispatching to it, the mirror would be spelling numbers the screen no longer
    /// spells that way, and nothing else would say so.
    /// </remarks>
    [GameAssemblyFact]
    public void TheNotationTheSuitePinsStillNarrowsItsDecimals()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "Utils", "BeautifyNumberSwitch", "Utils", "BeautifyNumberScientific"));
        Assert.True(assembly.MethodReferencesMethod(
            "Utils", "BeautifyNumberSwitch", "SettingsManager", "GetNumberDisplayOption"));

        Assert.True(assembly.MethodReferencesMethod(
            "Utils", "BeautifyNumberScientific", "Utils", "BeautifyNumberSimplify"));

        // The coarse predicate and the four widths: the simplifier is where `x0.700` comes from,
        // and `HasDecimalsOld` is the gate that sends a whole number past all four of them.
        Assert.Equal(
            new[] { "Utils.HasDecimalsOld", "Utils.ToDecimalPlace" },
            assembly.GetMethodBodyDefinitionReferences("Utils", "BeautifyNumberSimplify")
                .Where(reference => reference.DeclaringType == "Utils")
                .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                .Distinct()
                .ToArray());
    }

    /// <summary>
    /// The five kinds are still five, and each still reaches the number rule through the overload
    /// the mirror gives it — the two multiplicative kinds with a threshold, the other three
    /// without.
    /// </summary>
    [GameAssemblyFact]
    public void EachModifierKindStillReachesTheNumberRuleTheMirrorGivesIt()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            new[] { "Raw", "MultiDiminishing", "MultiStacking", "Reduction", "Exponent" },
            assembly.GetInt32EnumMembers("ValueModifier+ValueModifierType")
                .OrderBy(member => member.Value)
                .Select(member => member.Key)
                .ToArray());

        var spelling = Assert.Single(assembly.GetMethods("ValueModifier", "ToStringValue"));
        Assert.Equal("System.String", spelling.ReturnType);
        Assert.False(spelling.IsStatic);

        Assert.True(assembly.MethodReferencesField(
            "ValueModifier", "ToStringValue", "ValueModifier", "type"));
        Assert.True(assembly.MethodReferencesField(
            "ValueModifier", "ToStringValue", "ValueModifier", "adjustReal"));

        // Three kinds take the two-argument overload, which carries no threshold; the `x` and `^`
        // kinds take the three-argument one and pass a threshold of exactly one. Both overloads
        // are reached and nothing else in Utils is, so the number rule is the only thing between
        // an ordinal and a printed magnitude.
        var tokens = assembly.GetMethodBodyDefinitionReferences("ValueModifier", "ToStringValue")
            .Where(reference => reference.DeclaringType == "Utils")
            .Select(reference => reference.Token)
            .ToArray();
        Assert.Equal(
            new[]
            {
                assembly.GetMethodToken("Utils", "BeautifyNumber", "BigDouble", "System.Boolean"),
                assembly.GetMethodToken(
                    "Utils", "BeautifyNumber", "BigDouble", "System.Boolean", "BigDouble"),
            }.OrderBy(token => token).ToArray(),
            tokens.OrderBy(token => token).ToArray());
    }

    /// <summary>
    /// The overload every ordinary magnitude on the screen goes through carries no threshold of
    /// its own: it hands the three-argument rule a zero, so a plain number is the notation branch
    /// and nothing else. The suite's renderer is that overload, and the duration text the same
    /// screens draw reaches it too.
    /// </summary>
    [GameAssemblyFact]
    public void TheOverloadAPlainMagnitudeTakesCarriesNoThresholdOfItsOwn()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var magnitude = Assert.Single(
            assembly.GetMethods("Utils", "BeautifyNumber"),
            method => method.ParameterTypes.SequenceEqual(new[] { "BigDouble" }));
        Assert.Equal("System.String", magnitude.ReturnType);
        Assert.True(magnitude.IsStatic);

        Assert.Equal(
            new[]
            {
                assembly.GetMethodToken(
                    "Utils", "BeautifyNumber", "BigDouble", "System.Boolean", "BigDouble"),
            },
            assembly.GetMethodBodyDefinitionReferences("Utils", "BeautifyNumber", "BigDouble")
                .Where(reference => reference.DeclaringType == "Utils")
                .Select(reference => reference.Token)
                .ToArray());

        Assert.Contains(
            assembly.GetMethodToken("Utils", "BeautifyNumber", "BigDouble"),
            assembly.GetMethodBodyDefinitionReferences("Utils", "BeautifyTimeUltraPrecise")
                .Select(reference => reference.Token));
    }

    /// <summary>
    /// The notation the screen draws is a setting the game owns, and the getter behind it is
    /// unreadable anywhere but the player loop — which is why the suite writes the option rather
    /// than capturing it, and why the mirror implements one of the switch's five arms.
    /// </summary>
    /// <remarks>
    /// <c>GetNumberDisplayOption</c> opens on <c>UnityEngine.Application.isPlaying</c> and reaches
    /// the setting through <c>SettingsManager.instance</c>, a Unity object, so a pass that asked it
    /// off the player loop would be told <c>Named</c> whatever the player had chosen. The five arms
    /// are pinned as a set: a sixth notation, or a renamed arm, is a screen the mirror no longer
    /// spells and nothing else would say so.
    /// </remarks>
    [GameAssemblyFact]
    public void TheNotationIsASettingOnlyThePlayerLoopCanRead()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesField(
            "SettingsManager", "GetNumberDisplayOption", "SettingsManager", "instance"));
        Assert.True(assembly.MethodReferencesField(
            "SettingsManager", "GetNumberDisplayOption", "SettingsManager", "numDisplay"));
        Assert.True(assembly.MethodReferencesMethod(
            "SettingsManager", "GetNumberDisplayOption", "StringVariable", "GetValue"));
        Assert.Contains(
            "get_isPlaying",
            assembly.GetMethodBodyMemberReferences("SettingsManager", "GetNumberDisplayOption")
                .Select(reference => reference.MemberName));

        Assert.Equal(
            new[]
            {
                "Utils.BeautifyNumberNamed",
                "Utils.BeautifyNumberCompact",
                "Utils.BeautifyNumberCompactNumerical",
                "Utils.BeautifyNumberScientific",
                "Utils.BeautifyNumberEngineering",
            },
            assembly.GetMethodBodyDefinitionReferences("Utils", "BeautifyNumberSwitch")
                .Where(reference => reference.DeclaringType == "Utils")
                .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                .ToArray());
    }
}
