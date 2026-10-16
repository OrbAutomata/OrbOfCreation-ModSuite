using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal interface IMentorNativePort
{
    MentorNativeGrant Grant(in MentorCycleAction action);
}

internal sealed class MentorNativeAdapter : IMentorNativePort, IDisposable
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly TypedRegistryResolver _registry;
    private readonly AlchemyGameplayDomainClassifier _alchemy;

    internal MentorNativeAdapter(
        TypedRegistryResolver? registry = null,
        AlchemyGameplayDomainClassifier? alchemy = null)
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
        _alchemy = alchemy ?? new AlchemyGameplayDomainClassifier();
    }

    public MentorNativeGrant Grant(in MentorCycleAction action)
    {
        var typeName = action.Domain switch
        {
            MasteryExperienceDomain.Spell => "SpellRecipeSO",
            MasteryExperienceDomain.Artifact => "EquipmentSO",
            _ => "AlchemyRecipeSO",
        };
        var type = ReflectionUtil.FindLoadedType(typeName);
        if (type is null)
            return Contract($"native {typeName} type is unavailable");
        var resolution = _registry.Resolve(action.RecipientId, type);
        if (!resolution.IsResolved)
            return resolution.IsRetryable
                ? Identity(resolution.Format())
                : Contract(resolution.Format());
        var recipient = resolution.Value!;

        try
        {
            var mastery = ReadInt(recipient, "masteryLevel");
            if (!IsAvailable(recipient, action.Domain) ||
                mastery >= action.MasteryCeilingExclusive)
            {
                return new MentorNativeGrant(
                    MentorNativeGrantStatus.RecipientIneligible,
                    "recipient is no longer available below the source mastery ceiling");
            }
            if (action.Domain == MasteryExperienceDomain.Alchemy)
            {
                if (!_alchemy.TryInitialize(out var reason))
                    return _alchemy.Status == AlchemyDomainClassifierStatus.Blocked
                        ? Contract(reason)
                        : Identity(reason);
                var classification = _alchemy.ClassifyRecipe(recipient);
                if (classification.Domain != AlchemyGameplayDomain.OrdinaryAlchemy ||
                    !classification.IsMutationGrade)
                    return Contract(
                        "recipient is not proven to be an ordinary alchemy recipe: " +
                        classification.Reason);
            }

            return action.Domain == MasteryExperienceDomain.Artifact
                ? GrantArtifact(recipient, action.Amount)
                : GrantDirect(recipient, action.Domain, action.Amount);
        }
        catch (Exception ex) when (
            ex is TargetInvocationException or ArgumentException or InvalidOperationException or
            TargetException or MemberAccessException or MissingMemberException or FormatException or
            OverflowException)
        {
            return Contract(ex.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle() => _alchemy.InvalidateLifecycle();

    public void Dispose() => _alchemy.Dispose();

    private static MentorNativeGrant GrantDirect(
        object recipient,
        MasteryExperienceDomain domain,
        MentorAmount amount)
    {
        var experienceField = RequiredField(
            recipient.GetType(),
            domain == MasteryExperienceDomain.Spell ? "masteryExperience" : "masteryXp");
        var method = RequiredMethod(
            recipient.GetType(),
            domain == MasteryExperienceDomain.Spell ? "GainMasteryExp" : "GainMasteryXp",
            typeof(BigDouble));
        var expected = new BigDouble(amount.Mantissa, amount.Exponent);
        var evidence = NativeMutationVerifier.Execute(
            "Mentor mastery XP grant",
            ReflectionUtil.ReadStableId(recipient) ?? "<unknown>",
            "mastery XP increased",
            () => ReadBigDouble(experienceField, recipient),
            () =>
            {
                using var ignored = MentorMasteryPatchBridge.Suppress();
                method.Invoke(recipient, new object[] { expected });
            },
            (before, after) => after.CompareTo(before) > 0);
        return FromEvidence(evidence);
    }

    private static MentorNativeGrant GrantArtifact(object equipment, MentorAmount amount)
    {
        var equipmentType = equipment.GetType();
        var savedXp = RequiredField(equipmentType, "masteryXp");
        var getContainer = RequiredMethod(equipmentType, "GetExperienceElement");
        var gainLevels = RequiredMethod(equipmentType, "GainMasteryLevels", typeof(int));
        var container = getContainer.Invoke(equipment, null) ??
                        throw new MissingMemberException("artifact experience container is unavailable");
        var containerType = container.GetType();
        var gain = RequiredMethod(containerType, "GainExperience", typeof(BigDouble));
        var gainedLevels = RequiredMethod(containerType, "GetGainedLevels");
        var getLevel = RequiredMethod(containerType, "GetLevel");
        var getExperience = RequiredMethod(containerType, "GetExperience");
        var value = new BigDouble(amount.Mantissa, amount.Exponent);

        var evidence = NativeMutationVerifier.Execute(
            "Mentor artifact mastery XP grant",
            ReflectionUtil.ReadStableId(equipment) ?? "<unknown>",
            "artifact mastery progress increased",
            () => CaptureArtifactProgress(container, getLevel, getExperience),
            () =>
            {
                using var ignored = MentorMasteryPatchBridge.Suppress();
                gain.Invoke(container, new object[] { value });
                var actualLevels = Convert.ToInt32(gainedLevels.Invoke(container, null) ?? 0);
                if (actualLevels > 0) gainLevels.Invoke(equipment, new object[] { actualLevels });
                var actualXp = (BigDouble)(getExperience.Invoke(container, null) ??
                                           throw new MissingMemberException("artifact XP is unavailable"));
                savedXp.SetValue(equipment, actualXp);
            },
            static (before, after) => after.IsAfter(before));
        return FromEvidence(evidence);
    }

    private static MentorArtifactProgress CaptureArtifactProgress(
        object container,
        MethodInfo getLevel,
        MethodInfo getExperience) =>
        new(
            Convert.ToInt32(getLevel.Invoke(container, null) ?? 0),
            (BigDouble)(getExperience.Invoke(container, null) ??
                        throw new MissingMemberException("artifact XP is unavailable")));

    private static MentorNativeGrant FromEvidence<T>(NativeMutationEvidence<T> evidence)
    {
        var call = NativeMutationCallOutcome.FromEvidence(evidence);
        return evidence.IsVerified
            ? new MentorNativeGrant(
                MentorNativeGrantStatus.Committed, string.Empty, evidence.Outcome, call)
            : new MentorNativeGrant(
                MentorNativeGrantStatus.PostconditionFailed,
                evidence.Format(static state => state?.ToString() ?? "<null>"),
                evidence.Outcome,
                call);
    }

    private static bool IsAvailable(object recipient, MasteryExperienceDomain domain)
    {
        var name = domain switch
        {
            MasteryExperienceDomain.Spell => "IsDiscovered",
            MasteryExperienceDomain.Artifact => "isCreated",
            _ => "IsAvailable",
        };
        return domain == MasteryExperienceDomain.Artifact
            ? RequiredField(recipient.GetType(), name).GetValue(recipient) is true
            : RequiredMethod(recipient.GetType(), name).Invoke(recipient, null) is true;
    }

    private static int ReadInt(object target, string field) =>
        Convert.ToInt32(RequiredField(target.GetType(), field).GetValue(target) ?? 0);

    private static BigDouble ReadBigDouble(FieldInfo field, object target) =>
        (BigDouble)(field.GetValue(target) ??
                    throw new MissingMemberException(field.Name + " is unavailable"));
    private static FieldInfo RequiredField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                InstanceFlags | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method is not null) return method;
        }
        throw new MissingMethodException(type.FullName, name);
    }

    private static MentorNativeGrant Contract(string reason) =>
        new(MentorNativeGrantStatus.ContractUnavailable, reason);

    private static MentorNativeGrant Identity(string reason) =>
        new(MentorNativeGrantStatus.IdentityChanged, reason);

    private readonly struct MentorArtifactProgress
    {
        internal MentorArtifactProgress(int level, BigDouble experience)
        {
            Level = level;
            Experience = experience;
        }

        internal int Level { get; }
        internal BigDouble Experience { get; }

        internal bool IsAfter(in MentorArtifactProgress before) =>
            Level > before.Level ||
            (Level == before.Level && Experience.CompareTo(before.Experience) > 0);

        public override string ToString() => $"level={Level}, xp={Experience}";
    }
}
