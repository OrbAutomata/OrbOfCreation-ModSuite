using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>Where a published bulletin came from. Provenance is required evidence, not decoration.</summary>
internal enum SuiteStrategyProvenance
{
    /// <summary>
    /// The permissive bulletin every service starts from. It constrains nothing, so a suite whose
    /// strategist never runs, is disabled, or faults behaves exactly as it did before strategy
    /// existed.
    /// </summary>
    Neutral = 0,

    /// <summary>Derived from an active milestone in the curated table.</summary>
    Milestone = 1,
}

/// <summary>
/// The one immutable strategy bulletin every service consumes, published latest-wins exactly like
/// <see cref="SuiteRuntimeConfiguration"/>: a cycle pins the current bulletin when it opens and keeps
/// it through evaluation and the whole action batch, and a newer publication is picked up by the next
/// cycle without disturbing work in flight.
/// </summary>
/// <remarks>
/// <para>
/// Public with internal members, the same bargain <see cref="SuiteRuntimeConfiguration"/> makes: the
/// runtime's public worker contract names this type, so it cannot be internal, and nothing outside
/// the suite has any business reading a stance off it.
/// </para>
/// <para>
/// The bulletin says <em>what the suite wants</em>, never what any service should do about it. The
/// strategist does not decide which upgrade Auto Buy purchases or what Agrimancy plants; it says
/// which resources are being saved and which are wanted, and each domain service maps that onto its
/// own legal actions. That boundary is what keeps the strategist out of every domain's catalogs.
/// </para>
/// <para>
/// Replacement semantics are total: publishing a bulletin replaces the previous one entirely, so
/// there are no per-constraint expiry timers to reconcile and no way for two constraints to
/// disagree. A stance that should stop applying is dropped by the next publication.
/// </para>
/// <para>
/// Every stance may only tighten what configuration already permits. Consumers apply user policy
/// first and the bulletin second, so a wrong or stale bulletin can cost throughput but can never
/// spend more aggressively than the operator configured.
/// </para>
/// </remarks>
public sealed record SuiteStrategy
{
    internal SuiteStrategyProvenance Provenance { get; init; } = SuiteStrategyProvenance.Neutral;

    /// <summary>The milestone this bulletin serves, or <see cref="Guid.Empty"/> when none is active.</summary>
    internal Guid ActiveMilestoneId { get; init; }

    /// <summary>
    /// Per-resource stances, sorted by <see cref="SuiteResourceStance.ResourceId"/> so consumers
    /// can binary-search rather than scan. Resources absent from the table are
    /// <see cref="SuiteResourceStanceKind.Free"/>: the table lists exceptions, not every
    /// resource, so an ordinary bulletin stays small.
    /// </summary>
    internal PublicationTable<SuiteResourceStance> Resources { get; init; } =
        PublicationTable<SuiteResourceStance>.Empty;

    /// <summary>
    /// Resolves the stance for one resource. Absent means <see cref="SuiteResourceStanceKind.Free"/>,
    /// which is what makes an empty bulletin exactly equivalent to no bulletin.
    /// </summary>
    internal SuiteResourceStance StanceFor(Guid resourceId)
    {
        var rows = Resources.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = rows[middle].ResourceId.CompareTo(resourceId);
            if (comparison == 0) return rows[middle];
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        return SuiteResourceStance.Free(resourceId);
    }
}
