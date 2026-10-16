using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which class wears a keyword.</summary>
/// <remarks>
/// Thirteen of these author their membership as <see cref="WorldEntityKeyword"/> rows.
/// <see cref="Research"/> and <see cref="Consumable"/> have none: their edges are published inside
/// their own categories — <c>ResearchSO.researchTypes</c> beside each type's investment levels, and
/// <c>ConsumableSO.consumableTypes</c> as the family relation the item verbs pick by — so this enum
/// reaches two classes the keyword table itself never emits. See
/// <see cref="WorldKeywordMembership"/>, which is what joins the three.
/// </remarks>
internal enum WorldKeywordOwnerKind
{
    Structure = 0,
    AlchemyRecipe = 1,
    Resource = 2,
    Equipment = 3,
    PassiveAbility = 4,
    TimeRune = 5,
    Glyph = 6,
    PlotNodeAction = 7,
    Ritual = 8,
    Character = 9,
    PlotNode = 10,
    HarvestElement = 11,
    HarvestAction = 12,
    Research = 13,
    Consumable = 14,
}

/// <summary>Which authored member a keyword row came off.</summary>
/// <remarks>
/// Two classes author their word line from two members rather than one, and the game composes them in
/// opposite orders: <c>StructureSO.GetAllTypes()</c> is <c>structureSubTypes.Prepend(structureType)</c>
/// while <c>EquipmentSO.GetAllEquipmentTypes()</c> is <c>subEquipmentTypes.Append(equipmentType)</c>.
/// Capture publishes each member under its own source with its own authored ordinal rather than
/// flattening the two into one sequence, so the composition order stays a derivation rather than an
/// assumption baked into a main-thread read. The table's own order is therefore not display order:
/// it sorts primary before list because that is the order this enum reads in, and equipment's word
/// line puts its primary type last.
/// </remarks>
internal enum WorldKeywordSource
{
    PrimaryType = 0,
    TypeList = 1,
}

/// <summary>
/// One authored keyword: an entity, and one type asset whose display name the game prints in that
/// entity's tooltip word line.
/// </summary>
/// <remarks>
/// The word line is <c>ITooltipable.GetDisplayType()</c>, which every concrete class must author, and
/// sixteen classes author it by joining the display names of a list of type assets. The type asset is
/// a modifier-bearing sibling entity rather than a tag, so the keyword and the bonus that rides on it
/// ("all Cantrips cast faster") are the same object; this table publishes the membership edge, and the
/// word itself is the target's display name in the lifecycle identity catalog.
/// </remarks>
internal readonly struct WorldEntityKeyword
{
    internal WorldEntityKeyword(
        Guid ownerId,
        WorldKeywordOwnerKind ownerKind,
        WorldKeywordSource source,
        int ordinal,
        Guid keywordId)
    {
        OwnerId = ownerId;
        OwnerKind = ownerKind;
        Source = source;
        Ordinal = ordinal;
        KeywordId = keywordId;
    }

    /// <summary>The entity whose tooltip shows the word.</summary>
    internal Guid OwnerId { get; }

    internal WorldKeywordOwnerKind OwnerKind { get; }

    /// <summary>Which authored member carried this reference, and its position within that member.</summary>
    internal WorldKeywordSource Source { get; }

    internal int Ordinal { get; }

    /// <summary>The type asset whose display name is the word.</summary>
    internal Guid KeywordId { get; }
}

/// <summary>
/// Reads the authored keyword membership of the thirteen classes whose type lists no other category
/// binds.
/// </summary>
/// <remarks>
/// <para>
/// The three classes left out are left out because they are already bound whole elsewhere and carry
/// more than the keyword: <c>ResearchSO.researchTypes</c> is published with each type's investment
/// levels, <c>ConsumableSO.consumableTypes</c> with each type's carry load, and
/// <c>SpellRecipeSO.spellTypes</c> as spell-graph relations. Re-reading them here would publish the
/// same native member twice under two owners.
/// </para>
/// <para>
/// Whole-or-withheld: one unbindable member withholds the entire category rather than publishing a
/// partial vocabulary, because a keyword table that is silently short some classes reads exactly like
/// a table whose entities genuinely have no keywords. The reason names every member that failed.
/// </para>
/// </remarks>
internal sealed class WorldEntityKeywordReader : IWorldCategoryReader
{
    private readonly OwnerBinding[] _owners;
    private readonly string _unavailable;

