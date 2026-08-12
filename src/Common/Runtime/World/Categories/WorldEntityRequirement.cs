using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// A glyph the game unlocks rather than offers for discovery. <c>GlyphSO.IsAvailable()</c> returns
    /// <c>discovered</c> when <c>discoverable</c> is set and <c>prerequisites.Check()</c> otherwise, so
    /// only the second population authors a container here, and its single condition is the whole of
    /// what holds that glyph shut.
    /// </summary>
    Glyph = 6,
}

internal enum WorldRequirementProgramKind
{
    NextLevel = 0,
    Usage = 1,
}

/// <summary>How the leaves at one top-level container position combine.</summary>
internal enum WorldRequirementGroupKind
{
    /// <summary>A leaf or an explicit AndRequirement: every row must hold.</summary>
    All = 0,

    /// <summary>An explicit OrRequirement: one row must hold.</summary>
    Any = 1,
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
}

internal enum WorldRequirementNodeKind
{
    Leaf = 0,
    Group = 1,
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
        int ordinal,
        WorldRequirementConditionKind kind,
        string conditionTypeName,
        Guid targetId,
        int reqType,
        double baseValue,
        in WorldRequirementScaling perLevel,
        in WorldRequirementScaling modPerLevel,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        WorldRequirementGroupKind groupKind = WorldRequirementGroupKind.All,
        int groupOrdinal = -1)
        : this(
            ownerId,
            ownerKind,
            containerIndex: 0,
            ordinal,
            parentOrdinal: -1,
            depth: 0,
            WorldRequirementNodeKind.Leaf,
            WorldRequirementOperator.None,
            kind,
            conditionTypeName,
            targetId,
            reqType,
            baseValue,
            in perLevel,
            in modPerLevel,
            program,
            groupKind,
            groupOrdinal)
    {
    }

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
        WorldRequirementGroupKind groupKind = WorldRequirementGroupKind.All,
        int groupOrdinal = -1)
    {
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
        GroupKind = groupKind;
        GroupOrdinal = groupOrdinal < 0 ? ordinal : groupOrdinal;
    }

    /// <summary>The entity whose next level this condition gates.</summary>
    internal Guid OwnerId { get; }

    internal WorldRequirementOwnerKind OwnerKind { get; }
    internal WorldRequirementProgramKind Program { get; }

    /// <summary>The native fold for the leaves at <see cref="GroupOrdinal"/>.</summary>
    internal WorldRequirementGroupKind GroupKind { get; }

    /// <summary>
    /// The top-level container position this leaf belongs to. The container ANDs positions; rows
    /// sharing one position are the children of an explicit Or/And composite.
    /// </summary>
    internal int GroupOrdinal { get; }

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
    internal static bool TryFindRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].OwnerId.CompareTo(ownerId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].OwnerId == ownerId) count++;
        return true;
    }

    internal static bool TryFindContainerRange(
        PublicationTable<WorldEntityRequirement> table,
        Guid ownerId,
        int containerIndex,
        out int start,
        out int count)
    {
        if (!TryFindRange(table, ownerId, out var ownerStart, out var ownerCount))
        {
            start = 0;
            count = 0;
            return false;
        }

        var rows = table.AsSpan();
        start = ownerStart;
        var ownerEnd = ownerStart + ownerCount;
        while (start < ownerEnd && rows[start].ContainerIndex < containerIndex) start++;
        if (start >= ownerEnd || rows[start].ContainerIndex != containerIndex)
        {
            start = 0;
            count = 0;
            return false;
        }

        count = 0;
        while (start + count < ownerEnd && rows[start + count].ContainerIndex == containerIndex)
            count++;
        return true;
    }
}

/// <summary>Every authored per-level condition and group as read, held where a cycle can own them.</summary>
internal sealed class WorldEntityRequirementBuffer
{
    private const int InitialCapacity = 32;

    private WorldEntityRequirement[] _samples = new WorldEntityRequirement[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldEntityRequirement this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldEntityRequirement sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>Publishes the requirement readings, sorted by owner and then position.</summary>
internal static class WorldEntityRequirementDeriver
{
    internal static PublicationTable<WorldEntityRequirement> Build(WorldEntityRequirementBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldEntityRequirement>.Empty;

        var derived = new WorldEntityRequirement[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, RequirementComparer.ByOwnerThenOrdinal);
        return PublicationTable<WorldEntityRequirement>.Create(derived, derived.Length);
    }

    private sealed class RequirementComparer : IComparer<WorldEntityRequirement>
    {
        internal static readonly IComparer<WorldEntityRequirement> ByOwnerThenOrdinal =
            new RequirementComparer();

        public int Compare(WorldEntityRequirement left, WorldEntityRequirement right)
        {
            var byOwner = left.OwnerId.CompareTo(right.OwnerId);
            if (byOwner != 0) return byOwner;
            var byContainer = left.ContainerIndex.CompareTo(right.ContainerIndex);
            return byContainer != 0 ? byContainer : left.Ordinal.CompareTo(right.Ordinal);
        }
    }
}

/// <summary>
/// One position in a list variable an authored <c>ListRequirement</c> compares against.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is over a whole list — <c>GetCount()</c>, <c>IsAnyVisible()</c>, or
/// <c>IsAnyAvailable()</c> — so what a snapshot needs is the membership, not one edge. Each member's
/// own visibility is already a per-pass row in this same snapshot, which is what lets the fold be
/// arithmetic over published facts rather than a native sweep.
/// </para>
/// <para>
/// A <see cref="Position"/> of -1 is the list's header row: it says the list was read and its
/// membership is authored rather than played into. Without it an empty list and an unread one would
/// be the same absence, and only one of those may be answered.
/// </para>
/// </remarks>
internal readonly struct WorldRequirementListMember
{
    internal const int HeaderPosition = -1;

    internal WorldRequirementListMember(Guid listId, int position, Guid memberId)
    {
        ListId = listId;
        Position = position;
        MemberId = memberId;
    }

    internal Guid ListId { get; }

    /// <summary>The index in the list's own order, or -1 for the header row.</summary>
    internal int Position { get; }

    /// <summary>
    /// The member at that position, or <see cref="Guid.Empty"/> for a null element — which the
    /// game's own <c>isinst</c> folds as neither visible nor available rather than skipping.
    /// </summary>
    internal Guid MemberId { get; }
}

internal static class WorldRequirementListLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldRequirementListMember> table,
        Guid listId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;
        if (listId == Guid.Empty) return false;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].ListId.CompareTo(listId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].ListId == listId) count++;
        return true;
    }
}

internal sealed class WorldRequirementListBuffer
{
    private WorldRequirementListMember[] _samples = new WorldRequirementListMember[32];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldRequirementListMember this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldRequirementListMember sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldRequirementListDeriver
{
    internal static PublicationTable<WorldRequirementListMember> Build(
        WorldRequirementListBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldRequirementListMember>.Empty;

        var rows = new WorldRequirementListMember[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var byList = left.ListId.CompareTo(right.ListId);
            return byList != 0 ? byList : left.Position.CompareTo(right.Position);
        });
        return PublicationTable<WorldRequirementListMember>.Create(rows, rows.Length);
    }
}

