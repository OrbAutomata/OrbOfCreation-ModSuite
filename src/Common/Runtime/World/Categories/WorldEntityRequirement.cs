using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which kind of entity a requirement row belongs to.</summary>
/// <remarks>
/// The kind travels because the level a per-level container is checked at is a property of the owner,
/// not of the condition: an upgrade asks about <c>level + queuedLevels + 1</c>, a structure asks
/// about <c>quantity</c>, and Research supplies its native effective requirement level. A consumer
/// that only knew the identity would have to guess which, or search every registry to find out.
/// </remarks>
internal enum WorldRequirementOwnerKind
{
    /// <summary>The owner's registry could not be established. No consumer may treat this as met.</summary>
    Unknown = 0,
    Upgrade = 1,
    Structure = 2,
    PrerequisiteLink = 3,
    Research = 4,
    AlchemyRecipe = 5,

    /// <summary>
    /// A Recipe Book. <c>RecipeBookSO.IsAvailable()</c> is <c>prerequisites.Check()</c> and
    /// <c>prerequisites</c> is the class's only instance field, so its single condition is the whole
    /// of what holds the book shut and the whole of what a row can say about owning one.
    /// </summary>
    /// <remarks>
    /// The retired unlocker glyph beside each book authors a container of its own, and it is not the
    /// same condition: Gloves and Herbalize hold theirs behind <c>UnobtainableResearch</c> while
    /// their books answer to <c>ArtifactGloves</c> and <c>LearnBiology</c>. Reading the glyph's would
    /// name the wrong purchase on those two, so the book's own container is what is walked.
    /// </remarks>
    RecipeBook = 6,

    /// <summary>
    /// A resource. <c>ResourceSO.CheckVisibility()</c> latches <c>visible</c> from
    /// <c>startVisible &amp;&amp; visiblePrerequisites.Check()</c>, so that container is the whole of
    /// what puts a resource in the game's resource list and the whole of what a row can say about
    /// one that is not in it yet.
    /// </summary>
    Resource = 7,
}

internal enum WorldRequirementProgramKind
{
    NextLevel = 0,
    Usage = 1,

    /// <summary>
    /// What holds the entity shut at all, rather than what its next level costs.
    /// </summary>
    /// <remarks>
    /// The game reaches these containers through the no-argument <c>Container.Check()</c>, which
    /// evaluates at level nought and adds the container's own <c>adjustValue</c> to every threshold —
    /// so the program is checked at a different level and against different numbers than the per-level
    /// one, and folding the two into one set would answer neither question.
    /// </remarks>
    Unlock = 2,
}

/// <summary>
/// Which of the game's condition classes a requirement row was read from, as this suite models them.
/// </summary>
/// <remarks>
/// The game distinguishes conditions by class rather than by a discriminator field, so this is a
/// suite-owned classification of the runtime type name rather than a mirror of a game enum. Anything
/// this suite has not been audited against reads as <see cref="Unknown"/>, which is the whole point:
/// an unmodelled condition must make a candidate inadmissible rather than silently absent.
/// </remarks>
internal enum WorldRequirementConditionKind
{
    /// <summary>
    /// A condition class this build has that this suite does not model, or one whose members did not
    /// bind. The row still carries its type name so an operator can name what was found.
    /// </summary>
    Unknown = 0,
    Upgrade = 1,
    Research = 2,
    Structure = 3,
    Spell = 4,
    AlchemyRecipe = 5,
    Ritual = 6,
    Number = 7,
    Generic = 8,
    PrerequisiteLink = 9,

    /// <summary>An authored empty composite's exact Any/All identity value.</summary>
    Literal = 10,

    /// <summary>A comparison against a whole list variable rather than against one entity.</summary>
    List = 11,

    /// <summary>
    /// A comparison about one resource: whether the game lists it yet, how much has ever been
    /// gained, or how high its ceiling has been raised.
    /// </summary>
    Resource = 12,
}

internal enum WorldRequirementNodeKind
{
    Leaf = 0,
    Group = 1,

