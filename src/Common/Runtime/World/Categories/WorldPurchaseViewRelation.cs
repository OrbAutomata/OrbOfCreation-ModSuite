using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>The exact purchasable native family named by an owning-view relation.</summary>
internal enum WorldPurchaseCandidateKind
{
    Structure = 1,
    Upgrade = 2,
}

/// <summary>Whether one candidate has a complete authored set of owning-view routes.</summary>
internal enum WorldPurchaseViewRelationStatus
{
    Resolved = 0,
    Missing,
    Unreadable,
    Contradictory,
}

/// <summary>One detached authored route from a candidate through a list to a view.</summary>
internal readonly struct WorldPurchaseViewRoute
{
    internal WorldPurchaseViewRoute(Guid candidateId, Guid listId, Guid viewId)
    {
        CandidateId = candidateId;
        ListId = listId;
        ViewId = viewId;
    }

    internal Guid CandidateId { get; }
    internal Guid ListId { get; }
    internal Guid ViewId { get; }
}

/// <summary>
/// One candidate's authored route-set header: exact candidate kind and category plus the number of
/// detached list/view routes. A non-resolved row is retained so consumers can name why the candidate
/// was withheld instead of silently dropping it.
/// </summary>
internal readonly struct WorldPurchaseViewRelation : IWorldEntity
{
    internal WorldPurchaseViewRelation(
        Guid candidateId,
        WorldPurchaseCandidateKind kind,
        Guid categoryId,
        int routeCount,
        WorldPurchaseViewRelationStatus status)
    {
        CandidateId = candidateId;
        Kind = kind;
        CategoryId = categoryId;
        RouteCount = routeCount;
        Status = status;
    }

    public Guid EntityId => CandidateId;
    internal Guid CandidateId { get; }
    internal WorldPurchaseCandidateKind Kind { get; }

    /// <summary>The exact <c>StructureTypeSO</c> identity; empty for an <c>UpgradeSO</c>.</summary>
    internal Guid CategoryId { get; }

    internal int RouteCount { get; }
    internal WorldPurchaseViewRelationStatus Status { get; }
}

internal readonly struct WorldPurchaseViewPublication
{
    internal WorldPurchaseViewPublication(
        PublicationTable<WorldPurchaseViewRelation> relations,
        PublicationTable<WorldPurchaseViewRoute> routes)
    {
        Relations = relations;
        Routes = routes;
    }

    internal PublicationTable<WorldPurchaseViewRelation> Relations { get; }
    internal PublicationTable<WorldPurchaseViewRoute> Routes { get; }
}

/// <summary>Validates the complete detached candidate/edge graph on the worker.</summary>
internal static class WorldPurchaseViewRelationDeriver
{
    internal static WorldPurchaseViewPublication Build(
        WorldRelationBuffer<WorldPurchaseViewRelation> relationBuffer,
        WorldRelationBuffer<WorldPurchaseViewRoute> routeBuffer)
    {
        var routesByCandidate = new Dictionary<Guid, List<WorldPurchaseViewRoute>>();
        var edgeKeys = new HashSet<RouteKey>();
        for (var index = 0; index < routeBuffer.Count; index++)
        {
            var route = routeBuffer[index];
            if (route.CandidateId == Guid.Empty || route.ListId == Guid.Empty ||
                route.ViewId == Guid.Empty ||
                !edgeKeys.Add(new RouteKey(route.CandidateId, route.ListId, route.ViewId)))
                continue;
            if (!routesByCandidate.TryGetValue(route.CandidateId, out var routes))
                routesByCandidate.Add(route.CandidateId, routes = new List<WorldPurchaseViewRoute>());
            routes.Add(route);
        }

        var relations = new WorldPurchaseViewRelation[relationBuffer.Count];
        var publishedRoutes = new WorldPurchaseViewRoute[routeBuffer.Count];
        var relationIds = new HashSet<Guid>();
        var relationCount = 0;
        var routeCount = 0;
        for (var index = 0; index < relationBuffer.Count; index++)
        {
            var relation = relationBuffer[index];
            routesByCandidate.TryGetValue(relation.CandidateId, out var routes);
            var count = routes?.Count ?? 0;
            var shapeValid = relation.CandidateId != Guid.Empty &&
                relationIds.Add(relation.CandidateId) &&
                (relation.Kind == WorldPurchaseCandidateKind.Structure
                    ? relation.CategoryId != Guid.Empty
                    : relation.CategoryId == Guid.Empty) &&
                relation.Status == WorldPurchaseViewRelationStatus.Resolved &&
                relation.RouteCount == count && count > 0;
            var status = shapeValid
                ? WorldPurchaseViewRelationStatus.Resolved
                : relation.Status == WorldPurchaseViewRelationStatus.Resolved
                    ? WorldPurchaseViewRelationStatus.Contradictory
                    : relation.Status;
            relations[relationCount++] = new WorldPurchaseViewRelation(
                relation.CandidateId,
                relation.Kind,
                relation.CategoryId,
                status == WorldPurchaseViewRelationStatus.Resolved ? count : 0,
                status);
            if (status != WorldPurchaseViewRelationStatus.Resolved || routes is null) continue;
            for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                publishedRoutes[routeCount++] = routes[routeIndex];
        }

        Array.Sort(relations, 0, relationCount, RelationComparer.Instance);
        Array.Sort(publishedRoutes, 0, routeCount, RouteComparer.Instance);
        return new WorldPurchaseViewPublication(
            PublicationTable<WorldPurchaseViewRelation>.Create(relations, relationCount),
            PublicationTable<WorldPurchaseViewRoute>.Create(publishedRoutes, routeCount));
    }