/// <summary>The volatile native gates around one authored prerequisite-link tier.</summary>
internal readonly struct WorldPrerequisiteLinkTier
{
    internal WorldPrerequisiteLinkTier(
        Guid linkId,
        int tierIndex,
        bool activeEnabled,
        bool passiveEnabled,
        long evaluatedFrame,
        long collectedFrame)
    {
        LinkId = linkId;
        TierIndex = tierIndex;
        ActiveEnabled = activeEnabled;
        PassiveEnabled = passiveEnabled;
        EvaluatedFrame = evaluatedFrame;
        CollectedFrame = collectedFrame;
    }

    internal Guid LinkId { get; }
    internal int TierIndex { get; }
    internal bool ActiveEnabled { get; }
    internal bool PassiveEnabled { get; }
    internal long EvaluatedFrame { get; }
    internal long CollectedFrame { get; }
    internal bool EvaluatedThisFrame => EvaluatedFrame == CollectedFrame;
}

internal static class WorldPrerequisiteLinkTierLookup
{
    internal static bool TryFind(
        PublicationTable<WorldPrerequisiteLinkTier> table,
        Guid linkId,
        int tierIndex,
        out WorldPrerequisiteLinkTier row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            ref readonly var candidate = ref rows[middle];
            var comparison = candidate.LinkId.CompareTo(linkId);
            if (comparison == 0) comparison = candidate.TierIndex.CompareTo(tierIndex);
            if (comparison == 0)
            {
                row = candidate;
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

internal sealed class WorldPrerequisiteLinkTierBuffer
{
    private WorldPrerequisiteLinkTier[] _samples = new WorldPrerequisiteLinkTier[64];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldPrerequisiteLinkTier this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldPrerequisiteLinkTier sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldPrerequisiteLinkTierDeriver
{
    internal static PublicationTable<WorldPrerequisiteLinkTier> Build(
        WorldPrerequisiteLinkTierBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPrerequisiteLinkTier>.Empty;

        var rows = new WorldPrerequisiteLinkTier[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var byLink = left.LinkId.CompareTo(right.LinkId);
            return byLink != 0 ? byLink : left.TierIndex.CompareTo(right.TierIndex);
        });
        return PublicationTable<WorldPrerequisiteLinkTier>.Create(rows, rows.Length);
    }
}

/// <summary>
/// The game's own parameterized per-level prerequisite verdict for one structure or upgrade,
/// captured beside the authored graph in the same world generation.
/// </summary>
/// <remarks>
/// <c>Container.Check(ConditionInfo)</c> is the safe oracle: unlike the parameterless overload it
/// neither stamps a frame nor latches <c>available</c>. Keeping its exact input level is essential;
/// the two owner families intentionally ask different questions.
/// </remarks>
internal readonly struct WorldRequirementNativeVerdict
{
    internal WorldRequirementNativeVerdict(
        Guid entityId,
        WorldRequirementOwnerKind ownerKind,
        long checkLevel,
        bool met)
    {
        EntityId = entityId;
        OwnerKind = ownerKind;
        CheckLevel = checkLevel;
        Met = met;
    }

    internal Guid EntityId { get; }
    internal WorldRequirementOwnerKind OwnerKind { get; }
    internal long CheckLevel { get; }
    internal bool Met { get; }
}

/// <summary>
/// Reads the live active and passive cache gates around prerequisite-link tiers without calling the
/// native <c>IsEnabled()</c>, whose passive branch can latch prerequisite state.
/// </summary>
internal sealed class WorldPrerequisiteLinkTierReader : IWorldCategoryReader
{
    private readonly Type? _linkType;
    private readonly Func<IList?>? _links;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<object, IList?>? _tiers;
    private readonly Func<object, bool>? _activeEnabled;
    private readonly Func<object, bool>? _passiveEnabled;
    private readonly Func<object, long>? _evaluatedFrame;
    private readonly Func<long>? _currentFrame;
    private readonly string _unavailable;

    internal WorldPrerequisiteLinkTierReader(Type? linkType, Type? gameManagerType)
    {
        _linkType = linkType;
        if (linkType is null || gameManagerType is null)
        {
            _unavailable = linkType is null
                ? "the PrerequisiteLinkSO type was not found on this build"
                : "the GameManager type was not found on this build";
            return;
        }

        var link = new WorldMemberBinding(linkType, "PrerequisiteLinkSO");
        _links = NativeAccessorBinder.StaticListAccessor(linkType, "All");
        _identity = link.Call<Guid>("GetGuid");
        _tiers = NativeAccessorBinder.CollectionField(linkType, "linkTiers");
        var definitionType = linkType.GetNestedType(
            "LinkDefinition", BindingFlags.Public | BindingFlags.NonPublic);
        _activeEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isActiveEnabled");
        _passiveEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isPassiveEnabled");
        _evaluatedFrame = NativeAccessorBinder.Field<long>(definitionType, "currentFrame");
        _currentFrame = NativeAccessorBinder.StaticField<long>(gameManagerType, "currentFrame");

        _unavailable = _links is null || _identity is null || _tiers is null ||
            _activeEnabled is null || _passiveEnabled is null || _evaluatedFrame is null ||
            _currentFrame is null
                ? "PrerequisiteLinkSO tiers did not expose their complete live gate state on this build"
                : link.Failure;
    }

    public string Category => "prerequisite link states";
    public bool IsAvailable => _linkType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.PrerequisiteLinkTiers;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var links = _links!();
        if (links is null) return WorldCategoryReport.Missing(Category, "the link registry was null");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        var collectedFrame = _currentFrame!();
        for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
        {
            var link = links[linkIndex];
            if (link is null) continue;
            try
            {
                var linkId = _identity!(link);
                var tiers = _tiers!(link);
                if (linkId == Guid.Empty || tiers is null) continue;
                for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
                {
                    var tier = tiers[tierIndex];
                    if (tier is null) continue;
                    var row = new WorldPrerequisiteLinkTier(
                        linkId,
                        tierIndex,
                        _activeEnabled!(tier),
                        _passiveEnabled!(tier),
                        _evaluatedFrame!(tier),
                        collectedFrame);
                    buffer.Append(in row);
                    sampled++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a prerequisite-link live gate threw: " +
                        ex.GetBaseException().Message;
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }
}

/// <summary>
/// Reads every upgrade's and every structure's per-level prerequisite container.
/// </summary>
/// <remarks>
/// <para>
/// It claims no identities: the rows are keyed by an entity its own category already claimed, and
/// claiming again would report every upgrade as a duplicate of itself.
/// </para>
/// <para>
/// The container's list is <c>[SerializeReference]</c>, so its entries are of whatever class the
/// author picked and there is no element type to bind against. Accessors are therefore compiled per
/// concrete runtime class, on first sight, and kept — which costs one binding pass per condition class
/// per game session rather than per entity. A class whose members do not bind yields a row of kind
/// <see cref="WorldRequirementConditionKind.Unknown"/> rather than none, so an unmodelled condition is
/// visible as a requirement nobody can evaluate instead of as an entity with no requirements.
/// </para>
/// <para>
/// Authored graph reads are field loads performed once per lifecycle. This reader calls no container
/// predicate at all: the game's own verdict is asked for one entity at a time by
/// <see cref="WorldRequirementNativeVerdictProbe"/> when a request wants it, and compared wholesale
/// by the differential checker's requirement passes. The no-argument <c>Check()</c>, and whole-entity
/// visibility or availability predicates which can reach it, remain forbidden here because they write
/// cached prerequisite state. See W58.
/// </para>
/// </remarks>
internal sealed class WorldEntityRequirementReader : IWorldCategoryReader
{
    private const int MaximumGraphDepth = 32;

    private readonly Type? _upgradeType;
    private readonly Type? _structureType;
    private readonly Type? _researchType;
    private readonly Type? _prerequisiteLinkType;
    private readonly Type? _alchemyType;
    private readonly Type? _glyphType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _glyphId;
    private readonly Func<object, object?>? _glyphContainer;
    private readonly Func<object, Guid>? _upgradeId;
    private readonly Func<object, object?>? _upgradeContainer;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, object?>? _structureContainer;
    private readonly Func<object, Guid>? _researchId;
    private readonly Func<object, object?>? _researchContainer;
    private readonly Func<object, Guid>? _prerequisiteLinkId;
    private readonly Func<object, IList?>? _prerequisiteLinkTiers;
    private readonly Func<object, object?>? _prerequisiteLinkTierContainer;
    private readonly Func<object, IList?>? _conditions;
    private readonly Func<object, object?>? _alchemyUsageContainer;
    private readonly Func<object, IList?>? _alchemyConditions;

    /// <summary>
    /// One compiled accessor set per condition class seen so far. Not on the frame: these are
    /// delegates, and a frame crosses to a worker.
    /// </summary>
    private readonly Dictionary<Type, ConditionAccessors> _accessors = new();

    /// <summary>
    /// The same per-class binding, for the list-variable classes a <c>ListRequirement</c> names.
    /// </summary>
    private readonly Dictionary<Type, ListVariableAccessors> _listAccessors = new();

    private readonly Dictionary<Type, Func<object, Guid>?> _memberIdentities = new();

    /// <summary>Which lists this pass has already read, so a list two conditions name is read once.</summary>
    private readonly HashSet<Guid> _capturedLists = new();

    /// <summary>
    /// Where this pass's list membership goes. Held for the pass rather than threaded through eight
    /// signatures whose subject is the condition graph rather than the lists hanging off two of them.
    /// </summary>
    private WorldRequirementListBuffer? _lists;

    internal WorldEntityRequirementReader(
        Type? upgradeType,
        Type? structureType,
        Type? researchType,
        Type? prerequisiteLinkType,
        Type? alchemyType,
        Type? glyphType)
    {
        _upgradeType = upgradeType;
        _structureType = structureType;
        _researchType = researchType;
        _prerequisiteLinkType = prerequisiteLinkType;
        _alchemyType = alchemyType;
        _glyphType = glyphType;
        if (upgradeType is null || structureType is null || researchType is null ||
            prerequisiteLinkType is null || alchemyType is null || glyphType is null)
        {
            _unavailable = upgradeType is null
                ? "the UpgradeSO type was not found on this build"
                : structureType is null
                    ? "the StructureSO type was not found on this build"
                    : researchType is null
                        ? "the ResearchSO type was not found on this build"
                        : prerequisiteLinkType is null
                            ? "the PrerequisiteLinkSO type was not found on this build"
                            : alchemyType is null
                                ? "the AlchemyRecipeSO type was not found on this build"
                                : "the GlyphSO type was not found on this build";
            return;
        }

        var upgrade = new WorldMemberBinding(upgradeType, "UpgradeSO");
        _upgradeId = upgrade.Call<Guid>("GetGuid");
        _upgradeContainer = NativeAccessorBinder.Reference(upgradeType, "prerequisitesPerLevel");

        var structure = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = structure.Call<Guid>("GetGuid");
        _structureContainer = NativeAccessorBinder.Reference(structureType, "prerequisitesPerLevel");
        _alchemyUsageContainer = NativeAccessorBinder.Reference(alchemyType, "usagePrerequisites");

        var research = new WorldMemberBinding(researchType, "ResearchSO");
        _researchId = research.Call<Guid>("GetGuid");
        _researchContainer = NativeAccessorBinder.Reference(researchType, "levelPrerequisites");

        var glyph = new WorldMemberBinding(glyphType, "GlyphSO");
        _glyphId = glyph.Call<Guid>("GetGuid");
        _glyphContainer = NativeAccessorBinder.Reference(glyphType, "prerequisites");

        var link = new WorldMemberBinding(prerequisiteLinkType, "PrerequisiteLinkSO");
        _prerequisiteLinkId = link.Call<Guid>("GetGuid");
        _prerequisiteLinkTiers = NativeAccessorBinder.CollectionField(prerequisiteLinkType, "linkTiers");
        var linkDefinitionType = prerequisiteLinkType.GetNestedType(
            "LinkDefinition", BindingFlags.Public | BindingFlags.NonPublic);
        _prerequisiteLinkTierContainer =
            NativeAccessorBinder.Reference(linkDefinitionType, "prerequisites");

        // Both owners and prerequisite-link tiers hold the same container type, so the list accessor
        // is bound once against whichever owner declared it rather than once per native surface.
        var containerType = ContainerTypeOf(upgradeType) ?? ContainerTypeOf(structureType);
        _conditions = NativeAccessorBinder.CollectionField(containerType, "prerequisites");

        var alchemyContainerType = alchemyType.GetField("usagePrerequisites", Instance)?.FieldType;
        _alchemyConditions = NativeAccessorBinder.CollectionField(alchemyContainerType, "prerequisites");

        if (_upgradeContainer is null || _structureContainer is null ||
            _researchId is null || _researchContainer is null ||
            _prerequisiteLinkId is null || _prerequisiteLinkTiers is null ||
            _prerequisiteLinkTierContainer is null || _conditions is null ||
            _alchemyUsageContainer is null || _alchemyConditions is null ||
            _glyphId is null || _glyphContainer is null)
        {
            _unavailable = "UpgradeSO, StructureSO, ResearchSO, PrerequisiteLinkSO, " +
                "AlchemyRecipeSO, and GlyphSO did not " +
                "expose the complete prerequisite graph on this build";
            return;
        }
        _unavailable = upgrade.Failure.Length > 0
            ? upgrade.Failure
            : structure.Failure.Length > 0
                ? structure.Failure
                : research.Failure.Length > 0
                    ? research.Failure
                    : link.Failure.Length > 0
                        ? link.Failure
                        : glyph.Failure;
    }

    public string Category => "entity requirements";

    public bool IsAvailable =>
        _upgradeType is not null && _structureType is not null &&
        _researchType is not null && _prerequisiteLinkType is not null &&
        _alchemyType is not null && _glyphType is not null &&
        _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.EntityRequirements;
        buffer.Reset();
        frame.RequirementLists.Reset();
        _lists = frame.RequirementLists;
        _capturedLists.Clear();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var sampled = 0;
        var unmodelled = 0;
        var firstFailure = string.Empty;

        Walk(
            NativeAccessorBinder.StaticList(_upgradeType, "All"),
            WorldRequirementOwnerKind.Upgrade,
            _upgradeId!,
            _upgradeContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        WalkPrerequisiteLinks(
            NativeAccessorBinder.StaticList(_prerequisiteLinkType, "All"),
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            NativeAccessorBinder.StaticList(_researchType, "All"),
            WorldRequirementOwnerKind.Research,
            _researchId!,
            _researchContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        WalkKnownIds(
            NativeAccessorBinder.StaticList(_alchemyType, "All"),
            WorldRequirementOwnerKind.AlchemyRecipe,
            WorldRequirementProgramKind.Usage,
            frame.AlchemyRecipes,
            _alchemyUsageContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            NativeAccessorBinder.StaticList(_structureType, "All"),
            WorldRequirementOwnerKind.Structure,
            _structureId!,
            _structureContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            NativeAccessorBinder.StaticList(_glyphType, "All"),
            WorldRequirementOwnerKind.Glyph,
            _glyphId!,
            _glyphContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);

        // An unmodelled condition is counted as skipped even though its row is published. The row
        // exists so the shortfall can be named; the count exists so the pass reports itself as
        // incomplete, which is what puts the subtype's name in front of an operator. The reader runs
        // once per lifecycle and the announcement is deduplicated on its text, so that is one line per
        // run of the game rather than one per pass.
        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, unmodelled, firstFailure);
    }

    private void WalkPrerequisiteLinks(
        IList? links,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (links is null)
        {
            if (firstFailure.Length == 0)
                firstFailure = "the PrerequisiteLink registry was unreadable";
            return;
        }

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (link is null) continue;
            try
            {
                var ownerId = _prerequisiteLinkId!(link);
                if (ownerId == Guid.Empty) continue;
                var tiers = _prerequisiteLinkTiers!(link);
                var tierCount = tiers?.Count ?? 0;
                for (var tier = 0; tier < tierCount; tier++)
                {
                    var definition = tiers![tier];
                    if (definition is null) continue;
                    var held = _prerequisiteLinkTierContainer!(definition);
                    if (held is null) continue;
                    sampled += ReadContainer(
                        ownerId,
                        WorldRequirementOwnerKind.PrerequisiteLink,
                        tier,
                        held,
                        publishContainerRoot: true,
                        buffer,
                        ref unmodelled,
                        ref firstFailure);
                }
            }
            catch (Exception ex)
            {
                unmodelled++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading a prerequisite-link tier threw: " +
                        ex.GetBaseException().Message;
                }
            }
        }
    }

    private void Walk(
        IList? owners,
        WorldRequirementOwnerKind kind,
        Func<object, Guid> identity,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (owners is null)
        {
            if (firstFailure.Length == 0) firstFailure = $"the {kind} registry was unreadable";
            return;
        }

        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            if (owner is null) continue;

            try
            {
                sampled += Read(
                    owner,
                    kind,
                    identity,
                    container,
                    buffer,
                    ref unmodelled,
                    ref firstFailure);
            }
            catch (Exception ex)
            {
                unmodelled++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading a per-level prerequisite threw: " +
                        ex.GetBaseException().Message;
                }
            }
        }
    }

    private int Read(
        object owner,
        WorldRequirementOwnerKind kind,
        Func<object, Guid> identity,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel)
    {
        var ownerId = identity(owner);
        if (ownerId == Guid.Empty) return 0;

        var held = container(owner);
        if (held is null) return 0;
        var conditions = _conditions!(held);
        return AppendConditions(
            ownerId, kind, program, conditions, buffer, ref unmodelled, ref firstFailure);
    }

    private int ReadContainer(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object held,
        bool publishContainerRoot,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var conditions = _conditions!(held);
        var count = conditions?.Count ?? 0;
        var appended = 0;
        var nextOrdinal = publishContainerRoot ? 1 : 0;
        var parentOrdinal = publishContainerRoot ? 0 : -1;
        var depth = publishContainerRoot ? 1 : 0;
        if (publishContainerRoot)
        {
            var root = new WorldEntityRequirement(
                ownerId,
                kind,
                containerIndex,
                ordinal: 0,
                parentOrdinal: -1,
                depth: 0,
                WorldRequirementNodeKind.Group,
                WorldRequirementOperator.And,
                WorldRequirementConditionKind.Unknown,
                "Prerequisites.Container",
                Guid.Empty,
                reqType: -1,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling));
            buffer.Append(in root);
        }
        for (var index = 0; index < count; index++)
        {
            var condition = conditions![index];
            if (condition is null) continue;
            appended += ReadCondition(
                ownerId, kind, containerIndex, condition, parentOrdinal, depth,
                ref nextOrdinal, buffer, ref unmodelled, ref firstFailure);
        }
        return appended;
    }

