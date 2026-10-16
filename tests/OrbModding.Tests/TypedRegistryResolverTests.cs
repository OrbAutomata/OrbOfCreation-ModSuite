using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class TypedRegistryResolverTests
{
    [Fact]
    public void DistinguishesRegistryReadinessFromContractFailure()
    {
        var notReady = Resolver(() => TypedRegistrySourceSnapshot.NotReady("initializing"));
        var contract = Resolver(() => TypedRegistrySourceSnapshot.ContractUnavailable("field missing"));

        Assert.Equal(TypedRegistryResolutionStatus.RegistryNotReady, notReady.Resolve(Guid.NewGuid(), typeof(Entity)).Status);
        Assert.Equal(TypedRegistryResolutionStatus.ContractUnavailable, contract.Resolve(Guid.NewGuid(), typeof(Entity)).Status);
    }

    [Fact]
    public void MissingUuidReturnsStructuredRuntimeEvidence()
    {
        var uuid = Guid.NewGuid();
        var resolver = Resolver(Ready(new Hashtable()));

        var result = resolver.Resolve(uuid, typeof(Entity));

        Assert.Equal(TypedRegistryResolutionStatus.NotFound, result.Status);
        Assert.Equal(uuid, result.Uuid);
        Assert.Equal(typeof(Entity), result.ExpectedType);
        Assert.Equal(EvidenceLevel.RuntimeObserved, result.Evidence.Level);
        Assert.True(result.Evidence.Sources.HasFlag(EvidenceSource.RuntimeRegistry));
    }

    [Fact]
    public void MissingRegistrationIsNotCachedWithinTheSameGeneration()
    {
        var uuid = Guid.NewGuid();
        var lookup = new Hashtable();
        var resolver = Resolver(Ready(lookup));

        Assert.Equal(TypedRegistryResolutionStatus.NotFound, resolver.Resolve(uuid, typeof(Entity)).Status);
        var entity = new Entity(uuid);
        lookup[uuid] = entity;

        var resolved = resolver.Resolve(uuid, typeof(Entity));
        Assert.True(resolved.IsResolved);
        Assert.Same(entity, resolved.Value);
    }

    [Fact]
    public void WrongTypeNeverFallsBackToSameNamedEntity()
    {
        var uuid = Guid.NewGuid();
        var wrong = new OtherEntity(uuid) { Name = "Expected display name" };
        var registry = new Hashtable { [uuid] = wrong };
        var resolver = Resolver(Ready(registry));

        var result = resolver.Resolve(uuid, typeof(Entity));

        Assert.Equal(TypedRegistryResolutionStatus.WrongType, result.Status);
        Assert.Null(result.Value);
        Assert.True(result.Evidence.IsContradictory);
        Assert.Contains(typeof(Entity).FullName!, result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulResolutionValidatesExactTypeStableUuidAndGeneration()
    {
        var uuid = Guid.NewGuid();
        var entity = new Entity(uuid);
        var registry = new Hashtable { [uuid] = entity };
        var generation = 7L;
        var resolver = Resolver(Ready(registry), () => generation);

        var result = resolver.Resolve(uuid, typeof(Entity));

        Assert.True(result.IsResolved);
        Assert.Same(entity, result.Value);
        Assert.Equal(7, result.LifecycleGeneration);
        Assert.Equal(EvidenceLevel.RuntimeObserved, result.Evidence.Level);
        Assert.True(result.Evidence.Meets(
            EvidenceLevel.RuntimeObserved,
            EvidenceSource.StaticContract |
            EvidenceSource.RuntimeNativeType |
            EvidenceSource.StableIdentity |
            EvidenceSource.RuntimeRegistry));
    }

    [Fact]
    public void RegistryKeyAndStableUuidContradictionFailsClosed()
    {
        var key = Guid.NewGuid();
        var registry = new Hashtable { [key] = new Entity(Guid.NewGuid()) };
        var resolver = Resolver(Ready(registry));

        var result = resolver.Resolve(key, typeof(Entity));

        Assert.Equal(TypedRegistryResolutionStatus.AmbiguousEvidence, result.Status);
        Assert.True(result.Evidence.IsContradictory);
    }

    [Fact]
    public void MembershipResolutionRecordsExactNativeRelationship()
    {
        var entityId = Guid.NewGuid();
        var registryId = Guid.NewGuid();
        var entity = new Entity(entityId);
        var membership = new MembershipRegistry(registryId, entity);
        var lookup = new Hashtable { [entityId] = entity, [registryId] = membership };
        var resolver = Resolver(Ready(lookup));

        var result = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            value => ((MembershipRegistry)value).Members);

        Assert.True(result.IsResolved);
        Assert.True(result.Evidence.Sources.HasFlag(EvidenceSource.NativeRelationship));
        Assert.Equal(TypedRegistryMembership.Included, result.Membership);
    }

    [Fact]
    public void MissingMembershipAndReplacementIdentityAreDistinct()
    {
        var entityId = Guid.NewGuid();
        var registryId = Guid.NewGuid();
        var entity = new Entity(entityId);
        var membership = new MembershipRegistry(registryId);
        var lookup = new Hashtable { [entityId] = entity, [registryId] = membership };
        var resolver = Resolver(Ready(lookup));

        var missing = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            value => ((MembershipRegistry)value).Members);
        membership.Members.Add(new Entity(entityId));
        var ambiguous = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            value => ((MembershipRegistry)value).Members);

        Assert.True(missing.IsResolved);
        Assert.Equal(TypedRegistryMembership.Excluded, missing.Membership);
        Assert.Equal(TypedRegistryResolutionStatus.AmbiguousEvidence, ambiguous.Status);
        Assert.True(ambiguous.Evidence.IsContradictory);
    }

    [Fact]
    public void LifecycleChangeDuringReadRejectsResolvedValue()
    {
        var uuid = Guid.NewGuid();
        var entity = new Entity(uuid);
        var registry = new Hashtable { [uuid] = entity };
        var generation = 3L;
        var resolver = new TypedRegistryResolver(
            () => generation,
            Ready(registry),
            value =>
            {
                generation++;
                return ((IStableEntity)value).Id;
            });

        var result = resolver.Resolve(uuid, typeof(Entity));

        Assert.Equal(TypedRegistryResolutionStatus.StaleGeneration, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(3, result.LifecycleGeneration);
    }

    [Fact]
    public void LifecycleChangeOverridesOldGenerationContradiction()
    {
        var uuid = Guid.NewGuid();
        var registry = new Hashtable { [uuid] = new Entity(uuid) };
        var generation = 9L;
        var resolver = new TypedRegistryResolver(
            () => generation,
            Ready(registry),
            _ =>
            {
                generation++;
                return Guid.NewGuid();
            });

        var result = resolver.Resolve(uuid, typeof(Entity));

        Assert.Equal(TypedRegistryResolutionStatus.StaleGeneration, result.Status);
        Assert.True(result.IsRetryable);
        Assert.False(result.Evidence.IsContradictory);
    }

    [Fact]
    public void SharedAdapterRequiresExactPublicNativeDeclarations()
    {
        const BindingFlags publicDeclared = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var exactField = typeof(IdScriptableObject).GetField("RuntimeLookup", publicDeclared);
        var exactMethod = typeof(IdScriptableObject).GetMethod("GetGuid", publicDeclared, null, Type.EmptyTypes, null);
        var nearField = typeof(NearMatchRegistryContract).GetField("RuntimeLookup", publicDeclared);
        var hiddenMethod = typeof(HiddenIdentityContract).GetMethod("GetGuid", publicDeclared, null, Type.EmptyTypes, null);

        Assert.True(RuntimeIdentityRegistryBinding.HasExactRuntimeLookupContract(exactField, typeof(IdScriptableObject)));
        Assert.True(RuntimeIdentityRegistryBinding.HasExactGetGuidContract(exactMethod, typeof(IdScriptableObject)));
        Assert.False(RuntimeIdentityRegistryBinding.HasExactRuntimeLookupContract(nearField, typeof(NearMatchRegistryContract)));
        Assert.False(RuntimeIdentityRegistryBinding.HasExactGetGuidContract(hiddenMethod, typeof(IdScriptableObject)));
    }

    [Fact]
    public void OlderResultBecomesStaleAndReplacementResolvesInNewGeneration()
    {
        var uuid = Guid.NewGuid();
        var first = new Entity(uuid);
        var replacement = new Entity(uuid);
        var lookup = new Hashtable { [uuid] = first };
        var generation = 4L;
        var resolver = Resolver(Ready(lookup), () => generation);

        var oldResult = resolver.Resolve(uuid, typeof(Entity));
        generation = 5;
        lookup[uuid] = replacement;
        var newResult = resolver.Resolve(uuid, typeof(Entity));

        Assert.False(oldResult.IsCurrent(generation));
        Assert.True(newResult.IsCurrent(generation));
        Assert.Same(first, oldResult.Value);
        Assert.Same(replacement, newResult.Value);
    }

    [Fact]
    public void EmptyUuidAndNullMembershipContentsFailClosed()
    {
        var registryId = Guid.NewGuid();
        var membership = new MembershipRegistry(registryId);
        var lookup = new Hashtable { [registryId] = membership };
        var resolver = Resolver(Ready(lookup));

        Assert.Equal(
            TypedRegistryResolutionStatus.AmbiguousEvidence,
            resolver.Resolve(Guid.Empty, typeof(Entity)).Status);

        var entityId = Guid.NewGuid();
        lookup[entityId] = new Entity(entityId);
        var member = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            _ => null);
        Assert.Equal(TypedRegistryResolutionStatus.RegistryNotReady, member.Status);
    }

    [Fact]
    public void MalformedMembershipCannotProveExclusion()
    {
        var entityId = Guid.NewGuid();
        var registryId = Guid.NewGuid();
        var entity = new Entity(entityId);
        var membership = new MembershipRegistry(registryId);
        var lookup = new Hashtable { [entityId] = entity, [registryId] = membership };
        var resolver = Resolver(Ready(lookup));

        var wrongType = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            _ => new object[] { new OtherEntity(Guid.NewGuid()) });
        var nullEntry = resolver.ResolveMember(
            entityId,
            typeof(Entity),
            registryId,
            typeof(MembershipRegistry),
            _ => new object?[] { null });

        Assert.Equal(TypedRegistryResolutionStatus.WrongType, wrongType.Status);
        Assert.Equal(TypedRegistryResolutionStatus.AmbiguousEvidence, nullEntry.Status);
        Assert.NotEqual(TypedRegistryMembership.Excluded, wrongType.Membership);
        Assert.NotEqual(TypedRegistryMembership.Excluded, nullEntry.Membership);
    }

    private static TypedRegistryResolver Resolver(
        Func<TypedRegistrySourceSnapshot> readRegistry,
        Func<long>? readGeneration = null) =>
        new(
            readGeneration ?? (() => 1),
            readRegistry,
            value => ((IStableEntity)value).Id);

    private static Func<TypedRegistrySourceSnapshot> Ready(IDictionary registry) =>
        () => TypedRegistrySourceSnapshot.Ready(registry);

    private interface IStableEntity
    {
        Guid Id { get; }
    }

    private sealed class Entity : IStableEntity
    {
        public Entity(Guid id) => Id = id;
        public Guid Id { get; }
    }

    private sealed class OtherEntity : IStableEntity
    {
        public OtherEntity(Guid id) => Id = id;
        public Guid Id { get; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MembershipRegistry : IStableEntity
    {
        public MembershipRegistry(Guid id, params Entity[] members)
        {
            Id = id;
            Members.AddRange(members);
        }

        public Guid Id { get; }
        public List<Entity> Members { get; } = new();
    }

    private sealed class NearMatchRegistryContract
    {
        public static Dictionary<Guid, object> RuntimeLookup = new();
    }

    private sealed class HiddenIdentityContract : IdScriptableObject
    {
        public new Guid GetGuid() => base.GetGuid();
    }
}
