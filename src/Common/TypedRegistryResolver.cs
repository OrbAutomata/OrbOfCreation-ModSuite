using System;
using System.Collections;
using System.Reflection;

namespace OrbModding.Common;

public enum TypedRegistryResolutionStatus
{
    Resolved = 0,
    RegistryNotReady = 1,
    NotFound = 2,
    WrongType = 3,
    AmbiguousEvidence = 4,
    ContractUnavailable = 5,
    StaleGeneration = 6,
}

public enum TypedRegistryMembership
{
    NotEvaluated = 0,
    Included = 1,
    Excluded = 2,
}

public readonly struct TypedRegistrySourceSnapshot
{
    private TypedRegistrySourceSnapshot(
        TypedRegistryResolutionStatus status,
        IDictionary? registry,
        string reason)
    {
        Status = status;
        Registry = registry;
        Reason = reason;
    }

    public TypedRegistryResolutionStatus Status { get; }
    public IDictionary? Registry { get; }
    public string Reason { get; }
    public bool IsReady => Status == TypedRegistryResolutionStatus.Resolved && Registry is not null;

    public static TypedRegistrySourceSnapshot Ready(IDictionary registry) =>
        new(TypedRegistryResolutionStatus.Resolved, registry, string.Empty);

    public static TypedRegistrySourceSnapshot NotReady(string reason) =>
        new(TypedRegistryResolutionStatus.RegistryNotReady, null, reason);

    public static TypedRegistrySourceSnapshot ContractUnavailable(string reason) =>
        new(TypedRegistryResolutionStatus.ContractUnavailable, null, reason);
}

public sealed class TypedRegistryResolution
{
    internal TypedRegistryResolution(
        Guid uuid,
        Type expectedType,
        TypedRegistryResolutionStatus status,
        object? value,
        EvidenceAssessment evidence,
        long lifecycleGeneration,
        string reason,
        TypedRegistryMembership membership)
    {
        Uuid = uuid;
        ExpectedType = expectedType;
        Status = status;
        Value = value;
        Evidence = evidence;
        LifecycleGeneration = lifecycleGeneration;
        Reason = reason;
        Membership = membership;
    }

    public Guid Uuid { get; }
    public Type ExpectedType { get; }
    public string ExpectedTypeName => ExpectedType.FullName ?? ExpectedType.Name;
    public TypedRegistryResolutionStatus Status { get; }
    public object? Value { get; }
    public EvidenceAssessment Evidence { get; }
    public long LifecycleGeneration { get; }
    public string Reason { get; }
    public TypedRegistryMembership Membership { get; }
    public bool IsResolved => Status == TypedRegistryResolutionStatus.Resolved && Value is not null;
    public bool IsRetryable => Status is
        TypedRegistryResolutionStatus.RegistryNotReady or
        TypedRegistryResolutionStatus.NotFound or
        TypedRegistryResolutionStatus.StaleGeneration;

    public bool IsCurrent(GameLifecycleMonitor monitor) =>
        monitor.IsCurrent(LifecycleGeneration);

    public bool IsCurrent(long lifecycleGeneration) =>
        lifecycleGeneration == LifecycleGeneration;

    public string Format() =>
        $"Identity={EntityIdentityFormatter.Format(Uuid)}, ExpectedType={ExpectedTypeName}, Status={Status}, Membership={Membership}, " +
        $"Level={Evidence.Level}, Sources={Evidence.Sources}, Reason={Reason}";
}

public sealed class TypedRegistryResolver
{
    private static readonly EvidenceSource RegistrySources =
        EvidenceSource.StaticContract |
        EvidenceSource.RuntimeNativeType |
        EvidenceSource.StableIdentity |
        EvidenceSource.RuntimeRegistry;

    private readonly Func<long> _readGeneration;
    private readonly Func<TypedRegistrySourceSnapshot> _readRegistry;
    private readonly Func<object, Guid?> _readStableUuid;

    public TypedRegistryResolver(
        Func<long> readGeneration,
        Func<TypedRegistrySourceSnapshot> readRegistry,
        Func<object, Guid?> readStableUuid)
    {
        _readGeneration = readGeneration ?? throw new ArgumentNullException(nameof(readGeneration));
        _readRegistry = readRegistry ?? throw new ArgumentNullException(nameof(readRegistry));
        _readStableUuid = readStableUuid ?? throw new ArgumentNullException(nameof(readStableUuid));
    }

    public static TypedRegistryResolver Shared { get; } = CreateShared();

    public bool IsCurrent(TypedRegistryResolution resolution) =>
        resolution is not null && resolution.IsCurrent(_readGeneration());

    public TypedRegistryResolution Resolve(Guid uuid, Type expectedType)
    {
        if (expectedType is null) throw new ArgumentNullException(nameof(expectedType));
        var generation = _readGeneration();
        return Resolve(uuid, expectedType, generation);
    }