    private int ReadCondition(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object condition,
        int parentOrdinal,
        int depth,
        ref int nextOrdinal,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (depth >= MaximumGraphDepth)
        {
            var exceededOrdinal = nextOrdinal++;
            var unknown = new WorldEntityRequirement(
                ownerId, kind, containerIndex, exceededOrdinal, parentOrdinal, depth,
                WorldRequirementNodeKind.Leaf, WorldRequirementOperator.None,
                WorldRequirementConditionKind.Unknown, "RequirementGraphDepthExceeded",
                Guid.Empty, -1, 0d, default, default);
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = $"a prerequisite graph exceeded {MaximumGraphDepth} nested nodes. " +
                    "Entities gated by one are never planned.";
            buffer.Append(in unknown);
            return 1;
        }

        var accessors = AccessorsFor(condition.GetType());
        var ordinal = nextOrdinal++;
        var row = accessors.ReadGraph(
            ownerId, kind, containerIndex, ordinal, parentOrdinal, depth, condition);
        if (row.Kind == WorldRequirementConditionKind.List)
            CaptureList(accessors, condition, row.TargetId, ref unmodelled, ref firstFailure);
        if (row.Kind == WorldRequirementConditionKind.Unknown &&
            row.NodeKind == WorldRequirementNodeKind.Leaf)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.";
        }

