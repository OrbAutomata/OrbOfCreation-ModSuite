using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>
/// Assembles a bulletin and enforces the invariants <see cref="SuiteStrategy.StanceFor"/> relies
/// on: rows sorted by resource UUID, and one stance per resource. The builder is ordinary mutable
/// scratch owned by whoever is composing a bulletin — it is never published, so it carries none of
/// the publication shape rules; <see cref="Build"/> copies into the immutable table.
/// </summary>
/// <remarks>
/// Two stances for one resource is a strategist bug, not a precedence question to resolve at read
/// time. Rejecting it here keeps the resolution rule "the table says exactly one thing or nothing",
/// which is what lets a consumer treat an absent row as unconstrained without ambiguity.
/// </remarks>
internal sealed class SuiteStrategyBuilder
{
    private readonly List<SuiteResourceStance> _stances = new();

    internal int Count => _stances.Count;

    internal SuiteStrategyBuilder With(in SuiteResourceStance stance)
    {
        if (stance.ResourceId == Guid.Empty)
            throw new ArgumentException("A stance requires a stable resource identity.", nameof(stance));
        for (var index = 0; index < _stances.Count; index++)
        {
            if (_stances[index].ResourceId == stance.ResourceId)
            {
                throw new ArgumentException(
                    $"Resource '{stance.ResourceId}' already has a stance in this bulletin.", nameof(stance));
            }
        }

        _stances.Add(stance);
        return this;
    }

    internal SuiteStrategy Build(SuiteStrategyProvenance provenance, Guid activeGoalId)
    {
        if (_stances.Count == 0)
        {
            return new SuiteStrategy
            {
                Provenance = provenance,
                ActiveGoalId = activeGoalId,
                Resources = PublicationTable<SuiteResourceStance>.Empty,
            };
        }

        var rows = _stances.ToArray();
        Array.Sort(rows, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        return new SuiteStrategy
        {
            Provenance = provenance,
            ActiveGoalId = activeGoalId,
            Resources = PublicationTable<SuiteResourceStance>.Create(rows, rows.Length),
        };
    }
}