    /// <summary>
    /// Capture could not establish this program/container. An empty OwnerId scopes all its owners;
    /// ContainerIndex -1 scopes every tier of one owner when the tier list itself was unreadable.
    /// </summary>
    CaptureGap = 2,
}

internal enum WorldRequirementOperator
{
    None = 0,
    And = 1,
    Or = 2,
}

/// <summary>
/// One of the two per-level modifiers a requirement threshold scales by, as read.
/// </summary>
/// <remarks>
/// The game's <c>LeveledValue</c> is <c>baseValue</c> plus two <c>ValueModifier</c>s, and the modifier
/// is a plain authored struct — its <c>gc</c> is its own identity, not a pointer at a live variable —
/// so the threshold is a pure function of authored data and the level being bought. That is why these
/// travel as values rather than as an identity into the global modifier registry.
/// </remarks>
internal readonly struct WorldRequirementScaling
{
    internal WorldRequirementScaling(int modifierType, BigDouble amount, int order)
    {
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
    }

    /// <summary>The game's <c>ValueModifierType</c>, as its underlying integer. See D17.</summary>
    internal int ModifierType { get; }

    /// <summary>The original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    internal int Order { get; }
}

/// <summary>
/// One authored condition or group on one entity's per-level purchase, described by what it compares
/// rather than by whether it currently holds.
/// </summary>
/// <remarks>
/// <para>
/// Upgrade and structure pipelines ask <c>prerequisitesPerLevel.Check(level)</c>; Research asks
/// <c>levelPrerequisites.Check(GetRequirementLevel())</c>. Those parameterized calls cannot be
/// published as reusable latches the way a whole-entity <c>available</c> can. The containers' contents
/// publish as the durable explanation. A separate row carries the safe parameterized native answer
/// at one exact level as a same-generation differential oracle.
/// </para>
/// <para>
/// Rows are facts, not a verdict. A consumer deciding "can I buy one more of this now" wants the
/// boolean; a consumer planning a chain wants to know that this upgrade is waiting on that research
/// reaching level six, which is a fact only the rows carry.
/// </para>
/// <para>
/// The runtime type name travels beside the modelled kind for the same reason it does on
/// <see cref="WorldEffectBlock"/>: when the kind is <see cref="WorldRequirementConditionKind.Unknown"/>
/// the name is the only thing that lets anyone say <em>what</em> was not modelled.
/// </para>
/// </remarks>
internal readonly struct WorldEntityRequirement
{
    internal WorldEntityRequirement(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        int containerIndex,
        int ordinal,
        int parentOrdinal,
        int depth,
        WorldRequirementNodeKind nodeKind,
        WorldRequirementOperator @operator,
        WorldRequirementConditionKind kind,
        string conditionTypeName,
        Guid targetId,
        int reqType,
        double baseValue,
        in WorldRequirementScaling perLevel,
        in WorldRequirementScaling modPerLevel,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        BigDouble thresholdAdjustment = default)
    {
        ThresholdAdjustment = thresholdAdjustment;
        OwnerId = ownerId;
        OwnerKind = ownerKind;
        ContainerIndex = containerIndex;
        Ordinal = ordinal;
        ParentOrdinal = parentOrdinal;
        Depth = depth;
        NodeKind = nodeKind;
        Operator = @operator;
        Kind = kind;
        ConditionTypeName = conditionTypeName;
        TargetId = targetId;
        ReqType = reqType;
        BaseValue = baseValue;
        PerLevel = perLevel;
        ModPerLevel = modPerLevel;
        Program = program;
    }

    /// <summary>
    /// The entity whose condition this is. Empty only on a CaptureGap for an entire program/container
    /// whose prerequisite identity capture was incomplete; it is scope metadata, not an entity UUID.
    /// </summary>
    internal Guid OwnerId { get; }

    internal WorldRequirementOwnerKind OwnerKind { get; }
    internal WorldRequirementProgramKind Program { get; }

    /// <summary>
    /// Zero for an entity's per-level container; otherwise the exact tier index on a
    /// <c>PrerequisiteLinkSO</c>. It is not a level guessed from list position.
    /// </summary>
    internal int ContainerIndex { get; }

    /// <summary>The condition's position in its owner's container.</summary>
    internal int Ordinal { get; }

    /// <summary>The enclosing explicit group, or -1 for a child of the container's implicit AND.</summary>
    internal int ParentOrdinal { get; }

    internal int Depth { get; }

    internal WorldRequirementNodeKind NodeKind { get; }

    internal WorldRequirementOperator Operator { get; }

    internal WorldRequirementConditionKind Kind { get; }

    /// <summary>The condition's runtime class name, as the game names it.</summary>
    internal string ConditionTypeName { get; }

    /// <summary>
    /// The entity the condition looks at, or <see cref="Guid.Empty"/> when the condition names none —
    /// which for a modelled kind means the reference was never authored.
    /// </summary>
    internal Guid TargetId { get; }

    /// <summary>
    /// Which comparison the condition makes, as the game's own enum integer. Its meaning depends on
    /// <see cref="Kind"/>: the same integer names a different comparison for each condition class.
    /// </summary>
    internal int ReqType { get; }

    /// <summary>The authored threshold before any per-level scaling.</summary>
    internal double BaseValue { get; }

    /// <summary>
    /// What the game's own evaluation adds to this row's scaled threshold.
    /// </summary>
    /// <remarks>
    /// <c>ConditionValueInstance</c> is <c>LeveledValue.AtCondition(info) + info.adjustValue</c>, and
    /// which <c>ConditionInfo</c> arrives depends on the overload the game used. The no-argument
    /// <c>Container.Check()</c> builds its own from the container's stored <c>adjustValue</c>, so an
    /// unlock row carries that number. Every other program's caller supplies the <c>ConditionInfo</c>
    /// through the implicit conversion off the level, which leaves the adjustment at nought — so nought
    /// is those rows' correct reading rather than a stand-in for an unread field.
    /// </remarks>
    internal BigDouble ThresholdAdjustment { get; }

    /// <summary>How the threshold grows with the level being bought.</summary>
    internal WorldRequirementScaling PerLevel { get; }

    /// <summary>How <see cref="PerLevel"/> itself grows, for a threshold that accelerates.</summary>
    internal WorldRequirementScaling ModPerLevel { get; }
}

