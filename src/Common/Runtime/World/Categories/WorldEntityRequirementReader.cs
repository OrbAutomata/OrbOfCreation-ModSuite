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
        var firstFailure = default(WorldCollectionFailure);

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
            frame.Report.For("alchemy recipes").Failure,
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
        ref WorldCollectionFailure firstFailure)
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
            var ownerId = Guid.Empty;
            var linkStart = buffer.Count;
            var linkSampled = sampled;
            var linkUnmodelled = unmodelled;
            var linkFailure = firstFailure;
            try
            {
                ownerId = _prerequisiteLinkId!(link);
                if (ownerId == Guid.Empty) continue;
                var tiers = _prerequisiteLinkTiers!(link);
                if (tiers is null)
                {
                    AppendCaptureGap(ownerId, WorldRequirementOwnerKind.PrerequisiteLink, -1,
                        WorldRequirementProgramKind.NextLevel, buffer);
                    WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                        "a prerequisite-link tier list was unreadable");
                    continue;
                }
                var tierCount = tiers.Count;
                for (var tier = 0; tier < tierCount; tier++)
                {
                    var start = buffer.Count;
                    var previousUnmodelled = unmodelled;
                    var previousFailure = firstFailure;
                    try
                    {
                        var definition = tiers[tier];
                        var held = definition is null ? null : _prerequisiteLinkTierContainer!(definition);
                        if (held is null)
                        {
                            AppendCaptureGap(ownerId, WorldRequirementOwnerKind.PrerequisiteLink, tier,
                                WorldRequirementProgramKind.NextLevel, buffer);
                            WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                                "a prerequisite-link tier container was unreadable");
                            continue;
                        }
                        sampled += ReadContainer(ownerId, WorldRequirementOwnerKind.PrerequisiteLink,
                            tier, held, publishContainerRoot: true, buffer,
                            ref unmodelled, ref firstFailure);
                    }
                    catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
                    {
                        // A prefix of an AND is not the authored gate. Discard the whole tier and
                        // continue capturing later tiers, whose truth is independent of this fault.
                        buffer.Truncate(start);
                        unmodelled = previousUnmodelled;
                        firstFailure = previousFailure;
                        AppendCaptureGap(ownerId, WorldRequirementOwnerKind.PrerequisiteLink, tier,
                            WorldRequirementProgramKind.NextLevel, buffer);
                        WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                            WorldFailures.ReadFault("reading a prerequisite-link tier threw", ex, ownerId));
                    }
                }
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                buffer.Truncate(linkStart);
                sampled = linkSampled;
                unmodelled = linkUnmodelled;
                firstFailure = linkFailure;
                if (ownerId != Guid.Empty)
                    AppendCaptureGap(ownerId, WorldRequirementOwnerKind.PrerequisiteLink, -1,
                        WorldRequirementProgramKind.NextLevel, buffer);
                WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                    WorldFailures.ReadFault("reading a prerequisite-link tier list threw", ex, ownerId));
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
        ref WorldCollectionFailure firstFailure,
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
            var previousUnmodelled = unmodelled;
            var previousFailure = firstFailure;
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
                unmodelled = previousUnmodelled;
                firstFailure = previousFailure;
                WorldSkipTally.Skip(
                    ref unmodelled,
                    ref firstFailure,
                    WorldFailures.ReadFault("reading a per-level prerequisite threw", ex, ownerId));
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
        ref WorldCollectionFailure firstFailure,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        Func<object, object?>? unlockContainer = null,
        Func<object, object?>? secondUnlockContainer = null)
    {
        var appended = 0;
        // A missing accessor means this owner declares no program (for example Resource NextLevel).
        // A declared accessor returning null is unreadable authored data, never an empty program.
        if (container is not null)
            appended = ReadRequiredContainer(ownerId, kind, program, container(owner), buffer, 0,
                ref unmodelled, ref firstFailure);

        // Both research unlock containers share the implicit AND root, with distinct ordinals.
        if (unlockContainer is not null)
            appended += ReadRequiredContainer(ownerId, kind, WorldRequirementProgramKind.Unlock,
                unlockContainer(owner), buffer, appended, ref unmodelled, ref firstFailure);
        if (secondUnlockContainer is not null)
            appended += ReadRequiredContainer(ownerId, kind, WorldRequirementProgramKind.Unlock,
                secondUnlockContainer(owner), buffer, appended, ref unmodelled, ref firstFailure);
        return appended;
    }

    private int ReadRequiredContainer(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        object? held,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        int ordinalBase,
        ref int unmodelled,
        ref WorldCollectionFailure firstFailure)
    {
        if (held is null)
            return AppendUnreadableConditions(ownerId, kind, program, buffer,
                ref unmodelled, ref firstFailure, UnreadableRequirements,
                $"a required {program} prerequisite container was unreadable", ordinal: ordinalBase);
        return AppendConditions(ownerId, kind, program, _conditions!(held), buffer,
            ref unmodelled, ref firstFailure,
            program == WorldRequirementProgramKind.Unlock ? _containerAdjustValue!(held) : default,
            ordinalBase);
    }

    private int ReadContainer(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        object held,
        bool publishContainerRoot,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref WorldCollectionFailure firstFailure)
    {
        var conditions = _conditions!(held);
        if (conditions is null)
            return AppendUnreadableConditions(ownerId, kind, WorldRequirementProgramKind.NextLevel,
                buffer, ref unmodelled, ref firstFailure, UnreadableRequirements,
                "a prerequisite-link tier condition list was unreadable", containerIndex);
        var count = conditions.Count;
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
        object? condition,
        int parentOrdinal,
        int depth,
        ref int nextOrdinal,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref WorldCollectionFailure firstFailure,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel,
        BigDouble adjustment = default)
    {
        var start = nextOrdinal;
        var pending = new Stack<ConditionWalkFrame>();
        var ancestors = new HashSet<object>(WorldObjectReferenceComparer.Instance);
        pending.Push(new ConditionWalkFrame(condition, parentOrdinal, depth));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Exit)
            {
                ancestors.Remove(current.Condition!);
                continue;
            }

            var ordinal = nextOrdinal++;
            var value = current.Condition;
            if (value is null || ancestors.Contains(value))
            {
                var unknown = new WorldEntityRequirement(ownerId, kind, containerIndex, ordinal,
                    current.ParentOrdinal, current.Depth, WorldRequirementNodeKind.Leaf,
                    WorldRequirementOperator.None, WorldRequirementConditionKind.Unknown,
                    value is null ? "NullRequirementCondition" : "CyclicRequirementGraph",
                    Guid.Empty, -1, 0d, default, default, program, adjustment);
                buffer.Append(in unknown);
                WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                    value is null
                        ? "a prerequisite graph contained a null condition. Entities gated by one are never planned."
                        : "a prerequisite graph contained a reference cycle. Entities gated by one are never planned.");
                continue;
            }

            var accessors = AccessorsFor(value.GetType());
            var row = accessors.ReadGraph(ownerId, kind, containerIndex, ordinal,
                current.ParentOrdinal, current.Depth, value, program, adjustment);
            var children = row.NodeKind == WorldRequirementNodeKind.Group
                ? accessors.ReadChildren(value) : null;
            if (row.NodeKind == WorldRequirementNodeKind.Group && children is null)
                row = new WorldEntityRequirement(ownerId, kind, containerIndex, ordinal,
                    current.ParentOrdinal, current.Depth, WorldRequirementNodeKind.Leaf,
                    WorldRequirementOperator.None, WorldRequirementConditionKind.Unknown,
                    row.ConditionTypeName, Guid.Empty, -1, 0d, default, default, program, adjustment);
            if (row.Kind == WorldRequirementConditionKind.List)
                CaptureList(accessors, value, row.TargetId, ref unmodelled, ref firstFailure);
            if (row.Kind == WorldRequirementConditionKind.Unknown && row.NodeKind == WorldRequirementNodeKind.Leaf)
                WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                    "this build authors a condition this suite does not model: " +
                    $"{row.ConditionTypeName}. Entities gated by one are never planned.");
            buffer.Append(in row);
            if (row.NodeKind != WorldRequirementNodeKind.Group) continue;

            // Only ancestors are a cycle. The same child authored in two branches is captured twice
            // with distinct ordinals, preserving the native tree's comparisons and adjustments.
            ancestors.Add(value);
            pending.Push(new ConditionWalkFrame(value, current.ParentOrdinal, current.Depth, exit: true));
            for (var index = children!.Count - 1; index >= 0; index--)
                pending.Push(new ConditionWalkFrame(children[index], ordinal, current.Depth + 1));
        }
        return nextOrdinal - start;
    }

    private readonly struct ConditionWalkFrame
    {
        internal ConditionWalkFrame(object? condition, int parentOrdinal, int depth, bool exit = false)
        {
            Condition = condition;
            ParentOrdinal = parentOrdinal;
            Depth = depth;
            Exit = exit;
        }
        internal object? Condition { get; }
        internal int ParentOrdinal { get; }
        internal int Depth { get; }
        internal bool Exit { get; }
    }

    private static void AppendCaptureGap(
        Guid ownerId,
        WorldRequirementOwnerKind kind,
        int containerIndex,
        WorldRequirementProgramKind program,
        WorldRelationBuffer<WorldEntityRequirement> buffer)
    {
        var gap = new WorldEntityRequirement(ownerId, kind, containerIndex, 0, -1, 0,
            WorldRequirementNodeKind.CaptureGap, WorldRequirementOperator.None,
            WorldRequirementConditionKind.Unknown, "IncompleteRequirementCapture", Guid.Empty,
            -1, 0d, default, default, program);
        buffer.Append(in gap);
    }

    private void WalkKnownIds(
        IList? owners,
        WorldRequirementOwnerKind kind,
        WorldRequirementProgramKind program,
        WorldSampleBuffer<WorldAlchemyRecipe, WorldAlchemyRecipe> identities,
        WorldCollectionFailure identityFailure,
        Func<object, object?> container,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int sampled,
        ref int unmodelled,
        ref WorldCollectionFailure firstFailure)
    {
        if (owners is null || owners.Count != identities.Count)
        {
            var failure = WorldFailures.Dependency(
                "the AlchemyRecipeSO identity snapshot was incomplete", identityFailure);
            WorldSkipTally.Skip(ref unmodelled, ref firstFailure, failure);
            // No positional identity mapping is authoritative here. Capture incompleteness belongs
            // only to this program/container, independently of whether the upstream cause can retry.
            AppendCaptureGap(Guid.Empty, kind, 0, program, buffer);
            return;
        }

        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            if (owner is null) continue;
            var start = buffer.Count;
            var previousUnmodelled = unmodelled;
            var previousFailure = firstFailure;
            try
            {
                sampled += ReadKnownId(
                    owner, identities[index].EntityId, kind, program, container, buffer,
                    ref unmodelled, ref firstFailure);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                buffer.Truncate(start);
                unmodelled = previousUnmodelled;
                firstFailure = previousFailure;
                var row = Unreadable(
                    identities[index].EntityId, kind, program, UnreadableUsageRequirements);
                buffer.Append(in row);
                sampled++;
                WorldSkipTally.Skip(
                    ref unmodelled,
                    ref firstFailure,
                    WorldFailures.ReadFault("reading a usage prerequisite threw", ex, identities[index].EntityId));
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
        ref WorldCollectionFailure firstFailure)
    {
        var held = container(owner);
        var conditions = held is null ? null : _alchemyConditions!(held);
        if (conditions is null) return AppendUnreadableConditions(
            ownerId, kind, program, buffer, ref unmodelled, ref firstFailure,
            UnreadableUsageRequirements, "an AlchemyRecipeSO usage-prerequisite container was unreadable");
        return AppendConditions(
            ownerId, kind, program, conditions, buffer, ref unmodelled, ref firstFailure);
    }

    private static int AppendUnreadableConditions(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        WorldRelationBuffer<WorldEntityRequirement> buffer,
        ref int unmodelled,
        ref WorldCollectionFailure firstFailure,
        string typeName,
        string reason,
        int containerIndex = 0,
        int ordinal = 0)
    {
        var row = Unreadable(ownerId, ownerKind, program, typeName, containerIndex, ordinal);
        buffer.Append(in row);
        WorldSkipTally.Skip(ref unmodelled, ref firstFailure, reason);
        return 1;
    }

    private const string UnreadableRequirements = "UnreadableRequirements";
    private const string UnreadableUsageRequirements = "UnreadableUsageRequirements";

    private static WorldEntityRequirement Unreadable(
        Guid ownerId,
        WorldRequirementOwnerKind ownerKind,
        WorldRequirementProgramKind program,
        string typeName,
        int containerIndex = 0,
        int ordinal = 0) =>
        new(
            ownerId,
            ownerKind,
            containerIndex,
            ordinal,
            parentOrdinal: -1,
            depth: 0,
            WorldRequirementNodeKind.Leaf,
            WorldRequirementOperator.None,
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
        ref WorldCollectionFailure firstFailure,
        BigDouble adjustment = default,
        int ordinalBase = 0)
    {
        if (conditions is null)
            return AppendUnreadableConditions(ownerId, ownerKind, program, buffer,
                ref unmodelled, ref firstFailure, UnreadableRequirements,
                $"a {program} prerequisite condition list was unreadable", ordinal: ordinalBase);
        var nextOrdinal = ordinalBase;
        for (var position = 0; position < conditions.Count; position++)
        {
            var condition = conditions![position];
            ReadCondition(ownerId, ownerKind, 0, condition, -1, 0, ref nextOrdinal,
                buffer, ref unmodelled, ref firstFailure, program, adjustment);
        }
        return nextOrdinal - ordinalBase;
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
        ref WorldCollectionFailure firstFailure)
    {
        if (_lists is null || listId == Guid.Empty || !_capturedLists.Add(listId)) return;

        var start = _lists.Count;
        try
        {
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
            if (members is null)
            {
                WorldSkipTally.Skip(ref unmodelled, ref firstFailure,
                    "a static prerequisite list's membership was unreadable. " +
                        "Entities gated by one are never planned.");
                return;
            }
            var count = members.Count;
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
        catch
        {
            _lists.Truncate(start);
            _capturedLists.Remove(listId);
            throw;
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
