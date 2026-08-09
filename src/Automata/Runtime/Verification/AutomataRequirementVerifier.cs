using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>The native shape a requirement parity pass reads its oracle out of.</summary>
/// <remarks>
/// Each member names one game predicate, and the level expression is that predicate's own. They are
/// not variations on a theme: an upgrade asks about the level it is about to reach, a structure about
/// the quantity it already has, a research about a level it computes, and a prerequisite-link tier
/// about nothing at all.
/// </remarks>
internal enum RequirementOwnerShape
{
    /// <summary><c>UpgradeSO.HasMetQueuedLevelRequirements()</c>.</summary>
    UpgradeQueuedLevel = 0,

    /// <summary><c>StructureSO.HasMetLevelRequirements()</c>.</summary>
    StructureQuantity = 1,

    /// <summary><c>ResearchSO.MeetsLevelRequirements()</c>.</summary>
    ResearchRequirementLevel = 2,

    /// <summary><c>PrerequisiteLinkSO.LinkDefinition.CheckPassivesEnabled()</c>, once per tier.</summary>
    PrerequisiteLinkTier = 3,
}

/// <summary>
/// Compares the admission verdict the suite derives from the snapshot against the verdict the game's
/// own prerequisite container gives, for real entities in a live session.
/// </summary>
/// <remarks>
/// <para>
/// The offline tests prove the port agrees with values read out of the decompiled source, which is
/// exactly the check a misreading survives: the same misreading would sit in the port and in its
/// expected value. <c>Container.Check(level)</c> is the only oracle that cannot be talked into
/// agreeing with us. It is also the reason the snapshot models conditions at all — Auto Buy planned a
/// purchase the game then refused, and no published fact said why.
/// </para>
/// <para>
/// <b>The oracle is safe to call.</b> The parameterised overload takes the level as an argument and
/// walks the conditions; unlike the no-argument one it neither stamps a game id nor latches
/// <c>available</c>. That difference is the whole reason this verification is possible without the
/// verifier changing the state it is verifying.
/// </para>
/// <para>
/// The level is read from the live entity rather than from the snapshot, so a disagreement means the
/// conditions were evaluated differently rather than that the two sides were asked different
/// questions. Every level expression reproduced here is the game's own, and they are not the same
/// shape; see <see cref="RequirementOwnerShape"/>.
/// </para>
/// <para>
/// <b>Neither side models the container's <c>adjustValue</c>.</b> The parameterised overload the
/// oracle uses builds its <c>ConditionInfo</c> from the level alone, so both sides answer the
/// unadjusted question and the comparison stays honest — but a container authored with a nonzero
/// adjustment is a shortfall this pass cannot see. It is recorded as parity debt rather than papered
/// over here.
/// </para>
/// <para>
/// An entity whose conditions the suite cannot evaluate is unverifiable rather than a mismatch. That
/// is not leniency: an unevaluable verdict already refuses the purchase, so the planner is safe
/// either way, and counting it as a disagreement would drown the signal this pass exists to give in
/// noise from condition classes nobody has modelled yet. The session reports it as an incomplete run
/// and names the first one.
/// </para>
/// </remarks>
internal sealed class AutomataRequirementVerifier
{
    private readonly RequirementContract? _contract;

    internal AutomataRequirementVerifier(Type ownerType, RequirementOwnerShape shape)
    {
        _contract = RequirementContract.TryResolve(ownerType, shape);
    }

    /// <summary>Whether the native members needed to reach the oracle at all were resolved.</summary>
    internal bool IsAvailable => _contract is not null;

