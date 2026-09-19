using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One eligible target in the native order shown by the current target request.</summary>
internal readonly struct WorldTargetingCandidate
{
    internal WorldTargetingCandidate(int position, Guid structureId)
    {
        Position = position;
        StructureId = structureId;
    }

    internal int Position { get; }
    internal Guid StructureId { get; }
}

/// <summary>The game's one current targeting request and every immediate next decision.</summary>
internal readonly struct WorldTargetingRequest
{
    internal WorldTargetingRequest(
        string ownerName,
        string ownerNativeType,
        string selectionNativeType,
        bool cancelAvailable,
        PublicationTable<WorldTargetingCandidate> candidates)
    {
        OwnerName = ownerName ?? string.Empty;
        OwnerNativeType = ownerNativeType ?? string.Empty;
        SelectionNativeType = selectionNativeType ?? string.Empty;
        CancelAvailable = cancelAvailable;
        Candidates = candidates;
    }

    internal string OwnerName { get; }
    internal string OwnerNativeType { get; }
    internal string SelectionNativeType { get; }
    internal bool CancelAvailable { get; }
    internal PublicationTable<WorldTargetingCandidate> Candidates { get; }
}

internal sealed class WorldTargetingBuffer
{
    private WorldTargetingCandidate[] _candidates = new WorldTargetingCandidate[8];
    private int _candidateCount;
    private bool _hasRequest;
    private string _ownerName = string.Empty;
    private string _ownerNativeType = string.Empty;
    private string _selectionNativeType = string.Empty;
    private bool _cancelAvailable;

    internal void Reset()
    {
        _candidateCount = 0;
        _hasRequest = false;
        _ownerName = string.Empty;
        _ownerNativeType = string.Empty;
        _selectionNativeType = string.Empty;
        _cancelAvailable = false;
    }

    internal void Begin(
        string ownerName,
        string ownerNativeType,
        string selectionNativeType,
        bool cancelAvailable)
    {
        _hasRequest = true;
        _ownerName = ownerName;
        _ownerNativeType = ownerNativeType;
        _selectionNativeType = selectionNativeType;
        _cancelAvailable = cancelAvailable;
    }

    internal void Append(Guid structureId)
    {
        if (_candidateCount >= _candidates.Length)
            Array.Resize(ref _candidates, _candidates.Length * 2);
        _candidates[_candidateCount] = new WorldTargetingCandidate(_candidateCount, structureId);
        _candidateCount++;
    }

    internal PublicationTable<WorldTargetingRequest> Build()
    {
        if (!_hasRequest) return PublicationTable<WorldTargetingRequest>.Empty;
        return PublicationTable<WorldTargetingRequest>.Create(new[]
        {
            new WorldTargetingRequest(
                _ownerName,
                _ownerNativeType,
                _selectionNativeType,
                _cancelAvailable,
                PublicationTable<WorldTargetingCandidate>.Create(_candidates, _candidateCount)),
        });
    }
}

/// <summary>
/// Captures targeting only while a native request is open. Idle frames do one static read and no
/// candidate traversal; an active request copies its bounded structure list into the shared world.
/// </summary>
internal sealed class WorldTargetingReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _linkType;
    private readonly Type? _structureType;
    private readonly Func<bool>? _isTargeting;
    private readonly Func<object?>? _getLink;
    private readonly Func<object, IList?>? _getAllTargets;
    private readonly Func<object, object?>? _getOwner;
    private readonly Func<object, object?>? _getSelection;
    private readonly Func<object, object?>? _resultInfo;
    private readonly Func<object, string>? _getName;
    private readonly Func<object, Guid>? _getGuid;
    private readonly string _unavailable;

    internal WorldTargetingReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var managerType = resolveType("TargetingManager");
        _linkType = resolveType("TargetingManager+TargetLink");
        var tooltipableType = resolveType("ITooltipable");
        var selectionType = resolveType("Targeting.BaseTargetSelection");
        var resultInfoType = resolveType("EffectResultInfo");
        _structureType = resolveType("StructureSO");
        _isTargeting = StaticCall<bool>(managerType, "IsTargeting");
        _getLink = StaticObjectCall(managerType, "GetTargetingLink", _linkType);
        _getAllTargets = NativeAccessorBinder.CallList(
            _linkType, "GetAllTargets", tooltipableType);
        _getOwner = NativeAccessorBinder.CallObject(_linkType, "GetOwner", tooltipableType);
        _getSelection = NativeAccessorBinder.CallObject(
            _linkType, "GetTargetSelection", selectionType);
        _resultInfo = NativeAccessorBinder.Reference(_linkType, "resultInfo", resultInfoType);
        _getName = NativeAccessorBinder.Call<string>(tooltipableType, "GetName");
        _getGuid = NativeAccessorBinder.Call<Guid>(_structureType, "GetGuid");
        _unavailable = managerType is null || _linkType is null || tooltipableType is null ||
            selectionType is null || resultInfoType is null || _structureType is null ||
            _isTargeting is null || _getLink is null || _getAllTargets is null ||
            _getOwner is null || _getSelection is null || _resultInfo is null ||
            _getName is null || _getGuid is null
                ? "the complete read-only targeting binding set was unavailable"
                : string.Empty;
    }

    public string Category => "targeting";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.Targeting;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            if (!_isTargeting!())
                return new WorldCategoryReport(
                    Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
            var link = _getLink!();
            if (link is null || link.GetType() != _linkType)
            {
                buffer.Reset();
                return WorldCategoryReport.Missing(
                    Category,
                    "TargetingManager reported an active request without one exact current TargetLink");
            }
            var owner = _getOwner!(link);
            var selection = _getSelection!(link);
            if (selection is null)
            {
                buffer.Reset();
                return WorldCategoryReport.Missing(
                    Category,
                    "the current TargetLink had no exact target selection");
            }
            buffer.Begin(
                owner is null ? string.Empty : (_getName!(owner)?.Trim() ?? string.Empty),
                owner?.GetType().Name ?? string.Empty,
                selection.GetType().Name,
                _resultInfo!(link) is not null);
            var candidates = _getAllTargets!(link);
            for (var index = 0; index < (candidates?.Count ?? 0); index++)
            {
                var candidate = candidates![index];
                if (candidate is null || candidate.GetType() != _structureType)
                {
                    buffer.Reset();
                    return WorldCategoryReport.Missing(
                        Category,
                        "target candidate " + index + " was not one exact StructureSO");
                }
                var id = _getGuid!(candidate);
                if (id == Guid.Empty)
                {
                    buffer.Reset();
                    return WorldCategoryReport.Missing(
                        Category,
                        "target candidate " + index + " had an empty UUID");
                }
                buffer.Append(id);
            }
            return new WorldCategoryReport(
                Category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);
        }
        catch (Exception ex)
        {
            buffer.Reset();
            return WorldCategoryReport.Missing(Category, ex.GetBaseException().Message);
        }
    }

    private static Func<T>? StaticCall<T>(Type? type, string name)
    {
        var method = type?.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != typeof(T)) return null;
        try { return Expression.Lambda<Func<T>>(Expression.Call(method)).Compile(); }
        catch (Exception) { return null; }
    }

    private static Func<object?>? StaticObjectCall(
        Type? type,
        string name,
        Type? returnType)
    {
        if (type is null || returnType is null) return null;
        var method = type.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || method.ReturnType != returnType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Call(method), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }
}