        buffer.Append(in row);
        var appended = 1;
        if (row.NodeKind != WorldRequirementNodeKind.Group) return appended;

        var children = accessors.ReadChildren(condition);
        var childCount = children?.Count ?? 0;
        for (var index = 0; index < childCount; index++)
        {
            var child = children![index];
            if (child is null) continue;
            appended += ReadCondition(
                ownerId, kind, containerIndex, child, ordinal, depth + 1,
                ref nextOrdinal, buffer, ref unmodelled, ref firstFailure);
        }
        return appended;
    }

    private void WalkKnownIds(
        IList? owners,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        WorldSampleBuffer<WorldAlchemyRecipe, WorldAlchemyRecipe> identities,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (owners is null || owners.Count != identities.Count)
        {
            if (firstFailure.Length == 0)
                firstFailure = "the AlchemyRecipeSO identity snapshot was incomplete";
            return;
        }

        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            if (owner is null) continue;
            try
            {
                sampled += ReadKnownId(
                    owner, identities[index].EntityId, kind, program, container, buffer,
                    ref unmodelled, ref firstFailure);
            }
            catch (Exception ex)
            {
                var row = UnreadableUsage(identities[index].EntityId, program);
                buffer.Append(in row);
                sampled++;
                unmodelled++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a usage prerequisite threw: " +
                        ex.GetBaseException().Message;
            }
        }
    }

    private int ReadKnownId(
        object owner,
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        Func<object, object?> container,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var held = container(owner);
        if (held is null) return AppendUnreadableUsage(
            ownerId, kind, program, buffer, ref unmodelled, ref firstFailure);
        var conditions = _alchemyConditions!(held);
        if (conditions is null) return AppendUnreadableUsage(
            ownerId, kind, program, buffer, ref unmodelled, ref firstFailure);
        return AppendConditions(
            ownerId, kind, program, conditions, buffer, ref unmodelled, ref firstFailure);
    }

    private static int AppendUnreadableUsage(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var row = UnreadableUsage(ownerId, program, ownerKind);
        buffer.Append(in row);
        unmodelled++;
        if (firstFailure.Length == 0)
            firstFailure = "an AlchemyRecipeSO usage-prerequisite container was unreadable";
        return 1;
    }

    private static WorldEntityRequirement UnreadableUsage(
        Guid ownerId,
        WorldRequirementProgramKind program,
        WorldRequirementOwnerKind ownerKind = WorldRequirementOwnerKind.AlchemyRecipe) =>
        new(
            ownerId,
            ownerKind,
            0,
            WorldRequirementConditionKind.Unknown,
            "UnreadableUsageRequirements",
            Guid.Empty,
            -1,
            0,
            default,
            default,
            program);

    private int AppendConditions(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        IList? conditions,
        WorldEntityRequirementBuffer buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var appended = 0;
        for (var groupOrdinal = 0; groupOrdinal < (conditions?.Count ?? 0); groupOrdinal++)
        {
            var condition = conditions![groupOrdinal];
            if (condition is null) continue;

            var accessors = AccessorsFor(condition.GetType());
            if (!accessors.IsComposite)
            {
                var row = accessors.Read(
                    ownerId, ownerKind, appended, condition, program,
                    WorldRequirementGroupKind.All, groupOrdinal);
                if (row.Kind == WorldRequirementConditionKind.List)
                    CaptureList(accessors, condition, row.TargetId, ref unmodelled, ref firstFailure);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            var children = accessors.ReadChildren(condition);
            if (children is null)
            {
                var row = accessors.Unknown(
                    ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            if (children.Count == 0)
            {
                // Enumerable.Any(empty) is false; Enumerable.All(empty) is true.
                var row = accessors.EmptyComposite(
                    ownerId, ownerKind, appended, program, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                if (child is null)
                {
                    var nullRow = accessors.Unknown(
                        ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal);
                    Append(in nullRow, buffer, ref appended, ref unmodelled, ref firstFailure);
                    continue;
                }
                var childAccessors = AccessorsFor(child.GetType());
                // This baseline authors no deeper composite in the programs collected here. Publish
                // one named unknown child instead of flattening away its parentheses; the enclosing
                // three-way fold then decides whether another OR arm proves the group met.
                var row = childAccessors.IsComposite
                    ? childAccessors.Unknown(
                        ownerId, ownerKind, appended, program, accessors.GroupKind, groupOrdinal)
                    : childAccessors.Read(
                        ownerId, ownerKind, appended, child, program,
                        accessors.GroupKind, groupOrdinal);
                if (row.Kind == WorldRequirementConditionKind.List)
                    CaptureList(childAccessors, child, row.TargetId, ref unmodelled, ref firstFailure);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
            }
        }

        return appended;
    }

    private static void Append(
        in WorldEntityRequirement row,
        WorldEntityRequirementBuffer buffer,
        ref int appended,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (row.Kind == WorldRequirementConditionKind.Unknown)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.";
        }

        buffer.Append(in row);
        appended++;
    }

    private ConditionAccessors AccessorsFor(Type conditionType)
    {
        if (_accessors.TryGetValue(conditionType, out var cached)) return cached;

        var built = ConditionAccessors.Bind(conditionType);
        _accessors.Add(conditionType, built);
        return built;
    }

    /// <summary>
    /// Reads the membership behind one <c>ListRequirement</c>, once per pass per list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison the game makes is over the whole list, so membership is the fact a worker needs;
    /// each member's own visibility is already a per-pass row elsewhere in the same snapshot. Nothing
    /// here calls a native predicate: <c>value</c> and <c>isStatic</c> are stored fields and
    /// <c>GetGuid()</c> is an identity read.
    /// </para>
    /// <para>
    /// Only a list the game itself marks <c>isStatic</c> is published. This reader is epoch-scoped, so
    /// a list the run plays into would be frozen at whatever it held when the lifecycle began, and a
    /// membership that stale answers worse than no membership at all. One that is not, or whose
    /// members carry no readable identity, is counted as a shortfall and named — the same fail-closed
    /// reading a condition class this suite does not model gets.
    /// </para>
    /// </remarks>
    private void CaptureList(
        ConditionAccessors accessors,
        object condition,
        Guid listId,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (_lists is null || listId == Guid.Empty || !_capturedLists.Add(listId)) return;

        var list = accessors.ReadListVariable(condition);
        if (list is null)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = "a list requirement names no list variable. " +
                    "Entities gated by one are never planned.";
            return;
        }

        var binding = ListAccessorsFor(list.GetType());
        if (!binding.IsBound)
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = $"this build's {list.GetType().Name} did not expose its membership. " +
                    "Entities gated by one are never planned.";
            return;
        }

        if (!binding.IsStatic(list))
        {
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = "this build compares against a list the run plays into: " +
                    $"{list.GetType().Name}. Entities gated by one are never planned.";
            return;
        }

        var members = binding.Members(list);
        var count = members?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            var member = members![index];
            if (member is null || MemberIdentityFor(member.GetType()) is not null) continue;
            unmodelled++;
            if (firstFailure.Length == 0)
                firstFailure = $"a list requirement names a {member.GetType().Name} with no readable " +
                    "identity. Entities gated by one are never planned.";
            return;
        }

        var header = new WorldRequirementListMember(
            listId, WorldRequirementListMember.HeaderPosition, Guid.Empty);
        _lists.Append(in header);
        for (var index = 0; index < count; index++)
        {
            var member = members![index];
            var identity = member is null ? Guid.Empty : MemberIdentityFor(member.GetType())!(member);
            var row = new WorldRequirementListMember(listId, index, identity);
            _lists.Append(in row);
        }
    }

    private ListVariableAccessors ListAccessorsFor(Type listType)
    {
        if (_listAccessors.TryGetValue(listType, out var cached)) return cached;

        var built = ListVariableAccessors.Bind(listType);
        _listAccessors.Add(listType, built);
        return built;
    }

    private Func<object, Guid>? MemberIdentityFor(Type memberType)
    {
        if (_memberIdentities.TryGetValue(memberType, out var cached)) return cached;

        var built = NativeAccessorBinder.Call<Guid>(memberType, "GetGuid");
        _memberIdentities.Add(memberType, built);
        return built;
    }

    /// <summary>The two stored fields one list-variable class answers its membership from.</summary>
    private sealed class ListVariableAccessors
    {
        private readonly Func<object, IList?>? _members;
        private readonly Func<object, bool>? _isStatic;

        private ListVariableAccessors(Func<object, IList?>? members, Func<object, bool>? isStatic)
        {
            _members = members;
            _isStatic = isStatic;
        }

        internal static ListVariableAccessors Bind(Type listType) =>
            new(
                NativeAccessorBinder.CollectionField(listType, "value"),
                NativeAccessorBinder.Field<bool>(listType, "isStatic"));

        internal bool IsBound => _members is not null && _isStatic is not null;

        internal bool IsStatic(object list) => _isStatic!(list);

        internal IList? Members(object list) => _members!(list);
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type? ContainerTypeOf(Type owner) =>
        owner.GetField("prerequisitesPerLevel", Instance)?.FieldType;

    /// <summary>
    /// The compiled reads for one concrete condition class, and the kind this suite models it as.
    /// </summary>
    /// <remarks>
    /// The three members are declared on the game's <c>BaseCondition&lt;T, TE&gt;</c>, whose <c>item</c>
    /// and <c>reqType</c> are its own type parameters — so there is no single closed type to bind
    /// against and the accessors are compiled against each concrete subclass instead. The two
    /// composites bind their explicit child lists and retain both their graph node and group-fold
    /// semantics.
    /// </remarks>
    private sealed class ConditionAccessors
    {
        private readonly WorldRequirementConditionKind _kind;
        private readonly string _typeName;
        private readonly WorldRequirementNodeKind _nodeKind;
        private readonly WorldRequirementOperator _operator;
        private readonly Func<object, IList?>? _children;
        private readonly Func<object, Guid>? _item;
        private readonly Func<object, int>? _reqType;
        private readonly Func<object, object?>? _value;
        private readonly Func<object, double>? _baseValue;
        private readonly Func<object, int>? _perLevelType;
        private readonly Func<object, BigDouble>? _perLevelAmount;
        private readonly Func<object, int>? _perLevelOrder;
        private readonly Func<object, int>? _modPerLevelType;
        private readonly Func<object, BigDouble>? _modPerLevelAmount;
        private readonly Func<object, int>? _modPerLevelOrder;
        private readonly WorldRequirementGroupKind? _groupKind;
        private readonly Func<object, object?>? _itemObject;

        private ConditionAccessors(
            WorldRequirementConditionKind kind,
            string typeName,
            WorldRequirementNodeKind nodeKind,
            WorldRequirementOperator @operator,
            Func<object, IList?>? children,
            Func<object, Guid>? item,
            Func<object, int>? reqType,
            Func<object, object?>? value,
            Func<object, double>? baseValue,
            Func<object, int>? perLevelType,
            Func<object, BigDouble>? perLevelAmount,
            Func<object, int>? perLevelOrder,
            Func<object, int>? modPerLevelType,
            Func<object, BigDouble>? modPerLevelAmount,
            Func<object, int>? modPerLevelOrder,
            WorldRequirementGroupKind? groupKind = null,
            Func<object, object?>? itemObject = null)
        {
            _itemObject = itemObject;
            _kind = kind;
            _typeName = typeName;
            _nodeKind = nodeKind;
            _operator = @operator;
            _children = children;
            _item = item;
            _reqType = reqType;
            _value = value;
            _baseValue = baseValue;
            _perLevelType = perLevelType;
            _perLevelAmount = perLevelAmount;
            _perLevelOrder = perLevelOrder;
            _modPerLevelType = modPerLevelType;
            _modPerLevelAmount = modPerLevelAmount;
            _modPerLevelOrder = modPerLevelOrder;
            _groupKind = groupKind;
        }

        internal bool IsComposite => _groupKind.HasValue;
        internal WorldRequirementGroupKind GroupKind => _groupKind ?? WorldRequirementGroupKind.All;

        /// <summary>
        /// The list variable a <c>ListRequirement</c> compares against, as the object rather than as
        /// its identity: the membership behind the fold is only reachable through the instance.
        /// </summary>
        internal object? ReadListVariable(object condition) => _itemObject?.Invoke(condition);

        internal IList? ReadChildren(object condition) => _children?.Invoke(condition);

        internal static ConditionAccessors Bind(Type conditionType)
        {
            var typeName = conditionType.Name;
            var @operator = typeName switch
            {
                "AndRequirement" => WorldRequirementOperator.And,
                "OrRequirement" => WorldRequirementOperator.Or,
                _ => WorldRequirementOperator.None,
            };
            if (@operator != WorldRequirementOperator.None)
            {
                var children = NativeAccessorBinder.CollectionField(
                    conditionType,
                    @operator == WorldRequirementOperator.And ? "andConditions" : "orConditions");
                return new ConditionAccessors(
                    WorldRequirementConditionKind.Unknown,
                    typeName,
                    children is null ? WorldRequirementNodeKind.Leaf : WorldRequirementNodeKind.Group,
                    children is null ? WorldRequirementOperator.None : @operator,
                    children,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    @operator == WorldRequirementOperator.Or
                        ? WorldRequirementGroupKind.Any
                        : WorldRequirementGroupKind.All);
            }

            var kind = Classify(typeName);

            var item = NativeAccessorBinder.ReferenceGuid(conditionType, "item");
            var reqType = NativeAccessorBinder.EnumField(conditionType, "reqType");
            var value = NativeAccessorBinder.Reference(conditionType, "value");
            var thresholdType = conditionType.GetField("value", Instance)?.FieldType;

            var baseValue = NativeAccessorBinder.Field<double>(thresholdType, "baseValue");
            var perLevelType = NativeAccessorBinder.NestedEnumField(thresholdType, "perLevel", "type");
            var perLevelAmount =
                NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "perLevel", "adjustReal");
            var perLevelOrder = NativeAccessorBinder.NestedField<int>(thresholdType, "perLevel", "order");
            var modPerLevelType =
                NativeAccessorBinder.NestedEnumField(thresholdType, "modPerLevel", "type");
            var modPerLevelAmount =
                NativeAccessorBinder.NestedField<BigDouble>(thresholdType, "modPerLevel", "adjustReal");
            var modPerLevelOrder =
                NativeAccessorBinder.NestedField<int>(thresholdType, "modPerLevel", "order");

            // A list comparison folds over the whole list, so the instance behind the identity is part
            // of this class's binding set rather than an extra the reader reaches for separately.
            var itemObject = kind == WorldRequirementConditionKind.List
                ? NativeAccessorBinder.Reference(conditionType, "item")
                : null;

            // A modelled kind whose members did not bind is not modelled after all. Collapsing the two
            // into one verdict is what keeps every consumer's fail-closed test a single comparison.
            var bound = item is not null && reqType is not null && value is not null &&
                baseValue is not null && perLevelType is not null && perLevelAmount is not null &&
                perLevelOrder is not null && modPerLevelType is not null &&
                modPerLevelAmount is not null && modPerLevelOrder is not null &&
                (kind != WorldRequirementConditionKind.List || itemObject is not null);

            return new ConditionAccessors(
                bound ? kind : WorldRequirementConditionKind.Unknown,
                typeName,
                WorldRequirementNodeKind.Leaf,
                WorldRequirementOperator.None,
                null,
                item,
                reqType,
                value,
                baseValue,
                perLevelType,
                perLevelAmount,
                perLevelOrder,
                modPerLevelType,
                modPerLevelAmount,
                modPerLevelOrder,
                groupKind: null,
                itemObject: itemObject);
        }

        internal IList? Children(object condition) => _children?.Invoke(condition);

        internal WorldEntityRequirement Read(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            object condition,
            WorldRequirementProgramKind program,
            WorldRequirementGroupKind groupKind,
            int groupOrdinal)
        {
            if (_kind == WorldRequirementConditionKind.Unknown)
                return Unknown(ownerId, ownerKind, ordinal, program, groupKind, groupOrdinal);
            ReadScaling(condition, out var threshold, out var perLevel, out var modPerLevel);

            return new WorldEntityRequirement(
                ownerId,
                ownerKind,
                ordinal,
                _kind,
                _typeName,
                _item!(condition),
                _reqType!(condition),
                threshold is null ? 0d : _baseValue!(threshold),
                in perLevel,
                in modPerLevel,
                program,
                groupKind,
                groupOrdinal);
        }

        internal WorldEntityRequirement ReadGraph(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int containerIndex,
            int ordinal,
            int parentOrdinal,
            int depth,
            object condition)
        {
            if (_nodeKind == WorldRequirementNodeKind.Group ||
                _kind == WorldRequirementConditionKind.Unknown)
            {
                return new WorldEntityRequirement(
                    ownerId, ownerKind, containerIndex, ordinal, parentOrdinal, depth,
                    _nodeKind, _operator, WorldRequirementConditionKind.Unknown, _typeName,
                    Guid.Empty, -1, 0d, default, default,
                    WorldRequirementProgramKind.NextLevel,
                    GroupKind,
                    groupOrdinal: containerIndex);
            }

            ReadScaling(condition, out var threshold, out var perLevel, out var modPerLevel);
            return new WorldEntityRequirement(
                ownerId, ownerKind, containerIndex, ordinal, parentOrdinal, depth,
                _nodeKind, _operator, _kind, _typeName, _item!(condition),
                _reqType!(condition), threshold is null ? 0d : _baseValue!(threshold),
                in perLevel, in modPerLevel,
                WorldRequirementProgramKind.NextLevel,
                WorldRequirementGroupKind.All,
                groupOrdinal: containerIndex);
        }

        private void ReadScaling(
            object condition,
            out object? threshold,
            out WorldRequirementScaling perLevel,
            out WorldRequirementScaling modPerLevel)
        {
            threshold = _value!(condition);
            perLevel = threshold is null
                ? default
                : new WorldRequirementScaling(
                    _perLevelType!(threshold), _perLevelAmount!(threshold), _perLevelOrder!(threshold));
            modPerLevel = threshold is null
                ? default
                : new WorldRequirementScaling(
                    _modPerLevelType!(threshold), _modPerLevelAmount!(threshold),
                    _modPerLevelOrder!(threshold));
        }

        internal WorldEntityRequirement Unknown(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            WorldRequirementProgramKind program,
            WorldRequirementGroupKind groupKind,
            int groupOrdinal) =>
            new(
                ownerId,
                ownerKind,
                ordinal,
                WorldRequirementConditionKind.Unknown,
                _typeName,
                Guid.Empty,
                reqType: -1,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling),
                program,
                groupKind,
                groupOrdinal);

        internal WorldEntityRequirement EmptyComposite(
            Guid ownerId,
            WorldRequirementOwnerKind ownerKind,
            int ordinal,
            WorldRequirementProgramKind program,
            int groupOrdinal) =>
            new(
                ownerId,
                ownerKind,
                ordinal,
                WorldRequirementConditionKind.Literal,
                _typeName,
                Guid.Empty,
                reqType: GroupKind == WorldRequirementGroupKind.All ? 1 : 0,
                baseValue: 0d,
                default(WorldRequirementScaling),
                default(WorldRequirementScaling),
                program,
                GroupKind,
                groupOrdinal);

        /// <summary>
        /// The condition classes this suite has been audited against, by the name the game gives them.
        /// </summary>
        /// <remarks>
        /// A name rather than a resolved type, because the list the container holds is
        /// <c>[SerializeReference]</c> and its entries are only ever known by what they turn out to be.
        /// Composite classes are bound separately to their child lists. Everything absent from this
        /// switch and that composite branch is unknown by construction, which is the fail-closed
        /// reading.
        /// </remarks>
        private static WorldRequirementConditionKind Classify(string typeName) => typeName switch
        {
            "UpgradeRequirement" => WorldRequirementConditionKind.Upgrade,
            "ResearchRequirement" => WorldRequirementConditionKind.Research,
            "StructureRequirement" => WorldRequirementConditionKind.Structure,
            "SpellRequirement" => WorldRequirementConditionKind.Spell,
            "AlchemyRecipeRequirement" => WorldRequirementConditionKind.AlchemyRecipe,
            "RitualRequirement" => WorldRequirementConditionKind.Ritual,
            "NumberRequirement" => WorldRequirementConditionKind.Number,
            "GenericRequirement" => WorldRequirementConditionKind.Generic,
            "PrerequisiteLinkRequirement" => WorldRequirementConditionKind.PrerequisiteLink,
            "ListRequirement" => WorldRequirementConditionKind.List,
            _ => WorldRequirementConditionKind.Unknown,
        };
    }
}

