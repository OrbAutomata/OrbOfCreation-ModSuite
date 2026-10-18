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
    private readonly Func<IDictionary?>? _identityRegistry;
    private Dictionary<CandidateKey, NativePurchaseViewResolution> _snapshot = new();
    private long _snapshotEpoch;
    internal long CapturedEpoch => _snapshotEpoch;

    /// <summary>How many candidates the published snapshot holds. Named by an unadmitted purchase,
    /// where "stamped at an epoch you did not ask for" and "stamped but empty" are different
    /// stories and the row count is what separates them.</summary>
    internal int CapturedCount => _snapshot.Count;

    private NativePurchaseViewAdmissionResolver(BindingSet native)
    {
        _native = native;
        _identityRegistry = NativeAccessorBinder.StaticDictionaryAccessor(native.IdentityType, "RuntimeLookup");
    }

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
        WorldRelationBuffer<WorldUpgradeListMembership> membershipOutput,
        out int unresolved,
        out int skipped,
        out string membershipFailure)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (membershipOutput is null) throw new ArgumentNullException(nameof(membershipOutput));
        unresolved = 0;
        skipped = 0;
        membershipFailure = string.Empty;
        var identities = new HashSet<Guid>();
        var identityCache = new Dictionary<object, Guid>(ReferenceComparer.Instance);
        var candidates = new List<ForwardCandidate>();
        var snapshot = new Dictionary<CandidateKey, NativePurchaseViewResolution>();
        var memberships = new List<WorldUpgradeListMembership>();
        var readUpgradeLists = new HashSet<Guid>();
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
            ReadForwardViews(candidates, identityCache, memberships, readUpgradeLists);
        }
        catch (RelationFailure failure)
        {
            for (var index = 0; index < candidates.Count; index++)
                candidates[index].Status = failure.Status;
            membershipFailure = "the authored view walk failed: " + failure.Message;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            for (var index = 0; index < candidates.Count; index++)
                candidates[index].Status = WorldPurchaseViewRelationStatus.Unreadable;
            membershipFailure = "the authored view walk failed: " + ex.GetBaseException().Message;
        }

        if (membershipFailure.Length == 0)
            ReadPinnedUpgradeLists(memberships, readUpgradeLists, identityCache, out membershipFailure);

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
        // Membership is published whole or not at all. A row that is on no list and a row whose list
        // was never read look identical from the outside, so a partial table would silently move the
        // second kind into the first: exactly the "on no screen" answer the column exists to give
        // truthfully. One named failure over every row beats a quiet demotion on thirty of them.
        if (membershipFailure.Length == 0)
            for (var index = 0; index < memberships.Count; index++)
                membershipOutput.Append(memberships[index]);

        if (lifecycleEpoch > 0)
        {
            _snapshot = snapshot;
            _snapshotEpoch = lifecycleEpoch;
        }

        return candidates.Count;
    }

    /// <summary>
    /// Reads the authored upgrade lists the view walk never reached, by the identity they carry.
    /// </summary>
    /// <remarks>
    /// The registry lookup is the same one the rest of the suite resolves stable uuids through, and
    /// the expected native type is checked on the way out — an identity that answers with something
    /// other than an <c>UpgradeListVariable</c> is a build the pin no longer describes, so it is
    /// named rather than skipped.
    /// </remarks>
    private void ReadPinnedUpgradeLists(
        List<WorldUpgradeListMembership> memberships,
        HashSet<Guid> alreadyRead,
        Dictionary<object, Guid> identityCache,
        out string failure)
    {
        failure = string.Empty;
        var registry = _identityRegistry?.Invoke();
        for (var index = 0; index < WorldUpgradeScreenLists.All.Length; index++)
        {
            var listId = WorldUpgradeScreenLists.All[index];
            if (alreadyRead.Contains(listId)) continue;
            if (registry is null)
            {
                failure = "the native identity registry was unreadable";
                return;
            }

            var list = registry[listId];
            if (list is null || list.GetType() != _native.UpgradeListType)
            {
                failure = "pinned upgrade list " + listId.ToString("D") +
                    " did not resolve to an UpgradeListVariable";
                return;
            }

            try
            {
                if (CachedIdentity(_native.ListIdentity, list, identityCache) != listId)
                {
                    failure = "pinned upgrade list " + listId.ToString("D") +
                        " answered to a different identity";
                    return;
                }
                var authored = List(_native.UpgradeListMembers(list), "UpgradeListVariable.value");
                var seen = new HashSet<Guid>();
                foreach (var value in authored)
                {
                    var member = Exact(value, _native.UpgradeType, "UpgradeListVariable.value[]");
                    var memberId = CachedIdentity(_native.UpgradeIdentity, member, identityCache);
                    if (memberId == Guid.Empty || !seen.Add(memberId))
                    {
                        failure = "pinned upgrade list " + listId.ToString("D") +
                            " carried a member with no usable identity";
                        return;
                    }
                    memberships.Add(new WorldUpgradeListMembership(memberId, listId));
                }
            }
            catch (Exception ex) when (ex is RelationFailure || NativeFailureClassifier.IsExpected(ex))
            {
                failure = "pinned upgrade list " + listId.ToString("D") + " was unreadable: " +
                    ex.GetBaseException().Message;
                return;
            }
        }
    }

    private void ReadForwardRegistry(
        WorldPurchaseCandidateKind kind,
        Func<IList?> registryField,
        Type expectedType,
        HashSet<Guid> identities,
        Dictionary<object, Guid> identityCache,
        List<ForwardCandidate> candidates,
        ref int skipped)
    {
        var registry = List(registryField(), expectedType.Name + ".All");
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
            catch (Exception ex) when (ex is RelationFailure || NativeFailureClassifier.IsExpected(ex))
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
                    _native.StructureCategory(candidate.Native),
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
            catch (Exception ex) when (ex is RelationFailure || NativeFailureClassifier.IsExpected(ex))
            {
                candidate.Status = WorldPurchaseViewRelationStatus.Unreadable;
            }
        }

        foreach (var pair in categories)
        {
            try
            {
                var authored = List(
                    _native.StructureCategoryMembers(pair.Key),
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
            catch (Exception ex) when (ex is RelationFailure || NativeFailureClassifier.IsExpected(ex))
            {
                for (var index = 0; index < pair.Value.Count; index++)
                    pair.Value[index].Status = WorldPurchaseViewRelationStatus.Unreadable;
            }
        }
    }

    private void ReadForwardViews(
        List<ForwardCandidate> candidates,
        Dictionary<object, Guid> identityCache,
        List<WorldUpgradeListMembership> memberships,
        HashSet<Guid> readUpgradeLists)
    {
        var byNative = new Dictionary<object, ForwardCandidate>(ReferenceComparer.Instance);
        for (var index = 0; index < candidates.Count; index++)
            byNative[candidates[index].Native] = candidates[index];
        var listMembers = new Dictionary<object, List<ForwardCandidate>>(ReferenceComparer.Instance);
        var seenViews = new HashSet<Guid>();
        var views = List(_native.ViewAll(), "ViewSO.All");
        foreach (var value in views)
        {
            var view = Exact(value, _native.ViewType, "ViewSO");
            var viewId = CachedIdentity(_native.ViewIdentity, view, identityCache);
            if (viewId == Guid.Empty || !seenViews.Add(viewId))
                throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory);
            ReadForwardLists(
                view, viewId, _native.RelevantLists, "ViewSO.relevantLists", byNative, listMembers,
                identityCache, memberships, readUpgradeLists);
            ReadForwardLists(
                view, viewId, _native.AvailableLists, "ViewSO.availableLists", byNative, listMembers,
                identityCache, memberships, readUpgradeLists);
        }
    }

    private void ReadForwardLists(
        object view,
        Guid viewId,
        Func<object, IList?> source,
        string sourceName,
        Dictionary<object, ForwardCandidate> candidates,
        Dictionary<object, List<ForwardCandidate>> listMembers,
        Dictionary<object, Guid> identityCache,
        List<WorldUpgradeListMembership> memberships,
        HashSet<Guid> readUpgradeLists)
    {
        var lists = List(source(view), sourceName);
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
                var authored = List(CandidateListMembers(kind)(value), runtimeType.Name + ".value");
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

                // The same walk, published a second way. A route says a view carries this candidate;
                // membership says which authored list it sits on, which is what names the screen.
                if (kind == WorldPurchaseCandidateKind.Upgrade && readUpgradeLists.Add(listId))
                    for (var index = 0; index < members.Count; index++)
                        memberships.Add(new WorldUpgradeListMembership(members[index].Id, listId));
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
        Func<object, Guid> identity,
        object target,
        Dictionary<object, Guid> cache)
    {
        if (cache.TryGetValue(target, out var id)) return id;
        var value = identity(target);
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
                    if (route.View is not null && route.View.GetType() == _native.ViewType)
                        available = _native.ViewAvailable(route.View) ? "true" : "false";
                }
                catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
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
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
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
                    if (!_native.ViewAvailable(view)) routeAvailable = false;
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
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }

    private NativePurchaseViewResolution ResolveViewLists(
        WorldPurchaseCandidateKind kind,
        Guid candidateId,
        Guid categoryId,
        object candidate,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var routes = new List<NativePurchaseViewRoute>();
        reads.AddFieldRead();
        var views = List(_native.ViewAll(), "ViewSO.All");
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
                "ViewSO.relevantLists",
                routes,
                ref reads);
            MatchLists(
                kind,
                candidateId,
                candidate,
                view,
                viewId,
                _native.AvailableLists,
                "ViewSO.availableLists",
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
        Func<object, IList?> source,
        string sourceName,
        List<NativePurchaseViewRoute> routes,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        var lists = List(ReadField(source, view, ref reads), sourceName);
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
        Func<object, Guid> identity,
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

    private Func<object, Guid> CandidateIdentity(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureIdentity
            : _native.UpgradeIdentity;

    private Func<object, IList?> CandidateListMembers(WorldPurchaseCandidateKind kind) =>
        kind == WorldPurchaseCandidateKind.Structure
            ? _native.StructureListMembers
            : _native.UpgradeListMembers;

    private static Guid Identity(
        Func<object, Guid> identity,
        object target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddMethodCall();
        return identity(target);
    }

    private static T ReadField<T>(
        Func<object, T> field,
        object target,
        ref NativePurchaseViewAdmissionReadCounts reads)
    {
        reads.AddFieldRead();
        return field(target);
    }

    private static object Exact(object? value, Type expected, string contract) =>
        value is not null && value.GetType() == expected
            ? value
            : throw new RelationFailure(WorldPurchaseViewRelationStatus.Contradictory, contract);

    private static IList List(object? value, string contract) =>
        value as IList ??
        throw new RelationFailure(WorldPurchaseViewRelationStatus.Unreadable, contract);

    private sealed class RelationFailure : Exception
    {
        internal RelationFailure(WorldPurchaseViewRelationStatus status, string? contract = null)
            : base(contract ?? status.ToString()) => Status = status;

        internal WorldPurchaseViewRelationStatus Status { get; }
    }

    /// <summary>One complete lifecycle-bound metadata set; partial binding never reaches a read.</summary>
    private sealed class BindingSet
    {
        internal static readonly string[] ContractIds =
        {
            "resource-bar.identity.type-capture",
            "auto-buy.owning-view.abstract-list-type-capture",
            "spell-workbench.view-type-action",
            "structure-lifecycle.structure.type-action",
            "auto-buy.owning-view.upgrade-type-capture",
            "structure-type-so.upgradeable-base",
            "auto-buy.owning-view.structure-list-type-capture",
            "upgrade-screen.list-type",
            "view.all",
            "structure.all",
            "upgrade.all",
            "auto-buy.owning-view.relevant-lists-capture",
            "auto-buy.owning-view.available-lists-capture",
            "structure.structure-type",
            "auto-buy.owning-view.structure-category-members-capture",
            "id-scriptable-object.get-guid",
            "auto-buy.owning-view.view-availability-action",
            "auto-buy.owning-view.list-membership-capture",
        };

        private BindingSet(
            Type identityType,
            Type viewType,
            Type structureType,
            Type upgradeType,
            Type structureCategoryType,
            Type structureListType,
            Type upgradeListType,
            Func<IList?> viewAll,
            Func<IList?> structureAll,
            Func<IList?> upgradeAll,
            Func<object, IList?> relevantLists,
            Func<object, IList?> availableLists,
            Func<object, object?> structureCategory,
            Func<object, IList?> structureCategoryMembers,
            Func<object, Guid> viewIdentity,
            Func<object, Guid> structureIdentity,
            Func<object, Guid> upgradeIdentity,
            Func<object, Guid> structureCategoryIdentity,
            Func<object, Guid> listIdentity,
            Func<object, bool> viewAvailable,
            Func<object, IList?> structureListMembers,
            Func<object, IList?> upgradeListMembers)
        {
            IdentityType = identityType;
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

        internal Type IdentityType { get; }
        internal Type ViewType { get; }
        internal Type StructureType { get; }
        internal Type UpgradeType { get; }
        internal Type StructureCategoryType { get; }
        internal Type StructureListType { get; }
        internal Type UpgradeListType { get; }
        internal Func<IList?> ViewAll { get; }
        internal Func<IList?> StructureAll { get; }
        internal Func<IList?> UpgradeAll { get; }
        internal Func<object, IList?> RelevantLists { get; }
        internal Func<object, IList?> AvailableLists { get; }
        internal Func<object, object?> StructureCategory { get; }
        internal Func<object, IList?> StructureCategoryMembers { get; }
        internal Func<object, Guid> ViewIdentity { get; }
        internal Func<object, Guid> StructureIdentity { get; }
        internal Func<object, Guid> UpgradeIdentity { get; }
        internal Func<object, Guid> StructureCategoryIdentity { get; }
        internal Func<object, Guid> ListIdentity { get; }
        internal Func<object, bool> ViewAvailable { get; }
        internal Func<object, IList?> StructureListMembers { get; }
        internal Func<object, IList?> UpgradeListMembers { get; }

        internal static bool TryCreate(
            Func<string, Type?> resolve,
            out BindingSet? bindings,
            out string reason) =>
            NativeBindingSet.TryBind("Auto Buy owning-view", ContractIds, resolve, null, set =>
            {
                var id = set.Type("resource-bar.identity.type-capture", "IdScriptableObject");
                var abstractList = set.Type("auto-buy.owning-view.abstract-list-type-capture", "AbstractListVariable");
                var view = set.Type("spell-workbench.view-type-action", "ViewSO");
                var structure = set.Type("structure-lifecycle.structure.type-action", "StructureSO");
                var upgrade = set.Type("auto-buy.owning-view.upgrade-type-capture", "UpgradeSO");
                var structureCategory = set.Type("structure-type-so.upgradeable-base", "StructureTypeSO");
                var structureList = set.Type(
                    "auto-buy.owning-view.structure-list-type-capture", "StructureListVariable");
                var upgradeList = set.Type("upgrade-screen.list-type", "UpgradeListVariable");
                var listCollection = ListOf(abstractList);
                var structureListBase = ListBase(structureList, abstractList);
                var upgradeListBase = ListBase(upgradeList, abstractList);

                return new BindingSet(
                    id,
                    view,
                    structure,
                    upgrade,
                    structureCategory,
                    structureList,
                    upgradeList,
                    set.StaticField<Func<IList?>>("view.all", view, "All", ListOf(view)),
                    set.StaticField<Func<IList?>>("structure.all", structure, "All", ListOf(structure)),
                    set.StaticField<Func<IList?>>("upgrade.all", upgrade, "All", ListOf(upgrade)),
                    set.Field<Func<object, IList?>>(
                        "auto-buy.owning-view.relevant-lists-capture", view, "relevantLists", listCollection),
                    set.Field<Func<object, IList?>>(
                        "auto-buy.owning-view.available-lists-capture", view, "availableLists", listCollection),
                    set.Field<Func<object, object?>>(
                        "structure.structure-type", structure, "structureType", structureCategory),
                    set.Field<Func<object, IList?>>(
                        "auto-buy.owning-view.structure-category-members-capture",
                        structureCategory,
                        "structures",
                        ListOf(structure)),
                    SharedIdentity(set, view, id),
                    SharedIdentity(set, structure, id),
                    SharedIdentity(set, upgrade, id),
                    SharedIdentity(set, structureCategory, id),
                    SharedIdentity(set, structureList, id),
                    set.Call<Func<object, bool>>(
                        "auto-buy.owning-view.view-availability-action", view, "IsAvailable", typeof(bool)),
                    set.Field<Func<object, IList?>>(
                        "auto-buy.owning-view.list-membership-capture", structureListBase, "value", ListOf(structure)),
                    set.Field<Func<object, IList?>>(
                        "auto-buy.owning-view.list-membership-capture", upgradeListBase, "value", ListOf(upgrade)));
            }, out bindings, out reason);

        private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);

        private static Type ListBase(Type list, Type abstractList)
        {
            for (var current = list; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition().BaseType == abstractList)
                    return current;
            }
            throw new NativeBindingException(
                "auto-buy.owning-view.list-membership-capture: " + list.Name +
                " does not inherit a generic " + abstractList.Name + ".");
        }

        private static Func<object, Guid> SharedIdentity(NativeBindingSet set, Type owner, Type identity)
        {
            var uuid = set.StableUuid("id-scriptable-object.get-guid", owner);
            var declared = owner.GetMethod(
                "GetGuid", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            return declared?.DeclaringType == identity
                ? uuid
                : throw new NativeBindingException(
                    "id-scriptable-object.get-guid: " + owner.Name + ".GetGuid() is not inherited from " +
                    identity.Name + ".");
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
        frame.UpgradeListMemberships.Reset();
        if (_resolver is null && !TryBind())
            return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            var sampled = _resolver!.ReadAll(
                frame.CollectedAtEpoch,
                frame.PurchaseViewRelations,
                frame.PurchaseViewRoutes,
                frame.UpgradeListMemberships,
                out var unresolved,
                out var skipped,
                out var membershipFailure);
            var notes = unresolved == 0 && skipped == 0
                ? string.Empty
                : unresolved + " candidate owning-view relation(s) were retained as named fail-closed facts; " +
                  skipped + " candidate(s) lacked a publishable exact identity";
            if (membershipFailure.Length > 0)
            {
                notes = notes.Length == 0
                    ? "authored upgrade list membership was withheld: " + membershipFailure
                    : notes + "; authored upgrade list membership was withheld: " + membershipFailure;
            }
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                skipped,
                notes);
        }
        // The buffers are reset because a half-appended pass is not a route table. The published
        // snapshot is deliberately left alone: ReadAll assembles its own and assigns it only on the
        // way out, so a throw never touched it, and dropping it here would turn one transient native
        // exception into a whole lifecycle of refused purchases. Nothing stale can slip through —
        // TryGetCaptured admits only under the epoch that stamped it — and the failed report keeps
        // the collector's structural gate open, so the next pass reads again.
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            frame.PurchaseViewRelations.Reset();
            frame.PurchaseViewRoutes.Reset();
            frame.UpgradeListMemberships.Reset();
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
