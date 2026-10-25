using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Live differential oracle for the usage-prerequisite programs published on Concept recipes.
/// </summary>
/// <remarks>
/// It asks through the parameterised <c>Check</c> at level zero. The no-argument overload latches
/// <c>available</c> and stamps a frame, so it would turn this read into a mutation, and once latched
/// it would keep answering for conditions that no longer hold. The game authors no adjustment on a
/// usage container, so level zero is the question the no-argument overload asks.
/// </remarks>
internal sealed class AutomataUsagePrerequisiteVerifier
{
    private const BindingFlags Instance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly FieldInfo? _container;
    private readonly MethodInfo? _check;
    private readonly ConstructorInfo? _conditionInfo;
    private readonly MethodInfo? _getGuid;

    internal AutomataUsagePrerequisiteVerifier(Type ownerType)
    {
        _container = ownerType?.GetField("usagePrerequisites", Instance);
        _getGuid = ownerType is null ? null : RuntimeIdentityRegistryBinding.Shared.StableUuidMethod(ownerType);
        _check = _container is null ? null : AutomataRequirementVerifier.FindParameterisedCheck(_container.FieldType);
        _conditionInfo = _check?.GetParameters()[0].ParameterType.GetConstructor(new[] { typeof(long) });
    }

    internal bool IsAvailable =>
        _container is not null && _check is not null && _conditionInfo is not null && _getGuid is not null;

    internal bool TryVerify(object entity, GameWorldState world, DifferentialRun run, out string failure)
    {
        if (!IsAvailable)
        {
            failure = "The usage-prerequisite oracle contract is unavailable on this build.";
            return false;
        }

        try
        {
            var entityId = (Guid)_getGuid!.Invoke(entity, null)!;
            if (!CanEvaluateUsage(world, entityId))
            {
                // The class the suite does not model is named once, on the collection line that
                // owns that fact. Here the entity is named the way every other sentence names one,
                // and what went unchecked is said in the words of the screen it is read on.
                failure = "a condition this suite does not model holds them, first on " +
                    EntityIdentityFormatter.PlayerHandle(entityId) +
                    ", so whether the game lets you use those concepts is unchecked.";
                return false;
            }

            var ours = WorldRequirementEvaluator.Evaluate(
                world, entityId, level: 0, WorldRequirementProgramKind.Usage);
            var container = _container!.GetValue(entity);
            var theirs = container is null ||
                (bool)_check!.Invoke(container, new[] { _conditionInfo!.Invoke(new object[] { 0L }) })!;
            run.Compare(
                entityId,
                "usage-prerequisites",
                ours == WorldRequirementVerdict.Met ? BigDouble.One : BigDouble.Zero,
                theirs ? BigDouble.One : BigDouble.Zero);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"Reading usage prerequisites threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool CanEvaluateUsage(GameWorldState world, Guid ownerId)
    {
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, ownerId, out var start, out var count)) return true;
        if (WorldRequirementEvaluator.Evaluate(
                world, ownerId, level: 0, WorldRequirementProgramKind.Usage) !=
            WorldRequirementVerdict.Unevaluable) return true;

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.Program != WorldRequirementProgramKind.Usage ||
                row.NodeKind != WorldRequirementNodeKind.Leaf) continue;
            if (WorldRequirementEvaluator.Evaluate(world, in row, 0) !=
                WorldRequirementVerdict.Unevaluable) continue;
            return false;
        }

        return true;
    }
}
