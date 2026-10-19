using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.World;

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
/// predicate at all: the game's own verdict is compared wholesale by the differential checker's
/// requirement passes. The no-argument <c>Check()</c>, and whole-entity
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
    private readonly Type? _recipeBookType;
    private readonly Type? _resourceType;
    private readonly Func<IList?>? _upgrades;
    private readonly Func<IList?>? _structures;
    private readonly Func<IList?>? _research;
    private readonly Func<IList?>? _prerequisiteLinks;
    private readonly Func<IList?>? _alchemyRecipes;
    private readonly Func<IList?>? _recipeBooks;
    private readonly Func<IList?>? _resources;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _recipeBookId;
    private readonly Func<object, object?>? _recipeBookContainer;
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
    private readonly Func<object, object?>? _upgradeUnlockContainer;
    private readonly Func<object, object?>? _structureUnlockContainer;
    private readonly Func<object, object?>? _researchVisibilityContainer;
    private readonly Func<object, object?>? _researchLevelVisibilityContainer;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, object?>? _resourceVisibilityContainer;
    private readonly Func<object, BigDouble>? _containerAdjustValue;

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
    private WorldRelationBuffer<WorldRequirementListMember>? _lists;

    internal WorldEntityRequirementReader(
        Type? upgradeType,
        Type? structureType,
        Type? researchType,
        Type? prerequisiteLinkType,
        Type? alchemyType,
        Type? recipeBookType,
        Type? resourceType)
    {
        _upgradeType = upgradeType;
        _structureType = structureType;
        _researchType = researchType;
        _prerequisiteLinkType = prerequisiteLinkType;
        _alchemyType = alchemyType;
        _recipeBookType = recipeBookType;
        _resourceType = resourceType;
        _upgrades = NativeAccessorBinder.StaticListAccessor(upgradeType, "All");
        _structures = NativeAccessorBinder.StaticListAccessor(structureType, "All");
        _research = NativeAccessorBinder.StaticListAccessor(researchType, "All");
        _prerequisiteLinks = NativeAccessorBinder.StaticListAccessor(prerequisiteLinkType, "All");
        _alchemyRecipes = NativeAccessorBinder.StaticListAccessor(alchemyType, "All");
        _recipeBooks = NativeAccessorBinder.StaticListAccessor(recipeBookType, "All");
        _resources = NativeAccessorBinder.StaticListAccessor(resourceType, "All");
        if (upgradeType is null || structureType is null || researchType is null ||
            prerequisiteLinkType is null || alchemyType is null || recipeBookType is null ||
            resourceType is null)
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
                                : recipeBookType is null
                                    ? "the RecipeBookSO type was not found on this build"
                                    : "the ResourceSO type was not found on this build";
            return;
        }

        var upgrade = new WorldMemberBinding(upgradeType, "UpgradeSO");
        _upgradeId = upgrade.StableUuid();
        _upgradeContainer = NativeAccessorBinder.Reference(upgradeType, "prerequisitesPerLevel");

        var structure = new WorldMemberBinding(structureType, "StructureSO");
        _structureId = structure.StableUuid();
        _structureContainer = NativeAccessorBinder.Reference(structureType, "prerequisitesPerLevel");
        _alchemyUsageContainer = NativeAccessorBinder.Reference(alchemyType, "usagePrerequisites");

        var research = new WorldMemberBinding(researchType, "ResearchSO");
        _researchId = research.StableUuid();
        _researchContainer = NativeAccessorBinder.Reference(researchType, "levelPrerequisites");

        var recipeBook = new WorldMemberBinding(recipeBookType, "RecipeBookSO");
        _recipeBookId = recipeBook.StableUuid();
        _recipeBookContainer = NativeAccessorBinder.Reference(recipeBookType, "prerequisites");

        var link = new WorldMemberBinding(prerequisiteLinkType, "PrerequisiteLinkSO");
        _prerequisiteLinkId = link.StableUuid();
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

        // The unlock containers: what the game asks through the no-argument Check(). Field loads only —
        // binding the predicate itself is what this reader must never do.
        _upgradeUnlockContainer = NativeAccessorBinder.Reference(upgradeType, "prerequisites");
        _structureUnlockContainer = NativeAccessorBinder.Reference(structureType, "prerequisites");
        _researchVisibilityContainer =
            NativeAccessorBinder.Reference(researchType, "visibilityPrerequisites");
        _researchLevelVisibilityContainer =
            NativeAccessorBinder.Reference(researchType, "levelVisibilityPrereq");
        var resource = new WorldMemberBinding(resourceType, "ResourceSO");
        _resourceId = resource.StableUuid();
        _resourceVisibilityContainer =
            NativeAccessorBinder.Reference(resourceType, "visiblePrerequisites");
        _containerAdjustValue = NativeAccessorBinder.Field<BigDouble>(containerType, "adjustValue");

        if (_upgradeContainer is null || _structureContainer is null ||
            _researchId is null || _researchContainer is null ||
            _prerequisiteLinkId is null || _prerequisiteLinkTiers is null ||
            _prerequisiteLinkTierContainer is null || _conditions is null ||
            _alchemyUsageContainer is null || _alchemyConditions is null ||
            _recipeBookId is null || _recipeBookContainer is null ||
            _upgradeUnlockContainer is null || _structureUnlockContainer is null ||
            _researchVisibilityContainer is null || _researchLevelVisibilityContainer is null ||
            _resourceId is null || _resourceVisibilityContainer is null ||
            _containerAdjustValue is null)
        {
            _unavailable = "UpgradeSO, StructureSO, ResearchSO, PrerequisiteLinkSO, " +
                "AlchemyRecipeSO, RecipeBookSO, and ResourceSO did not " +
                "expose the complete prerequisite graph on this build";
            return;
        }
        _unavailable = resource.Failure.Length > 0
            ? resource.Failure
            : upgrade.Failure.Length > 0
                ? upgrade.Failure
                : structure.Failure.Length > 0
                    ? structure.Failure
                    : research.Failure.Length > 0
                        ? research.Failure
                        : link.Failure.Length > 0
                            ? link.Failure
                            : recipeBook.Failure;
    }

    public string Category => "entity requirements";

    public bool IsAvailable =>
        _upgradeType is not null && _structureType is not null &&
        _researchType is not null && _prerequisiteLinkType is not null &&
        _alchemyType is not null && _recipeBookType is not null &&
        _resourceType is not null &&
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
            _upgrades?.Invoke(),
            WorldRequirementOwnerKind.Upgrade,
            _upgradeId!,
            _upgradeContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure,
            _upgradeUnlockContainer);
        WalkPrerequisiteLinks(
            _prerequisiteLinks?.Invoke(),
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            _research?.Invoke(),
            WorldRequirementOwnerKind.Research,
            _researchId!,
            _researchContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure,
            _researchVisibilityContainer,
            _researchLevelVisibilityContainer);
        WalkKnownIds(
            _alchemyRecipes?.Invoke(),
            WorldRequirementOwnerKind.AlchemyRecipe,
            WorldRequirementProgramKind.Usage,
            frame.AlchemyRecipes,
            _alchemyUsageContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        Walk(
            _structures?.Invoke(),
            WorldRequirementOwnerKind.Structure,
            _structureId!,
            _structureContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure,
            _structureUnlockContainer);
        Walk(
            _recipeBooks?.Invoke(),
            WorldRequirementOwnerKind.RecipeBook,
            _recipeBookId!,
            _recipeBookContainer!,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure);
        // A resource has no per-level program at all: nothing about it is bought a level at a time.
        // Its one container is the unlock gate ResourceSO.CheckVisibility() consults every frame.
        Walk(
            _resources?.Invoke(),
            WorldRequirementOwnerKind.Resource,
            _resourceId!,
            container: null,
            buffer,
            ref sampled,
            ref unmodelled,
            ref firstFailure,
            _resourceVisibilityContainer);

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
        WorldRelationBuffer<WorldEntityRequirement> buffer,
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
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                WorldSkipTally.Skip(
                    ref unmodelled,
                    ref firstFailure,
                    "reading a prerequisite-link tier threw: " +
                        ex.GetBaseException().Message);
            }
        }
    }

    private void Walk(
        IList? owners,
        WorldRequirementOwnerKind kind,
        Func<object, Guid> identity,
        Func<object, object?>? container,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int sampled,
        ref int unmodelled,
        ref string firstFailure,
        Func<object, object?>? unlockContainer = null,
        Func<object, object?>? secondUnlockContainer = null)
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

            var start = buffer.Count;
            var ownerId = Guid.Empty;
            try
            {
                ownerId = identity(owner);
                if (ownerId == Guid.Empty) continue;
                sampled += Read(
                    owner,
                    ownerId,
                    kind,
                    container,
                    buffer,
                    ref unmodelled,
                    ref firstFailure,
                    unlockContainer: unlockContainer,
                    secondUnlockContainer: secondUnlockContainer);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                buffer.Truncate(start);
                WorldSkipTally.Skip(
                    ref unmodelled,
                    ref firstFailure,
                    "reading a per-level prerequisite threw: " +
                        ex.GetBaseException().Message);
                if (ownerId == Guid.Empty) continue;
                if (container is not null)
                {
                    var row = Unreadable(
                        ownerId, kind, WorldRequirementProgramKind.NextLevel, UnreadableRequirements);
                    buffer.Append(in row);
                    sampled++;
                }
                if (unlockContainer is not null || secondUnlockContainer is not null)
                {
                    var row = Unreadable(
                        ownerId, kind, WorldRequirementProgramKind.Unlock, UnreadableRequirements);
                    buffer.Append(in row);
                    sampled++;
                }
            }
        }
    }

    private int Read(
        object owner,
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        Func<object, object?>? container,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref string firstFailure,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        Func<object, object?>? unlockContainer = null,
        Func<object, object?>? secondUnlockContainer = null)
    {
        var appended = 0;
        var held = container?.Invoke(owner);
        if (held is not null)
        {
            appended = AppendConditions(
                ownerId, kind, program, _conditions!(held), buffer,
                ref unmodelled, ref firstFailure);
        }

        // Ordinals continue across the owner's programs so that one owner's rows stay contiguous per
        // program after the sort, which is what lets the fold skip a group it has already folded.
        var groupOrdinalBase = 0;
        if (unlockContainer is not null)
        {
            appended += ReadUnlock(
                ownerId, kind, unlockContainer(owner), buffer, appended, ref groupOrdinalBase,
                ref unmodelled, ref firstFailure);
        }

        if (secondUnlockContainer is not null)
        {
            appended += ReadUnlock(
                ownerId, kind, secondUnlockContainer(owner), buffer, appended, ref groupOrdinalBase,
                ref unmodelled, ref firstFailure);
        }

        return appended;
    }

    /// <summary>
    /// One unlock container's conditions, carrying that container's own threshold adjustment.
    /// </summary>
    /// <remarks>
    /// The group ordinals continue where the previous unlock container's left off, so an owner holding
    /// two of them — <c>ResearchSO</c> holds <c>visibilityPrerequisites</c> and
    /// <c>levelVisibilityPrereq</c>, and <c>IsVisible()</c> ANDs their two <c>Check()</c>s — folds as
    /// the AND the existing per-owner walk already performs across distinct group positions, rather
    /// than needing a node kind for the join.
    /// </remarks>
    private int ReadUnlock(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        object? held,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        int ordinalBase,
        ref int groupOrdinalBase,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (held is null) return 0;
        var conditions = _conditions!(held);
        var appended = AppendConditions(
            ownerId,
            kind,
            WorldRequirementProgramKind.Unlock,
            conditions,
            buffer,
            ref unmodelled,
            ref firstFailure,
            _containerAdjustValue!(held),
            ordinalBase,
            groupOrdinalBase);
        groupOrdinalBase += conditions?.Count ?? 0;
        return appended;
    }

    private int ReadContainer(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object held,
        bool publishContainerRoot,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
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
        WorldRelationBuffer<WorldEntityRequirement> buffer,
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
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                $"a prerequisite graph exceeded {MaximumGraphDepth} nested nodes. " +
                    "Entities gated by one are never planned.");
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
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.");
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
        WorldRelationBuffer<WorldEntityRequirement> buffer,
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
            var start = buffer.Count;
            try
            {
                sampled += ReadKnownId(
                    owner, identities[index].EntityId, kind, program, container, buffer,
                    ref unmodelled, ref firstFailure);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                buffer.Truncate(start);
                var row = Unreadable(
                    identities[index].EntityId, kind, program, UnreadableUsageRequirements);
                buffer.Append(in row);
                sampled++;
                WorldSkipTally.Skip(
                    ref unmodelled,
                    ref firstFailure,
                    "reading a usage prerequisite threw: " +
                        ex.GetBaseException().Message);
            }
        }
    }

    private int ReadKnownId(
        object owner,
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        Func<object, object?> container,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
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
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref string firstFailure)
    {
        var row = Unreadable(ownerId, ownerKind, program, UnreadableUsageRequirements);
        buffer.Append(in row);
        WorldSkipTally.Skip(
            ref unmodelled,
            ref firstFailure,
            "an AlchemyRecipeSO usage-prerequisite container was unreadable");
        return 1;
    }

    private const string UnreadableRequirements = "UnreadableRequirements";
    private const string UnreadableUsageRequirements = "UnreadableUsageRequirements";

    private static WorldEntityRequirement Unreadable(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        string typeName) =>
        new(
            ownerId,
            ownerKind,
            0,
            WorldRequirementConditionKind.Unknown,
            typeName,
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
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref string firstFailure,
        BigDouble adjustment = default,
        int ordinalBase = 0,
        int groupOrdinalBase = 0)
    {
        var appended = 0;
        for (var position = 0; position < (conditions?.Count ?? 0); position++)
        {
            var condition = conditions![position];
            if (condition is null) continue;
            var groupOrdinal = groupOrdinalBase + position;

            var accessors = AccessorsFor(condition.GetType());
            if (!accessors.IsComposite)
            {
                var row = accessors.Read(
                    ownerId, ownerKind, ordinalBase + appended, condition, program,
                    WorldRequirementGroupKind.All, groupOrdinal, adjustment);
                if (row.Kind == WorldRequirementConditionKind.List)
                    CaptureList(accessors, condition, row.TargetId, ref unmodelled, ref firstFailure);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            var children = accessors.ReadChildren(condition);
            if (children is null)
            {
                var row = accessors.Unknown(
                    ownerId, ownerKind, ordinalBase + appended, program,
                    accessors.GroupKind, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            if (children.Count == 0)
            {
                // Enumerable.Any(empty) is false; Enumerable.All(empty) is true.
                var row = accessors.EmptyComposite(
                    ownerId, ownerKind, ordinalBase + appended, program, groupOrdinal);
                Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                continue;
            }

            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                if (child is null)
                {
                    var nullRow = accessors.Unknown(
                        ownerId, ownerKind, ordinalBase + appended, program,
                        accessors.GroupKind, groupOrdinal);
                    Append(in nullRow, buffer, ref appended, ref unmodelled, ref firstFailure);
                    continue;
                }
                var childAccessors = AccessorsFor(child.GetType());
                if (!childAccessors.IsComposite)
                {
                    var row = childAccessors.Read(
                        ownerId, ownerKind, ordinalBase + appended, child, program,
                        accessors.GroupKind, groupOrdinal, adjustment);
                    if (row.Kind == WorldRequirementConditionKind.List)
                        CaptureList(childAccessors, child, row.TargetId, ref unmodelled, ref firstFailure);
                    Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
                    continue;
                }
                AppendBranch(
                    ownerId, ownerKind, program, accessors.GroupKind, groupOrdinal, childAccessors,
                    child, buffer, ref appended, ref unmodelled, ref firstFailure, adjustment,
                    ordinalBase);
            }
        }

        return appended;
    }

    /// <summary>
    /// A composite nested inside a group's composite, as one branch of that group.
    /// </summary>
    /// <remarks>
    /// Scholarism's unlock is the one container the game authors this way:
    /// <c>Or(Magic Points capacity, And(World Resets, Novice Spells))</c>. An opposite-operator
    /// nesting shares one branch; a same-operator one folds as its parent, so each of its leaves is a
    /// branch of its own. No container nests a third level, so a composite there reads as unknown.
    /// </remarks>
    private void AppendBranch(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        WorldRequirementGroupKind groupKind,
        int groupOrdinal,
        ConditionAccessors branchAccessors,
        object branch,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int appended,
        ref int unmodelled,
        ref string firstFailure,
        BigDouble adjustment,
        int ordinalBase)
    {
        var leaves = branchAccessors.ReadChildren(branch);
        if (leaves is null || leaves.Count == 0)
        {
            var row = branchAccessors.Unknown(
                ownerId, ownerKind, ordinalBase + appended, program, groupKind, groupOrdinal);
            Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
            return;
        }

        var shared = branchAccessors.GroupKind != groupKind;
        var branchOrdinal = ordinalBase + appended;
        for (var index = 0; index < leaves.Count; index++)
        {
            var leaf = leaves[index];
            var ordinal = ordinalBase + appended;
            var leafBranch = shared ? branchOrdinal : ordinal;
            var leafAccessors = leaf is null ? branchAccessors : AccessorsFor(leaf.GetType());
            var row = leaf is null || leafAccessors.IsComposite
                ? leafAccessors.Unknown(
                    ownerId, ownerKind, ordinal, program, groupKind, groupOrdinal, leafBranch)
                : leafAccessors.Read(
                    ownerId, ownerKind, ordinal, leaf, program, groupKind, groupOrdinal,
                    adjustment, leafBranch);
            if (row.Kind == WorldRequirementConditionKind.List)
                CaptureList(leafAccessors, leaf!, row.TargetId, ref unmodelled, ref firstFailure);
            Append(in row, buffer, ref appended, ref unmodelled, ref firstFailure);
        }
    }

    private static void Append(
        in WorldEntityRequirement row,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int appended,
        ref int unmodelled,
        ref string firstFailure)
    {
        if (row.Kind == WorldRequirementConditionKind.Unknown)
        {
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.");
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
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                "a list requirement names no list variable. " +
                    "Entities gated by one are never planned.");
            return;
        }

        var binding = ListAccessorsFor(list.GetType());
        if (!binding.IsBound)
        {
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                $"this build's {list.GetType().Name} did not expose its membership. " +
                    "Entities gated by one are never planned.");
            return;
        }

        if (!binding.IsStatic(list))
        {
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                "this build compares against a list the run plays into: " +
                    $"{list.GetType().Name}. Entities gated by one are never planned.");
            return;
        }

        var members = binding.Members(list);
        var count = members?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            var member = members![index];
            if (member is null || MemberIdentityFor(member.GetType()) is not null) continue;
            WorldSkipTally.Skip(
                ref unmodelled,
                ref firstFailure,
                $"a list requirement names a {member.GetType().Name} with no readable " +
                    "identity. Entities gated by one are never planned.");
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

        var built = NativeAccessorBinder.StableUuid(memberType);
        _memberIdentities.Add(memberType, built);
        return built;
    }

    internal const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type? ContainerTypeOf(Type owner) =>
        owner.GetField("prerequisitesPerLevel", Instance)?.FieldType;
}