    public TypedRegistryResolution ResolveMember(
        Guid uuid,
        Type expectedType,
        Guid registryUuid,
        Type expectedRegistryType,
        Func<object, IEnumerable?> readMembers)
    {
        if (expectedType is null) throw new ArgumentNullException(nameof(expectedType));
        if (expectedRegistryType is null) throw new ArgumentNullException(nameof(expectedRegistryType));
        if (readMembers is null) throw new ArgumentNullException(nameof(readMembers));

        var generation = _readGeneration();
        var entity = Resolve(uuid, expectedType, generation);
        if (!entity.IsResolved) return entity;
        var registry = Resolve(registryUuid, expectedRegistryType, generation);
        if (!registry.IsResolved)
        {
            return Result(
                uuid,
                expectedType,
                registry.Status,
                null,
                registry.Evidence,
                generation,
                $"membership registry {registryUuid} ({registry.ExpectedTypeName}) is unavailable: {registry.Reason}");
        }

        IEnumerable? members;
        try
        {
            members = readMembers(registry.Value!);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.ContractUnavailable,
                null,
                EvidenceAssessment.Unresolved(entity.Evidence.Sources | registry.Evidence.Sources),
                generation,
                "membership accessor failed: " + ex.GetBaseException().Message);
        }

        if (members is null)
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.RegistryNotReady,
                null,
                EvidenceAssessment.Unresolved(entity.Evidence.Sources | registry.Evidence.Sources),
                generation,
                "membership registry contents are not ready");
        }

        try
        {
            foreach (var member in members)
            {
                if (ReferenceEquals(member, entity.Value))
                    return EnsureCurrent(Result(
                        uuid,
                        expectedType,
                        TypedRegistryResolutionStatus.Resolved,
                        entity.Value,
                        new EvidenceAssessment(
                            EvidenceLevel.RuntimeObserved,
                            entity.Evidence.Sources |
                            registry.Evidence.Sources |
                            EvidenceSource.NativeRelationship),
                        generation,
                        string.Empty,
                        TypedRegistryMembership.Included));

                if (member is null)
                {
                    return Result(
                        uuid,
                        expectedType,
                        TypedRegistryResolutionStatus.AmbiguousEvidence,
                        null,
                        EvidenceAssessment.Contradictory(
                            entity.Evidence.Sources |
                            registry.Evidence.Sources |
                            EvidenceSource.NativeRelationship),
                        generation,
                        "membership contains a null entry");
                }
                if (member.GetType() != expectedType)
                {
                    return Result(
                        uuid,
                        expectedType,
                        TypedRegistryResolutionStatus.WrongType,
                        null,
                        EvidenceAssessment.Contradictory(
                            entity.Evidence.Sources |
                            registry.Evidence.Sources |
                            EvidenceSource.RuntimeNativeType |
                            EvidenceSource.NativeRelationship),
                        generation,
                        $"membership contains {member.GetType().FullName ?? member.GetType().Name}, not {expectedType.FullName ?? expectedType.Name}");
                }
                var memberUuid = _readStableUuid(member);
                if (!memberUuid.HasValue || memberUuid.Value == Guid.Empty)
                {
                    return Result(
                        uuid,
                        expectedType,
                        TypedRegistryResolutionStatus.AmbiguousEvidence,
                        null,
                        EvidenceAssessment.Contradictory(
                            entity.Evidence.Sources |
                            registry.Evidence.Sources |
                            EvidenceSource.StableIdentity |
                            EvidenceSource.NativeRelationship),
                        generation,
                        "membership contains an entry without a stable UUID");
                }
                if (memberUuid == uuid)
                {
                    return Result(
                        uuid,
                        expectedType,
                        TypedRegistryResolutionStatus.AmbiguousEvidence,
                        null,
                        EvidenceAssessment.Contradictory(
                            entity.Evidence.Sources |
                            registry.Evidence.Sources |
                            EvidenceSource.NativeRelationship),
                        generation,
                        "membership contains the UUID under a different lifecycle-scoped native reference");
                }
            }
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.ContractUnavailable,
                null,
                EvidenceAssessment.Unresolved(entity.Evidence.Sources | registry.Evidence.Sources),
                generation,
                "membership enumeration failed: " + ex.GetBaseException().Message);
        }

        return EnsureCurrent(Result(
            uuid,
            expectedType,
            TypedRegistryResolutionStatus.Resolved,
            entity.Value,
            new EvidenceAssessment(
                EvidenceLevel.RuntimeObserved,
                entity.Evidence.Sources |
                registry.Evidence.Sources |
                EvidenceSource.NativeRelationship),
            generation,
            string.Empty,
            TypedRegistryMembership.Excluded));
    }

    private TypedRegistryResolution Resolve(Guid uuid, Type expectedType, long generation)
    {
        if (uuid == Guid.Empty)
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.AmbiguousEvidence,
                null,
                EvidenceAssessment.Unresolved(),
                generation,
                "stable UUID must not be empty");
        }

        TypedRegistrySourceSnapshot source;
        try
        {
            source = _readRegistry();
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.ContractUnavailable,
                null,
                EvidenceAssessment.Unresolved(EvidenceSource.StaticContract),
                generation,
                "runtime registry source failed: " + ex.GetBaseException().Message);
        }

        if (!source.IsReady)
        {
            return Result(
                uuid,
                expectedType,
                source.Status,
                null,
                EvidenceAssessment.Unresolved(
                    source.Status == TypedRegistryResolutionStatus.RegistryNotReady
                        ? EvidenceSource.StaticContract
                        : EvidenceSource.None),
                generation,
                source.Reason);
        }

        object? value;
        try
        {
            if (!source.Registry!.Contains(uuid))
            {
                return EnsureCurrent(Result(
                    uuid,
                    expectedType,
                    TypedRegistryResolutionStatus.NotFound,
                    null,
                    new EvidenceAssessment(
                        EvidenceLevel.RuntimeObserved,
                        EvidenceSource.StaticContract | EvidenceSource.RuntimeRegistry),
                    generation,
                    $"UUID {uuid} has not registered yet"));
            }
            value = source.Registry[uuid];
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.ContractUnavailable,
                null,
                EvidenceAssessment.Unresolved(EvidenceSource.StaticContract),
                generation,
                "runtime registry lookup failed: " + ex.GetBaseException().Message);
        }

        if (value is null)
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.AmbiguousEvidence,
                null,
                EvidenceAssessment.Contradictory(EvidenceSource.StaticContract | EvidenceSource.RuntimeRegistry),
                generation,
                "registered UUID resolves to null");
        }
        if (value.GetType() != expectedType)
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.WrongType,
                null,
                EvidenceAssessment.Contradictory(
                    EvidenceSource.StaticContract |
                    EvidenceSource.RuntimeNativeType |
                    EvidenceSource.RuntimeRegistry),
                generation,
                $"registered UUID resolves to {value.GetType().FullName ?? value.GetType().Name}, not {expectedType.FullName ?? expectedType.Name}");
        }

        Guid? resolvedUuid;
        try
        {
            resolvedUuid = _readStableUuid(value);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.ContractUnavailable,
                null,
                EvidenceAssessment.Unresolved(
                    EvidenceSource.StaticContract |
                    EvidenceSource.RuntimeNativeType |
                    EvidenceSource.RuntimeRegistry),
                generation,
                "stable UUID accessor failed: " + ex.GetBaseException().Message);
        }
        if (!resolvedUuid.HasValue || resolvedUuid.Value == Guid.Empty || resolvedUuid.Value != uuid)
        {
            return Result(
                uuid,
                expectedType,
                TypedRegistryResolutionStatus.AmbiguousEvidence,
                null,
                EvidenceAssessment.Contradictory(
                    EvidenceSource.StaticContract |
                    EvidenceSource.RuntimeNativeType |
                    EvidenceSource.StableIdentity |
                    EvidenceSource.RuntimeRegistry),
                generation,
                $"registered value stable UUID {resolvedUuid?.ToString() ?? "unavailable"} does not match key {uuid}");
        }

        return EnsureCurrent(Result(
            uuid,
            expectedType,
            TypedRegistryResolutionStatus.Resolved,
            value,
            new EvidenceAssessment(EvidenceLevel.RuntimeObserved, RegistrySources),
            generation,
            string.Empty));
    }

    private TypedRegistryResolution EnsureCurrent(TypedRegistryResolution result)
    {
        var current = _readGeneration();
        if (current == result.LifecycleGeneration) return result;
        return RawResult(
            result.Uuid,
            result.ExpectedType,
            TypedRegistryResolutionStatus.StaleGeneration,
            null,
            EvidenceAssessment.Unresolved(result.Evidence.Sources),
            result.LifecycleGeneration,
            $"lifecycle generation changed during resolution; captured={result.LifecycleGeneration}; current={current}");
    }

    private TypedRegistryResolution Result(
        Guid uuid,
        Type expectedType,
        TypedRegistryResolutionStatus status,
        object? value,
        EvidenceAssessment evidence,
        long generation,
        string reason,
        TypedRegistryMembership membership = TypedRegistryMembership.NotEvaluated) =>
        EnsureCurrent(RawResult(
            uuid,
            expectedType,
            status,
            value,
            evidence,
            generation,
            reason,
            membership));

    private static TypedRegistryResolution RawResult(
        Guid uuid,
        Type expectedType,
        TypedRegistryResolutionStatus status,
        object? value,
        EvidenceAssessment evidence,
        long generation,
        string reason,
        TypedRegistryMembership membership = TypedRegistryMembership.NotEvaluated) =>
        new(uuid, expectedType, status, value, evidence, generation, reason, membership);

    private static TypedRegistryResolver CreateShared()
    {
        var source = RuntimeIdentityRegistryBinding.Shared;
        return new TypedRegistryResolver(
            () => GameLifecycleMonitor.Shared.Current.Generation,
            source.Read,
            source.ReadStableUuid);
    }

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is InvalidOperationException or
        ArgumentException or
        NotSupportedException or
        TargetException or
        TargetInvocationException or
        TargetParameterCountException or
        MethodAccessException or
        FieldAccessException or
        MissingMemberException or
        TypeLoadException;

}