    /// <summary>
    /// Verifies one entity. Returns false when the entity could not be read or its conditions could
    /// not be evaluated, which is distinct from the two sides disagreeing.
    /// </summary>
    internal bool TryVerify(
        object entity,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession session,
        out string failure)
    {
        if (entity is null) throw new ArgumentNullException(nameof(entity));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (session is null) throw new ArgumentNullException(nameof(session));

        if (_contract is null)
        {
            failure = "The prerequisite contract is unavailable on this build.";
            return false;
        }

        try
        {
            return TryVerifyCore(_contract, entity, world, run, session, out failure);
        }
        catch (Exception ex)
        {
            failure = $"Reading the entity's prerequisites threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool TryVerifyCore(
        RequirementContract contract,
        object entity,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession session,
        out string failure)
    {
        var entityId = contract.ReadGuid(entity);
        if (contract.IsNeverAsked(entity))
        {
            // The game short-circuits before it reaches the container, so there is no answer of its
            // own to disagree with. Counting it as verified would inflate the pass with entities it
            // never actually compared.
            session.RecordExpectedSkip();
            failure = string.Empty;
            return true;
        }

        var containers = contract.CountContainers(entity);
        var recordedSkip = false;
        for (var containerIndex = 0; containerIndex < containers; containerIndex++)
        {
            var level = contract.ReadCheckLevel(entity);
            var reading = ReadContainer(
                contract, world, entityId, containerIndex, level, out var name);
            if (reading == ContainerReading.Unauthored)
            {
                // The game authored no reference for this condition, so its own Check would
                // dereference nothing and there is no verdict to compare against. Recorded as an
                // expected skip once per entity, the way an owner the game never asks is.
                if (!recordedSkip)
                {
                    session.RecordExpectedSkip();
                    recordedSkip = true;
                }
                continue;
            }

            if (reading == ContainerReading.Unmodelled)
            {
                failure = $"the {name} condition on {entityId} is not modelled.";
                return false;
            }

            var ours = contract.Evaluate(world, entityId, containerIndex, level);
            var theirs = contract.InvokeCheck(entity, containerIndex, level);

            run.Compare(
                entityId,
                contract.Label(containerIndex, level),
                ours == WorldRequirementVerdict.Met ? BigDouble.One : BigDouble.Zero,
                theirs ? BigDouble.One : BigDouble.Zero);
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>Why one container cannot be compared, if it cannot.</summary>
    private enum ContainerReading
    {
        Comparable = 0,

        /// <summary>A condition of a modelled class whose target the game never authored.</summary>
        Unauthored = 1,

        /// <summary>A condition this suite cannot evaluate. A gap in the suite, not in the game.</summary>
        Unmodelled = 2,
    }

    /// <summary>
    /// Whether every published condition in this container could be evaluated, and if not, whether
    /// the shortfall is the suite's or the game's — naming the class either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walked here rather than inside the evaluator so that the evaluator stays a predicate. What a
    /// consumer needs is the verdict; what an operator reading this pass needs is the class name that
    /// produced it, and only the verifier needs both.
    /// </para>
    /// <para>
    /// A condition of a modelled class whose target is the empty identity is a reference the game's
    /// author never filled in. Both sides refuse it — the suite because nothing can satisfy an
    /// entity that does not exist, the game because its own comparison reads a field off the missing
    /// reference — so the pass declines to compare the container rather than reporting a modelling
    /// gap it does not have. It declines the whole container even where an earlier condition would
    /// have short-circuited the game's own walk: not reaching into an oracle that can throw is worth
    /// more than the one comparison it would buy.
    /// </para>
    /// </remarks>
    private static ContainerReading ReadContainer(
        RequirementContract contract,
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long level,
        out string conditionTypeName)
    {
        conditionTypeName = string.Empty;
        if (!contract.TryFindRows(world, ownerId, containerIndex, out var start, out var count))
        {
            return ContainerReading.Comparable;
        }

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.NodeKind == WorldRequirementNodeKind.Group) continue;
            if (!NamesNothing(in row)) continue;

            conditionTypeName = Name(in row);
            return ContainerReading.Unauthored;
        }

        if (contract.Evaluate(world, ownerId, containerIndex, level) !=
            WorldRequirementVerdict.Unevaluable)
        {
            return ContainerReading.Comparable;
        }

        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.NodeKind == WorldRequirementNodeKind.Group) continue;
            if (WorldRequirementEvaluator.Evaluate(world, in row, level) !=
                WorldRequirementVerdict.Unevaluable)
            {
                continue;
            }

            conditionTypeName = Name(in row);
            return ContainerReading.Unmodelled;
        }