/// <summary>
/// Asks the game its own prerequisite verdict for one entity, when something asks.
/// </summary>
/// <remarks>
/// <para>
/// Not a category reader, deliberately. This was a per-pass sweep that invoked
/// <c>Container.Check(ConditionInfo)</c> once for every upgrade, structure, and research four times
/// a second, and published a table whose only consumer was one diagnostic answering about one entity
/// at a time. Capture grabs facts; a verdict the game computes on demand is not a fact lying around,
/// and the differential checker — which now covers upgrades, structures, research, and
/// prerequisite-link tiers — is where the two sides are compared wholesale.
/// </para>
/// <para>
/// <b>Safe to call.</b> Only the parameterised overload is bound; the no-argument one latches
/// <c>available</c> and stamps a game id. The parameter type is taken from the overload rather than
/// resolved by name, so a same-named type elsewhere in the domain cannot be bound by mistake.
/// </para>
/// <para>
/// The registry scan is linear, and that is the right cost here: one request asks about one entity,
/// and an index would have to be maintained by the pass this type exists to delete.
/// </para>
/// </remarks>
internal sealed class WorldRequirementNativeVerdictProbe
{
    private readonly OwnerProbe[] _owners;
    private readonly string _unavailable;

    internal WorldRequirementNativeVerdictProbe()
        : this(WorldNativeTypes.Resolve)
    {
    }