    internal WorldEntityKeywordReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _owners = new[]
        {
            Bind(resolveType, WorldKeywordOwnerKind.Structure, "StructureSO",
                "structureSubTypes", "structureType"),
            Bind(resolveType, WorldKeywordOwnerKind.AlchemyRecipe, "AlchemyRecipeSO",
                "alchemyTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.Resource, "ResourceSO",
                "resourceTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.Equipment, "EquipmentSO",
                "subEquipmentTypes", "equipmentType"),
            Bind(resolveType, WorldKeywordOwnerKind.PassiveAbility, "PassiveAbilitySO",
                "passiveTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.TimeRune, "TimeRuneSO",
                "timeRuneTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.Glyph, "GlyphSO",
                "glyphTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.PlotNodeAction, "PlotNodeActionSO",
                "actionTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.Ritual, "RitualSO",
                "ritualTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.Character, "CharacterSO",
                "characterTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.PlotNode, "PlotNodeSO",
                "nodeTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.HarvestElement, "HarvestElementSO",
                "harvestTypes", primaryField: null),
            Bind(resolveType, WorldKeywordOwnerKind.HarvestAction, "HarvestActionSO",
                "actionTypes", primaryField: null),
        };

        var failures = string.Empty;
        foreach (var owner in _owners)
        {
            if (owner.Failure.Length == 0) continue;
            failures = failures.Length == 0 ? owner.Failure : failures + "; " + owner.Failure;
        }

        _unavailable = failures;
    }

    public string Category => "entity keywords";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        frame.EntityKeywords.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        foreach (var owner in _owners)
        {
            var entities = owner.Registry!();
            if (entities is null)
            {
                Skip(ref skipped, ref firstFailure,
                    "the " + owner.TypeName + " registry was unreadable");
                continue;
            }

            for (var index = 0; index < entities.Count; index++)
            {
                var entity = entities[index];
                if (entity is null)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a " + owner.TypeName + " registry entry was null");
                    continue;
                }

                try
                {
                    var ownerId = owner.OwnerId!(entity);
                    if (ownerId == Guid.Empty)
                    {
                        Skip(ref skipped, ref firstFailure,
                            "a " + owner.TypeName + " entry had no stable identity");
                        continue;
                    }

                    if (owner.PrimaryTypeId is not null)
                    {
                        var primary = owner.PrimaryTypeId(entity);
                        if (primary != Guid.Empty)
                        {
                            frame.EntityKeywords.Append(new WorldEntityKeyword(
                                ownerId, owner.Kind, WorldKeywordSource.PrimaryType, 0, primary));
                        }
                    }

                    var types = owner.TypeList!(entity);
                    for (var ordinal = 0; ordinal < (types?.Count ?? 0); ordinal++)
                    {
                        var type = types![ordinal];
                        if (type is null) continue;
                        var keywordId = owner.KeywordId!(type);
                        if (keywordId == Guid.Empty) continue;
                        frame.EntityKeywords.Append(new WorldEntityKeyword(
                            ownerId, owner.Kind, WorldKeywordSource.TypeList, ordinal, keywordId));
                    }

                    sampled++;
                }
                catch (Exception exception)
                {
                    Skip(ref skipped, ref firstFailure,
                        "reading a " + owner.TypeName + " keyword row threw: " +
                        exception.GetBaseException().Message);
                }
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }

    private static OwnerBinding Bind(
        Func<string, Type?> resolveType,
        WorldKeywordOwnerKind kind,
        string typeName,
        string listField,
        string? primaryField)
    {
        var type = resolveType(typeName);
        if (type is null)
        {
            return new OwnerBinding(kind, typeName,
                "the " + typeName + " type was not found on this build");
        }

        var bind = new WorldMemberBinding(type, typeName);
        var ownerId = bind.Call<Guid>("GetGuid");
        var primaryTypeId = primaryField is null ? null : bind.ReferenceGuid(primaryField);
        var typeList = bind.CollectionField(listField);
        var keywordId = bind
            .Elements(bind.CollectionElementType(listField), typeName + "." + listField + "[]")
            .Call<Guid>("GetGuid");
        var registry = NativeAccessorBinder.StaticListAccessor(type, "All");

        var failure = bind.Failure;
        if (registry is null)
        {
            var missingRegistry = typeName + " did not expose All on this build";
            failure = failure.Length == 0 ? missingRegistry : failure + "; " + missingRegistry;
        }

        return failure.Length == 0
            ? new OwnerBinding(kind, typeName, registry!, ownerId!, primaryTypeId, typeList!, keywordId!)
            : new OwnerBinding(kind, typeName, failure);
    }

    private sealed class OwnerBinding
    {
        internal OwnerBinding(WorldKeywordOwnerKind kind, string typeName, string failure)
        {
            Kind = kind;
            TypeName = typeName;
            Failure = failure;
        }

        internal OwnerBinding(
            WorldKeywordOwnerKind kind,
            string typeName,
            Func<IList?> registry,
            Func<object, Guid> ownerId,
            Func<object, Guid>? primaryTypeId,
            Func<object, IList?> typeList,
            Func<object, Guid> keywordId)
        {
            Kind = kind;
            TypeName = typeName;
            Registry = registry;
            OwnerId = ownerId;
            PrimaryTypeId = primaryTypeId;
            TypeList = typeList;
            KeywordId = keywordId;
            Failure = string.Empty;
        }

        internal WorldKeywordOwnerKind Kind { get; }
        internal string TypeName { get; }
        internal Func<IList?>? Registry { get; }
        internal Func<object, Guid>? OwnerId { get; }
        internal Func<object, Guid>? PrimaryTypeId { get; }
        internal Func<object, IList?>? TypeList { get; }
        internal Func<object, Guid>? KeywordId { get; }
        internal string Failure { get; }
    }
}

