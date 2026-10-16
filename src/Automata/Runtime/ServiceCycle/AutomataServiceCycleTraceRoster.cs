using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

/// <summary>
/// What the suite calls the services it registers, for the benefit of anyone reading a capture.
/// </summary>
/// <remarks>
/// <para>
/// The runtime identifies a service by the string it registered and the trace identifies it by a
/// number; neither is a name a reader recognises. This is the one place the suite says that
/// <c>orbautomata.auto-harvest</c>, trace identity 2, is the thing the rest of the UI calls Auto
/// Harvest, and it is read once per recording rather than per event.
/// </para>
/// <para>
/// A service with no entry here is recorded under its registered identity rather than dropped. That is
/// deliberate: a capture that says <c>orbautomata.auto-agromancy</c> is telling the truth and is
/// obviously missing a display name, where one that says "Service 4" is telling a reader nothing and
/// looks finished.
/// </para>
/// </remarks>
internal static class AutomataServiceCycleTraceRoster
{
    private const string WorldCollectionId = "orbautomata.world-collection";
    private const string AutoHarvestId = "orbautomata.auto-harvest";
    private const string AutoBuyId = "orbautomata.auto-buy";
    private const string SpellLevelId = "orbautomata.spell-level";
    private const string AutoCastId = "orbautomata.auto-cast";
    private const string AutoConceptId = "orbautomata.auto-concept";
    private const string AutoItemsId = "orbautomata.auto-items";
    private const string AutoScribeId = "orbautomata.auto-scribe";
    private const string MentorId = "orbmentor.mastery-sharing";

    /// <summary>
    /// The roster for a registry whose registrations are complete. Trace identity is the registration
    /// ordinal plus one, the same derivation the semantic emitters use, because the roster has to name
    /// the number a reader will actually see in the stream.
    /// </summary>
    internal static ServiceCycleTraceRoster Build(ServiceCycleRegistry registry) =>
        Build(registry, ReadOnlySpan<string>.Empty);

    /// <inheritdoc cref="Build(ServiceCycleRegistry)"/>
    /// <param name="worldCategories">
    /// What the collector calls its categories, in traversal order, so a collection span reads as a
    /// category rather than as a number. Empty when collection is not registered on this build, which
    /// is a roster with no category rows rather than a roster that is wrong.
    /// </param>
    internal static ServiceCycleTraceRoster Build(
        ServiceCycleRegistry registry,
        ReadOnlySpan<string> worldCategories)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var count = registry.OrdinalCount;
        if (count <= 0) return ServiceCycleTraceRoster.Empty;
        var entries = new ServiceCycleTraceRosterEntry[count + worldCategories.Length];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var service = registry.GetServiceId(ordinal);
            entries[ordinal] = new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.ServiceKind,
                checked((ulong)ordinal + 1),
                service.Value,
                DisplayName(service));
        }
        for (var index = 0; index < worldCategories.Length; index++)
        {
            // No display name: a category's registered name is already the name a reader wants, and
            // restating it in both fields would invite the two to disagree.
            entries[count + index] = new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.WorldCategoryKind,
                checked((ulong)index + 1),
                worldCategories[index],
                string.Empty);
        }
        return new ServiceCycleTraceRoster(entries);
    }

    /// <summary>The suite's own name for a registered service, or empty when it has none.</summary>
    internal static string DisplayName(ServiceId service) => service.Value switch
    {
        WorldCollectionId => "World collection",
        AutoHarvestId => "Auto Harvest",
        AutoBuyId => "Auto Buy",
        SpellLevelId => "Spell Leveling",
        AutoCastId => "Auto Cast",
        AutoConceptId => "Auto Concept",
        AutoItemsId => "Auto Items",
        AutoScribeId => "Auto Scribe",
        MentorId => "Mentor",
        _ => string.Empty,
    };
}