    internal WorldRequirementNativeVerdictProbe(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        var upgrade = OwnerProbe.TryBind(
            resolveType("UpgradeSO"),
            WorldRequirementOwnerKind.Upgrade,
            "prerequisitesPerLevel");
        var structure = OwnerProbe.TryBind(
            resolveType("StructureSO"),
            WorldRequirementOwnerKind.Structure,
            "prerequisitesPerLevel");
        var research = OwnerProbe.TryBind(
            resolveType("ResearchSO"),
            WorldRequirementOwnerKind.Research,
            "levelPrerequisites");

        if (upgrade is null || structure is null || research is null)
        {
            _owners = Array.Empty<OwnerProbe>();
            _unavailable = upgrade is null
                ? "UpgradeSO did not expose its per-level prerequisite container on this build"
                : structure is null
                    ? "StructureSO did not expose its per-level prerequisite container on this build"
                    : "ResearchSO did not expose its level prerequisite container on this build";
            return;
        }

        _owners = new[] { upgrade, structure, research };
        _unavailable = string.Empty;
    }

    internal bool IsAvailable => _unavailable.Length == 0;

    /// <summary>
    /// The game's answer for <paramref name="entityId"/>, or why there is none.
    /// </summary>
    internal bool TryRead(
        Guid entityId,
        out WorldRequirementNativeVerdict verdict,
        out string failure)
    {
        verdict = default;
        if (!IsAvailable)
        {
            failure = _unavailable;
            return false;
        }

        for (var index = 0; index < _owners.Length; index++)
        {
            if (_owners[index].TryRead(entityId, out verdict, out failure)) return true;
            if (failure.Length > 0) return false;
        }

        failure = "no upgrade, structure, or research in this session carries that identity";
        return false;
    }

