using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal interface IMentorNativePort
{
    NativeActionOutcome Grant(in MentorCycleAction action);
}

internal sealed class MentorNativeAdapter : IMentorNativePort, IDisposable
{
    private readonly TypedRegistryResolver _registry;
    private readonly AlchemyGameplayDomainClassifier _alchemy;
    private readonly MentorNativeBindings? _bindings;
    private readonly string _bindingFailure;

    internal MentorNativeAdapter(
        TypedRegistryResolver? registry = null,
        AlchemyGameplayDomainClassifier? alchemy = null,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
        _alchemy = alchemy ?? new AlchemyGameplayDomainClassifier();
        MentorNativeBindings.TryCreate(out _bindings, out _bindingFailure, resolveType, includeContract);
    }

    public NativeActionOutcome Grant(in MentorCycleAction action)
    {
        if (_bindings is not { } native)
            return Contract(_bindingFailure);
        var type = action.Domain switch
        {
            MasteryExperienceDomain.Spell => native.SpellType,
            MasteryExperienceDomain.Artifact => native.EquipmentType,
            _ => native.AlchemyType,
        };
        var resolution = _registry.Resolve(action.RecipientId, type);
        if (!resolution.IsResolved)
            return resolution.IsRetryable
                ? Identity(resolution.FormatCause())
                : Contract(resolution.FormatCause());
        var recipient = resolution.Value!;
        var identity = action.RecipientId.ToString();

        try
        {
            if (!IsAvailable(native, recipient, action.Domain) ||
                Mastery(native, recipient, action.Domain) >= action.MasteryCeilingExclusive)
            {
                return NativeActionOutcome.Reject(
                    CommonActionResultCodes.NativeRejected,
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
                ? GrantArtifact(native, recipient, identity, action.Amount)
                : GrantDirect(native, recipient, identity, action.Domain, action.Amount);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Contract(GameActionFaultLog.Reason("Mentor could not grant mastery XP.", ex, "Mentor"));
        }
    }

    internal void InvalidateLifecycle() => _alchemy.InvalidateLifecycle();

    public void Dispose() => _alchemy.Dispose();

    private static NativeActionOutcome GrantDirect(
        MentorNativeBindings native,
        object recipient,
        string identity,
        MasteryExperienceDomain domain,
        MentorAmount amount)
    {
        var spell = domain == MasteryExperienceDomain.Spell;
        var readExperience = spell ? native.SpellExperience : native.AlchemyExperience;
        var gain = spell ? native.SpellGain : native.AlchemyGain;
        var expected = new BigDouble(amount.Mantissa, amount.Exponent);
        if (ChangesNothing(readExperience(recipient), expected)) return BelowResolution();
        var evidence = NativeMutationVerifier.Execute(
            "Mentor mastery XP grant",
            identity,
            "mastery progress increased",
            () => new MentorMasteryProgress(Mastery(native, recipient, domain), readExperience(recipient)),
            () =>
            {
                using var ignored = MentorMasteryPatchBridge.Suppress();
                gain(recipient, expected);
            },
            static (before, after) => after.IsAfter(before));
        return FromEvidence(evidence);
    }

    private static NativeActionOutcome GrantArtifact(
        MentorNativeBindings native,
        object equipment,
        string identity,
        MentorAmount amount)
    {
        var container = native.ExperienceElement(equipment) ??
                        throw new InvalidOperationException("artifact experience container is unavailable");
        var value = new BigDouble(amount.Mantissa, amount.Exponent);
        if (ChangesNothing(native.ContainerExperience(container), value)) return BelowResolution();

        var evidence = NativeMutationVerifier.Execute(
            "Mentor artifact mastery XP grant",
            identity,
            "artifact mastery progress increased",
            () => new MentorMasteryProgress(native.ContainerLevel(container), native.ContainerExperience(container)),
            () =>
            {
                using var ignored = MentorMasteryPatchBridge.Suppress();
                native.ContainerGain(container, value);
                var actualLevels = native.ContainerGainedLevels(container);
                if (actualLevels > 0) native.GainMasteryLevels(equipment, actualLevels);
                native.SetEquipmentXp(equipment, native.ContainerExperience(container));
            },
            static (before, after) => after.IsAfter(before));
        return FromEvidence(evidence);
    }

    private static NativeActionOutcome FromEvidence<T>(NativeMutationEvidence<T> evidence)
    {
        var call = NativeMutationCallOutcome.FromEvidence(evidence);
        return evidence.IsVerified
            ? NativeActionOutcome.Commit(call, string.Empty)
            : NativeActionOutcome.Fault(
                CommonActionResultCodes.AdapterFault,
                evidence.Outcome,
                call,
                new GameActionReason(
                    "Mentor could not verify the mastery XP grant.",
                    GameActionFaultLog.RecordReference(
                        evidence.Exception, "Mentor mastery XP grant", evidence.PutBackException)));
    }

    private static bool IsAvailable(MentorNativeBindings native, object recipient, MasteryExperienceDomain domain) =>
        domain switch
        {
            MasteryExperienceDomain.Spell => native.SpellDiscovered(recipient),
            MasteryExperienceDomain.Artifact => native.EquipmentCreated(recipient),
            _ => native.AlchemyAvailable(recipient),
        };

    private static int Mastery(MentorNativeBindings native, object recipient, MasteryExperienceDomain domain) =>
        domain switch
        {
            MasteryExperienceDomain.Spell => native.SpellMastery(recipient),
            MasteryExperienceDomain.Artifact => native.EquipmentMastery(recipient),
            _ => native.AlchemyMastery(recipient),
        };

    private static bool ChangesNothing(BigDouble experience, BigDouble grant) =>
        (experience + grant).CompareTo(experience) == 0;

    private static NativeActionOutcome BelowResolution() =>
        NativeActionOutcome.Reject(
            CommonActionResultCodes.NativeRejected,
            "the grant is too small to change the recipient's mastery XP at its magnitude");

    private static NativeActionOutcome Contract(GameActionReason reason) =>
        NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason);

    private static NativeActionOutcome Identity(GameActionReason reason) =>
        NativeActionOutcome.Reject(CommonActionResultCodes.NativeRejected, reason);

    private readonly struct MentorMasteryProgress
    {
        internal MentorMasteryProgress(int level, BigDouble experience)
        {
            Level = level;
            Experience = experience;
        }

        internal int Level { get; }
        internal BigDouble Experience { get; }

        internal bool IsAfter(in MentorMasteryProgress before) =>
            Level > before.Level ||
            (Level == before.Level && Experience.CompareTo(before.Experience) > 0);

        public override string ToString() => $"level={Level}, xp={Experience}";
    }
}
