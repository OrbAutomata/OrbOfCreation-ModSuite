using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.World;

/// <summary>Captures authored spell mastery costs and the one global per-level modifier program.</summary>
internal sealed class WorldMasteryCostReader : IWorldCategoryReader
{
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _spellType;
    private readonly FieldInfo? _globalsInstance;
    private readonly Func<object, object?>? _baseCost;
    private readonly Func<object, IList?>? _costs;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, BigDouble>? _amount;
    private readonly Func<object, object?>? _standard;
    private readonly Func<object, Guid>? _standardId;
    private readonly Func<object, object?>? _standardValue;
    private readonly NativeModifierProgramReader? _programReader;
    private readonly string _unavailable;

    internal WorldMasteryCostReader(Func<string, Type?> resolveType)
    {
        _spellType = resolveType("SpellRecipeSO");
        var globalsType = resolveType("GlobalValues");
        var variableType = resolveType("ModifierListVariable");
        var listType = resolveType("ValueModifierList");
        var recordType = resolveType("ValueModifierRecord");

        _baseCost = NativeAccessorBinder.Reference(_spellType, "baseLevelingCost");
        var costType = _spellType?.GetField(
            "baseLevelingCost",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;
        _costs = NativeAccessorBinder.CollectionField(costType, "costs");
        var entryType = NativeAccessorBinder.CollectionElementType(costType, "costs");
        _resourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _amount = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");

        _globalsInstance = globalsType?.GetField("instance", Static);
        _standard = NativeAccessorBinder.Reference(globalsType, "spellLevelingStandard");
        _standardId = NativeAccessorBinder.Call<Guid>(variableType, "GetGuid");
        _standardValue = NativeAccessorBinder.Reference(variableType, "value");
        _programReader = new NativeModifierProgramReader(recordType, listType);

        _unavailable = IsFullyBound()
            ? string.Empty
            : "spell mastery costs did not expose their authored tuples and global modifier program";
    }

    public string Category => "spell level costs";
    public bool IsAvailable => _spellType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.MasteryCosts.Reset();
        frame.MasteryCostStandardId = Guid.Empty;
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var globals = _globalsInstance!.GetValue(null);
        var standard = globals is null ? null : _standard!(globals);
        var list = standard is null ? null : _standardValue!(standard);
        if (standard is null || list is null)
            return WorldCategoryReport.Missing(Category, "spellLevelingStandard was unreadable");

        frame.MasteryCostStandardId = _standardId!(standard);
        if (frame.MasteryCostStandardId == Guid.Empty)
            return WorldCategoryReport.Missing(Category, "spellLevelingStandard carried no identity");
        _programReader!.CaptureList(
            frame.MasteryCostStandardId,
            WorldModifierProgramRole.SpellLevelingStandard,
            list,
            frame.ModifierPrograms,
            frame.ModifierProgramEntries);

        var spells = NativeAccessorBinder.StaticList(_spellType, "All");
        if (spells is null)
            return WorldCategoryReport.Missing(Category, "the SpellRecipeSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < spells.Count; index++)
        {
            var spell = spells[index];
            if (spell is null) { skipped++; continue; }
            try
            {
                if (index >= frame.SpellRecipes.Count)
                {
                    skipped++;
                    if (firstFailure.Length == 0)
                        firstFailure = "spell registry identity snapshot was incomplete";
                    continue;
                }
                var id = frame.SpellRecipes[index].EntityId;
                var costs = _costs!(_baseCost!(spell)!);
                if (id == Guid.Empty) { skipped++; continue; }
                var rows = 0;
                for (var position = 0; costs is not null && position < costs.Count; position++)
                {
                    var cost = costs[position];
                    if (cost is null) continue;
                    frame.MasteryCosts.Append(new RawMasteryCost(
                        id, position, _resourceId!(cost), _amount!(cost)));
                    rows++;
                }
                if (rows == 0)
                {
                    skipped++;
                    if (firstFailure.Length == 0)
                        firstFailure = $"{EntityIdentityFormatter.Format(id)} has no authored level cost";
                    continue;
                }
                sampled++;
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = $"reading a spell level cost threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private bool IsFullyBound() =>
        _spellType is not null && _globalsInstance is not null &&
        _baseCost is not null && _costs is not null && _resourceId is not null && _amount is not null &&
        _standard is not null && _standardId is not null && _standardValue is not null &&
        _programReader?.IsAvailable == true;
}