    private sealed class OwnerProbe
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly Type _ownerType;
        private readonly WorldRequirementOwnerKind _kind;
        private readonly Func<object, Guid> _identity;
        private readonly Func<object, object?> _container;
        private readonly Func<object, long> _level;
        private readonly Func<object, long, bool> _check;

        private OwnerProbe(
            Type ownerType,
            WorldRequirementOwnerKind kind,
            Func<object, Guid> identity,
            Func<object, object?> container,
            Func<object, long> level,
            Func<object, long, bool> check)
        {
            _ownerType = ownerType;
            _kind = kind;
            _identity = identity;
            _container = container;
            _level = level;
            _check = check;
        }

        internal static OwnerProbe? TryBind(
            Type? ownerType,
            WorldRequirementOwnerKind kind,
            string containerField)
        {
            if (ownerType is null) return null;

            var binding = new WorldMemberBinding(ownerType, ownerType.Name);
            var identity = binding.Call<Guid>("GetGuid");
            var container = NativeAccessorBinder.Reference(ownerType, containerField);
            var level = LevelAccessor(ownerType, kind, binding);
            var containerType = ownerType.GetField(containerField, Instance)?.FieldType;
            var check = NativeAccessorBinder.CallWithConstructedLongArgument<bool>(
                containerType, "Check", "Requirements.ConditionInfo");

            return identity is null || container is null || level is null || check is null ||
                binding.Failure.Length > 0
                ? null
                : new OwnerProbe(ownerType, kind, identity, container, level, check);
        }

