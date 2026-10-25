using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored membership edge: this upgrade is on this <c>UpgradeListVariable</c>.
/// </summary>
/// <remarks>
/// <para>
/// The game groups upgrades by screen, and it does it with hand-authored membership lists swapped
/// into one panel by <c>ListViewSwapper</c> on the active <c>ViewSO</c>. Membership is therefore the
/// only fact that says which screen shows a row, and it is authored — immutable for the whole
/// lifecycle, like the route table beside it.
/// </para>
/// <para>
/// This is deliberately not a <see cref="WorldPurchaseViewRoute"/>. A route names a view, and its
/// consumer asks that view whether it is available before admitting a purchase. Three of the nine
/// authored upgrade lists are named by no view at all, so they can never carry a route, and adding
/// one for them would hand Auto Buy an admission path the game never published. Membership answers a
/// different question — where the player finds this row — and answers it for all nine lists.
/// </para>
/// </remarks>
internal readonly struct WorldUpgradeListMembership
{
    internal WorldUpgradeListMembership(Guid upgradeId, Guid listId)
    {
        UpgradeId = upgradeId;
        ListId = listId;
    }

    internal Guid UpgradeId { get; }
    internal Guid ListId { get; }
}

/// <summary>
/// Every authored <c>UpgradeListVariable</c> of the pinned build, by stable uuid.
/// </summary>
/// <remarks>
/// Six of the nine are reachable by walking <c>ViewSO.relevantLists</c>, and the walk reads their
/// membership already. The other three — the Scholar screen's 27 upgrades, the three world aspects,
/// and the empty Time screen list — are named only by prefab <c>ListViewSwapper</c> data, which is
/// outside both the assembly and the serialized object graph. Nothing in the game points at them, so
/// the only way to reach them is by the identity they carry, which is what this pins. The build is
/// pinned too, so an authored list that stops answering to its uuid is a defect the read reports
/// rather than a case it works around.
/// </remarks>
internal static class WorldUpgradeScreenLists
{
    internal static readonly Guid[] All =
    {
        KnownEntities.UpgradesAll.Uuid,
        KnownEntities.UpgradesAlchemyScreen.Uuid,
        KnownEntities.UpgradesAspectsScreen.Uuid,
        KnownEntities.UpgradesMagicScreen.Uuid,
        KnownEntities.UpgradesRitualScreen.Uuid,
        KnownEntities.UpgradesScholarScreen.Uuid,
        KnownEntities.UpgradesTimeScreen.Uuid,
        KnownEntities.UpgradesWorkshopScreen.Uuid,
        KnownEntities.UpgradesWorldScreen.Uuid,
    };

    /// <summary>Each screen list and the view whose availability is that screen's unlock gate.</summary>
    private static readonly (Guid ListId, Guid ViewId)[] ScreenGates =
    {
        (KnownEntities.UpgradesMagicScreen.Uuid, KnownEntities.ScreenMagic.Uuid),
        (KnownEntities.UpgradesWorkshopScreen.Uuid, KnownEntities.ScreenWorkshop.Uuid),
        (KnownEntities.UpgradesWorldScreen.Uuid, KnownEntities.ScreenWorld.Uuid),
        (KnownEntities.UpgradesAlchemyScreen.Uuid, KnownEntities.AlchemyScreen.Uuid),
        (KnownEntities.UpgradesRitualScreen.Uuid, KnownEntities.ScreenRitual.Uuid),
        (KnownEntities.UpgradesScholarScreen.Uuid, KnownEntities.ScreenScholar.Uuid),
        (KnownEntities.UpgradesAspectsScreen.Uuid, KnownEntities.WorldAspects.Uuid),
        (KnownEntities.UpgradesTimeScreen.Uuid, KnownEntities.ScreenTime.Uuid),
    };

    /// <summary>
    /// Whether the game draws this upgrade's row: its own prerequisites hold and the screen it is
    /// drawn on is unlocked. While a screen's view is unavailable the game draws neither the screen
    /// nor the panel, so an upgrade whose prerequisites hold is still not offered, and a row whose
    /// screen could not be read is not offered either, because nobody asked whether it is unlocked.
    /// </summary>
    internal static bool IsOffered(GameWorldState world, in WorldUpgrade upgrade) =>
        upgrade.Reading.Available &&
        TryReadScreenGate(world, upgrade.EntityId, out var viewId) &&
        (viewId == Guid.Empty || IsUnlocked(world, viewId));

    /// <summary>
    /// The view that gates the one screen this upgrade is drawn on, or <see cref="Guid.Empty"/> for
    /// a row on no screen panel. False when the membership cannot say: withheld, naming a list the
    /// build does not pin, or claimed by two screen panels.
    /// </summary>
    internal static bool TryReadScreenGate(GameWorldState world, Guid upgradeId, out Guid viewId)
    {
        viewId = Guid.Empty;
        if (!WorldUpgradeListMembershipLookup.TryFindRange(
                world.UpgradeListMemberships, upgradeId, out var start, out var count))
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            var listId = world.UpgradeListMemberships[start + index].ListId;
            if (listId == KnownEntities.UpgradesAll.Uuid) continue;
            var gate = Guid.Empty;
            for (var candidate = 0; candidate < ScreenGates.Length; candidate++)
            {
                if (ScreenGates[candidate].ListId == listId) gate = ScreenGates[candidate].ViewId;
            }
            if (gate == Guid.Empty || (viewId != Guid.Empty && viewId != gate))
            {
                viewId = Guid.Empty;
                return false;
            }
            viewId = gate;
        }

        return true;
    }

    internal static bool IsUnlocked(GameWorldState world, Guid viewId) =>
        WorldLookup.TryFind(world.Views, viewId, out var view) && view.Available;
}

internal static class WorldUpgradeListMembershipLookup
{
    internal static readonly Comparison<WorldUpgradeListMembership> Order =
        static (left, right) =>
        {
            var upgrade = left.UpgradeId.CompareTo(right.UpgradeId);
            return upgrade != 0 ? upgrade : left.ListId.CompareTo(right.ListId);
        };

    /// <summary>
    /// The lists one upgrade is authored onto, as a contiguous range of the sorted table.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldUpgradeListMembership> table,
        Guid upgradeId,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), upgradeId,
            static (row, key) => row.UpgradeId.CompareTo(key), out start, out count, resetOnMiss: true);
    }
}
