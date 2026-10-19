using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
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
        bool readThrew = false)
    {
        Uuid = uuid;
        ExpectedType = expectedType;
        Status = status;
        Value = value;
        Evidence = evidence;
        LifecycleGeneration = lifecycleGeneration;
        Reason = reason;
        _readThrew = readThrew;
    }

    private readonly bool _readThrew;

    public Guid Uuid { get; }
    public Type ExpectedType { get; }
    public string ExpectedTypeName => ExpectedType.FullName ?? ExpectedType.Name;
    public TypedRegistryResolutionStatus Status { get; }
    public object? Value { get; }
    public EvidenceAssessment Evidence { get; }
    public long LifecycleGeneration { get; }
    public string Reason { get; }
    public bool IsResolved => Status == TypedRegistryResolutionStatus.Resolved && Value is not null;

    /// <summary>
    /// Whether asking again can succeed. An unresolved answer that is not retryable is a mismatch the
    /// game proved: the wrong type, contradictory identity, or a registry whose shape does not bind.
    /// A read that threw proves none of that, so it is retried.
    /// </summary>
    public bool IsRetryable => _readThrew || Status is
        TypedRegistryResolutionStatus.RegistryNotReady or
        TypedRegistryResolutionStatus.NotFound or
        TypedRegistryResolutionStatus.StaleGeneration;

    public bool IsCurrent(GameLifecycleMonitor monitor) =>
        monitor.IsCurrent(LifecycleGeneration);

    public bool IsCurrent(long lifecycleGeneration) =>
        lifecycleGeneration == LifecycleGeneration;

    public string Format() =>
        $"Identity={EntityIdentityFormatter.Format(Uuid)}, ExpectedType={ExpectedTypeName}, Status={Status}, " +
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
    private readonly Func<object, Guid> _readStableUuid;

    public TypedRegistryResolver(
        Func<long> readGeneration,
        Func<TypedRegistrySourceSnapshot> readRegistry,
        Func<object, Guid> readStableUuid)
    {
        _readGeneration = readGeneration ?? throw new ArgumentNullException(nameof(readGeneration));
        _readRegistry = readRegistry ?? throw new ArgumentNullException(nameof(readRegistry));
        _readStableUuid = readStableUuid ?? throw new ArgumentNullException(nameof(readStableUuid));
    }

    public static TypedRegistryResolver Shared { get; } = CreateShared();

    public bool IsCurrent(TypedRegistryResolution resolution) =>
        resolution is not null && resolution.IsCurrent(_readGeneration());

    /// <summary>
    /// The live object a GameAction acts on, or the refusal it gives: the resolver's own reason, or
    /// that a resolved object belongs to a lifecycle that has since been replaced.
    /// </summary>
    public bool TryResolveCurrent(
        Guid uuid,
        Type expectedType,
        [NotNullWhen(true)] out object? value,
        out string reason)
    {
        var resolution = Resolve(uuid, expectedType);
        if (resolution.IsResolved && IsCurrent(resolution))
        {
            value = resolution.Value!;
            reason = string.Empty;
            return true;
        }
        value = null;
        reason = resolution.IsResolved
            ? EntityIdentityFormatter.PlayerName(uuid) + " became stale."
            : resolution.Reason;
        return false;
    }

    public TypedRegistryResolution Resolve(Guid uuid, Type expectedType)
    {
        if (expectedType is null) throw new ArgumentNullException(nameof(expectedType));
        var generation = _readGeneration();
        return Resolve(uuid, expectedType, generation);
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
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Threw(
                uuid,
                expectedType,
                EvidenceAssessment.Unresolved(EvidenceSource.StaticContract),
                generation,
                "runtime registry source failed.",
                ex);
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
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Threw(
                uuid,
                expectedType,
                EvidenceAssessment.Unresolved(EvidenceSource.StaticContract),
                generation,
                "runtime registry lookup failed.",
                ex);
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

        Guid resolvedUuid;
        try
        {
            resolvedUuid = _readStableUuid(value);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Threw(
                uuid,
                expectedType,
                EvidenceAssessment.Unresolved(
                    EvidenceSource.StaticContract |
                    EvidenceSource.RuntimeNativeType |
                    EvidenceSource.RuntimeRegistry),
                generation,
                "stable UUID accessor failed.",
                ex);
        }
        if (resolvedUuid == Guid.Empty || resolvedUuid != uuid)
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
                $"registered value stable UUID {resolvedUuid} does not match key {uuid}");
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

    /// <summary>
    /// A read the game threw on. The exception goes to the suite log, and the reason names only its
    /// reference, so the text never reaches a status or the wire.
    /// </summary>
    private TypedRegistryResolution Threw(
        Guid uuid,
        Type expectedType,
        EvidenceAssessment evidence,
        long generation,
        string what,
        Exception exception) =>
        EnsureCurrent(new TypedRegistryResolution(
            uuid,
            expectedType,
            TypedRegistryResolutionStatus.ContractUnavailable,
            null,
            evidence,
            generation,
            what + GameActionFaultLog.Sentence(GameActionFaultLog.RecordReference(exception, "the runtime registry")),
            readThrew: true));

    private TypedRegistryResolution Result(
        Guid uuid,
        Type expectedType,
        TypedRegistryResolutionStatus status,
        object? value,
        EvidenceAssessment evidence,
        long generation,
        string reason) =>
        EnsureCurrent(RawResult(
            uuid,
            expectedType,
            status,
            value,
            evidence,
            generation,
            reason));

    private static TypedRegistryResolution RawResult(
        Guid uuid,
        Type expectedType,
        TypedRegistryResolutionStatus status,
        object? value,
        EvidenceAssessment evidence,
        long generation,
        string reason) =>
        new(uuid, expectedType, status, value, evidence, generation, reason);

    private static TypedRegistryResolver CreateShared()
    {
        var source = RuntimeIdentityRegistryBinding.Shared;
        return new TypedRegistryResolver(
            () => GameLifecycleMonitor.Shared.Current.Generation,
            source.Read,
            source.ReadStableUuid);
    }

}