        return ContainerReading.Comparable;
    }

    /// <summary>
    /// A modelled comparison pointed at nothing. The two classes that carry no target of their own —
    /// an unmodelled class and an authored empty composite — are not this.
    /// </summary>
    private static bool NamesNothing(in WorldEntityRequirement row) =>
        row.TargetId == Guid.Empty &&
        row.Kind != WorldRequirementConditionKind.Unknown &&
        row.Kind != WorldRequirementConditionKind.Literal;

    private static string Name(in WorldEntityRequirement row) =>
        row.ConditionTypeName.Length == 0 ? "unnamed" : row.ConditionTypeName;

    /// <summary>
    /// The reflected members needed to ask the game its own answer, for one owner shape. Resolved
    /// once; a missing member makes the whole verifier unavailable rather than partial.
    /// </summary>
    private sealed class RequirementContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly RequirementOwnerShape _shape;
        private readonly MethodInfo _check;
        private readonly ConstructorInfo _conditionInfo;
        private readonly MethodInfo _getGuid;

        // One of these two reaches the container: a field on the owner, or a tier list whose entries
        // each hold one.
        private readonly FieldInfo? _container;
        private readonly FieldInfo? _tiers;
        private readonly FieldInfo? _tierContainer;

        private readonly FieldInfo? _level;
        private readonly FieldInfo? _queuedLevels;
        private readonly MethodInfo? _requirementLevel;

        private RequirementContract(
            RequirementOwnerShape shape,
            MethodInfo check,
            ConstructorInfo conditionInfo,
            MethodInfo getGuid,
            FieldInfo? container,
            FieldInfo? tiers,
            FieldInfo? tierContainer,
            FieldInfo? level,
            FieldInfo? queuedLevels,
            MethodInfo? requirementLevel)
        {
            _shape = shape;
            _check = check;
            _conditionInfo = conditionInfo;
            _getGuid = getGuid;
            _container = container;
            _tiers = tiers;
            _tierContainer = tierContainer;
            _level = level;
            _queuedLevels = queuedLevels;
            _requirementLevel = requirementLevel;
        }

        internal static RequirementContract? TryResolve(Type ownerType, RequirementOwnerShape shape)
        {
            if (ownerType is null) return null;

            var getGuid = ownerType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
            if (getGuid is null || getGuid.ReturnType != typeof(Guid)) return null;

            FieldInfo? container = null;
            FieldInfo? tiers = null;
            FieldInfo? tierContainer = null;
            Type containerType;

            if (shape == RequirementOwnerShape.PrerequisiteLinkTier)
            {
                tiers = ownerType.GetField("linkTiers", Instance);
                var tierType = tiers?.FieldType.IsGenericType == true
                    ? tiers.FieldType.GetGenericArguments()[0]
                    : null;
                tierContainer = tierType?.GetField("prerequisites", Instance);
                if (tierContainer is null) return null;
                containerType = tierContainer.FieldType;
            }
            else
            {
                container = ownerType.GetField(
                    shape == RequirementOwnerShape.ResearchRequirementLevel
                        ? "levelPrerequisites"
                        : "prerequisitesPerLevel",
                    Instance);
                if (container is null) return null;
                containerType = container.FieldType;
            }

            FieldInfo? level = null;
            FieldInfo? queuedLevels = null;
            MethodInfo? requirementLevel = null;
            switch (shape)
            {
                case RequirementOwnerShape.UpgradeQueuedLevel:
                    level = ownerType.GetField("level", Instance);
                    queuedLevels = ownerType.GetField("queuedLevels", Instance);
                    if (level is null || queuedLevels is null) return null;
                    break;
                case RequirementOwnerShape.StructureQuantity:
                    level = ownerType.GetField("quantity", Instance);
                    if (level is null) return null;
                    break;
                case RequirementOwnerShape.ResearchRequirementLevel:
                    requirementLevel = ownerType.GetMethod(
                        "GetRequirementLevel", Instance, null, Type.EmptyTypes, null);
                    if (requirementLevel is null) return null;
                    break;
            }

            // The parameter type is taken from the overload rather than resolved by name, so the
            // verifier cannot bind to a same-named type from somewhere else in the domain.
            var check = FindParameterisedCheck(containerType);
            if (check is null) return null;

            var conditionInfo = check.GetParameters()[0].ParameterType
                .GetConstructor(new[] { typeof(long) });
            return conditionInfo is null
                ? null
                : new RequirementContract(
                    shape, check, conditionInfo, getGuid,
                    container, tiers, tierContainer, level, queuedLevels, requirementLevel);
        }

        /// <summary>
        /// The one-argument <c>Check</c>. The no-argument overload of the same name latches and stamps,
        /// so picking by name alone would turn this verifier into a mutation.
        /// </summary>
        private static MethodInfo? FindParameterisedCheck(Type containerType)
        {
            foreach (var candidate in containerType.GetMethods(Instance))
            {
                if (candidate.Name != "Check" || candidate.ReturnType != typeof(bool)) continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsValueType) return candidate;
            }

            return null;
        }

        internal Guid ReadGuid(object entity) => (Guid)_getGuid.Invoke(entity, null)!;

        /// <summary>
        /// Whether the game reaches its own container at all. <c>MeetsLevelRequirements()</c> answers
        /// true outright when <c>GetRequirementLevel()</c> is negative, and never looks at the
        /// conditions.
        /// </summary>
        internal bool IsNeverAsked(object entity) =>
            _shape == RequirementOwnerShape.ResearchRequirementLevel && ReadCheckLevel(entity) < 0L;

        /// <summary>How many containers this owner holds. One, unless it is a tiered link.</summary>
        internal int CountContainers(object entity) =>
            _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? (_tiers!.GetValue(entity) as IList)?.Count ?? 0
                : 1;

        /// <summary>
        /// The level the game itself would check at: <c>level + queuedLevels + 1</c> for an upgrade's
        /// <c>HasMetQueuedLevelRequirements()</c>, the bare <c>quantity</c> for a structure's
        /// <c>HasMetLevelRequirements()</c>, <c>GetRequirementLevel()</c> for a research's
        /// <c>MeetsLevelRequirements()</c>, and zero for a link tier, whose gate is reached through
        /// the no-argument <c>Check()</c> and has no level of its own.
        /// </summary>
        internal long ReadCheckLevel(object entity) => _shape switch
        {
            RequirementOwnerShape.UpgradeQueuedLevel =>
                Convert.ToInt64(_level!.GetValue(entity)) +
                Convert.ToInt64(_queuedLevels!.GetValue(entity)) + 1L,
            RequirementOwnerShape.StructureQuantity => Convert.ToInt64(_level!.GetValue(entity)),
            RequirementOwnerShape.ResearchRequirementLevel =>
                Convert.ToInt64(_requirementLevel!.Invoke(entity, null)),
            _ => 0L,
        };

        /// <summary>
        /// The snapshot's answer. A tiered link publishes one container per tier as a parent-and-child
        /// graph; every other owner publishes one flat set of groups, and the two are different walks.
        /// </summary>
        internal WorldRequirementVerdict Evaluate(
            GameWorldState world,
            Guid ownerId,
            int containerIndex,
            long level) =>
            _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? WorldRequirementEvaluator.EvaluateContainer(world, ownerId, containerIndex, level)
                : WorldRequirementEvaluator.Evaluate(world, ownerId, level);

        internal bool TryFindRows(
            GameWorldState world,
            Guid ownerId,
            int containerIndex,
            out int start,
            out int count) =>
            _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? WorldEntityRequirementLookup.TryFindContainerRange(
                    world.EntityRequirements, ownerId, containerIndex, out start, out count)
                : WorldEntityRequirementLookup.TryFindRange(
                    world.EntityRequirements, ownerId, out start, out count);

        internal string Label(int containerIndex, long level) =>
            _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? $"tier{containerIndex}@{level}"
                : $"requirements@{level}";

        internal bool InvokeCheck(object entity, int containerIndex, long level)
        {
            var tier = _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? (_tiers!.GetValue(entity) as IList)?[containerIndex]
                : entity;
            if (tier is null) return true;

            var container = _shape == RequirementOwnerShape.PrerequisiteLinkTier
                ? _tierContainer!.GetValue(tier)
                : _container!.GetValue(tier);
            if (container is null) return true;

            var info = _conditionInfo.Invoke(new object[] { level });
            return (bool)_check.Invoke(container, new[] { info })!;
        }
    }
}
