using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common;

public enum AlchemyGameplayDomain
{
    Unknown = 0,
    OrdinaryAlchemy = 1,
    ScholarConcept = 2,
}

public enum AlchemyDomainClassifierStatus
{
    Uninitialized = 0,
    Retryable = 1,
    Ready = 2,
    Blocked = 3,
}

[Flags]
public enum AlchemyDomainEvidence
{
    None = 0,
    ExactNativeRecipeType = 1 << 0,
    StableRecipeUuid = 1 << 1,
    ConceptRegistrySnapshot = 1 << 2,
    ConceptRegistryMember = 1 << 3,
    ExactNativeAlchemyType = 1 << 4,
    StableAlchemyTypeUuid = 1 << 5,
    KnownOrdinaryAlchemyType = 1 << 6,
    KnownScholarConceptType = 1 << 7,
}

public sealed class AlchemyDomainClassification
{
    internal AlchemyDomainClassification(
        AlchemyGameplayDomain domain,
        Guid? recipeUuid,
        IReadOnlyList<Guid> alchemyTypeUuids,
        AlchemyDomainEvidence evidence,
        EvidenceAssessment assessment,
        long lifecycleGeneration,
        string reason)
    {
        Domain = domain;
        RecipeUuid = recipeUuid;
        AlchemyTypeUuids = alchemyTypeUuids;
        Evidence = evidence;
        Assessment = assessment;
        LifecycleGeneration = lifecycleGeneration;
        Reason = reason;
    }

    public AlchemyGameplayDomain Domain { get; }
    public Guid? RecipeUuid { get; }
    public IReadOnlyList<Guid> AlchemyTypeUuids { get; }
    public AlchemyDomainEvidence Evidence { get; }
    public EvidenceAssessment Assessment { get; }
    public long LifecycleGeneration { get; }
    public string Reason { get; }
    public bool IsKnown => Domain != AlchemyGameplayDomain.Unknown;
    public bool IsMutationGrade =>
        RecipeUuid.HasValue &&
        IsKnown &&
        Assessment.Meets(
            EvidenceLevel.SerializedAssetVerified,
            EvidenceSource.StaticContract |
            EvidenceSource.SerializedAsset |
            EvidenceSource.RuntimeNativeType |
            EvidenceSource.StableIdentity |
            EvidenceSource.RuntimeRegistry |
            EvidenceSource.NativeRelationship);
}

/// <summary>
/// Classifies native alchemy recipes and types without using asset names.
/// Initialize once per game lifecycle, classify from the cached snapshot, and invalidate on
/// scene, save-load, reset, or NG+ transitions.
/// </summary>
public sealed class AlchemyGameplayDomainClassifier : IDisposable
{
    public static readonly Guid ConceptRecipesUuid = KnownEntities.ConceptRecipes.Uuid;

    public static readonly Guid AlchemyTypeUuid = KnownEntities.Alchemy.Uuid;
    public static readonly Guid BrewingTypeUuid = KnownEntities.Brewing.Uuid;
    public static readonly Guid DismantleTypeUuid = KnownEntities.Dismantle.Uuid;
    public static readonly Guid EnchantmentTypeUuid = KnownEntities.Enchantment.Uuid;
    public static readonly Guid RefinementTypeUuid = KnownEntities.Refinement.Uuid;
    public static readonly Guid TransmutationTypeUuid = KnownEntities.Transmutation.Uuid;

    public static readonly Guid ReductiveConceptTypeUuid = KnownEntities.Reductive.Uuid;
    public static readonly Guid ReflectiveConceptTypeUuid = KnownEntities.Reflective.Uuid;
    public static readonly Guid ConceptualizationTypeUuid = KnownEntities.Conceptualization.Uuid;

    private static readonly HashSet<Guid> OrdinaryTypeUuids = new HashSet<Guid>
    {
        AlchemyTypeUuid,
        BrewingTypeUuid,
        DismantleTypeUuid,
        EnchantmentTypeUuid,
        RefinementTypeUuid,
        TransmutationTypeUuid,
    };