    private readonly struct RouteKey : IEquatable<RouteKey>
    {
        internal RouteKey(Guid candidate, Guid list, Guid view)
        {
            Candidate = candidate;
            List = list;
            View = view;
        }
        private Guid Candidate { get; }
        private Guid List { get; }
        private Guid View { get; }
        public bool Equals(RouteKey other) =>
            Candidate == other.Candidate && List == other.List && View == other.View;
        public override bool Equals(object? value) => value is RouteKey other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((Candidate.GetHashCode() * 397) ^ List.GetHashCode()) * 397 ^ View.GetHashCode());
    }

    private sealed class RelationComparer : IComparer<WorldPurchaseViewRelation>
    {
        internal static readonly IComparer<WorldPurchaseViewRelation> Instance = new RelationComparer();
        public int Compare(WorldPurchaseViewRelation left, WorldPurchaseViewRelation right) =>
            left.CandidateId.CompareTo(right.CandidateId);
    }

    private sealed class RouteComparer : IComparer<WorldPurchaseViewRoute>
    {
        internal static readonly IComparer<WorldPurchaseViewRoute> Instance = new RouteComparer();
        public int Compare(WorldPurchaseViewRoute left, WorldPurchaseViewRoute right)
        {
            var candidate = left.CandidateId.CompareTo(right.CandidateId);
            if (candidate != 0) return candidate;
            var view = left.ViewId.CompareTo(right.ViewId);
            return view != 0 ? view : left.ListId.CompareTo(right.ListId);
        }
    }
}

/// <summary>One lifecycle-bound native route used only for live action admission.</summary>
internal readonly struct NativePurchaseViewRoute
{
    internal NativePurchaseViewRoute(Guid listId, string listName, Guid viewId, string viewName, object view)
    {
        ListId = listId;
        ListName = listName ?? string.Empty;
        ViewId = viewId;
        ViewName = viewName ?? string.Empty;
        View = view;
    }

    internal Guid ListId { get; }
    internal string ListName { get; }
    internal Guid ViewId { get; }
    internal string ViewName { get; }
    internal object View { get; }
}

/// <summary>A detached relation plus its lifecycle-bound live view routes.</summary>
internal readonly struct NativePurchaseViewResolution
{
    internal NativePurchaseViewResolution(
        in WorldPurchaseViewRelation relation,
        NativePurchaseViewRoute[] routes,
        string candidateName)
    {
        Relation = relation;
        Routes = routes ?? Array.Empty<NativePurchaseViewRoute>();
        CandidateName = candidateName ?? string.Empty;
    }

    internal WorldPurchaseViewRelation Relation { get; }
    internal NativePurchaseViewRoute[] Routes { get; }
    internal string CandidateName { get; }
}

/// <summary>Exact reflection/list work performed by one live owning-view resolution.</summary>
internal struct NativePurchaseViewAdmissionReadCounts
{
    internal uint FieldReads { get; private set; }
    internal uint MethodCalls { get; private set; }
    internal uint ListEntries { get; private set; }

    internal void AddFieldRead() => FieldReads = checked(FieldReads + 1);
    internal void AddMethodCall() => MethodCalls = checked(MethodCalls + 1);
    internal void AddListEntry() => ListEntries = checked(ListEntries + 1);
}

/// <summary>
/// The one audited resolver for Auto Buy owning-view admission. World collection uses it to publish
/// one immutable candidate-to-list/view route snapshot per lifecycle. The action boundary never
/// enumerates authored topology: it finds the candidate in that snapshot and asks each captured
/// <c>ViewSO.IsAvailable()</c> immediately before purchase admission.
/// </summary>
/// <remarks>
/// <para>
/// Both <c>ViewSO.relevantLists</c> and <c>ViewSO.availableLists</c> contribute normal player
/// reachability. All views carrying the same list form one complete route and must be available.
/// Distinct list identities are alternate routes, of which one proven-available route is sufficient.
/// Each route edge must name the exact candidate once, and identical edges from the two sources are
/// collapsed. No display name participates in identity or admission.
/// </para>
/// <para>
/// Structures additionally prove their own <c>structureType</c> identity and exact membership in
/// that category's <c>structures</c> list. This keeps the published list route tied to the same exact
/// category identity the structure row carries.
/// </para>
/// </remarks>
internal sealed class NativePurchaseViewAdmissionResolver
{
    private static NativePurchaseViewAdmissionResolver? _production;
    private readonly BindingSet _native;
    private Dictionary<CandidateKey, NativePurchaseViewResolution> _snapshot = new();
    private long _snapshotEpoch;
    internal long CapturedEpoch => _snapshotEpoch;

    private NativePurchaseViewAdmissionResolver(BindingSet native) => _native = native;

    internal static bool TryCreate(
        Func<string, Type?> resolve,
        out NativePurchaseViewAdmissionResolver? resolver,
        out string reason)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        if (!BindingSet.TryCreate(resolve, out var bindings, out reason))
        {
            resolver = null;
            return false;
        }

