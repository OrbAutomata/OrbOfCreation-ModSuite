using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Which of the game's requirement comparisons <see cref="WorldRequirementEvaluator"/> answers, and
/// which it refuses, counted from the evaluator itself.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator names one <c>private const int</c> per comparison it models and branches on it; a
/// comparison it does not name falls to a handler's default arm and reads as
/// <c>Unevaluable</c>. So the refused set is every member of the game's ten discriminant enums that
/// the evaluator does not name, and this reads both sides rather than restating either: the
/// constants come off the type by reflection, the members off the mirrored enums whose numbers
/// <c>RequirementEnumContractTests</c> pins against the audited copy.
/// </para>
/// <para>
/// It exists because the count drifted silently once already: <c>world-collection-decisions.md</c>
/// W58 still said eight refusals after <c>GenericRequirement.Discovered</c> started resolving
/// through the snapshot's own <c>IsDiscovered()</c> rows. A number in prose that nothing recomputes
/// is a number that goes stale between rounds.
/// </para>
/// <para>
/// <c>ResearchRequirement</c> carries no enum of its own — it compares through
/// <c>UpgradeRequirementType</c> — so a research row's <c>Visible</c> is the same discriminant,
/// refused for the same reason, and is counted once.
/// </para>
/// </remarks>
public sealed class WorldRequirementRefusalTests
{
    /// <summary>Each discriminant enum, and the prefix the evaluator's constants for it carry.</summary>
    private static readonly (Type Enum, string Prefix)[] Discriminants =
    {
        (typeof(global::Requirements.UpgradeRequirementType), "Upgrade"),
        (typeof(global::Requirements.StructureRequirementType), "Structure"),
        (typeof(global::Requirements.SpellRequirementType), "Spell"),
        (typeof(global::Requirements.AlchemyRecipeType), "Alchemy"),
        (typeof(global::Requirements.RitualRequirementType), "Ritual"),
        (typeof(global::Requirements.NumberRequirementType), "Number"),
        (typeof(global::Requirements.GenericRequirementType), "Generic"),
        (typeof(global::Requirements.PrerequisiteLinkType), "PrerequisiteLink"),
        (typeof(global::Requirements.ListRequirementType), "List"),
        (typeof(global::Requirements.ResourceRequirementType), "Resource"),
    };

    /// <summary>The one evaluator constant that is a bound rather than a discriminant.</summary>
    private const string NotADiscriminant = "MaximumExpansionDepth";

    [Fact]
    public void TheEvaluatorRefusesExactlySevenOfTheGamesThirtyComparisons()
    {
        var named = NamedConstants();
        var modelled = new List<string>();
        var refused = new List<string>();

        foreach (var (type, prefix) in Discriminants)
        {
            var ordinals = named
                .Where(constant => constant.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(constant => constant.Value)
                .ToHashSet();
            foreach (var member in Enum.GetNames(type))
            {
                var name = type.Name + "." + member;
                var ordinal = (int)Enum.Parse(type, member);
                (ordinals.Contains(ordinal) ? modelled : refused).Add(name);
            }
        }

        Assert.Equal(
            new[]
            {
                "UpgradeRequirementType.Visible",
                "StructureRequirementType.Available",
                "SpellRequirementType.Visible",
                "SpellRequirementType.MasteryLevelReady",
                "AlchemyRecipeType.Visible",
                "GenericRequirementType.Visible",
                "ListRequirementType.Count",
            },
            refused);
        Assert.Equal(23, modelled.Count);
        Assert.Equal(30, modelled.Count + refused.Count);
    }

    /// <summary>
    /// Every discriminant constant belongs to one of the ten enums above, so a comparison modelled
    /// under a name this sweep does not recognise fails here rather than being counted as refused.
    /// </summary>
    [Fact]
    public void EveryDiscriminantTheEvaluatorNamesBelongsToOneOfTheGamesEnums()
    {
        var unclaimed = NamedConstants().Keys
            .Where(name => !Discriminants.Any(
                pair => name.StartsWith(pair.Prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { NotADiscriminant }, unclaimed);
    }

    private static IReadOnlyDictionary<string, int> NamedConstants() =>
        typeof(WorldRequirementEvaluator)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);
}
