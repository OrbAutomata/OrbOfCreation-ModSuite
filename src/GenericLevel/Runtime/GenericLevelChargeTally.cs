using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// What a multi-level press asked for, gathered one rung at a time as it is bought.
/// </summary>
/// <remarks>
/// The game prices every rung on its own, so the price standing before the press answers for the
/// whole ask only when the ask is one level. Each rung's price is already read here to decide
/// whether it is payable, so recording it costs one dictionary write and nothing native; the
/// alternative is a response that calls a press free because the ladder's first rung was.
/// </remarks>
internal struct GenericLevelChargeTally
{
    private Dictionary<Guid, BigDouble>? _amounts;
    private List<Guid>? _order;

    internal int Levels { get; private set; }

    internal void Take(GenericLevelNativeBindings native, object cost)
    {
        Levels++;
        var rows = native.CostEntries(cost);
        if (rows is null) return;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.CostResource(row);
            if (resource is null) continue;
            var id = native.ResourceGuid(resource);
            _amounts ??= new Dictionary<Guid, BigDouble>();
            _order ??= new List<Guid>();
            if (_amounts.TryGetValue(id, out var running))
            {
                _amounts[id] = running + native.CostValue(row);
                continue;
            }
            _amounts[id] = native.CostValue(row);
            _order.Add(id);
        }
    }

    internal GenericLevelCharge[]? Rows()
    {
        if (_order is null || _amounts is null || _order.Count == 0) return null;
        var rows = new GenericLevelCharge[_order.Count];
        for (var index = 0; index < _order.Count; index++)
            rows[index] = new GenericLevelCharge(_order[index], _amounts[_order[index]]);
        return rows;
    }
}