        resolver = new NativePurchaseViewAdmissionResolver(bindings!);
        return true;
    }

    internal static bool TryCreateProduction(
        Func<string, Type?> resolve,
        out NativePurchaseViewAdmissionResolver? resolver,
        out string reason)
    {
        if (_production is not null)
        {
            resolver = _production;
            reason = string.Empty;
            return true;
        }
        if (!TryCreate(resolve, out resolver, out reason)) return false;
        _production = resolver;
        return true;
    }

    /// <summary>
    /// Drops the process-wide resolver so the next production composition builds a fresh one.
    /// </summary>
    /// <remarks>
    /// A test that composes the production way must start from no singleton, or it inherits another
    /// test's registries through a snapshot it never read. The game itself never needs this: the
    /// resolver binds member handles, not game state, and the epoch stamp is what scopes it.
    /// </remarks>
    internal static void ResetProductionForTests() => _production = null;

    /// <summary>Reads one relation row for every exact native Auto Buy candidate.</summary>
    /// <remarks>
    /// The rows always reach <paramref name="output"/>; the admission snapshot is published only
    /// under a lifecycle. A snapshot stamped at a non-positive epoch is not admission evidence —
    /// <see cref="TryGetCaptured"/> refuses one outright — so stamping it could never admit a
    /// candidate and would only discard the evidence a real lifecycle published. This is reachable
    /// on the session collector: a pass taken before a save is loaded reads a lifecycle epoch of
    /// zero, and without this refusal it would replace a live snapshot with one nothing can match.
    /// </remarks>
    internal int ReadAll(
        long lifecycleEpoch,
        WorldRelationBuffer<WorldPurchaseViewRelation> output,
        WorldRelationBuffer<WorldPurchaseViewRoute> routeOutput,
        out int unresolved,
        out int skipped)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        unresolved = 0;
        skipped = 0;
        var identities = new HashSet<Guid>();
        var identityCache = new Dictionary<object, Guid>(ReferenceComparer.Instance);
        var candidates = new List<ForwardCandidate>();
        var snapshot = new Dictionary<CandidateKey, NativePurchaseViewResolution>();
        ReadForwardRegistry(
            WorldPurchaseCandidateKind.Structure,
            _native.StructureAll,
            _native.StructureType,
            identities,
            identityCache,
            candidates,
            ref skipped);
        ReadForwardRegistry(
            WorldPurchaseCandidateKind.Upgrade,
            _native.UpgradeAll,
            _native.UpgradeType,
            identities,
            identityCache,
            candidates,
            ref skipped);

        ValidateStructureCategories(candidates, identityCache);
        try
        {
            ReadForwardViews(candidates, identityCache);
        }
        catch (RelationFailure failure)
        {
            for (var index = 0; index < candidates.Count; index++)
                candidates[index].Status = failure.Status;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            for (var index = 0; index < candidates.Count; index++)
                candidates[index].Status = WorldPurchaseViewRelationStatus.Unreadable;
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            candidate.Routes.Sort(static (left, right) =>
            {
                var view = left.ViewId.CompareTo(right.ViewId);
                return view != 0 ? view : left.ListId.CompareTo(right.ListId);
            });
            var status = candidate.Status != WorldPurchaseViewRelationStatus.Resolved
                ? candidate.Status
                : candidate.Routes.Count == 0
                    ? WorldPurchaseViewRelationStatus.Missing
                    : WorldPurchaseViewRelationStatus.Resolved;
            var relation = new WorldPurchaseViewRelation(
                candidate.Id,
                candidate.Kind,
                candidate.CategoryId,
                status == WorldPurchaseViewRelationStatus.Resolved ? candidate.Routes.Count : 0,
                status);
            var routes = status == WorldPurchaseViewRelationStatus.Resolved
                ? candidate.Routes.ToArray()
                : Array.Empty<NativePurchaseViewRoute>();
            output.Append(relation);
            for (var routeIndex = 0; routeIndex < routes.Length; routeIndex++)
                routeOutput.Append(new WorldPurchaseViewRoute(
                    candidate.Id, routes[routeIndex].ListId, routes[routeIndex].ViewId));
            snapshot.Add(
                new CandidateKey(candidate.Kind, candidate.Id),
                new NativePurchaseViewResolution(
                    in relation, routes, DiagnosticName(candidate.Id)));
            if (status != WorldPurchaseViewRelationStatus.Resolved) unresolved++;
        }
        if (lifecycleEpoch > 0)
        {
            _snapshot = snapshot;
            _snapshotEpoch = lifecycleEpoch;
        }

        return candidates.Count;
    }

    private void ReadForwardRegistry(
        WorldPurchaseCandidateKind kind,
        FieldInfo registryField,
        Type expectedType,
        HashSet<Guid> identities,
        Dictionary<object, Guid> identityCache,
        List<ForwardCandidate> candidates,
        ref int skipped)
    {
        var registry = List(registryField.GetValue(null), expectedType.Name + ".All");
        foreach (var value in registry)
        {
            if (value is null || value.GetType() != expectedType)
            {
                skipped++;
                continue;
            }

            try
            {
                var id = CachedIdentity(CandidateIdentity(kind), value, identityCache);
                if (id == Guid.Empty || !identities.Add(id))
                {
                    skipped++;
                    continue;
                }
                candidates.Add(new ForwardCandidate(kind, id, value));
            }
            catch (Exception ex) when (ex is RelationFailure || IsExpected(ex))
            {
                skipped++;
            }
        }
    }

    private void ValidateStructureCategories(
        List<ForwardCandidate> candidates,
        Dictionary<object, Guid> identityCache)
    {
        var categories = new Dictionary<object, List<ForwardCandidate>>(ReferenceComparer.Instance);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Kind != WorldPurchaseCandidateKind.Structure) continue;
            try
            {
                var category = Exact(
                    _native.StructureCategory.GetValue(candidate.Native),
                    _native.StructureCategoryType,
                    "StructureSO.structureType");
                candidate.CategoryId = CachedIdentity(
                    _native.StructureCategoryIdentity, category, identityCache);
                if (candidate.CategoryId == Guid.Empty)
                {
                    candidate.Status = WorldPurchaseViewRelationStatus.Contradictory;
                    continue;
                }
                if (!categories.TryGetValue(category, out var members))
                    categories.Add(category, members = new List<ForwardCandidate>());
                members.Add(candidate);
            }
            catch (Exception ex) when (ex is RelationFailure || IsExpected(ex))
            {
                candidate.Status = WorldPurchaseViewRelationStatus.Unreadable;
            }
        }

        foreach (var pair in categories)
        {
            try
            {
                var authored = List(
                    _native.StructureCategoryMembers.GetValue(pair.Key),
                    "StructureTypeSO.structures");
                var counts = new Dictionary<object, int>(ReferenceComparer.Instance);
                foreach (var value in authored)
                {
                    var member = Exact(value, _native.StructureType, "StructureTypeSO.structures[]");
                    if (CachedIdentity(_native.StructureIdentity, member, identityCache) == Guid.Empty)
                        throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
                    counts.TryGetValue(member, out var count);
                    counts[member] = count + 1;
                }
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    var candidate = pair.Value[index];
                    if (!counts.TryGetValue(candidate.Native, out var count) || count != 1)
                        candidate.Status = WorldPurchaseViewRelationStatus.Contradictory;
                }
            }
            catch (Exception ex) when (ex is RelationFailure || IsExpected(ex))
            {
                for (var index = 0; index < pair.Value.Count; index++)
                    pair.Value[index].Status = WorldPurchaseViewRelationStatus.Unreadable;
            }
        }
    }

    private void ReadForwardViews(
        List<ForwardCandidate> candidates,
        Dictionary<object, Guid> identityCache)
    {
        var byNative = new Dictionary<object, ForwardCandidate>(ReferenceComparer.Instance);
        for (var index = 0; index < candidates.Count; index++)
            byNative[candidates[index].Native] = candidates[index];
        var listMembers = new Dictionary<object, List<ForwardCandidate>>(ReferenceComparer.Instance);
        var seenViews = new HashSet<Guid>();
        var views = List(_native.ViewAll.GetValue(null), "ViewSO.All");
        foreach (var value in views)
        {
            var view = Exact(value, _native.ViewType, "ViewSO");
            var viewId = CachedIdentity(_native.ViewIdentity, view, identityCache);
            if (viewId == Guid.Empty || !seenViews.Add(viewId))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            ReadForwardLists(view, viewId, _native.RelevantLists, byNative, listMembers, identityCache);
            ReadForwardLists(view, viewId, _native.AvailableLists, byNative, listMembers, identityCache);
        }
    }

    private void ReadForwardLists(
        object view,
        Guid viewId,
        FieldInfo source,
        Dictionary<object, ForwardCandidate> candidates,
        Dictionary<object, List<ForwardCandidate>> listMembers,
        Dictionary<object, Guid> identityCache)
    {
        var lists = List(source.GetValue(view), "ViewSO." + source.Name);
        foreach (var value in lists)
        {
            if (value is null) throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            var runtimeType = value.GetType();
            WorldPurchaseCandidateKind kind;
            if (runtimeType == _native.StructureListType) kind = WorldPurchaseCandidateKind.Structure;
            else if (runtimeType == _native.UpgradeListType) kind = WorldPurchaseCandidateKind.Upgrade;
            else continue;

            var listId = CachedIdentity(_native.ListIdentity, value, identityCache);
            if (listId == Guid.Empty)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            if (!listMembers.TryGetValue(value, out var members))
            {
                members = new List<ForwardCandidate>();
                var authored = List(
                    CandidateListMembers(kind).GetValue(value), runtimeType.Name + ".value");
                var seen = new HashSet<object>(ReferenceComparer.Instance);
                foreach (var memberValue in authored)
                {
                    var member = Exact(memberValue, CandidateType(kind), runtimeType.Name + ".value[]");
                    if (CachedIdentity(CandidateIdentity(kind), member, identityCache) == Guid.Empty ||
                        !candidates.TryGetValue(member, out var candidate) ||
                        candidate.Kind != kind || !seen.Add(member))
                        throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
                    members.Add(candidate);
                }
                listMembers.Add(value, members);
            }

            for (var index = 0; index < members.Count; index++)
            {
                var candidate = members[index];
                var duplicate = false;
                for (var routeIndex = 0; routeIndex < candidate.Routes.Count; routeIndex++)
                {
                    var route = candidate.Routes[routeIndex];
                    if (route.ViewId == viewId && route.ListId == listId) duplicate = true;
                }
                if (!duplicate)
                    candidate.Routes.Add(new NativePurchaseViewRoute(
                        listId,
                        DiagnosticName(listId),
                        viewId,
                        DiagnosticName(viewId),
                        view));
            }
        }
    }

    private static Guid CachedIdentity(
        MethodInfo method,
        object target,
        Dictionary<object, Guid> cache)
    {
        if (cache.TryGetValue(target, out var id)) return id;
        if (method.Invoke(target, Array.Empty<object>()) is not Guid value)
            throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable);
        cache.Add(target, value);
        return value;
    }

    private sealed class ForwardCandidate
    {
        internal ForwardCandidate(WorldPurchaseCandidateKind kind, Guid id, object native)
        {
            Kind = kind;
            Id = id;
            Native = native;
        }

        internal WorldPurchaseCandidateKind Kind { get; }
        internal Guid Id { get; }
        internal object Native { get; }
        internal Guid CategoryId { get; set; }
        internal WorldPurchaseViewRelationStatus Status { get; set; }
        internal List<NativePurchaseViewRoute> Routes { get; } = new();
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly IEqualityComparer<object> Instance = new ReferenceComparer();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    internal void Invalidate()
    {
        _snapshot = new Dictionary<CandidateKey, NativePurchaseViewResolution>();
        _snapshotEpoch = 0;
    }

    internal bool TryGetCaptured(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        long lifecycleEpoch,
        out NativePurchaseViewResolution resolution)
    {
        if (lifecycleEpoch <= 0 || _snapshotEpoch != lifecycleEpoch)
        {
            resolution = default;
            return false;
        }
        return _snapshot.TryGetValue(new CandidateKey(kind, candidateId), out resolution);
    }

    internal NativePurchaseViewResolution[] Captured(long lifecycleEpoch)
    {
        if (lifecycleEpoch <= 0 || _snapshotEpoch != lifecycleEpoch || _snapshot.Count == 0)
            return Array.Empty<NativePurchaseViewResolution>();
        var rows = new NativePurchaseViewResolution[_snapshot.Count];
        _snapshot.Values.CopyTo(rows, 0);
        Array.Sort(rows, static (left, right) => left.Relation.CandidateId.CompareTo(right.Relation.CandidateId));
        return rows;
    }

    internal string[] DescribeCaptured(long lifecycleEpoch)
    {
        var captured = Captured(lifecycleEpoch);
        if (captured.Length == 0) return Array.Empty<string>();
        var descriptions = new string[captured.Length];
        for (var candidateIndex = 0; candidateIndex < captured.Length; candidateIndex++)
        {
            var candidate = captured[candidateIndex];
            var builder = new StringBuilder();
            builder.Append(candidate.Relation.CandidateId.ToString("D"))
                .Append(' ')
                .Append(candidate.CandidateName)
                .Append(" routes=[");
            for (var routeIndex = 0; routeIndex < candidate.Routes.Length; routeIndex++)
            {
                if (routeIndex > 0) builder.Append(", ");
                var route = candidate.Routes[routeIndex];
                builder.Append("(view=")
                    .Append(route.ViewId.ToString("D"))
                    .Append(' ')
                    .Append(route.ViewName)
                    .Append(", list=")
                    .Append(route.ListId.ToString("D"))
                    .Append(' ')
                    .Append(route.ListName)
                    .Append(", view-available=");
                var available = "unreadable";
                try
                {
                    if (route.View is not null && route.View.GetType() == _native.ViewType &&
                        _native.ViewAvailable.Invoke(route.View, Array.Empty<object>()) is bool value)
                        available = value ? "true" : "false";
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                }
                builder.Append(available).Append(')');
            }
            builder.Append(']');
            descriptions[candidateIndex] = builder.ToString();
        }
        return descriptions;
    }

    /// <summary>Resolves one already identity-checked candidate through the complete live graph.</summary>
    internal NativePurchaseViewResolution Resolve(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate)
    {
        var reads = default(NativePurchaseViewAdmissionReadCounts);
        return ResolveProfiled(kind, candidateId, candidate, ref reads);
    }

    internal NativePurchaseViewResolution ResolveProfiled(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var categoryId = Guid.Empty;
        try
        {
            var expected = CandidateType(kind);
            if (candidateId == Guid.Empty || candidate is null || candidate.GetType() != expected)
                return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Contradictory);
            if (Identity(CandidateIdentity(kind), candidate, ref reads) != candidateId)
                return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Contradictory);

            if (kind == WorldPurchaseCandidateKind.Structure)
            {
                var category = Exact(
                    ReadField(_native.StructureCategory, candidate, ref reads),
                    _native.StructureCategoryType,
                    "StructureSO.structureType");
                categoryId = Identity(_native.StructureCategoryIdentity, category, ref reads);
                if (categoryId == Guid.Empty ||
                    CountExactMembership(
                        List(ReadField(_native.StructureCategoryMembers, category, ref reads),
                            "StructureTypeSO.structures"),
                        _native.StructureType,
                        _native.StructureIdentity,
                        candidateId,
                        candidate,
                        ref reads) != 1)
                {
                    return Failed(
                        kind,
                        candidateId,
                        categoryId,
                        WorldPurchaseViewRelationStatus.Contradictory);
                }
            }

            return ResolveViewLists(kind, candidateId, categoryId, candidate, ref reads);
        }
        catch (RelationFailure failure)
        {
            return Failed(kind, candidateId, categoryId, failure.Status);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Unreadable);
        }
    }

    /// <summary>Asks the exact resolved live <c>ViewSO</c> for its composed progression verdict.</summary>
    internal bool TryReadAvailability(
        in NativePurchaseViewResolution resolution,
        out bool available)
    {
        var reads = default(NativePurchaseViewAdmissionReadCounts);
        return TryReadAvailabilityProfiled(in resolution, ref reads, out available);
    }

    internal bool TryReadAvailabilityProfiled(
        in NativePurchaseViewResolution resolution,
        ref NativePurchaseViewAdmissionReadCounts reads,
        out bool available)
    {
        available = false;
        if (resolution.Relation.Status != WorldPurchaseViewRelationStatus.Resolved ||
            resolution.Routes.Length == 0)
            return false;
        var unreadable = false;
        try
        {
            for (var index = 0; index < resolution.Routes.Length; index++)
            {
                var route = resolution.Routes[index];
                if (route.ListId == Guid.Empty)
                {
                    unreadable = true;
                    continue;
                }

                var alreadyVisited = false;
                for (var prior = 0; prior < index; prior++)
                {
                    if (resolution.Routes[prior].ListId != route.ListId) continue;
                    alreadyVisited = true;
                    break;
                }
                if (alreadyVisited) continue;

                var routeReadable = true;
                var routeAvailable = true;
                for (var member = index; member < resolution.Routes.Length; member++)
                {
                    var routeMember = resolution.Routes[member];
                    if (routeMember.ListId != route.ListId) continue;
                    var view = routeMember.View;
                    if (routeMember.ViewId == Guid.Empty ||
                        view is null ||
                        view.GetType() != _native.ViewType)
                    {
                        routeReadable = false;
                        continue;
                    }
                    reads.AddMethodCall();
                    if (_native.ViewAvailable.Invoke(view, Array.Empty<object>()) is not bool value)
                    {
                        routeReadable = false;
                        continue;
                    }
                    if (!value) routeAvailable = false;
                }

                if (routeReadable && routeAvailable)
                {
                    available = true;
                    return true;
                }
                if (!routeReadable) unreadable = true;
            }
            return !unreadable;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private int ReadRegistry(
        WorldPurchaseCandidateKind kind,
        FieldInfo registryField,
        Type expectedType,
        HashSet<Guid> identities,
        Dictionary<CandidateKey, NativePurchaseViewResolution> snapshot,
        WorldRelationBuffer<WorldPurchaseViewRelation> output,
        WorldRelationBuffer<WorldPurchaseViewRoute> routeOutput,
        ref int unresolved,
        ref int skipped)
    {
        var registry = List(registryField.GetValue(null), expectedType.Name + ".All");
        var sampled = 0;
        foreach (var value in registry)
        {
            if (value is null || value.GetType() != expectedType)
            {
                skipped++;
                continue;
            }

            var candidate = value;
            Guid id;
            try
            {
                var reads = default(NativePurchaseViewAdmissionReadCounts);
                id = Identity(CandidateIdentity(kind), candidate, ref reads);
            }
            catch (Exception ex) when (ex is RelationFailure || IsExpected(ex))
            {
                skipped++;
                continue;
            }
            if (id == Guid.Empty || !identities.Add(id))
            {
                skipped++;
                continue;
            }
            var resolution = Resolve(kind, id, candidate);
            if (resolution.CandidateName.Length == 0)
            {
                var relation = resolution.Relation;
                resolution = new NativePurchaseViewResolution(
                    in relation,
                    resolution.Routes,
                    DiagnosticName(id));
            }
            output.Append(resolution.Relation);
            for (var routeIndex = 0; routeIndex < resolution.Routes.Length; routeIndex++)
            {
                var route = resolution.Routes[routeIndex];
                routeOutput.Append(new WorldPurchaseViewRoute(id, route.ListId, route.ViewId));
            }
            snapshot.Add(new CandidateKey(kind, id), resolution);
            if (resolution.Relation.Status != WorldPurchaseViewRelationStatus.Resolved) unresolved++;
            sampled++;
        }
        return sampled;
    }

    private NativePurchaseViewResolution ResolveViewLists(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var routes = new List<NativePurchaseViewRoute>();
        var views = List(ReadField(_native.ViewAll, null, ref reads), "ViewSO.All");
        var seenViews = new HashSet<Guid>();
        foreach (var value in views)
        {
            reads.AddListEntry();
            var view = Exact(value, _native.ViewType, "ViewSO");
            var viewId = Identity(_native.ViewIdentity, view, ref reads);
            if (viewId == Guid.Empty || !seenViews.Add(viewId))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            MatchLists(
                kind,
                candidateId,
                candidate,
                view,
                viewId,
                _native.RelevantLists,
                routes,
                ref reads);
            MatchLists(
                kind,
                candidateId,
                candidate,
                view,
                viewId,
                _native.AvailableLists,
                routes,
                ref reads);
        }

        if (routes.Count == 0)
            return Failed(kind, candidateId, categoryId, WorldPurchaseViewRelationStatus.Missing);
        routes.Sort(static (left, right) =>
        {
            var view = left.ViewId.CompareTo(right.ViewId);
            return view != 0 ? view : left.ListId.CompareTo(right.ListId);
        });
        var relation = new WorldPurchaseViewRelation(
            candidateId,
            kind,
            categoryId,
            routes.Count,
            WorldPurchaseViewRelationStatus.Resolved);
        return new NativePurchaseViewResolution(
            in relation,
            routes.ToArray(),
            DiagnosticName(candidateId));
    }

    private void MatchLists(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        object candidate,
        object view,
        Guid viewId,
        FieldInfo source,
        List<NativePurchaseViewRoute> routes,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var lists = List(ReadField(source, view, ref reads), "ViewSO." + source.Name);
        foreach (var value in lists)
        {
            reads.AddListEntry();
            if (value is null)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            var runtimeType = value.GetType();
            var expectedListType = CandidateListType(kind);
            var otherListType = CandidateListType(
                kind == WorldPurchaseCandidateKind.Structure
                    ? WorldPurchaseCandidateKind.Upgrade
                    : WorldPurchaseCandidateKind.Structure);
            if (runtimeType == otherListType) continue;
            if (runtimeType != expectedListType)
            {
                if (expectedListType.IsAssignableFrom(runtimeType))
                    throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
                continue;
            }

            var listId = Identity(_native.ListIdentity, value, ref reads);
            if (listId == Guid.Empty)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            var members = List(
                ReadField(CandidateListMembers(kind), value, ref reads),
                runtimeType.Name + ".value");
            var matches = CountExactMembership(
                members,
                CandidateType(kind),
                CandidateIdentity(kind),
                candidateId,
                candidate,
                ref reads);
            if (matches == 0) continue;
            if (matches != 1)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);

            var duplicate = false;
            for (var index = 0; index < routes.Count; index++)
            {
                if (routes[index].ViewId == viewId && routes[index].ListId == listId)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate) continue;
            routes.Add(new NativePurchaseViewRoute(
                listId,
                DiagnosticName(listId),
                viewId,
                DiagnosticName(viewId),
                view));
        }
    }

    private static int CountExactMembership(
        IList members,
        Type expectedType,
        MethodInfo identity,
        Guid candidateId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var matches = 0;
        foreach (var value in members)
        {
            reads.AddListEntry();
            var member = Exact(value, expectedType, expectedType.Name + " list member");
            var memberId = Identity(identity, member, ref reads);
            if (memberId == Guid.Empty)
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            if (memberId != candidateId) continue;
            if (!ReferenceEquals(member, candidate))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            matches++;
        }
        return matches;
    }

    private static NativePurchaseViewResolution Failed(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        WorldPurchaseViewRelationStatus status)
    {
        var relation = new WorldPurchaseViewRelation(
            candidateId,
            kind,
            categoryId,
            0,
            status);
        return new NativePurchaseViewResolution(in relation, Array.Empty<NativePurchaseViewRoute>(), string.Empty);
    }

    private static string DiagnosticName(Guid uuid)
    {
#if SERVICE_CYCLE_PROFILE
        return OrbModding.Common.EntityIdentityFormatter.Format(uuid);
#else
        return string.Empty;
#endif
    }

    private readonly struct CandidateKey : IEquatable<CandidateKey>
    {
        internal CandidateKey(WorldPurchaseCandidateKind kind, Guid candidateId)
        {
            Kind = kind;
            CandidateId = candidateId;
        }
        private WorldPurchaseCandidateKind Kind { get; }
        private Guid CandidateId { get; }
        public bool Equals(CandidateKey other) => Kind == other.Kind && CandidateId == other.CandidateId;
        public override bool Equals(object? value) => value is CandidateKey other && Equals(other);
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ CandidateId.GetHashCode());
    }

    private Type CandidateType(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure ? _native.StructureType : _native.UpgradeType;

    private Type CandidateListType(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureListType
            : _native.UpgradeListType;

    private MethodInfo CandidateIdentity(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureIdentity
            : _native.UpgradeIdentity;

    private FieldInfo CandidateListMembers(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureListMembers
            : _native.UpgradeListMembers;

    private static Guid Identity(
        MethodInfo method,
        object target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddMethodCall();
        if (method.Invoke(target, Array.Empty<object>()) is Guid value) return value;
        throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable);
    }

    private static object? ReadField(
        FieldInfo field,
        object? target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddFieldRead();
        return field.GetValue(target);
    }

    private static object Exact(object? value, Type expected, string contract) =>
        value is not null && value.GetType() == expected
            ? value
            : throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory, contract);

    private static IList List(object? value, string contract) =>
        value as IList ??
        throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable, contract);

    private static bool IsExpected(Exception exception) => exception is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        TypeLoadException;

    private sealed class RelationFailure : Exception
    {
        internal RelationFailure(WorldPurchaseViewRelationStatus status, string? contract = null)
            : base(contract ?? status.ToString()) => Status = status;

        internal WorldPurchaseViewRelationStatus Status { get; }
    }

    /// <summary>One complete lifecycle-bound metadata set; partial binding never reaches a read.</summary>
    private sealed class BindingSet
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private BindingSet(
            Type viewType,
            Type structureType,
            Type upgradeType,
            Type structureCategoryType,
            Type structureListType,
            Type upgradeListType,
            FieldInfo viewAll,
            FieldInfo structureAll,
            FieldInfo upgradeAll,
            FieldInfo relevantLists,
            FieldInfo availableLists,
            FieldInfo structureCategory,
            FieldInfo structureCategoryMembers,
            MethodInfo viewIdentity,
            MethodInfo structureIdentity,
            MethodInfo upgradeIdentity,
            MethodInfo structureCategoryIdentity,
            MethodInfo listIdentity,
            MethodInfo viewAvailable,
            FieldInfo structureListMembers,
            FieldInfo upgradeListMembers)
        {
            ViewType = viewType;
            StructureType = structureType;
            UpgradeType = upgradeType;
            StructureCategoryType = structureCategoryType;
            StructureListType = structureListType;
            UpgradeListType = upgradeListType;
            ViewAll = viewAll;
            StructureAll = structureAll;
            UpgradeAll = upgradeAll;
            RelevantLists = relevantLists;
            AvailableLists = availableLists;
            StructureCategory = structureCategory;
            StructureCategoryMembers = structureCategoryMembers;
            ViewIdentity = viewIdentity;
            StructureIdentity = structureIdentity;
            UpgradeIdentity = upgradeIdentity;
            StructureCategoryIdentity = structureCategoryIdentity;
            ListIdentity = listIdentity;
            ViewAvailable = viewAvailable;
            StructureListMembers = structureListMembers;
            UpgradeListMembers = upgradeListMembers;
        }

        internal Type ViewType { get; }
        internal Type StructureType { get; }
        internal Type UpgradeType { get; }
        internal Type StructureCategoryType { get; }
        internal Type StructureListType { get; }
        internal Type UpgradeListType { get; }
        internal FieldInfo ViewAll { get; }
        internal FieldInfo StructureAll { get; }
        internal FieldInfo UpgradeAll { get; }
        internal FieldInfo RelevantLists { get; }
        internal FieldInfo AvailableLists { get; }
        internal FieldInfo StructureCategory { get; }
        internal FieldInfo StructureCategoryMembers { get; }
        internal MethodInfo ViewIdentity { get; }
        internal MethodInfo StructureIdentity { get; }
        internal MethodInfo UpgradeIdentity { get; }
        internal MethodInfo StructureCategoryIdentity { get; }
        internal MethodInfo ListIdentity { get; }
        internal MethodInfo ViewAvailable { get; }
        internal FieldInfo StructureListMembers { get; }
        internal FieldInfo UpgradeListMembers { get; }

        internal static bool TryCreate(
            Func<string, Type?> resolve,
            out BindingSet? bindings,
            out string reason)
        {
            bindings = null;
            try
            {
                var id = Type(resolve, "IdScriptableObject");
                var abstractList = Type(resolve, "AbstractListVariable");
                var view = Type(resolve, "ViewSO");
                var structure = Type(resolve, "StructureSO");
                var upgrade = Type(resolve, "UpgradeSO");
                var structureCategory = Type(resolve, "StructureTypeSO");
                var structureList = Type(resolve, "StructureListVariable");
                var upgradeList = Type(resolve, "UpgradeListVariable");
                var listCollection = typeof(List<>).MakeGenericType(abstractList);

                bindings = new BindingSet(
                    view,
                    structure,
                    upgrade,
                    structureCategory,
                    structureList,
                    upgradeList,
                    CollectionField(view, "All", view, Static),
                    CollectionField(structure, "All", structure, Static),
                    CollectionField(upgrade, "All", upgrade, Static),
                    Field(view, "relevantLists", Instance, listCollection),
                    Field(view, "availableLists", Instance, listCollection),
                    Field(structure, "structureType", Instance, structureCategory),
                    CollectionField(structureCategory, "structures", structure),
                    MethodFromHierarchy(view, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structure, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(upgrade, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structureCategory, id, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structureList, id, "GetGuid", typeof(Guid)),
                    Method(view, "IsAvailable", typeof(bool)),
                    FieldFromHierarchy(
                        structureList,
                        abstractList,
                        "value",
                        typeof(List<>).MakeGenericType(structure)),
                    FieldFromHierarchy(
                        upgradeList,
                        abstractList,
                        "value",
                        typeof(List<>).MakeGenericType(upgrade)));
                reason = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
            {
                reason = "The exact Auto Buy owning-view binding set is unavailable: " + ex.Message;
                return false;
            }
        }

        private static Type Type(Func<string, Type?> resolve, string name) =>
            resolve(name) ?? throw new InvalidOperationException(name + " was unavailable.");

        private static FieldInfo CollectionField(
            Type owner,
            string name,
            Type element,
            BindingFlags flags = Instance) =>
            Field(owner, name, flags, typeof(List<>).MakeGenericType(element));

        private static FieldInfo Field(Type owner, string name, BindingFlags flags, Type fieldType)
        {
            var field = owner.GetField(name, flags | BindingFlags.DeclaredOnly);
            return field is not null && field.FieldType == fieldType
                ? field
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name} : {fieldType.Name} was unavailable.");
        }

        private static FieldInfo FieldFromHierarchy(
            Type owner,
            Type abstractList,
            string name,
            Type fieldType)
        {
            var field = owner.GetField(name, Instance);
            var declaring = field?.DeclaringType;
            return field is not null && field.FieldType == fieldType && declaring is not null &&
                   declaring.IsGenericType &&
                   declaring.GetGenericTypeDefinition().BaseType == abstractList
                ? field
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name} inherited from AbstractListVariable`1 was unavailable.");
        }

        private static MethodInfo Method(Type owner, string name, Type returnType)
        {
            var method = owner.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                System.Type.EmptyTypes,
                null);
            return method is not null && method.ReturnType == returnType
                ? method
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name}() : {returnType.Name} was unavailable.");
        }

        private static MethodInfo MethodFromHierarchy(
            Type owner,
            Type declaringType,
            string name,
            Type returnType)
        {
            var method = owner.GetMethod(name, Instance, null, System.Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == declaringType &&
                   method.ReturnType == returnType
                ? method
                : throw new InvalidOperationException(
                    $"{owner.Name}.{name}() inherited from {declaringType.Name} was unavailable.");
        }
    }
}

internal static class WorldPurchaseViewRouteLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldPurchaseViewRoute> table,
        Guid candidateId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].CandidateId.CompareTo(candidateId);
            if (comparison < 0) low = middle + 1;
            else
            {
                if (comparison == 0) found = middle;
                high = middle - 1;
            }
        }
        if (found < 0)
        {
            start = 0;
            count = 0;
            return false;
        }
        start = found;
        var end = found + 1;
        while (end < table.Count && table[end].CandidateId == candidateId) end++;
        count = end - found;
        return true;
    }
}

/// <summary>Collects authored Auto Buy view relations once per lifecycle epoch.</summary>
internal sealed class WorldPurchaseViewRelationReader : IWorldCategoryReader
{
    internal const string CategoryName = "purchase view relations";

    private readonly Func<string, Type?> _resolve;
    private readonly bool _production;
    private NativePurchaseViewAdmissionResolver? _resolver;
    private string _unavailable = string.Empty;

    internal WorldPurchaseViewRelationReader(Func<string, Type?> resolve, bool production = false)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _production = production;
        TryBind();
    }

    public string Category => CategoryName;
    public bool IsAvailable => _resolver is not null;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.PurchaseViewRelations.Reset();
        frame.PurchaseViewRoutes.Reset();
        if (_resolver is null && !TryBind())
            return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            var sampled = _resolver!.ReadAll(
                frame.CollectedAtEpoch,
                frame.PurchaseViewRelations,
                frame.PurchaseViewRoutes,
                out var unresolved,
                out var skipped);
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                skipped,
                unresolved == 0 && skipped == 0
                    ? string.Empty
                    : unresolved + " candidate owning-view relation(s) were retained as named fail-closed facts; " +
                      skipped + " candidate(s) lacked a publishable exact identity");
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or
                                   InvalidOperationException or TargetException or MemberAccessException)
        {
            _resolver!.Invalidate();
            frame.PurchaseViewRelations.Reset();
            frame.PurchaseViewRoutes.Reset();
            return WorldCategoryReport.Missing(Category, ex.GetBaseException().Message);
        }
    }

    private bool TryBind()
    {
        var created = _production
            ? NativePurchaseViewAdmissionResolver.TryCreateProduction(
                _resolve,
                out var resolver,
                out var reason)
            : NativePurchaseViewAdmissionResolver.TryCreate(
                _resolve,
                out resolver,
                out reason);
        _resolver = resolver;
        _unavailable = created ? string.Empty : reason;
        return created;
    }
}
