using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

/// <summary>
/// A consumable of one family as the game authors it, owned once and registered under its UUID.
/// </summary>
internal static class ConsumableTestItems
{
    internal static ConsumableSO Create(
        IDictionary<Guid, object> registry,
        AutoItemsConsumableFamily family,
        bool withTarget = true,
        Guid? itemId = null)
    {
        var familyType = new ConsumableTypeSO();
        familyType.SetGuid(FamilyId(family));
        var item = new ConsumableSO
        {
            visible = true,
            canBeRandomized = family == AutoItemsConsumableFamily.Scroll,
        };
        item.SetGuid(itemId ?? Guid.NewGuid());
        item.SetStock(1, 0, 0);
        item.consumableTypes.Add(familyType);
        if (family == AutoItemsConsumableFamily.Scroll)
        {
            item.consumableCounts.Add(new ConsumableCount { Level = 1, Quantity = 1 });
            var targeting = new Targeting.TargetStructure();
            if (withTarget) targeting.Candidates.Add(new Target());
            var request = new RequestTargetEffectScript
            {
                targetOptions = new Targeting.TargetSelectOptions { Targeting = targeting },
            };
            var block = new InstantEffectBlock();
            block.effectScripts.Add(request);
            item.onUseEffects.Add(block);
        }
        if (AutoItemsConsumableFamilies.IsTemporary(family))
        {
            var toxicity = new ResourceSO
            {
                uuid = KnownEntities.PotionToxicity.Uuid.ToString("D"),
                quantity = new BigDouble(5),
            };
            item.hasDuration = true;
            item.durationBase = 60;
            item.consumeCost.costs.Add(
                new ResourceTuple(toxicity, new BigDouble(1)));
        }
        ConsumableSO.All.Add(item);
        registry.Add(item.GetGuid(), item);
        return item;
    }

    private static Guid FamilyId(AutoItemsConsumableFamily family) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => KnownEntities.ConsumableScrollType.Uuid,
            AutoItemsConsumableFamily.Relic => KnownEntities.ConsumableRelicType.Uuid,
            AutoItemsConsumableFamily.Fruit => KnownEntities.ConsumableFruitType.Uuid,
            AutoItemsConsumableFamily.Potion => KnownEntities.ConsumablePotionType.Uuid,
            AutoItemsConsumableFamily.Thread => KnownEntities.ConsumableThreadType.Uuid,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };

    private sealed class Target : Targeting.ITargetable
    {
    }
}
