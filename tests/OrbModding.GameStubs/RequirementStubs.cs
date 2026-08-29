using System;

namespace Requirements;

/// <summary>
/// The threshold a condition compares against, and how it grows with the level being bought.
/// </summary>
/// <remarks>
/// Named and shaped as the game shapes it. The two modifiers are plain authored structs — a
/// modifier's identity is its own, not a pointer at a live variable — so a threshold is a pure
/// function of authored data and one level number.
/// </remarks>
public class LeveledValue
{
    public double baseValue;
    public ValueModifier perLevel;
    public ValueModifier modPerLevel;
}

/// <summary>
/// The level a per-level container is being asked about.
/// </summary>
/// <remarks>
/// A readonly struct with a single-argument constructor, as the game has it. Only the shape matters
/// here: it is what distinguishes the parameterised <c>Check</c> from the parameterless one that
/// latches.
/// </remarks>
public readonly struct ConditionInfo
{
    public ConditionInfo(long levelN) => level = levelN;

    public readonly long level;
}

/// <summary>
/// What the game's <c>[SerializeReference]</c> prerequisite list holds. Modelled as the marker it is:
/// the collector never calls it, because the <c>Visible</c> and <c>Available</c> comparisons reach a
/// <c>Check()</c> that writes.
/// </summary>
public interface IRequirementCondition
{
}

/// <summary>
/// The generic base every leaf condition derives from, with the three members the collector reads.
/// </summary>
/// <remarks>
/// Generic in the game and generic here, because that is exactly what makes the members impossible to
/// bind against one closed type: <c>item</c> and <c>reqType</c> are the base's own type parameters, so
/// accessors have to be compiled per concrete subclass.
/// </remarks>
public abstract class BaseCondition<T, TE> : IRequirementCondition
    where TE : Enum
{
    public T item = default!;
    public TE reqType = default!;
    public LeveledValue value = new LeveledValue();
}

public enum UpgradeRequirementType
{
    OneLevel,
    MaxLevel,
    AtLeast,
    Visible,
}

public enum StructureRequirementType
{
    Quantity,
    Available,
}

public enum SpellRequirementType
{
    Discovered,
    Visible,
    SpellLevel,
    MasteryLevel,
    MasteryLevelReady,
}

public enum AlchemyRecipeType
{
    Discovered,
    Visible,
    RecipeLevel,
    MasteryLevel,
    AdvLevel,
}

public enum RitualRequirementType
{
    Discovered,
    ReachedLevel,
}

public enum NumberRequirementType
{
    Value,
}

public enum GenericRequirementType
{
    Visible,
    Level,
    Discovered,
}

public enum PrerequisiteLinkType
{
    Base,
    Tier,
}

public enum ListRequirementType
{
    Count,
    AnyVisible,
    AnyAvailable,
}

public sealed class UpgradeRequirement : BaseCondition<UpgradeSO, UpgradeRequirementType>
{
}

public sealed class ResearchRequirement : BaseCondition<ResearchSO, UpgradeRequirementType>
{
}

public sealed class StructureRequirement : BaseCondition<StructureSO, StructureRequirementType>
{
}

public sealed class SpellRequirement : BaseCondition<SpellRecipeSO, SpellRequirementType>
{
}

public sealed class AlchemyRecipeRequirement : BaseCondition<AlchemyRecipeSO, AlchemyRecipeType>
{
}

public sealed class RitualRequirement : BaseCondition<RitualSO, RitualRequirementType>
{
}

/// <summary>
/// The game's item type here is the abstract <c>NumberVariable</c> that <c>IntVariable</c> and
/// <c>DoubleVariable</c> derive from. These stubs model the two registries without that base, so the
/// concrete integer variable stands in: what the collector reads off the field is the referenced
/// entity's identity, which both spell the same way.
/// </summary>
public sealed class NumberRequirement : BaseCondition<IntVariable, NumberRequirementType>
{
}

public sealed class GenericRequirement : BaseCondition<UpgradeableObject, GenericRequirementType>
{
}

public sealed class PrerequisiteLinkRequirement :
    BaseCondition<global::PrerequisiteLinkSO, PrerequisiteLinkType>
{
}

/// <summary>
/// The one condition whose subject is a whole list rather than one entity. Its item type is the
/// abstract list base, as the game declares it, so the membership behind it is only reachable
/// through the concrete class the author picked.
/// </summary>
public sealed class ListRequirement : BaseCondition<global::AbstractListVariable, ListRequirementType>
{
}

/// <summary>
/// The two native composite shapes. They pass the same condition info to every child, and their
/// nested lists remain explicit because flattening an OR into the container's implicit AND changes
/// the game's answer.
/// </summary>
public sealed class OrRequirement : IRequirementCondition
{
    public System.Collections.Generic.List<IRequirementCondition> orConditions = new();
}

public sealed class AndRequirement : IRequirementCondition
{
    public System.Collections.Generic.List<IRequirementCondition> andConditions = new();
}

/// <summary>A future native condition shape the suite deliberately has no binding for.</summary>
public sealed class UnsupportedRequirement : IRequirementCondition
{
}

/// <summary>An intentionally unmodelled leaf used to prove fail-closed publication and evaluation.</summary>
public sealed class OpaqueRequirement : IRequirementCondition
{
}