        /// <summary>
        /// Each owner's own level expression, as the game writes it:
        /// <c>level + queuedLevels + 1</c> for <c>UpgradeSO.HasMetQueuedLevelRequirements()</c>,
        /// <c>quantity</c> for <c>StructureSO.HasMetLevelRequirements()</c>, and
        /// <c>GetRequirementLevel()</c> for <c>ResearchSO.MeetsLevelRequirements()</c>.
        /// </summary>
        private static Func<object, long>? LevelAccessor(
            Type ownerType,
            WorldRequirementOwnerKind kind,
            WorldMemberBinding binding)
        {
            switch (kind)
            {
                case WorldRequirementOwnerKind.Upgrade:
                    var level = NativeAccessorBinder.Field<int>(ownerType, "level");
                    var queued = NativeAccessorBinder.Field<int>(ownerType, "queuedLevels");
                    return level is null || queued is null
                        ? null
                        : owner => level(owner) + queued(owner) + 1L;
                case WorldRequirementOwnerKind.Structure:
                    var quantity = NativeAccessorBinder.Field<int>(ownerType, "quantity");
                    return quantity is null ? null : owner => quantity(owner);
                default:
                    var requirementLevel = binding.Call<int>("GetRequirementLevel");
                    return requirementLevel is null ? null : owner => requirementLevel(owner);
            }
        }

        internal bool TryRead(
            Guid entityId,
            out WorldRequirementNativeVerdict verdict,
            out string failure)
        {
            verdict = default;
            failure = string.Empty;
            var all = NativeAccessorBinder.StaticList(_ownerType, "All");
            if (all is null) return false;

            for (var index = 0; index < all.Count; index++)
            {
                var owner = all[index];
                if (owner is null) continue;
                try
                {
                    if (_identity(owner) != entityId) continue;
                    var container = _container(owner);
                    if (container is null)
                    {
                        // The one native oracle answers what the game answered. With no container
                        // there is no game answer, and inventing "met" would make the oracle agree
                        // with the suite for a reason neither of them checked.
                        failure = "the game holds no prerequisite container for that entity, " +
                            "so it published no verdict to read";
                        return false;
                    }

                    var level = _level(owner);
                    verdict = new WorldRequirementNativeVerdict(
                        entityId, _kind, level, _check(container, level));
                    return true;
                }
                catch (Exception exception)
                {
                    failure = "reading the live prerequisite verdict threw: " +
                        exception.GetBaseException().Message;
                    return false;
                }
            }

            return false;
        }
    }
}