/// <summary>
/// Every member one keyword reaches, closed over the subtype chain: the far side of the edge a
/// type's page names.
/// </summary>
/// <remarks>
/// <para>
/// The membership edge is the union of three published tables rather than one.
/// <see cref="WorldEntityKeywordReader"/> publishes thirteen classes; research and consumables
/// publish their own inside their categories, because <c>ResearchSO.researchTypes</c> carries each
/// type's investment levels there and <c>ConsumableSO.consumableTypes</c> is the relation the item
/// verbs already pick a family by — reading either twice would file one native member under two
/// owners. It is the same edge whichever table carries it:
/// <c>ResearchSO.GetBaseDisplayType()</c> and <c>ConsumableSO.GetDisplayType()</c> join those
/// types into the word line the game prints, exactly as <c>StructureSO.GetDisplayType()</c> joins
/// a structure's.
/// </para>
/// <para>
/// One index answers both the count a page prints and the rows a filter returns, so the two cannot
/// disagree — a page saying eleven things wear this word beside a filtered list handing back ten
/// would be two answers to one question.
/// </para>
/// </remarks>
internal sealed class WorldKeywordMembership
{
    private readonly Dictionary<Guid, List<Member>> _membership;
    private readonly Dictionary<Guid, List<Guid>> _children;

    private WorldKeywordMembership(
        Dictionary<Guid, List<Member>> membership,
        Dictionary<Guid, List<Guid>> children)
    {
        _membership = membership;
        _children = children;
    }

    /// <summary>One thing that wears a keyword, and the class it belongs to.</summary>
    internal readonly struct Member
    {
        internal Member(Guid ownerId, WorldKeywordOwnerKind ownerKind)
        {
            OwnerId = ownerId;
            OwnerKind = ownerKind;
        }

        internal Guid OwnerId { get; }

        internal WorldKeywordOwnerKind OwnerKind { get; }
    }

    internal static WorldKeywordMembership Build(
        PublicationTable<WorldEntityKeyword> keywords,
        PublicationTable<WorldResearch> research,
        PublicationTable<WorldConsumableType> consumableTypes,
        PublicationTable<WorldTypeSubtype> subtypes)
    {
        var membership = new Dictionary<Guid, List<Member>>();
        for (var index = 0; index < keywords.Count; index++)
        {
            var keyword = keywords[index];
            Add(membership, keyword.KeywordId, keyword.OwnerId, keyword.OwnerKind);
        }

        for (var index = 0; index < research.Count; index++)
        {
            var entry = research[index];
            var types = entry.Decision.ResearchTypes;
            for (var type = 0; type < types.Count; type++)
            {
                Add(
                    membership,
                    types[type].ResearchTypeId,
                    entry.EntityId,
                    WorldKeywordOwnerKind.Research);
            }
        }

        for (var index = 0; index < consumableTypes.Count; index++)
        {
            var edge = consumableTypes[index];
            Add(membership, edge.TypeId, edge.ConsumableId, WorldKeywordOwnerKind.Consumable);
        }

        var children = new Dictionary<Guid, List<Guid>>();
        for (var index = 0; index < subtypes.Count; index++)
        {
            var edge = subtypes[index];
            if (!children.TryGetValue(edge.TypeId, out var descendants))
            {
                descendants = new List<Guid>();
                children.Add(edge.TypeId, descendants);
            }

            descendants.Add(edge.SubTypeId);
        }

        return new WorldKeywordMembership(membership, children);
    }