    private static readonly HashSet<Guid> ConceptTypeUuids = new HashSet<Guid>
    {
        ReductiveConceptTypeUuid,
        ReflectiveConceptTypeUuid,
        ConceptualizationTypeUuid,
    };

    /// <summary>
    /// Which of the two alchemy screens a type belongs to, from its stable UUID alone.
    /// </summary>
    /// <remarks>
    /// The action boundary holds one native <c>AlchemyTypeSO</c> and no registry snapshot, so it
    /// cannot run the full recipe classification; what it needs is the same audited mapping the
    /// snapshot is built from, which is why the sets live here and are read rather than copied.
    /// </remarks>
    public static AlchemyGameplayDomain ClassifyTypeUuid(Guid alchemyTypeUuid) =>
        ConceptTypeUuids.Contains(alchemyTypeUuid)
            ? AlchemyGameplayDomain.ScholarConcept
            : OrdinaryTypeUuids.Contains(alchemyTypeUuid)
                ? AlchemyGameplayDomain.OrdinaryAlchemy
                : AlchemyGameplayDomain.Unknown;

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Dictionary<Guid, CachedRecipe> _recipes = new Dictionary<Guid, CachedRecipe>();
    private Type? _recipeType;
    private Type? _alchemyType;
    private FieldInfo? _alchemyTypesField;
    private MethodInfo? _getGuid;
    private TypedRegistryResolution? _registryResolution;
    private string _statusReason = "classifier has not been initialized for this lifecycle";
    private int _classifierGeneration = 1;

    public AlchemyDomainClassifierStatus Status { get; private set; } = AlchemyDomainClassifierStatus.Uninitialized;
    public string StatusReason => _statusReason;
    public long LifecycleGeneration => _registryResolution?.LifecycleGeneration ?? -1;
    public int ClassifierGeneration => _classifierGeneration;
    public int CachedRecipeCount => _recipes.Count;