/// <summary>
/// Range lookup over the requirement table, which is keyed by owner and then position.
/// </summary>
/// <remarks>
/// <see cref="WorldPurchaseCostLookup"/>'s shape and for the same reason: a condition is not an
/// entity, an owner authors several, and <see cref="WorldLookup"/> refuses duplicate identities.
/// <para>
/// A miss means the owner authored no per-level conditions, which is the overwhelmingly common case
/// and is not a degraded reading — an empty container's <c>Check</c> passes unconditionally.
/// </para>
/// </remarks>
internal static class WorldEntityRequirementLookup
{
    internal static readonly Comparison<WorldEntityRequirement> Order =
        static (left, right) =>
        {
            var byOwner = left.OwnerId.CompareTo(right.OwnerId);
            if (byOwner != 0) return byOwner;
            var byContainer = left.ContainerIndex.CompareTo(right.ContainerIndex);
            if (byContainer != 0) return byContainer;
            var byProgram = left.Program.CompareTo(right.Program);
            return byProgram != 0 ? byProgram : left.Ordinal.CompareTo(right.Ordinal);
        };

    internal static bool TryFindRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), ownerId,
            static (row, key) => row.OwnerId.CompareTo(key), out start, out count, resetOnMiss: true);
    }

    internal static bool TryFindContainerRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
        int containerIndex,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), (Owner: ownerId, Container: containerIndex),
            static (row, key) => row.OwnerId != key.Owner
                ? row.OwnerId.CompareTo(key.Owner) : row.ContainerIndex.CompareTo(key.Container),
            out start, out count, resetOnMiss: true);
    }
}