    /// <summary>Whether any published table names a member at all.</summary>
    internal bool IsEmpty => _membership.Count == 0;

    /// <summary>
    /// Every member the keyword reaches, by kind: its own, plus every descendant's, counted once.
    /// </summary>
    /// <remarks>
    /// The walk carries a visited set rather than trusting the edge to be acyclic. The audited build
    /// authors one chain of depth one, but a cycle would be a hang rather than a wrong number, and
    /// this runs on a worker thread the cycle would take with it. The scratch collections are the
    /// caller's because the deriver asks this once per published record.
    /// </remarks>
    internal void Reach(
        Guid keywordId,
        Dictionary<WorldKeywordOwnerKind, HashSet<Guid>> reach,
        List<WorldKeywordOwnerKind> kinds,
        HashSet<Guid> walked,
        Stack<Guid> pending)
    {
        if (reach is null) throw new ArgumentNullException(nameof(reach));
        if (kinds is null) throw new ArgumentNullException(nameof(kinds));
        if (walked is null) throw new ArgumentNullException(nameof(walked));
        if (pending is null) throw new ArgumentNullException(nameof(pending));

        reach.Clear();
        kinds.Clear();
        walked.Clear();
        pending.Clear();

        pending.Push(keywordId);
        walked.Add(keywordId);

        while (pending.Count > 0)
        {
            var type = pending.Pop();

            if (_membership.TryGetValue(type, out var members))
            {
                foreach (var member in members)
                {
                    if (!reach.TryGetValue(member.OwnerKind, out var owners))
                    {
                        owners = new HashSet<Guid>();
                        reach.Add(member.OwnerKind, owners);
                        kinds.Add(member.OwnerKind);
                    }

                    owners.Add(member.OwnerId);
                }
            }

            if (!_children.TryGetValue(type, out var descendants)) continue;
            foreach (var child in descendants)
            {
                if (walked.Add(child)) pending.Push(child);
            }
        }

        kinds.Sort(static (left, right) => ((int)left).CompareTo((int)right));
    }

    /// <summary>
    /// Every member the keyword reaches, of every kind, in one set — the same reach the counts are
    /// taken over, so a caller walking the edge lands on exactly what the page counted.
    /// </summary>
    internal HashSet<Guid> Members(Guid keywordId)
    {
        var reach = new Dictionary<WorldKeywordOwnerKind, HashSet<Guid>>();
        Reach(keywordId, reach, new List<WorldKeywordOwnerKind>(), new HashSet<Guid>(), new Stack<Guid>());
        var members = new HashSet<Guid>();
        foreach (var owners in reach.Values)
        {
            foreach (var owner in owners) members.Add(owner);
        }

        return members;
    }

    private static void Add(
        Dictionary<Guid, List<Member>> membership,
        Guid keywordId,
        Guid ownerId,
        WorldKeywordOwnerKind ownerKind)
    {
        if (keywordId == Guid.Empty || ownerId == Guid.Empty) return;
        if (!membership.TryGetValue(keywordId, out var members))
        {
            members = new List<Member>();
            membership.Add(keywordId, members);
        }

        members.Add(new Member(ownerId, ownerKind));
    }
}

/// <summary>Range lookups over the keyword table, which is sorted by owner first.</summary>
internal static class WorldEntityKeywordLookup
{
    internal static bool TryFind(
        PublicationTable<WorldEntityKeyword> table,
        Guid ownerId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        start = LowerBound(rows, ownerId);
        count = 0;
        while (start + count < rows.Length && rows[start + count].OwnerId == ownerId) count++;
        return count > 0;
    }

    private static int LowerBound(ReadOnlySpan<WorldEntityKeyword> rows, Guid ownerId)
    {
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].OwnerId.CompareTo(ownerId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        return low;
    }
}