    public AlchemyGameplayDomainClassifier(TypedRegistryResolver? registryResolver = null)
    {
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public bool TryInitialize(out string reason)
    {
        if (_registryResolution is not null && !_registryResolver.IsCurrent(_registryResolution))
            InvalidateLifecycle();
        if (Status == AlchemyDomainClassifierStatus.Ready)
        {
            reason = string.Empty;
            return true;
        }

        if (Status == AlchemyDomainClassifierStatus.Blocked)
        {
            reason = _statusReason;
            return false;
        }

        try
        {
            var idType = FindLoadedType("IdScriptableObject");
            _recipeType = FindLoadedType("AlchemyRecipeSO");
            _alchemyType = FindLoadedType(KnownEntities.Alchemy.ManagedTypeName);
            var recipeListType = FindLoadedType(KnownEntities.ConceptRecipes.ManagedTypeName);
            if (idType is null || _recipeType is null || _alchemyType is null || recipeListType is null)
                return Retry("native alchemy classifier types are not loaded yet", out reason);

            _getGuid = FindNoArgumentGuidMethod(idType, "GetGuid");
            _alchemyTypesField = FindField(_recipeType, "alchemyTypes", BindingFlags.Instance);
            var valuesField = FindField(recipeListType, "value", BindingFlags.Instance);
            if (_getGuid is null || _alchemyTypesField is null || valuesField is null)
                return Block("native alchemy classifier metadata does not match the audited contract", out reason);

            var registryResolution = _registryResolver.Resolve(ConceptRecipesUuid, recipeListType);
            if (!registryResolution.IsResolved)
                return HandleRegistryFailure(
                    EntityIdentityFormatter.Format(ConceptRecipesUuid),
                    registryResolution,
                    out reason);
            _registryResolution = registryResolution;
            var registry = registryResolution.Value!;
            if (valuesField.GetValue(registry) is not IEnumerable recipes)
                return Block("ConceptRecipes contents are unavailable", out reason);

            var snapshot = new Dictionary<Guid, CachedRecipe>();
            foreach (var recipe in recipes)
            {
                if (recipe is null || recipe.GetType() != _recipeType)
                    return Block("ConceptRecipes contains an unexpected native recipe type", out reason);
                if (!TryReadStableUuid(recipe, out var recipeUuid))
                    return Block("ConceptRecipes contains a recipe without a stable UUID", out reason);
                if (snapshot.ContainsKey(recipeUuid))
                    return Block("ConceptRecipes contains a duplicate recipe UUID", out reason);

                var classification = ClassifyRecipeEvidence(recipe, recipeUuid, isConceptRegistryMember: true);
                if (classification.Domain != AlchemyGameplayDomain.ScholarConcept ||
                    !classification.IsMutationGrade)
                    return Block("ConceptRecipes contains a recipe without verified Scholar type evidence", out reason);
                snapshot.Add(recipeUuid, new CachedRecipe(recipe, classification));
            }

            if (snapshot.Count == 0)
                return Retry("ConceptRecipes is empty for the current lifecycle", out reason);
            if (!_registryResolver.IsCurrent(registryResolution))
                return Retry("ConceptRecipes lifecycle generation changed while building the classifier snapshot", out reason);

            _recipes.Clear();
            foreach (var entry in snapshot) _recipes.Add(entry.Key, entry.Value);
            Status = AlchemyDomainClassifierStatus.Ready;
            _statusReason = string.Empty;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedReflectionFailure(ex))
        {
            return Block("native alchemy classifier initialization failed: " + ex.GetBaseException().Message, out reason);
        }
    }

    public AlchemyDomainClassification ClassifyRecipe(object? recipe)
    {
        if (Status == AlchemyDomainClassifierStatus.Ready &&
            (_registryResolution is null || !_registryResolver.IsCurrent(_registryResolution)))
            InvalidateLifecycle();
        if (Status != AlchemyDomainClassifierStatus.Ready)
            return Unknown(null, AlchemyDomainEvidence.None, _statusReason);
        if (recipe is null || recipe.GetType() != _recipeType)
            return Unknown(null, AlchemyDomainEvidence.None, "value is not the exact native AlchemyRecipeSO type");
        if (!TryReadStableUuid(recipe, out var recipeUuid))
            return Unknown(null, AlchemyDomainEvidence.ExactNativeRecipeType, "recipe stable UUID is unavailable");

        if (_recipes.TryGetValue(recipeUuid, out var cached))
        {
            if (!ReferenceEquals(recipe, cached.NativeRecipe))
            {
                return Unknown(
                    recipeUuid,
                    AlchemyDomainEvidence.ExactNativeRecipeType |
                    AlchemyDomainEvidence.StableRecipeUuid |
                    AlchemyDomainEvidence.ConceptRegistrySnapshot,
                    "recipe UUID resolves to a different lifecycle-scoped native reference",
                    contradictory: true);
            }
            return cached.Classification;
        }

        var classification = ClassifyRecipeEvidence(recipe, recipeUuid, isConceptRegistryMember: false);
        _recipes.Add(recipeUuid, new CachedRecipe(recipe, classification));
        return classification;
    }

    public AlchemyDomainClassification ClassifyType(object? alchemyType)
    {
        if (Status == AlchemyDomainClassifierStatus.Ready &&
            (_registryResolution is null || !_registryResolver.IsCurrent(_registryResolution)))
            InvalidateLifecycle();
        if (Status != AlchemyDomainClassifierStatus.Ready)
            return Unknown(null, AlchemyDomainEvidence.None, _statusReason);
        if (alchemyType is null || alchemyType.GetType() != _alchemyType)
            return Unknown(null, AlchemyDomainEvidence.None, "value is not the exact native AlchemyTypeSO type");
        if (!TryReadStableUuid(alchemyType, out var typeUuid))
            return Unknown(null, AlchemyDomainEvidence.ExactNativeAlchemyType, "alchemy type stable UUID is unavailable");

        var evidence = AlchemyDomainEvidence.ExactNativeAlchemyType | AlchemyDomainEvidence.StableAlchemyTypeUuid;
        if (ConceptTypeUuids.Contains(typeUuid))
        {
            return Result(
                AlchemyGameplayDomain.ScholarConcept,
                null,
                new[] { typeUuid },
                evidence | AlchemyDomainEvidence.KnownScholarConceptType,
                "stable UUID is one of the three audited Scholar concept types");
        }
        if (OrdinaryTypeUuids.Contains(typeUuid))
        {
            return Result(
                AlchemyGameplayDomain.OrdinaryAlchemy,
                null,
                new[] { typeUuid },
                evidence | AlchemyDomainEvidence.KnownOrdinaryAlchemyType,
                "stable UUID is one of the six audited ordinary alchemy types");
        }
        return Result(AlchemyGameplayDomain.Unknown, null, new[] { typeUuid }, evidence, "alchemy type UUID is not in the audited domain mapping");
    }

    public void InvalidateLifecycle()
    {
        _recipes.Clear();
        _recipeType = null;
        _alchemyType = null;
        _alchemyTypesField = null;
        _getGuid = null;
        _registryResolution = null;
        _classifierGeneration++;
        Status = AlchemyDomainClassifierStatus.Uninitialized;
        _statusReason = "classifier has not been initialized for this lifecycle";
    }

    public void Dispose() => InvalidateLifecycle();

    private AlchemyDomainClassification ClassifyRecipeEvidence(object recipe, Guid recipeUuid, bool isConceptRegistryMember)
    {
        var evidence = AlchemyDomainEvidence.ExactNativeRecipeType |
            AlchemyDomainEvidence.StableRecipeUuid |
            AlchemyDomainEvidence.ConceptRegistrySnapshot;
        if (isConceptRegistryMember) evidence |= AlchemyDomainEvidence.ConceptRegistryMember;

        if (_alchemyTypesField!.GetValue(recipe) is not IEnumerable types)
            return Unknown(recipeUuid, evidence, "recipe alchemy type evidence is unavailable");

        var typeUuids = new List<Guid>();
        var hasOrdinaryType = false;
        var hasConceptType = false;
        foreach (var type in types)
        {
            if (type is null || type.GetType() != _alchemyType || !TryReadStableUuid(type, out var typeUuid))
                return Unknown(recipeUuid, evidence, "recipe contains an invalid alchemy type reference");
            typeUuids.Add(typeUuid);
            evidence |= AlchemyDomainEvidence.ExactNativeAlchemyType | AlchemyDomainEvidence.StableAlchemyTypeUuid;
            if (OrdinaryTypeUuids.Contains(typeUuid)) hasOrdinaryType = true;
            if (ConceptTypeUuids.Contains(typeUuid)) hasConceptType = true;
        }

        if (hasOrdinaryType) evidence |= AlchemyDomainEvidence.KnownOrdinaryAlchemyType;
        if (hasConceptType) evidence |= AlchemyDomainEvidence.KnownScholarConceptType;
        var frozenTypeUuids = typeUuids.AsReadOnly();

        if (hasOrdinaryType && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe carries contradictory ordinary-alchemy and Scholar-concept type evidence",
                contradictory: true);
        }

        if (isConceptRegistryMember && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.ScholarConcept,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe is an exact ConceptRecipes member with an audited Scholar type UUID");
        }
        if (!isConceptRegistryMember && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe has a Scholar type UUID but is absent from the ConceptRecipes snapshot",
                contradictory: true);
        }
        if (isConceptRegistryMember)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "ConceptRecipes membership is missing an audited Scholar type UUID",
                contradictory: true);
        }
        if (hasOrdinaryType)
        {
            return Result(
                AlchemyGameplayDomain.OrdinaryAlchemy,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe is outside ConceptRecipes and has an audited ordinary alchemy type UUID");
        }
        return Result(
            AlchemyGameplayDomain.Unknown,
            recipeUuid,
            frozenTypeUuids,
            evidence,
            "recipe has no audited ordinary alchemy or Scholar concept type UUID");
    }

    private bool TryReadStableUuid(object instance, out Guid uuid)
    {
        try
        {
            var value = _getGuid!.Invoke(instance, Array.Empty<object>());
            if (value is Guid result && result != Guid.Empty)
            {
                uuid = result;
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedReflectionFailure(ex))
        {
        }
        uuid = Guid.Empty;
        return false;
    }

    private AlchemyDomainClassification Unknown(
        Guid? recipeUuid,
        AlchemyDomainEvidence evidence,
        string reason,
        bool contradictory = false) =>
        Result(
            AlchemyGameplayDomain.Unknown,
            recipeUuid,
            Array.Empty<Guid>(),
            evidence,
            reason,
            contradictory);

    private AlchemyDomainClassification Result(
        AlchemyGameplayDomain domain,
        Guid? recipeUuid,
        IReadOnlyList<Guid> typeUuids,
        AlchemyDomainEvidence evidence,
        string reason,
        bool contradictory = false) =>
        new AlchemyDomainClassification(
            domain,
            recipeUuid,
            typeUuids,
            evidence,
            AssessEvidence(domain, evidence, contradictory),
            LifecycleGeneration,
            reason);

    private static EvidenceAssessment AssessEvidence(
        AlchemyGameplayDomain domain,
        AlchemyDomainEvidence evidence,
        bool contradictory)
    {
        var sources = EvidenceSource.None;
        if (evidence != AlchemyDomainEvidence.None)
            sources |= EvidenceSource.StaticContract;
        if ((evidence & (AlchemyDomainEvidence.ExactNativeRecipeType |
                         AlchemyDomainEvidence.ExactNativeAlchemyType)) != 0)
            sources |= EvidenceSource.RuntimeNativeType;
        if ((evidence & (AlchemyDomainEvidence.StableRecipeUuid |
                         AlchemyDomainEvidence.StableAlchemyTypeUuid)) != 0)
            sources |= EvidenceSource.StableIdentity;
        if ((evidence & AlchemyDomainEvidence.ConceptRegistrySnapshot) != 0)
            sources |= EvidenceSource.RuntimeRegistry | EvidenceSource.NativeRelationship;
        if ((evidence & (AlchemyDomainEvidence.KnownOrdinaryAlchemyType |
                         AlchemyDomainEvidence.KnownScholarConceptType)) != 0)
            sources |= EvidenceSource.SerializedAsset;

        if (contradictory) return EvidenceAssessment.Contradictory(sources);
        if (domain != AlchemyGameplayDomain.Unknown)
            return new EvidenceAssessment(EvidenceLevel.SerializedAssetVerified, sources);
        if ((sources & (EvidenceSource.RuntimeNativeType | EvidenceSource.StableIdentity)) ==
            (EvidenceSource.RuntimeNativeType | EvidenceSource.StableIdentity))
            return new EvidenceAssessment(EvidenceLevel.RuntimeObserved, sources);
        return EvidenceAssessment.Unresolved(sources);
    }

    private bool Retry(string message, out string reason)
    {
        _recipes.Clear();
        Status = AlchemyDomainClassifierStatus.Retryable;
        _statusReason = message;
        reason = message;
        return false;
    }

    private bool Block(string message, out string reason)
    {
        _recipes.Clear();
        Status = AlchemyDomainClassifierStatus.Blocked;
        _statusReason = message;
        reason = message;
        return false;
    }

    private bool HandleRegistryFailure(
        string label,
        TypedRegistryResolution resolution,
        out string reason)
    {
        var message = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable
            ? Retry(message, out reason)
            : Block(message, out reason);
    }

    private static Type? FindLoadedType(string typeName)
    {
        var gameType = Type.GetType(typeName + ", Assembly-CSharp", throwOnError: false);
        if (gameType is not null) return gameType;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            var candidate = assemblies[index].GetType(typeName, throwOnError: false, ignoreCase: false);
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, string name, BindingFlags scope)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        return null;
    }

    private static MethodInfo? FindNoArgumentGuidMethod(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method is not null && method.ReturnType == typeof(Guid)) return method;
        }
        return null;
    }

    private static bool IsExpectedReflectionFailure(Exception ex) =>
        ex is ArgumentException ||
        ex is InvalidOperationException ||
        ex is TargetInvocationException ||
        ex is TargetException ||
        ex is MethodAccessException ||
        ex is FieldAccessException;

    private sealed class CachedRecipe
    {
        public CachedRecipe(object nativeRecipe, AlchemyDomainClassification classification)
        {
            NativeRecipe = nativeRecipe;
            Classification = classification;
        }

        public object NativeRecipe { get; }
        public AlchemyDomainClassification Classification { get; }
    }
}
